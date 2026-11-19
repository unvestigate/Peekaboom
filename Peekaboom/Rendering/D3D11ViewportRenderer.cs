using System.Numerics;
using System.Runtime.InteropServices;

using Avalonia.Platform;

using Peekaboom.Logging;
using Peekaboom.Model.Physics;

using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;

namespace Peekaboom.Rendering;

public sealed unsafe class D3D11ViewportRenderer : IViewportRenderer
{
    private const int IMAGE_COUNT = 3;
    private const uint MSAA_SAMPLES = 4;
    private const uint ACQUIRE_TIMEOUT_MS = 100;
    private const int WAIT_TIMEOUT = 0x102;
    private const int MIN_INSTANCE_CAPACITY = 256;
    private const int GRID_EXTENT = 100;
    private const int GRID_MAJOR = 10;
    private const long MESH_EVICT_FRAMES = 600;
    private const long MESH_EVICT_INTERVAL = 120;
    private const float WIRE_OVERLAY_SCALE = 0.3f;

    private const string BACKGROUND_SHADER = "Shaders.Background.hlsl";
    private const string SCENE_SHADER = "Shaders.Scene.hlsl";
    private const string LINE_SHADER = "Shaders.Line.hlsl";
    private const string PICK_SHADER = "Shaders.Pick.hlsl";
    private const float SELECTED_BRIGHTEN = 0.35f;
    private const float AXIS_LENGTH = 0.5f;
    private const float VELOCITY_SECONDS = 0.25f;
    private const int MIN_LINE_CAPACITY = 4096;

    private static readonly uint BoundsColor = OverlayBuilder.Rgba(255, 220, 60);
    private static readonly uint LinearVelocityColor = OverlayBuilder.Rgba(240, 90, 240);
    private static readonly uint AngularVelocityColor = OverlayBuilder.Rgba(80, 220, 240);

    private static readonly Vector4 LightDirection = new(Vector3.Normalize(new Vector3(0.35f, 0.9f, 0.25f)), 0.0f);
    private static readonly Vector4 HighlightColor = new(1.0f, 0.85f, 0.2f, 1.0f);

    private readonly GpuDevice mDevice;
    private readonly LogViewModel? mLog;
    private readonly uint mSampleCount;

    private readonly Dictionary<MeshKey, GpuMesh> mMeshes = new();
    private readonly List<InstanceData> mInstances = new();
    private readonly List<DrawCall> mDrawCalls = new();
    private readonly List<DrawCall> mSelectedCalls = new();
    private readonly List<int> mSelectedScratch = new();
    private readonly List<LineVertex> mDepthLines = new();
    private readonly List<LineVertex> mTopLines = new();
    private readonly List<int> mBoxItems = new();
    private readonly List<int> mCapsuleItems = new();
    private readonly List<int> mPlaneItems = new();
    private readonly Dictionary<MeshKey, List<int>> mMeshItems = new();
    private readonly List<MeshKey> mEvicted = new();

    private ID3D11VertexShader* mBackgroundVertexShader;
    private ID3D11PixelShader* mBackgroundPixelShader;
    private ID3D11VertexShader* mSceneVertexShader;
    private ID3D11PixelShader* mScenePixelShader;
    private ID3D11InputLayout* mSceneLayout;
    private ID3D11VertexShader* mLineVertexShader;
    private ID3D11PixelShader* mLinePixelShader;
    private ID3D11InputLayout* mLineLayout;
    private ID3D11VertexShader* mPickVertexShader;
    private ID3D11PixelShader* mPickPixelShader;

    private ID3D11Buffer* mConstants;
    private ID3D11Buffer* mInstanceBuffer;
    private int mInstanceCapacity;
    private ID3D11Buffer* mGridBuffer;
    private uint mGridVertexCount;
    private ID3D11Buffer* mLineBuffer;
    private int mLineCapacity;

    private ID3D11DepthStencilState* mDepthDisabled;
    private ID3D11DepthStencilState* mDepthWrite;
    private ID3D11DepthStencilState* mDepthOverlay;
    private ID3D11RasterizerState* mSolidRaster;
    private ID3D11RasterizerState* mWireRaster;
    private ID3D11RasterizerState* mWireOverlayRaster;

    private GpuMesh? mBoxMesh;
    private GpuMesh? mCapsuleMesh;
    private GpuMesh? mPlaneMesh;

    private long mFrameNumber;
    private bool mAcquireFailureLogged;

    private D3D11ViewportRenderer(GpuDevice device, LogViewModel? log, uint sampleCount)
    {
        mDevice = device;
        mLog = log;
        mSampleCount = sampleCount;
    }

    public string HandleType => KnownPlatformGraphicsExternalImageHandleTypes.D3D11TextureGlobalSharedHandle;

    public PlatformGraphicsExternalImageFormat ImageFormat => PlatformGraphicsExternalImageFormat.R8G8B8A8UNorm;

    public bool IsLost => mDevice.IsRemoved;

