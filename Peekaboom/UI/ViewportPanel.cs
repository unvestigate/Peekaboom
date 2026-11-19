using System.Diagnostics;
using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;

using Peekaboom.Docking;
using Peekaboom.Logging;
using Peekaboom.Rendering;

using Matrix4x4 = System.Numerics.Matrix4x4;
using Vector3 = System.Numerics.Vector3;

namespace Peekaboom.UI;

public sealed class ViewportPanel : Control
{
    private const float ORBIT_RADIANS_PER_PIXEL = 0.008f;
    private const float LOOK_RADIANS_PER_PIXEL = 0.004f;
    private const float DEFAULT_FLY_SPEED = 8.0f;
    private const float FAST_FLY_FACTOR = 4.0f;
    private const float MAX_FRAME_SECONDS = 0.1f;
    private const double CLICK_SLOP = 4.0;

    private static readonly TimeSpan ResizeSettleTime = TimeSpan.FromMilliseconds(150.0);
    private static readonly TimeSpan RetireTimeout = TimeSpan.FromSeconds(2.0);

    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
    private static readonly IBrush TextBrush = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));

    private readonly DispatcherTimer mResizeTimer;
    private readonly ViewportCamera mCamera = new();
    private readonly HashSet<Key> mKeys = new();
    private readonly Stopwatch mClock = Stopwatch.StartNew();

    private ViewportViewModel? mViewModel;
    private DragMode mDrag;
    private Point mLastPoint;
    private float mFlySpeed = DEFAULT_FLY_SPEED;
    private double mLastMoveTime;
    private bool mLeftHanded;
    private Point mPressPoint;
    private bool mPressMoved;
    private PickRequest? mPendingPick;
    private bool mPickDone;
    private DrawItem? mPickedItem;
    private bool mPickedShape;
    private bool mSelectWhenPicked;
    private bool mHasPivot;
    private Vector3 mPivot;

    private Compositor? mCompositor;
    private CompositionDrawingSurface? mSurface;
    private CompositionSurfaceVisual? mSurfaceVisual;
    private ICompositionGpuInterop? mInterop;
    private IViewportRenderer? mRenderer;
    private Frames? mFrames;
    private LogViewModel? mLog;

    private int mAttachGeneration;
    private bool mFrameQueued;
    private bool mHasPresented;
    private bool mCapabilitiesLogged;
    private bool mRenderFailureLogged;
    private bool mUpdateFaultLogged;
    private string mStatus = "";

    public ViewportPanel()
    {
        ClipToBounds = true;
        Focusable = true;

        mResizeTimer = new DispatcherTimer { Interval = ResizeSettleTime };
        mResizeTimer.Tick += OnResizeSettled;

        DataContextChanged += (_, _) => AttachViewModel();
        LostFocus += (_, _) => EndInput();
    }

    private enum DragMode
    {
        None,
        Orbit,
        Pan,
        Look,
    }

    private void AttachViewModel()
    {
        if (mViewModel != null)
        {
            mViewModel.Changed -= OnViewModelChanged;
            mViewModel.FrameSceneRequested -= FrameScene;
        }

        mViewModel = DataContext as ViewportViewModel;
        mLog = mViewModel?.Log;
        mLeftHanded = mViewModel?.LeftHanded ?? false;

        if (mViewModel != null)
        {
            mViewModel.Changed += OnViewModelChanged;
            mViewModel.FrameSceneRequested += FrameScene;
        }

        RequestFrame();
    }

    private void OnViewModelChanged()
    {
        bool leftHanded = mViewModel?.LeftHanded ?? false;
        if (leftHanded != mLeftHanded)
        {
            mLeftHanded = leftHanded;
            mCamera.MirrorZ();
        }

        RequestFrame();
    }

    public void FrameScene()
    {
        SceneDrawList? list = mViewModel?.DrawList;
        if (list == null || !list.HasBounds)
        {
            RequestFrame();
            return;
        }

        Vector3 min = list.BoundsMin;
        Vector3 max = list.BoundsMax;

        IReadOnlySet<ulong>? selection = mViewModel?.Selection;
        if (selection != null && selection.Count > 0 && list.TryGetBounds(selection, out Vector3 selectedMin, out Vector3 selectedMax))
        {
            min = selectedMin;
            max = selectedMax;
        }

        if (mLeftHanded)
        {
            float minZ = min.Z;
            min = new Vector3(min.X, min.Y, -max.Z);
            max = new Vector3(max.X, max.Y, -minZ);
        }

        mCamera.Frame(min, max);
        RequestFrame();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();

        PointerPointProperties properties = e.GetCurrentPoint(this).Properties;
        if (properties.IsRightButtonPressed)
        {
            mDrag = DragMode.Look;
            mLastMoveTime = mClock.Elapsed.TotalSeconds;
        }
        else if (properties.IsMiddleButtonPressed || (properties.IsLeftButtonPressed && (e.KeyModifiers & KeyModifiers.Shift) != 0))
        {
            mDrag = DragMode.Pan;
        }
        else if (properties.IsLeftButtonPressed)
        {
            mDrag = DragMode.Orbit;
        }
        else
        {
            return;
        }

        mLastPoint = e.GetPosition(this);
        mPressPoint = mLastPoint;
        mPressMoved = false;
        mHasPivot = false;
        mPickDone = false;
        mPickedItem = null;
        mSelectWhenPicked = false;

        if (mDrag == DragMode.Orbit || mDrag == DragMode.Pan)
        {
            RequestPick(mPressPoint, (e.KeyModifiers & KeyModifiers.Control) != 0);
        }

        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (mDrag == DragMode.None)
        {
            return;
        }

        Point point = e.GetPosition(this);
        float dx = (float)(point.X - mLastPoint.X);
        float dy = (float)(point.Y - mLastPoint.Y);
        mLastPoint = point;

        if (Math.Abs(point.X - mPressPoint.X) + Math.Abs(point.Y - mPressPoint.Y) > CLICK_SLOP)
        {
            mPressMoved = true;
        }

        switch (mDrag)
        {
            case DragMode.Orbit:
                if (mHasPivot)
                {
                    mCamera.OrbitAround(mPivot, -dx * ORBIT_RADIANS_PER_PIXEL, dy * ORBIT_RADIANS_PER_PIXEL);
                }
                else
                {
                    mCamera.Orbit(-dx * ORBIT_RADIANS_PER_PIXEL, dy * ORBIT_RADIANS_PER_PIXEL);
                }

                break;
            case DragMode.Pan:
                float depth = mHasPivot ? mCamera.DepthOf(mPivot) : mCamera.Distance;
                mCamera.Pan(dx, dy, (float)Bounds.Height, depth);
                break;
            case DragMode.Look:
                mCamera.Look(-dx * LOOK_RADIANS_PER_PIXEL, dy * LOOK_RADIANS_PER_PIXEL);
                break;
        }

        RequestFrame();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        PointerPointProperties properties = e.GetCurrentPoint(this).Properties;
        if (!properties.IsLeftButtonPressed && !properties.IsMiddleButtonPressed && !properties.IsRightButtonPressed)
        {
            if (mDrag == DragMode.Orbit && !mPressMoved && e.InitialPressMouseButton == MouseButton.Left)
            {
                if (mPickDone)
                {
                    mViewModel?.ReportPick(mPickedItem, mPickedShape);
                }
                else
                {
                    mSelectWhenPicked = true;
                }
            }

            mDrag = DragMode.None;
            e.Pointer.Capture(null);
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        mDrag = DragMode.None;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        float steps = (float)e.Delta.Y;

        if (mDrag == DragMode.Look)
        {
            mFlySpeed = Math.Clamp(mFlySpeed * MathF.Pow(1.25f, steps), 0.1f, 1000.0f);
        }
        else
        {
            mCamera.Dolly(steps);
            RequestFrame();
        }

        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.None && mDrag != DragMode.Look)
        {
            FrameScene();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            mViewModel?.ReportPick(null, false);
            e.Handled = true;
            return;
        }

        if (IsMoveKey(e.Key) || e.Key == Key.LeftShift || e.Key == Key.RightShift)
        {
            if (mKeys.Count == 0)
            {
                mLastMoveTime = mClock.Elapsed.TotalSeconds;
            }

            mKeys.Add(e.Key);
            RequestFrame();
            e.Handled = true;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        mKeys.Remove(e.Key);
    }

    private void EndInput()
    {
        mKeys.Clear();
        mDrag = DragMode.None;
    }

    private static bool IsMoveKey(Key key)
    {
        return key == Key.W || key == Key.A || key == Key.S || key == Key.D || key == Key.Q || key == Key.E;
    }

    private bool AdvanceCamera()
    {
        double now = mClock.Elapsed.TotalSeconds;
        float seconds = Math.Min((float)(now - mLastMoveTime), MAX_FRAME_SECONDS);
        mLastMoveTime = now;

        if (mDrag != DragMode.Look)
        {
            return false;
        }

        Vector3 direction = Vector3.Zero;
        if (mKeys.Contains(Key.W))
        {
            direction.Z += 1.0f;
        }

        if (mKeys.Contains(Key.S))
        {
            direction.Z -= 1.0f;
        }

        if (mKeys.Contains(Key.D))
        {
            direction.X += 1.0f;
        }

        if (mKeys.Contains(Key.A))
        {
            direction.X -= 1.0f;
        }

        if (mKeys.Contains(Key.E))
        {
            direction.Y += 1.0f;
        }

        if (mKeys.Contains(Key.Q))
        {
            direction.Y -= 1.0f;
        }

        if (direction == Vector3.Zero)
        {
            return false;
        }

        bool fast = mKeys.Contains(Key.LeftShift) || mKeys.Contains(Key.RightShift);
        float speed = mFlySpeed * (fast ? FAST_FLY_FACTOR : 1.0f);
        mCamera.Move(Vector3.Normalize(direction), speed * seconds);
        return true;
    }

    private ViewportFrame BuildFrame(IViewportTargets targets)
    {
        float aspect = targets.Width / (float)Math.Max(1, targets.Height);
        Matrix4x4 viewProjection = mCamera.View * mCamera.Projection(aspect);
        Vector3 cameraPosition = mCamera.Position;

        if (mLeftHanded)
        {
            viewProjection = Matrix4x4.CreateScale(1.0f, 1.0f, -1.0f) * viewProjection;
            cameraPosition = new Vector3(cameraPosition.X, cameraPosition.Y, -cameraPosition.Z);
        }

        return new ViewportFrame
        {
            ViewProjection = viewProjection,
            CameraPosition = cameraPosition,
            DrawList = mViewModel?.DrawList,
            Mode = mViewModel?.RenderMode ?? RenderMode.Solid,
            ShowGrid = mViewModel?.ShowGrid ?? true,
            LeftHanded = mLeftHanded,
            Selection = mViewModel?.Selection,
            Pick = mPendingPick,
            Display = mViewModel?.Display,
        };
    }

    private void RequestPick(Point point, bool selectShape)
    {
        Frames? frames = mFrames;
        if (frames == null || Bounds.Width <= 0.0 || Bounds.Height <= 0.0)
        {
            return;
        }

        int x = (int)(point.X * frames.Targets.Width / Bounds.Width);
        int y = (int)(point.Y * frames.Targets.Height / Bounds.Height);
        mPendingPick = new PickRequest(x, y, selectShape);
        RequestFrame();
    }

    private void CompletePick(ViewportFrame frame)
    {
        PickRequest? pick = frame.Pick;
        if (pick == null || !pick.Completed || pick != mPendingPick)
        {
            return;
        }

        mPendingPick = null;
        SceneDrawList? list = frame.DrawList;
        DrawItem? item = null;
        if (list != null && pick.ItemIndex >= 0 && pick.ItemIndex < list.Items.Count)
        {
            item = list.Items[pick.ItemIndex];
        }

        mPickDone = true;
        mPickedItem = item;
        mPickedShape = pick.SelectShape;

        if (pick.HasPoint)
        {
            Vector3 point = pick.WorldPoint;
            mPivot = mLeftHanded ? new Vector3(point.X, point.Y, -point.Z) : point;
            mHasPivot = true;
        }

        if (mSelectWhenPicked)
        {
            mSelectWhenPicked = false;
            mViewModel?.ReportPick(item, pick.SelectShape);
        }
    }

    public void RequestFrame()
    {
        Compositor? compositor = mCompositor;
        if (mFrameQueued || compositor == null || mRenderer == null)
        {
            return;
        }

        mFrameQueued = true;
        int generation = mAttachGeneration;
        compositor.RequestCompositionUpdate(() => UpdateFrame(generation));
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        StartInitialize();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Teardown();
        base.OnDetachedFromVisualTree(e);
    }

    private async void StartInitialize()
    {
        int generation = ++mAttachGeneration;

        CompositionVisual? selfVisual = ElementComposition.GetElementVisual(this);
        if (selfVisual == null)
        {
            SetStatus("Composition is unavailable");
            return;
        }

        mCompositor = selfVisual.Compositor;
        mSurface = mCompositor.CreateDrawingSurface();
        mSurfaceVisual = mCompositor.CreateSurfaceVisual();
        mSurfaceVisual.Size = new Vector(Bounds.Width, Bounds.Height);
        mSurfaceVisual.Surface = mSurface;
        ElementComposition.SetElementChildVisual(this, mSurfaceVisual);

        ICompositionGpuInterop? interop = null;
        try
        {
            interop = await mCompositor.TryGetCompositionGpuInterop();
        }
        catch (Exception exception)
        {
            LogError("Querying composition GPU interop failed. " + exception.Message);
        }

        if (generation != mAttachGeneration)
        {
            return;
        }

        if (interop == null || interop.IsLost)
        {
            SetStatus("GPU interop is unavailable");
            return;
        }

        mInterop = interop;

        if (!mCapabilitiesLogged)
        {
            mCapabilitiesLogged = true;
            LogCapabilities(interop);
        }

        string handleType = KnownPlatformGraphicsExternalImageHandleTypes.D3D11TextureGlobalSharedHandle;
        if (!Contains(interop.SupportedImageHandleTypes, handleType))
        {
            SetStatus("The compositor cannot import Direct3D 11 textures");
            return;
        }

        CompositionGpuImportedImageSynchronizationCapabilities sync = interop.GetSynchronizationCapabilities(handleType);
        if ((sync & CompositionGpuImportedImageSynchronizationCapabilities.KeyedMutex) == 0)
        {
            SetStatus("The compositor has no keyed mutex sync");
            return;
        }

        mRenderer = D3D11ViewportRenderer.Create(interop.DeviceLuid, mLog);
        if (mRenderer == null)
        {
            SetStatus("Direct3D 11 initialization failed");
            return;
        }

        SetStatus("");
        RequestFrame();
    }

    private void Teardown()
    {
        mAttachGeneration++;
        mResizeTimer.Stop();
        mFrameQueued = false;
        mHasPresented = false;

        RetireFrames();

        mRenderer?.Dispose();
        mRenderer = null;
        mInterop = null;

        ElementComposition.SetElementChildVisual(this, null);
        mSurface?.Dispose();
        mSurface = null;
        mSurfaceVisual = null;
        mCompositor = null;
    }

    private void Reinitialize()
    {
        Teardown();
        StartInitialize();
    }

    private void UpdateFrame(int generation)
    {
        if (generation != mAttachGeneration)
        {
            return;
        }

        mFrameQueued = false;

        if (mRenderer == null || mSurface == null || mInterop == null)
        {
            return;
        }

        if (mInterop.IsLost || mRenderer.IsLost)
        {
            LogWarning("The GPU device was lost. Reinitializing the viewport.");
            Reinitialize();
            return;
        }

        if (Bounds.Width <= 0.0 || Bounds.Height <= 0.0)
        {
            return;
        }

        PixelSize size = CurrentPixelSize();
        bool sizeChanged = mFrames != null && (mFrames.Targets.Width != size.Width || mFrames.Targets.Height != size.Height);

        if (mFrames == null || (sizeChanged && !mResizeTimer.IsEnabled))
        {
            RetireFrames();
            mFrames = CreateFrames(mRenderer, mInterop, size);
            if (mFrames == null)
            {
                SetStatus("Creating the viewport images failed");
                return;
            }
        }

        int index = mFrames.FindFreeImage();
        if (index < 0)
        {
            RequestFrame();
            return;
        }

        bool moving = AdvanceCamera();
        ViewportFrame frame = BuildFrame(mFrames.Targets);

        if (!mRenderer.Render(mFrames.Targets, index, frame))
        {
            if (!mRenderFailureLogged)
            {
                mRenderFailureLogged = true;
                LogWarning("Rendering a viewport frame failed.");
            }

            if (mRenderer.IsLost)
            {
                RequestFrame();
            }

            return;
        }

        try
        {
            mFrames.LastUpdate[index] = mSurface.UpdateWithKeyedMutexAsync(mFrames.Images[index], 1, 0);
        }
        catch (Exception exception)
        {
            if (!mUpdateFaultLogged)
            {
                mUpdateFaultLogged = true;
                LogError("Presenting a viewport frame failed. " + exception.Message);
            }

            return;
        }

        if (!mHasPresented)
        {
            mHasPresented = true;
            mStatus = "";
            InvalidateVisual();
        }

        CompletePick(frame);

        if (moving)
        {
            RequestFrame();
        }
    }

    private Frames? CreateFrames(IViewportRenderer renderer, ICompositionGpuInterop interop, PixelSize size)
    {
        IViewportTargets? targets = renderer.CreateTargets(size.Width, size.Height);
        if (targets == null)
        {
            return null;
        }

        var images = new ICompositionImportedGpuImage[targets.Handles.Count];

        for (int i = 0; i < images.Length; i++)
        {
            var properties = new PlatformGraphicsExternalImageProperties
            {
                Width = targets.Width,
                Height = targets.Height,
                Format = renderer.ImageFormat,
                TopLeftOrigin = true,
            };

            try
            {
                images[i] = interop.ImportImage(new PlatformHandle(targets.Handles[i], renderer.HandleType), properties);
            }
            catch (Exception exception)
            {
                LogError("Importing viewport image " + i + " failed. " + exception.Message);

                for (int j = 0; j < i; j++)
                {
                    _ = images[j].DisposeAsync();
                }

                targets.Dispose();
                return null;
            }
        }

        return new Frames(targets, images);
    }

    private void RetireFrames()
    {
        Frames? frames = mFrames;
        mFrames = null;

        if (frames == null)
        {
            return;
        }

        var pending = new List<Task>();
        foreach (Task? update in frames.LastUpdate)
        {
            if (update != null && !update.IsCompleted)
            {
                pending.Add(update);
            }
        }

        if (pending.Count == 0)
        {
            frames.Dispose();
            return;
        }

        Task.WhenAny(Task.WhenAll(pending), Task.Delay(RetireTimeout)).ContinueWith(
            _ => Dispatcher.UIThread.Post(frames.Dispose),
            TaskContinuationOptions.ExecuteSynchronously);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == BoundsProperty)
        {
            if (mSurfaceVisual != null)
            {
                mSurfaceVisual.Size = new Vector(Bounds.Width, Bounds.Height);
            }

            if (mFrames == null)
            {
                RequestFrame();
            }
            else
            {
                mResizeTimer.Stop();
                mResizeTimer.Start();
            }
        }
    }

    private void OnResizeSettled(object? sender, EventArgs e)
    {
        mResizeTimer.Stop();
        RequestFrame();
    }

    public PixelSize CurrentPixelSize()
    {
        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        int width = Math.Max(1, (int)Math.Round(Bounds.Width * scaling));
        int height = Math.Max(1, (int)Math.Round(Bounds.Height * scaling));
        return new PixelSize(width, height);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(BackgroundBrush, bounds);

        if (mHasPresented || mStatus.Length == 0)
        {
            return;
        }

        var formatted = new FormattedText(mStatus, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            Typeface.Default, 14.0, TextBrush);

        context.DrawText(formatted, new Point(
            (bounds.Width - formatted.Width) / 2.0,
            (bounds.Height - formatted.Height) / 2.0));
    }

    private void SetStatus(string status)
    {
        if (status.Length > 0 && status != mStatus)
        {
            LogError(status + ".");
        }

        mStatus = status;
        InvalidateVisual();
    }

    private void LogCapabilities(ICompositionGpuInterop interop)
    {
        LogInfo("Composition GPU interop available" + (interop.IsLost ? " (context lost)." : "."));
        LogInfo("  Image handle types " + Join(interop.SupportedImageHandleTypes) + ".");
        LogInfo("  Semaphore types " + Join(interop.SupportedSemaphoreTypes) + ".");
        LogInfo("  Device LUID " + Hex(interop.DeviceLuid) + ", UUID " + Hex(interop.DeviceUuid) + ".");

        foreach (string type in interop.SupportedImageHandleTypes)
        {
            LogInfo("  Sync for " + type + " is " + interop.GetSynchronizationCapabilities(type) + ".");
        }
    }

    private static bool Contains(IReadOnlyList<string> values, string value)
    {
        foreach (string candidate in values)
        {
            if (candidate == value)
            {
                return true;
            }
        }

        return false;
    }

    private static string Join(IReadOnlyList<string> values)
    {
        return values.Count == 0 ? "(none)" : string.Join(", ", values);
    }

    private static string Hex(byte[]? bytes)
    {
        return bytes == null ? "(none)" : Convert.ToHexString(bytes);
    }

    private void LogInfo(string text) => mLog?.Info(text);

    private void LogWarning(string text) => mLog?.Warning(text);

    private void LogError(string text) => mLog?.Error(text);

    private sealed class Frames : IDisposable
    {
        public Frames(IViewportTargets targets, ICompositionImportedGpuImage[] images)
        {
            Targets = targets;
            Images = images;
            LastUpdate = new Task?[images.Length];
        }

        public IViewportTargets Targets { get; }

        public ICompositionImportedGpuImage[] Images { get; }

        public Task?[] LastUpdate { get; }

        public int FindFreeImage()
        {
            for (int i = 0; i < LastUpdate.Length; i++)
            {
                Task? update = LastUpdate[i];
                if (update == null || update.IsCompleted)
                {
                    return i;
                }
            }

            return -1;
        }

        public void Dispose()
        {
            foreach (ICompositionImportedGpuImage image in Images)
            {
                _ = image.DisposeAsync();
            }

            Targets.Dispose();
        }
    }
}
