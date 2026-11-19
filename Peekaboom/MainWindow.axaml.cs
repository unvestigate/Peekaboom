using System.Diagnostics;
using System.Globalization;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

using Dock.Model.Controls;

using Peekaboom.Core;
using Peekaboom.Docking;
using Peekaboom.Logging;
using Peekaboom.Model;
using Peekaboom.Rendering;
using Peekaboom.UI;

namespace Peekaboom;

public partial class MainWindow : Window
{
    private const double INSPECTOR_INTERVAL_SECONDS = 0.1;

    private static readonly IBrush ConnectedBrush = new SolidColorBrush(Color.FromRgb(0x7E, 0xE0, 0x7E));
    private static readonly IBrush ListeningBrush = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0));
    private static readonly IBrush OfflineBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0x6C, 0x6C));

    private readonly PeekaboomDockFactory mFactory;
    private readonly AppSettings mSettings;
    private readonly CaptureEngine mEngine;

    private readonly Selection mSelection = new();
    private readonly DispatcherTimer mInspectorTimer;
    private readonly Stopwatch mInspectorClock = Stopwatch.StartNew();

    private FrameResult? mCurrentFrame;
    private int mFramedGeneration = -1;
    private double mLastInspectorUpdate = -1.0;
    private bool mInspectorDirty;

    public MainWindow()
    {
        InitializeComponent();

        mSettings = AppSettings.Load();

        mFactory = new PeekaboomDockFactory();
        IRootDock layout = mFactory.CreateLayout();
        mFactory.InitLayout(layout);

        DockHost.Factory = mFactory;
        DockHost.Layout = layout;

        ApplySettings();

        OpenMenuItem.Click += (_, _) => _ = OpenCaptureDialogAsync();
        ExitMenuItem.Click += (_, _) => Close();
        SolidMenuItem.Click += (_, _) => SetRenderMode(RenderMode.Solid);
        WireframeMenuItem.Click += (_, _) => SetRenderMode(RenderMode.Wireframe);
        SolidWireframeMenuItem.Click += (_, _) => SetRenderMode(RenderMode.SolidWireframe);
        GridMenuItem.Click += (_, _) =>
        {
            mSettings.ShowGrid = GridMenuItem.IsChecked;
            mSettings.Save();
            mFactory.Viewport.SetShowGrid(mSettings.ShowGrid);
        };
        LeftHandedMenuItem.Click += (_, _) =>
        {
            mSettings.LeftHanded = LeftHandedMenuItem.IsChecked;
            mSettings.Save();
            mFactory.Viewport.SetLeftHanded(mSettings.LeftHanded);
        };
        FrameSceneMenuItem.Click += (_, _) => mFactory.Viewport.FrameScene();

        mInspectorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(INSPECTOR_INTERVAL_SECONDS) };
        mInspectorTimer.Tick += (_, _) =>
        {
            mInspectorTimer.Stop();
            UpdateInspector();
        };

        mSelection.Changed += OnSelectionChanged;
        mFactory.Viewport.Picked += OnPicked;
        mFactory.Objects.NodeSelected += node =>
        {
            if (node != null && !node.IsGroup)
            {
                mSelection.Select(node.Id, node.Serial);
            }
        };
        mFactory.Properties.RefClicked += id => SelectId(id);

        mEngine = new CaptureEngine();
        mEngine.TimelineChanged += OnTimelineChanged;
        mEngine.FrameReady += OnFrameReady;
        mEngine.MessagesArrived += OnEngineMessages;
        mEngine.StatusChanged += OnEngineStatus;

        Timeline.FrameRequested += request => mEngine.RequestFrame(request);
        Timeline.SettingsChanged += () =>
        {
            mSettings.PlaybackSpeed = Timeline.Speed;
            mSettings.PreSimulation = Timeline.PreSimulation;
            mSettings.Save();
        };

        KeyDown += OnWindowKeyDown;
        Closed += (_, _) =>
        {
            mEngine.Dispose();
            mSettings.Save();
        };

        StatusBarText.Text = "Starting";
        mFactory.Log.Info("Peekaboom started.");
        mEngine.StartListening(mSettings.ListenPort, mSettings.ResolvedLiveCaptureFolder, mSettings.KeepLiveCaptures);
    }

    public FrameResult? CurrentFrame => mCurrentFrame;

    private void ApplySettings()
    {
        SolidMenuItem.IsChecked = mSettings.RenderMode == RenderMode.Solid;
        WireframeMenuItem.IsChecked = mSettings.RenderMode == RenderMode.Wireframe;
        SolidWireframeMenuItem.IsChecked = mSettings.RenderMode == RenderMode.SolidWireframe;
        GridMenuItem.IsChecked = mSettings.ShowGrid;
        LeftHandedMenuItem.IsChecked = mSettings.LeftHanded;
        Timeline.ApplySettings(mSettings.PlaybackSpeed, mSettings.PreSimulation);

        ViewportViewModel viewport = mFactory.Viewport;
        viewport.SetRenderMode(mSettings.RenderMode);
        viewport.SetShowGrid(mSettings.ShowGrid);
        viewport.SetLeftHanded(mSettings.LeftHanded);
        viewport.SetDisplay(mSettings.Display.Clone());

        mFactory.Display.Load(mSettings.Display);
        mFactory.Display.OptionsChanged += () =>
        {
            mSettings.Display = mFactory.Display.Options.Clone();
            mSettings.Save();
            mFactory.Viewport.SetDisplay(mSettings.Display.Clone());
        };
    }

    private void SetRenderMode(RenderMode mode)
    {
        mSettings.RenderMode = mode;
        mSettings.Save();
        mFactory.Viewport.SetRenderMode(mode);
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        if (e.Key == Key.O && e.KeyModifiers == KeyModifiers.Control)
        {
            _ = OpenCaptureDialogAsync();
            e.Handled = true;
            return;
        }

        if (Timeline.HandleKey(e))
        {
            e.Handled = true;
        }
    }

    private async Task OpenCaptureDialogAsync()
    {
        var options = new FilePickerOpenOptions
        {
            Title = "Open Capture",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Captures") { Patterns = new[] { "*.pxd2" } },
                FilePickerFileTypes.All,
            },
        };

        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(options);
        if (files.Count == 0)
        {
            return;
        }

        string? path = files[0].TryGetLocalPath();
        if (path != null)
        {
            OpenCapture(path);
        }
    }

    public void OpenCapture(string path)
    {
        mFactory.Log.Info("Opening " + path + ".");
        mEngine.OpenFile(path);
    }

    private void OnTimelineChanged(TimelineUpdate update)
    {
        if (update.Reset)
        {
            mFactory.Objects.ClearSceneNames();
            mSelection.Clear();
        }

        foreach (SceneUpdate scene in update.Scenes)
        {
            mFactory.Objects.SetSceneName(scene.Id, scene.Name);
        }

        Timeline.ApplyTimeline(update);
    }

    private void OnFrameReady(FrameResult result)
    {
        mCurrentFrame = result;
        Timeline.ShowFrame(result);
        mFactory.Viewport.SetDrawList(result.DrawList);

        if (result.Generation != mFramedGeneration && result.DrawList != null && result.DrawList.HasBounds)
        {
            mFramedGeneration = result.Generation;
            mFactory.Viewport.FrameScene();
        }

        ScheduleInspector();
    }

    private void ScheduleInspector()
    {
        double now = mInspectorClock.Elapsed.TotalSeconds;
        if (now - mLastInspectorUpdate >= INSPECTOR_INTERVAL_SECONDS)
        {
            mInspectorTimer.Stop();
            UpdateInspector();
            return;
        }

        if (!mInspectorTimer.IsEnabled)
        {
            mInspectorTimer.Start();
        }
    }

    private void UpdateInspector()
    {
        mLastInspectorUpdate = mInspectorClock.Elapsed.TotalSeconds;
        ObjectDb? state = mCurrentFrame?.State;
        mFactory.Objects.Update(state);
        mFactory.Properties.Update(state, mSelection.Id, mSelection.Serial);

        if (mInspectorDirty)
        {
            mInspectorDirty = false;
            mFactory.Objects.SelectId(mSelection.Id);
        }
    }

    private void OnPicked(DrawItem? item, bool selectShape)
    {
        if (item == null)
        {
            mSelection.Clear();
            return;
        }

        DrawItem picked = item.Value;
        SelectId(selectShape ? picked.ShapeId : picked.ActorId);
    }

    private void SelectId(ulong id)
    {
        ObjectInstance? instance = mCurrentFrame?.State?.Find(id);
        mSelection.Select(id, instance?.Serial ?? -1);
    }

    private void OnSelectionChanged()
    {
        var ids = new HashSet<ulong>();
        if (!mSelection.IsEmpty)
        {
            ids.Add(mSelection.Id);
        }

        mFactory.Viewport.SetSelection(ids);
        mFactory.Properties.Update(mCurrentFrame?.State, mSelection.Id, mSelection.Serial);
        mFactory.Objects.SelectId(mSelection.Id);
        mInspectorDirty = true;
    }

    private void OnEngineMessages(IReadOnlyList<EngineMessage> messages, int dropped)
    {
        foreach (EngineMessage message in messages)
        {
            mFactory.Log.Add(message.Text, message.Severity);
        }

        if (dropped > 0)
        {
            mFactory.Log.Warning(dropped + " more messages were not shown.");
        }
    }

    private void OnEngineStatus(EngineStatus status)
    {
        string text;
        if (status.Loading)
        {
            text = "Loading " + Path.GetFileName(status.Source) + "...";
        }
        else if (status.Connected)
        {
            text = "Live from " + status.Remote + ", " + FormatBytes(status.BytesReceived) + " recorded to " + Path.GetFileName(status.Source);
        }
        else
        {
            string listening = status.Listening ? "Listening on port " + status.Port : "Not listening";
            text = status.Source.Length > 0 ? Path.GetFileName(status.Source) + "   " + listening : listening;
        }

        StatusBarText.Text = text;
        ConnectionIcon.Data = status.Connected ? Icons.PlugConnected : Icons.PlugDisconnected;
        ConnectionIcon.Fill = status.Connected ? ConnectedBrush : status.Listening ? ListeningBrush : OfflineBrush;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1L << 20)
        {
            return (bytes / (double)(1L << 20)).ToString("0.0", CultureInfo.InvariantCulture) + " MiB";
        }

        return (bytes / 1024.0).ToString("0", CultureInfo.InvariantCulture) + " KiB";
    }
}