    public static D3D11ViewportRenderer? Create(byte[]? deviceLuid, LogViewModel? log)
    {
        GpuDevice? device = GpuDevice.Acquire(deviceLuid, log);
        if (device == null)
        {
            return null;
        }

        uint qualityLevels = 0;
        device.Device->CheckMultisampleQualityLevels(Format.FormatR8G8B8A8Unorm, MSAA_SAMPLES, &qualityLevels);
        uint sampleCount = qualityLevels > 0 ? MSAA_SAMPLES : 1;

        var renderer = new D3D11ViewportRenderer(device, log, sampleCount);
        if (!renderer.CreateResources())
        {
            renderer.Dispose();
            return null;
        }

        return renderer;
    }

    private bool CreateResources()
    {
        if (!CreateBackgroundShaders() || !CreateSceneShaders() || !CreateLineShaders() || !CreatePickShaders())
        {
            return false;
        }

        ID3D11Device* device = mDevice.Device;

        mConstants = GpuBuffers.CreateDynamic(device, (uint)sizeof(SceneConstants), BindFlag.ConstantBuffer);
        if (mConstants == null)
        {
            mLog?.Error("Creating the scene constant buffer failed.");
            return false;
        }

        mDepthDisabled = CreateDepthState(false, false, ComparisonFunc.Always);
        mDepthWrite = CreateDepthState(true, true, ComparisonFunc.Greater);
        mDepthOverlay = CreateDepthState(true, false, ComparisonFunc.GreaterEqual);
        mSolidRaster = CreateRasterizer(FillMode.Solid, 0, 0.0f);
        mWireRaster = CreateRasterizer(FillMode.Wireframe, 0, 0.0f);
        mWireOverlayRaster = CreateRasterizer(FillMode.Wireframe, 16, 1.0f);

        if (mDepthDisabled == null || mDepthWrite == null || mDepthOverlay == null
            || mSolidRaster == null || mWireRaster == null || mWireOverlayRaster == null)
        {
            mLog?.Error("Creating the render states failed.");
            return false;
        }

        PrimitiveMeshes.Box(out Vector4[] boxVertices, out uint[] boxIndices);
        mBoxMesh = GpuMesh.Create(device, boxVertices, boxIndices);
        PrimitiveMeshes.Capsule(out Vector4[] capsuleVertices, out uint[] capsuleIndices);
        mCapsuleMesh = GpuMesh.Create(device, capsuleVertices, capsuleIndices);
        PrimitiveMeshes.Plane(out Vector4[] planeVertices, out uint[] planeIndices);
        mPlaneMesh = GpuMesh.Create(device, planeVertices, planeIndices);

        if (mBoxMesh == null || mCapsuleMesh == null || mPlaneMesh == null)
        {
            mLog?.Error("Creating the primitive meshes failed.");
            return false;
        }

        CreateGrid();
        return true;
    }

    private ID3D10Blob* Compile(string resource, string entryPoint, string profile)
    {
        string? source = LoadShaderSource(resource);
        if (source == null)
        {
            mLog?.Error("Shader resource " + resource + " is missing.");
            return null;
        }

        return mDevice.CompileShader(source, resource, entryPoint, profile, mLog);
    }

    private ID3D11VertexShader* CreateVertexShader(ID3D10Blob* code)
    {
        ID3D11VertexShader* shader = null;
        int hr = mDevice.Device->CreateVertexShader(code->GetBufferPointer(), code->GetBufferSize(), (ID3D11ClassLinkage*)null, &shader);
        if (hr < 0)
        {
            mLog?.Error("CreateVertexShader failed with " + GpuDevice.Hex(hr) + ".");
            return null;
        }

        return shader;
    }

    private ID3D11PixelShader* CreatePixelShader(string resource)
    {
        ID3D10Blob* code = Compile(resource, "PSMain", "ps_4_0");
        if (code == null)
        {
            return null;
        }

        ID3D11PixelShader* shader = null;
        int hr = mDevice.Device->CreatePixelShader(code->GetBufferPointer(), code->GetBufferSize(), (ID3D11ClassLinkage*)null, &shader);
        code->Release();
        if (hr < 0)
        {
            mLog?.Error("CreatePixelShader failed with " + GpuDevice.Hex(hr) + ".");
            return null;
        }

        return shader;
    }

    private bool CreateBackgroundShaders()
    {
        ID3D10Blob* code = Compile(BACKGROUND_SHADER, "VSMain", "vs_4_0");
        if (code == null)
        {
            return false;
        }

        mBackgroundVertexShader = CreateVertexShader(code);
        code->Release();
        mBackgroundPixelShader = CreatePixelShader(BACKGROUND_SHADER);
        return mBackgroundVertexShader != null && mBackgroundPixelShader != null;
    }

    private bool CreatePickShaders()
    {
        ID3D10Blob* code = Compile(PICK_SHADER, "VSMain", "vs_4_0");
        if (code == null)
        {
            return false;
        }

        mPickVertexShader = CreateVertexShader(code);
        code->Release();
        mPickPixelShader = CreatePixelShader(PICK_SHADER);
        return mPickVertexShader != null && mPickPixelShader != null;
    }

    private bool CreateSceneShaders()
    {
        ID3D10Blob* code = Compile(SCENE_SHADER, "VSMain", "vs_4_0");
        if (code == null)
        {
            return false;
        }

        mSceneVertexShader = CreateVertexShader(code);

        fixed (byte* position = "POSITION\0"u8)
        fixed (byte* texcoord = "TEXCOORD\0"u8)
        {
            InputElementDesc* elements = stackalloc InputElementDesc[6];
            elements[0] = new InputElementDesc
            {
                SemanticName = position,
                SemanticIndex = 0,
                Format = Format.FormatR32G32B32A32Float,
                InputSlot = 0,
                AlignedByteOffset = 0,
                InputSlotClass = InputClassification.PerVertexData,
                InstanceDataStepRate = 0,
            };

            for (uint i = 0; i < 5; i++)
            {
                elements[i + 1] = new InputElementDesc
                {
                    SemanticName = texcoord,
                    SemanticIndex = i,
                    Format = Format.FormatR32G32B32A32Float,
                    InputSlot = 1,
                    AlignedByteOffset = i * 16,
                    InputSlotClass = InputClassification.PerInstanceData,
                    InstanceDataStepRate = 1,
                };
            }

            ID3D11InputLayout* layout = null;
            int hr = mDevice.Device->CreateInputLayout(elements, 6, code->GetBufferPointer(), code->GetBufferSize(), &layout);
            if (hr < 0)
            {
                mLog?.Error("Creating the scene input layout failed with " + GpuDevice.Hex(hr) + ".");
            }

            mSceneLayout = layout;
        }

        code->Release();
        mScenePixelShader = CreatePixelShader(SCENE_SHADER);
        return mSceneVertexShader != null && mScenePixelShader != null && mSceneLayout != null;
    }

    private bool CreateLineShaders()
    {
        ID3D10Blob* code = Compile(LINE_SHADER, "VSMain", "vs_4_0");
        if (code == null)
        {
            return false;
        }

        mLineVertexShader = CreateVertexShader(code);

        fixed (byte* position = "POSITION\0"u8)
        fixed (byte* color = "COLOR\0"u8)
        {
            InputElementDesc* elements = stackalloc InputElementDesc[2];
            elements[0] = new InputElementDesc
            {
                SemanticName = position,
                SemanticIndex = 0,
                Format = Format.FormatR32G32B32Float,
                InputSlot = 0,
                AlignedByteOffset = 0,
                InputSlotClass = InputClassification.PerVertexData,
                InstanceDataStepRate = 0,
            };
            elements[1] = new InputElementDesc
            {
                SemanticName = color,
                SemanticIndex = 0,
                Format = Format.FormatR8G8B8A8Unorm,
                InputSlot = 0,
                AlignedByteOffset = 12,
                InputSlotClass = InputClassification.PerVertexData,
                InstanceDataStepRate = 0,
            };

            ID3D11InputLayout* layout = null;
            int hr = mDevice.Device->CreateInputLayout(elements, 2, code->GetBufferPointer(), code->GetBufferSize(), &layout);
            if (hr < 0)
            {
                mLog?.Error("Creating the line input layout failed with " + GpuDevice.Hex(hr) + ".");
            }

            mLineLayout = layout;
        }

        code->Release();
        mLinePixelShader = CreatePixelShader(LINE_SHADER);
        return mLineVertexShader != null && mLinePixelShader != null && mLineLayout != null;
    }

    private ID3D11DepthStencilState* CreateDepthState(bool enable, bool write, ComparisonFunc func)
    {
        var face = new DepthStencilopDesc
        {
            StencilFailOp = StencilOp.Keep,
            StencilDepthFailOp = StencilOp.Keep,
            StencilPassOp = StencilOp.Keep,
            StencilFunc = ComparisonFunc.Always,
        };

        var desc = new DepthStencilDesc
        {
            DepthEnable = enable,
            DepthWriteMask = write ? DepthWriteMask.All : DepthWriteMask.Zero,
            DepthFunc = func,
            StencilEnable = false,
            StencilReadMask = 0xFF,
            StencilWriteMask = 0xFF,
            FrontFace = face,
            BackFace = face,
        };

        ID3D11DepthStencilState* state = null;
        int hr = mDevice.Device->CreateDepthStencilState(&desc, &state);
        return hr < 0 ? null : state;
    }

    private ID3D11RasterizerState* CreateRasterizer(FillMode fill, int depthBias, float slopeBias)
    {
        var desc = new RasterizerDesc
        {
            FillMode = fill,
            CullMode = CullMode.None,
            FrontCounterClockwise = false,
            DepthBias = depthBias,
            DepthBiasClamp = 0.0f,
            SlopeScaledDepthBias = slopeBias,
            DepthClipEnable = true,
            ScissorEnable = false,
            MultisampleEnable = true,
            AntialiasedLineEnable = false,
        };

        ID3D11RasterizerState* state = null;
        int hr = mDevice.Device->CreateRasterizerState(&desc, &state);
        return hr < 0 ? null : state;
    }

    private void CreateGrid()
    {
        uint minor = Rgba(66, 70, 76);
        uint major = Rgba(92, 97, 105);
        uint xAxis = Rgba(150, 64, 64);
        uint zAxis = Rgba(64, 86, 156);

        var vertices = new List<LineVertex>();
        for (int i = -GRID_EXTENT; i <= GRID_EXTENT; i++)
        {
            uint lineColor = i % GRID_MAJOR == 0 ? major : minor;
            uint alongX = i == 0 ? xAxis : lineColor;
            uint alongZ = i == 0 ? zAxis : lineColor;

            vertices.Add(new LineVertex(new Vector3(-GRID_EXTENT, 0.0f, i), alongX));
            vertices.Add(new LineVertex(new Vector3(GRID_EXTENT, 0.0f, i), alongX));
            vertices.Add(new LineVertex(new Vector3(i, 0.0f, -GRID_EXTENT), alongZ));
            vertices.Add(new LineVertex(new Vector3(i, 0.0f, GRID_EXTENT), alongZ));
        }

        LineVertex[] data = vertices.ToArray();
        fixed (LineVertex* pointer = data)
        {
            mGridBuffer = GpuBuffers.CreateImmutable(mDevice.Device, pointer, (uint)(data.Length * sizeof(LineVertex)), BindFlag.VertexBuffer);
        }

        mGridVertexCount = mGridBuffer != null ? (uint)data.Length : 0;
    }

    private static uint Rgba(byte r, byte g, byte b)
    {
        return r | ((uint)g << 8) | ((uint)b << 16) | (0xFFu << 24);
    }

    public IViewportTargets? CreateTargets(int width, int height)
    {
        return D3D11ViewportTargets.Create(mDevice, width, height, IMAGE_COUNT, mSampleCount, mLog);
    }

    public bool Render(IViewportTargets targets, int index, ViewportFrame? frame)
    {
        if (targets is not D3D11ViewportTargets d3dTargets || mDevice.Context == null)
        {
            return false;
        }

        IDXGIKeyedMutex* mutex = d3dTargets.GetMutex(index);
        int hr = mutex->AcquireSync(0, ACQUIRE_TIMEOUT_MS);
        if (hr == WAIT_TIMEOUT)
        {
            hr = mutex->AcquireSync(1, 0);
        }

        if (hr != 0)
        {
            if (!mAcquireFailureLogged)
            {
                mAcquireFailureLogged = true;
                mLog?.Warning("Acquiring shared image " + index + " failed with " + GpuDevice.Hex(hr) + ".");
            }

            return false;
        }

        ID3D11DeviceContext* context = mDevice.Context;
        ID3D11RenderTargetView* colorView = d3dTargets.ColorView;
        ID3D11DepthStencilView* depthView = d3dTargets.DepthView;

        context->OMSetRenderTargets(1, &colorView, depthView);

        var viewport = new Viewport
        {
            TopLeftX = 0.0f,
            TopLeftY = 0.0f,
            Width = targets.Width,
            Height = targets.Height,
            MinDepth = 0.0f,
            MaxDepth = 1.0f,
        };
        context->RSSetViewports(1, &viewport);

        float* clearColor = stackalloc float[] { 0.15f, 0.16f, 0.17f, 1.0f };
        context->ClearRenderTargetView(colorView, clearColor);
        context->ClearDepthStencilView(depthView, (uint)ClearFlag.Depth, 0.0f, 0);

        context->OMSetDepthStencilState(mDepthDisabled, 0);
        context->RSSetState(mSolidRaster);
        DrawBackground(context);

        if (frame != null)
        {
            bool prepared = DrawFrame(context, frame);
            if (frame.Pick != null && !frame.Pick.Completed)
            {
                RenderPick(context, d3dTargets, frame.Pick, prepared);
            }
        }

        ID3D11Resource* image = (ID3D11Resource*)d3dTargets.GetImage(index);
        ID3D11Resource* color = (ID3D11Resource*)d3dTargets.ColorTexture;
        if (d3dTargets.SampleCount > 1)
        {
            context->ResolveSubresource(image, 0, color, 0, Format.FormatR8G8B8A8Unorm);
        }
        else
        {
            context->CopyResource(image, color);
        }

        mutex->ReleaseSync(1);
        context->Flush();

        mFrameNumber++;
        if (mFrameNumber % MESH_EVICT_INTERVAL == 0)
        {
            EvictMeshes();
        }

        return true;
    }

    private void DrawBackground(ID3D11DeviceContext* context)
    {
        context->IASetInputLayout((ID3D11InputLayout*)null);
        context->IASetPrimitiveTopology(D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist);
        context->VSSetShader(mBackgroundVertexShader, (ID3D11ClassInstance**)null, 0);
        context->PSSetShader(mBackgroundPixelShader, (ID3D11ClassInstance**)null, 0);
        context->Draw(3, 0);
    }

    private bool DrawFrame(ID3D11DeviceContext* context, ViewportFrame frame)
    {
        if (!SetConstants(context, frame, 1.0f, 1.0f, 0.0f))
        {
            return false;
        }

        ID3D11Buffer* constants = mConstants;
        context->VSSetConstantBuffers(0, 1, &constants);
        context->PSSetConstantBuffers(0, 1, &constants);

        if (frame.ShowGrid && mGridBuffer != null)
        {
            DrawGrid(context);
        }

        SceneDrawList? list = frame.DrawList;
        if (list == null)
        {
            return false;
        }

        if (list.Items.Count == 0 || !PrepareInstances(context, list, frame.Selection, frame.Display))
        {
            DrawOverlays(context, frame, list);
            return false;
        }

        if (frame.Mode != RenderMode.Wireframe)
        {
            context->OMSetDepthStencilState(mDepthWrite, 0);
            context->RSSetState(mSolidRaster);
            DrawCalls(context, mDrawCalls, mSceneVertexShader, mScenePixelShader);
        }

        if (frame.Mode == RenderMode.Wireframe)
        {
            SetConstants(context, frame, 0.0f, 1.0f, 0.0f);
            context->OMSetDepthStencilState(mDepthWrite, 0);
            context->RSSetState(mWireRaster);
            DrawCalls(context, mDrawCalls, mSceneVertexShader, mScenePixelShader);
        }
        else if (frame.Mode == RenderMode.SolidWireframe)
        {
            SetConstants(context, frame, 0.0f, WIRE_OVERLAY_SCALE, 0.0f);
            context->OMSetDepthStencilState(mDepthOverlay, 0);
            context->RSSetState(mWireOverlayRaster);
            DrawCalls(context, mDrawCalls, mSceneVertexShader, mScenePixelShader);
        }

        if (mSelectedCalls.Count > 0)
        {
            SetConstants(context, frame, 0.0f, 1.0f, 1.0f);
            context->OMSetDepthStencilState(mDepthOverlay, 0);
            context->RSSetState(mWireOverlayRaster);
            DrawCalls(context, mSelectedCalls, mSceneVertexShader, mScenePixelShader);
        }

        DrawOverlays(context, frame, list);
        return true;
    }

    private static bool IsShown(OverlayMode mode, IReadOnlySet<ulong>? selection, ulong owner0, ulong owner1, ulong owner2)
    {
        if (mode == OverlayMode.All)
        {
            return true;
        }

        if (mode == OverlayMode.None || selection == null || selection.Count == 0)
        {
            return false;
        }

        return (owner0 != 0 && selection.Contains(owner0)) || (owner1 != 0 && selection.Contains(owner1)) || (owner2 != 0 && selection.Contains(owner2));
    }

    private static void AddLine(List<LineVertex> output, Vector3 a, Vector3 b, uint color)
    {
        output.Add(new LineVertex(a, color));
        output.Add(new LineVertex(b, color));
    }

    private void CollectOverlayLines(ViewportFrame frame, SceneDrawList list)
    {
        mDepthLines.Clear();
        mTopLines.Clear();

        DisplayOptions display = frame.Display ?? new DisplayOptions();
        IReadOnlySet<ulong>? selection = frame.Selection;

        foreach (OverlayLine line in list.Lines)
        {
            if (!IsShown(display.ModeOf(line.Kind), selection, line.Owner0, line.Owner1, line.Owner2))
            {
                continue;
            }

            AddLine(line.Kind == OverlayKind.DebugRender ? mDepthLines : mTopLines, line.A, line.B, line.Color);
        }

        if (display.Bounds != OverlayMode.None)
        {
            foreach (DrawItem item in list.Items)
            {
                if (!item.HasBounds || !display.IsVisible(item))
                {
                    continue;
                }

                if (display.Bounds == OverlayMode.Selected && (selection == null || !item.Matches(selection)))
                {
                    continue;
                }

                AddBox(mDepthLines, item.BoundsMin, item.BoundsMax, BoundsColor);
            }
        }

        bool axes = display.Axes != OverlayMode.None;
        bool velocities = display.Velocities != OverlayMode.None;
        if (!axes && !velocities)
        {
            return;
        }

        foreach (ActorOverlay actor in list.Actors)
        {
            if (axes && IsShown(display.Axes, selection, actor.ActorId, 0, 0))
            {
                Vector3 origin = actor.Pose.Translation;
                AddLine(mTopLines, origin, Vector3.Transform(new Vector3(AXIS_LENGTH, 0, 0), actor.Pose), OverlayBuilder.AxisX);
                AddLine(mTopLines, origin, Vector3.Transform(new Vector3(0, AXIS_LENGTH, 0), actor.Pose), OverlayBuilder.AxisY);
                AddLine(mTopLines, origin, Vector3.Transform(new Vector3(0, 0, AXIS_LENGTH), actor.Pose), OverlayBuilder.AxisZ);
            }

            if (velocities && actor.Kind != ActorKind.Static && IsShown(display.Velocities, selection, actor.ActorId, 0, 0))
            {
                Vector3 center = actor.CenterOfMass;
                if (actor.LinearVelocity != Vector3.Zero)
                {
                    AddLine(mTopLines, center, center + actor.LinearVelocity * VELOCITY_SECONDS, LinearVelocityColor);
                }

                if (actor.AngularVelocity != Vector3.Zero)
                {
                    AddLine(mTopLines, center, center + actor.AngularVelocity * VELOCITY_SECONDS, AngularVelocityColor);
                }
            }
        }
    }

    private static void AddBox(List<LineVertex> output, Vector3 min, Vector3 max, uint color)
    {
        for (int i = 0; i < 4; i++)
        {
            float y = (i & 1) != 0 ? max.Y : min.Y;
            float z = (i & 2) != 0 ? max.Z : min.Z;
            AddLine(output, new Vector3(min.X, y, z), new Vector3(max.X, y, z), color);

            float x = (i & 1) != 0 ? max.X : min.X;
            AddLine(output, new Vector3(x, min.Y, z), new Vector3(x, max.Y, z), color);

            float y2 = (i & 2) != 0 ? max.Y : min.Y;
            AddLine(output, new Vector3(x, y2, min.Z), new Vector3(x, y2, max.Z), color);
        }
    }

    private void DrawOverlays(ID3D11DeviceContext* context, ViewportFrame frame, SceneDrawList list)
    {
        CollectOverlayLines(frame, list);
        int total = mDepthLines.Count + mTopLines.Count;
        if (total == 0)
        {
            return;
        }

        if (total > mLineCapacity || mLineBuffer == null)
        {
            Release(ref mLineBuffer);
            int capacity = Math.Max(MIN_LINE_CAPACITY, mLineCapacity);
            while (capacity < total)
            {
                capacity *= 2;
            }

            mLineBuffer = GpuBuffers.CreateDynamic(mDevice.Device, (uint)(capacity * sizeof(LineVertex)), BindFlag.VertexBuffer);
            mLineCapacity = mLineBuffer != null ? capacity : 0;
            if (mLineBuffer == null)
            {
                return;
            }
        }

        MappedSubresource mapped = default;
        if (context->Map((ID3D11Resource*)mLineBuffer, 0, Map.WriteDiscard, 0, &mapped) < 0)
        {
            return;
        }

        var target = new Span<LineVertex>(mapped.PData, total);
        CollectionsMarshal.AsSpan(mDepthLines).CopyTo(target);
        CollectionsMarshal.AsSpan(mTopLines).CopyTo(target.Slice(mDepthLines.Count));
        context->Unmap((ID3D11Resource*)mLineBuffer, 0);

        context->RSSetState(mSolidRaster);
        context->IASetInputLayout(mLineLayout);
        context->IASetPrimitiveTopology(D3DPrimitiveTopology.D3DPrimitiveTopologyLinelist);
        context->VSSetShader(mLineVertexShader, (ID3D11ClassInstance**)null, 0);
        context->PSSetShader(mLinePixelShader, (ID3D11ClassInstance**)null, 0);

        ID3D11Buffer* buffer = mLineBuffer;
        uint stride = (uint)sizeof(LineVertex);
        uint offset = 0;
        context->IASetVertexBuffers(0, 1, &buffer, &stride, &offset);

        if (mDepthLines.Count > 0)
        {
            context->OMSetDepthStencilState(mDepthOverlay, 0);
            context->Draw((uint)mDepthLines.Count, 0);
        }

        if (mTopLines.Count > 0)
        {
            context->OMSetDepthStencilState(mDepthDisabled, 0);
            context->Draw((uint)mTopLines.Count, (uint)mDepthLines.Count);
        }
    }

    private void RenderPick(ID3D11DeviceContext* context, D3D11ViewportTargets targets, PickRequest pick, bool prepared)
    {
        pick.Completed = true;
        pick.ItemIndex = -1;

        if (!prepared || pick.X < 0 || pick.Y < 0 || pick.X >= targets.Width || pick.Y >= targets.Height
            || !targets.EnsurePickTargets(mDevice.Device, mLog))
        {
            return;
        }

        ID3D11RenderTargetView** views = stackalloc ID3D11RenderTargetView*[2];
        views[0] = targets.PickView;
        views[1] = targets.PickWorldView;
        context->OMSetRenderTargets(2, views, targets.PickDepthView);

        float* clear = stackalloc float[] { 0.0f, 0.0f, 0.0f, 0.0f };
        context->ClearRenderTargetView(views[0], clear);
        context->ClearRenderTargetView(views[1], clear);
        context->ClearDepthStencilView(targets.PickDepthView, (uint)ClearFlag.Depth, 0.0f, 0);
        context->OMSetDepthStencilState(mDepthWrite, 0);
        context->RSSetState(mSolidRaster);
        DrawCalls(context, mDrawCalls, mPickVertexShader, mPickPixelShader);

        var box = new Box
        {
            Left = (uint)pick.X,
            Top = (uint)pick.Y,
            Front = 0,
            Right = (uint)pick.X + 1,
            Bottom = (uint)pick.Y + 1,
            Back = 1,
        };
        context->CopySubresourceRegion((ID3D11Resource*)targets.PickStaging, 0, 0, 0, 0, (ID3D11Resource*)targets.PickTexture, 0, &box);

        context->CopySubresourceRegion((ID3D11Resource*)targets.PickWorldStaging, 0, 0, 0, 0, (ID3D11Resource*)targets.PickWorldTexture, 0, &box);

        MappedSubresource mapped = default;
        if (context->Map((ID3D11Resource*)targets.PickStaging, 0, Map.Read, 0, &mapped) >= 0)
        {
            uint id = *(uint*)mapped.PData;
            context->Unmap((ID3D11Resource*)targets.PickStaging, 0);
            pick.ItemIndex = (int)id - 1;
        }

        MappedSubresource mappedWorld = default;
        if (context->Map((ID3D11Resource*)targets.PickWorldStaging, 0, Map.Read, 0, &mappedWorld) >= 0)
        {
            Vector4 world = *(Vector4*)mappedWorld.PData;
            context->Unmap((ID3D11Resource*)targets.PickWorldStaging, 0);
            pick.HasPoint = world.W > 0.5f;
            pick.WorldPoint = new Vector3(world.X, world.Y, world.Z);
        }
    }

    private bool SetConstants(ID3D11DeviceContext* context, ViewportFrame frame, float lit, float scale, float highlight)
    {
        var constants = new SceneConstants
        {
            ViewProjection = frame.ViewProjection,
            CameraPosition = new Vector4(frame.CameraPosition, 1.0f),
            LightDirection = frame.LeftHanded ? LightDirection * new Vector4(1.0f, 1.0f, -1.0f, 0.0f) : LightDirection,
            Shading = new Vector4(lit, scale, highlight, 0.0f),
            Highlight = HighlightColor,
        };

        return GpuBuffers.Upload(context, mConstants, new ReadOnlySpan<SceneConstants>(&constants, 1));
    }

    private void DrawGrid(ID3D11DeviceContext* context)
    {
        context->OMSetDepthStencilState(mDepthWrite, 0);
        context->RSSetState(mSolidRaster);
        context->IASetInputLayout(mLineLayout);
        context->IASetPrimitiveTopology(D3DPrimitiveTopology.D3DPrimitiveTopologyLinelist);
        context->VSSetShader(mLineVertexShader, (ID3D11ClassInstance**)null, 0);
        context->PSSetShader(mLinePixelShader, (ID3D11ClassInstance**)null, 0);

        ID3D11Buffer* buffer = mGridBuffer;
        uint stride = (uint)sizeof(LineVertex);
        uint offset = 0;
        context->IASetVertexBuffers(0, 1, &buffer, &stride, &offset);
        context->Draw(mGridVertexCount, 0);
    }

    private bool PrepareInstances(ID3D11DeviceContext* context, SceneDrawList list, IReadOnlySet<ulong>? selection, DisplayOptions? display)
    {
        mInstances.Clear();
        mDrawCalls.Clear();
        mSelectedCalls.Clear();
        mBoxItems.Clear();
        mCapsuleItems.Clear();
        mPlaneItems.Clear();
        mMeshItems.Clear();

        List<DrawItem> items = list.Items;
        for (int i = 0; i < items.Count; i++)
        {
            DrawItem item = items[i];
            if (display != null && !display.IsVisible(item))
            {
                continue;
            }

            switch (item.Shape)
            {
                case DrawShape.Box:
                    mBoxItems.Add(i);
                    break;
                case DrawShape.Capsule:
                    mCapsuleItems.Add(i);
                    break;
                case DrawShape.Plane:
                    mPlaneItems.Add(i);
                    break;
                case DrawShape.Mesh:
                    if (!mMeshItems.TryGetValue(item.Key, out List<int>? group))
                    {
                        group = new List<int>();
                        mMeshItems[item.Key] = group;
                    }

                    group.Add(i);
                    break;
            }
        }

        AppendGroup(mBoxMesh, mBoxItems, items, selection);
        AppendGroup(mCapsuleMesh, mCapsuleItems, items, selection);
        AppendGroup(mPlaneMesh, mPlaneItems, items, selection);

        foreach (KeyValuePair<MeshKey, List<int>> pair in mMeshItems)
        {
            GpuMesh? mesh = GetGpuMesh(pair.Key, items[pair.Value[0]].Mesh);
            AppendGroup(mesh, pair.Value, items, selection);
        }

        if (mInstances.Count == 0)
        {
            return false;
        }

        if (mInstances.Count > mInstanceCapacity || mInstanceBuffer == null)
        {
            if (mInstanceBuffer != null)
            {
                mInstanceBuffer->Release();
                mInstanceBuffer = null;
            }

            int capacity = Math.Max(MIN_INSTANCE_CAPACITY, mInstanceCapacity);
            while (capacity < mInstances.Count)
            {
                capacity *= 2;
            }

            mInstanceBuffer = GpuBuffers.CreateDynamic(mDevice.Device, (uint)(capacity * sizeof(InstanceData)), BindFlag.VertexBuffer);
            mInstanceCapacity = mInstanceBuffer != null ? capacity : 0;
            if (mInstanceBuffer == null)
            {
                return false;
            }
        }

        return GpuBuffers.Upload<InstanceData>(context, mInstanceBuffer, CollectionsMarshal.AsSpan(mInstances));
    }

    private void AppendGroup(GpuMesh? mesh, List<int> indices, List<DrawItem> items, IReadOnlySet<ulong>? selection)
    {
        if (mesh == null || indices.Count == 0)
        {
            return;
        }

        bool anySelection = selection != null && selection.Count > 0;
        mSelectedScratch.Clear();

        uint start = (uint)mInstances.Count;
        foreach (int index in indices)
        {
            bool selected = anySelection && items[index].Matches(selection!);
            if (selected)
            {
                mSelectedScratch.Add(index);
            }

            mInstances.Add(ToInstance(items[index], index, selected));
        }

        mDrawCalls.Add(new DrawCall(mesh, start, (uint)indices.Count));

        if (mSelectedScratch.Count == 0)
        {
            return;
        }

        uint selectedStart = (uint)mInstances.Count;
        foreach (int index in mSelectedScratch)
        {
            mInstances.Add(ToInstance(items[index], index, true));
        }

        mSelectedCalls.Add(new DrawCall(mesh, selectedStart, (uint)mSelectedScratch.Count));
    }

    private static InstanceData ToInstance(DrawItem item, int index, bool selected)
    {
        Matrix4x4 m = item.World;
        Vector4 color = selected ? Vector4.Lerp(item.Color, Vector4.One, SELECTED_BRIGHTEN) : item.Color;
        float id = index + 1;

        return new InstanceData
        {
            World0 = new Vector4(m.M11, m.M21, m.M31, m.M41),
            World1 = new Vector4(m.M12, m.M22, m.M32, m.M42),
            World2 = new Vector4(m.M13, m.M23, m.M33, m.M43),
            Color = color,
            Shape = item.Shape == DrawShape.Capsule ? new Vector4(item.Radius, item.HalfHeight, 1.0f, id) : new Vector4(0.0f, 0.0f, 0.0f, id),
        };
    }

    private void DrawCalls(ID3D11DeviceContext* context, List<DrawCall> calls, ID3D11VertexShader* vertexShader, ID3D11PixelShader* pixelShader)
    {
        context->IASetInputLayout(mSceneLayout);
        context->IASetPrimitiveTopology(D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist);
        context->VSSetShader(vertexShader, (ID3D11ClassInstance**)null, 0);
        context->PSSetShader(pixelShader, (ID3D11ClassInstance**)null, 0);

        ID3D11Buffer** buffers = stackalloc ID3D11Buffer*[2];
        uint* strides = stackalloc uint[2];
        uint* offsets = stackalloc uint[2];
        strides[0] = (uint)sizeof(Vector4);
        strides[1] = (uint)sizeof(InstanceData);
        offsets[0] = 0;
        offsets[1] = 0;

        foreach (DrawCall call in calls)
        {
            buffers[0] = call.Mesh.Vertices;
            buffers[1] = mInstanceBuffer;
            context->IASetVertexBuffers(0, 2, buffers, strides, offsets);
            context->IASetIndexBuffer(call.Mesh.Indices, Format.FormatR32Uint, 0);
            context->DrawIndexedInstanced(call.Mesh.IndexCount, call.Count, 0, 0, call.Start);
        }
    }

    private GpuMesh? GetGpuMesh(MeshKey key, MeshData? data)
    {
        if (mMeshes.TryGetValue(key, out GpuMesh? mesh))
        {
            mesh.LastUsed = mFrameNumber;
            return mesh;
        }

        if (data == null)
        {
            return null;
        }

        mesh = GpuMesh.Create(mDevice.Device, PrimitiveMeshes.FromPositions(data.Positions), data.Indices);
        if (mesh == null)
        {
            mLog?.Warning("Uploading a mesh of " + data.Positions.Length + " vertices failed.");
            return null;
        }

        mesh.LastUsed = mFrameNumber;
        mMeshes[key] = mesh;
        return mesh;
    }

    private void EvictMeshes()
    {
        mEvicted.Clear();
        foreach (KeyValuePair<MeshKey, GpuMesh> pair in mMeshes)
        {
            if (mFrameNumber - pair.Value.LastUsed > MESH_EVICT_FRAMES)
            {
                mEvicted.Add(pair.Key);
            }
        }

        foreach (MeshKey key in mEvicted)
        {
            mMeshes[key].Dispose();
            mMeshes.Remove(key);
        }
    }

    private static string? LoadShaderSource(string name)
    {
        using Stream? stream = typeof(D3D11ViewportRenderer).Assembly.GetManifestResourceStream(name);
        if (stream == null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void Release<T>(ref T* pointer)
        where T : unmanaged
    {
        if (pointer != null)
        {
            ((IUnknown*)pointer)->Release();
            pointer = null;
        }
    }

    public void Dispose()
    {
        foreach (GpuMesh mesh in mMeshes.Values)
        {
            mesh.Dispose();
        }

        mMeshes.Clear();
        mBoxMesh?.Dispose();
        mCapsuleMesh?.Dispose();
        mPlaneMesh?.Dispose();
        mBoxMesh = null;
        mCapsuleMesh = null;
        mPlaneMesh = null;

        Release(ref mLineBuffer);
        mLineCapacity = 0;
        Release(ref mGridBuffer);
        Release(ref mInstanceBuffer);
        Release(ref mConstants);
        Release(ref mDepthDisabled);
        Release(ref mDepthWrite);
        Release(ref mDepthOverlay);
        Release(ref mSolidRaster);
        Release(ref mWireRaster);
        Release(ref mWireOverlayRaster);
        Release(ref mPickPixelShader);
        Release(ref mPickVertexShader);
        Release(ref mLineLayout);
        Release(ref mLinePixelShader);
        Release(ref mLineVertexShader);
        Release(ref mSceneLayout);
        Release(ref mScenePixelShader);
        Release(ref mSceneVertexShader);
        Release(ref mBackgroundPixelShader);
        Release(ref mBackgroundVertexShader);
        mInstanceCapacity = 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InstanceData
    {
        public Vector4 World0;
        public Vector4 World1;
        public Vector4 World2;
        public Vector4 Color;
        public Vector4 Shape;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SceneConstants
    {
        public Matrix4x4 ViewProjection;
        public Vector4 CameraPosition;
        public Vector4 LightDirection;
        public Vector4 Shading;
        public Vector4 Highlight;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct LineVertex
    {
        public LineVertex(Vector3 position, uint color)
        {
            Position = position;
            Color = color;
        }

        public readonly Vector3 Position;
        public readonly uint Color;
    }

    private readonly struct DrawCall
    {
        public DrawCall(GpuMesh mesh, uint start, uint count)
        {
            Mesh = mesh;
            Start = start;
            Count = count;
        }

        public GpuMesh Mesh { get; }

        public uint Start { get; }

        public uint Count { get; }
    }
}
