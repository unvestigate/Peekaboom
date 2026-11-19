using Peekaboom.Logging;

using Silk.NET.Direct3D11;
using Silk.NET.DXGI;

namespace Peekaboom.Rendering;

public sealed unsafe class D3D11ViewportTargets : IViewportTargets
{
    private readonly ID3D11Texture2D*[] mImages;
    private readonly IDXGIKeyedMutex*[] mMutexes;
    private readonly IntPtr[] mHandles;

    private ID3D11Texture2D* mColorTexture;
    private ID3D11RenderTargetView* mColorView;
    private ID3D11Texture2D* mDepthTexture;
    private ID3D11DepthStencilView* mDepthView;

    private ID3D11Texture2D* mPickTexture;
    private ID3D11RenderTargetView* mPickView;
    private ID3D11Texture2D* mPickDepthTexture;
    private ID3D11DepthStencilView* mPickDepthView;
    private ID3D11Texture2D* mPickStaging;
    private ID3D11Texture2D* mPickWorldTexture;
    private ID3D11RenderTargetView* mPickWorldView;
    private ID3D11Texture2D* mPickWorldStaging;

    private D3D11ViewportTargets(int width, int height, int count, uint sampleCount)
    {
        Width = width;
        Height = height;
        SampleCount = sampleCount;
        mImages = new ID3D11Texture2D*[count];
        mMutexes = new IDXGIKeyedMutex*[count];
        mHandles = new IntPtr[count];
    }

    public int Width { get; }

    public int Height { get; }

    public uint SampleCount { get; }

    public IReadOnlyList<IntPtr> Handles => mHandles;

    public ID3D11Texture2D* ColorTexture => mColorTexture;

    public ID3D11RenderTargetView* ColorView => mColorView;

    public ID3D11DepthStencilView* DepthView => mDepthView;

    public ID3D11Texture2D* PickTexture => mPickTexture;

    public ID3D11RenderTargetView* PickView => mPickView;

    public ID3D11DepthStencilView* PickDepthView => mPickDepthView;

    public ID3D11Texture2D* PickStaging => mPickStaging;

    public ID3D11Texture2D* PickWorldTexture => mPickWorldTexture;

    public ID3D11RenderTargetView* PickWorldView => mPickWorldView;

    public ID3D11Texture2D* PickWorldStaging => mPickWorldStaging;

    public ID3D11Texture2D* GetImage(int index) => mImages[index];

    public bool EnsurePickTargets(ID3D11Device* device, LogViewModel? log)
    {
        if (mPickTexture != null)
        {
            return true;
        }

        var desc = new Texture2DDesc
        {
            Width = (uint)Width,
            Height = (uint)Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.FormatR32Uint,
            SampleDesc = new SampleDesc { Count = 1, Quality = 0 },
            Usage = Usage.Default,
            BindFlags = (uint)BindFlag.RenderTarget,
            CPUAccessFlags = 0,
            MiscFlags = 0,
        };

        ID3D11Texture2D* texture = null;
        ID3D11RenderTargetView* view = null;
        ID3D11Texture2D* depth = null;
        ID3D11DepthStencilView* depthView = null;
        ID3D11Texture2D* staging = null;

        int hr = device->CreateTexture2D(&desc, (SubresourceData*)null, &texture);
        if (hr >= 0)
        {
            hr = device->CreateRenderTargetView((ID3D11Resource*)texture, (RenderTargetViewDesc*)null, &view);
        }

        Texture2DDesc depthDesc = desc;
        depthDesc.Format = Format.FormatD32Float;
        depthDesc.BindFlags = (uint)BindFlag.DepthStencil;
        if (hr >= 0)
        {
            hr = device->CreateTexture2D(&depthDesc, (SubresourceData*)null, &depth);
        }

        if (hr >= 0)
        {
            hr = device->CreateDepthStencilView((ID3D11Resource*)depth, (DepthStencilViewDesc*)null, &depthView);
        }

        Texture2DDesc stagingDesc = desc;
        stagingDesc.Width = 1;
        stagingDesc.Height = 1;
        stagingDesc.Usage = Usage.Staging;
        stagingDesc.BindFlags = 0;
        stagingDesc.CPUAccessFlags = (uint)CpuAccessFlag.Read;
        if (hr >= 0)
        {
            hr = device->CreateTexture2D(&stagingDesc, (SubresourceData*)null, &staging);
        }

        ID3D11Texture2D* worldTexture = null;
        ID3D11RenderTargetView* worldView = null;
        ID3D11Texture2D* worldStaging = null;

        Texture2DDesc worldDesc = desc;
        worldDesc.Format = Format.FormatR32G32B32A32Float;
        if (hr >= 0)
        {
            hr = device->CreateTexture2D(&worldDesc, (SubresourceData*)null, &worldTexture);
        }

        if (hr >= 0)
        {
            hr = device->CreateRenderTargetView((ID3D11Resource*)worldTexture, (RenderTargetViewDesc*)null, &worldView);
        }

        Texture2DDesc worldStagingDesc = stagingDesc;
        worldStagingDesc.Format = Format.FormatR32G32B32A32Float;
        if (hr >= 0)
        {
            hr = device->CreateTexture2D(&worldStagingDesc, (SubresourceData*)null, &worldStaging);
        }

        mPickTexture = texture;
        mPickView = view;
        mPickDepthTexture = depth;
        mPickDepthView = depthView;
        mPickStaging = staging;
        mPickWorldTexture = worldTexture;
        mPickWorldView = worldView;
        mPickWorldStaging = worldStaging;

        if (hr < 0)
        {
            log?.Error("Creating the pick targets failed with " + GpuDevice.Hex(hr) + ".");
            ReleasePickTargets();
            return false;
        }

        return true;
    }

    private void ReleasePickTargets()
    {
        if (mPickWorldStaging != null)
        {
            mPickWorldStaging->Release();
            mPickWorldStaging = null;
        }

        if (mPickWorldView != null)
        {
            mPickWorldView->Release();
            mPickWorldView = null;
        }

        if (mPickWorldTexture != null)
        {
            mPickWorldTexture->Release();
            mPickWorldTexture = null;
        }

        if (mPickStaging != null)
        {
            mPickStaging->Release();
            mPickStaging = null;
        }

        if (mPickDepthView != null)
        {
            mPickDepthView->Release();
            mPickDepthView = null;
        }

        if (mPickDepthTexture != null)
        {
            mPickDepthTexture->Release();
            mPickDepthTexture = null;
        }

        if (mPickView != null)
        {
            mPickView->Release();
            mPickView = null;
        }

        if (mPickTexture != null)
        {
            mPickTexture->Release();
            mPickTexture = null;
        }
    }

    public IDXGIKeyedMutex* GetMutex(int index) => mMutexes[index];

    public static D3D11ViewportTargets? Create(GpuDevice device, int width, int height, int count, uint sampleCount, LogViewModel? log)
    {
        var targets = new D3D11ViewportTargets(width, height, count, sampleCount);
        if (!targets.Allocate(device.Device, log))
        {
            targets.Dispose();
            return null;
        }

        return targets;
    }

    private bool Allocate(ID3D11Device* device, LogViewModel? log)
    {
        var imageDesc = new Texture2DDesc
        {
            Width = (uint)Width,
            Height = (uint)Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.FormatR8G8B8A8Unorm,
            SampleDesc = new SampleDesc { Count = 1, Quality = 0 },
            Usage = Usage.Default,
            BindFlags = (uint)(BindFlag.RenderTarget | BindFlag.ShaderResource),
            CPUAccessFlags = 0,
            MiscFlags = (uint)ResourceMiscFlag.SharedKeyedmutex,
        };

        Guid mutexId = IDXGIKeyedMutex.Guid;
        Guid resourceId = IDXGIResource.Guid;

        for (int i = 0; i < mImages.Length; i++)
        {
            ID3D11Texture2D* image = null;
            int hr = device->CreateTexture2D(&imageDesc, (SubresourceData*)null, &image);
            if (hr < 0)
            {
                log?.Error("Creating shared image " + i + " (" + Width + "x" + Height + ") failed with " + GpuDevice.Hex(hr) + ".");
                return false;
            }

            mImages[i] = image;

            IDXGIKeyedMutex* mutex = null;
            hr = image->QueryInterface(&mutexId, (void**)&mutex);
            if (hr < 0)
            {
                log?.Error("Shared image " + i + " has no keyed mutex, " + GpuDevice.Hex(hr) + ".");
                return false;
            }

            mMutexes[i] = mutex;

            IDXGIResource* resource = null;
            hr = image->QueryInterface(&resourceId, (void**)&resource);
            if (hr < 0)
            {
                log?.Error("Shared image " + i + " is not a DXGI resource, " + GpuDevice.Hex(hr) + ".");
                return false;
            }

            void* handle = null;
            hr = resource->GetSharedHandle(&handle);
            resource->Release();
            if (hr < 0 || handle == null)
            {
                log?.Error("Shared image " + i + " has no shared handle, " + GpuDevice.Hex(hr) + ".");
                return false;
            }

            mHandles[i] = (IntPtr)handle;
        }

        var colorDesc = new Texture2DDesc
        {
            Width = (uint)Width,
            Height = (uint)Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.FormatR8G8B8A8Unorm,
            SampleDesc = new SampleDesc { Count = SampleCount, Quality = 0 },
            Usage = Usage.Default,
            BindFlags = (uint)BindFlag.RenderTarget,
            CPUAccessFlags = 0,
            MiscFlags = 0,
        };

        ID3D11Texture2D* colorTexture = null;
        int colorResult = device->CreateTexture2D(&colorDesc, (SubresourceData*)null, &colorTexture);
        if (colorResult < 0)
        {
            log?.Error("Creating the color target failed with " + GpuDevice.Hex(colorResult) + ".");
            return false;
        }

        mColorTexture = colorTexture;

        ID3D11RenderTargetView* colorView = null;
        colorResult = device->CreateRenderTargetView((ID3D11Resource*)colorTexture, (RenderTargetViewDesc*)null, &colorView);
        if (colorResult < 0)
        {
            log?.Error("Creating the color target view failed with " + GpuDevice.Hex(colorResult) + ".");
            return false;
        }

        mColorView = colorView;

        var depthDesc = colorDesc;
        depthDesc.Format = Format.FormatD32Float;
        depthDesc.BindFlags = (uint)BindFlag.DepthStencil;

        ID3D11Texture2D* depthTexture = null;
        int depthResult = device->CreateTexture2D(&depthDesc, (SubresourceData*)null, &depthTexture);
        if (depthResult < 0)
        {
            log?.Error("Creating the depth target failed with " + GpuDevice.Hex(depthResult) + ".");
            return false;
        }

        mDepthTexture = depthTexture;

        ID3D11DepthStencilView* depthView = null;
        depthResult = device->CreateDepthStencilView((ID3D11Resource*)depthTexture, (DepthStencilViewDesc*)null, &depthView);
        if (depthResult < 0)
        {
            log?.Error("Creating the depth target view failed with " + GpuDevice.Hex(depthResult) + ".");
            return false;
        }

        mDepthView = depthView;
        return true;
    }

    public void Dispose()
    {
        ReleasePickTargets();

        if (mDepthView != null)
        {
            mDepthView->Release();
            mDepthView = null;
        }

        if (mDepthTexture != null)
        {
            mDepthTexture->Release();
            mDepthTexture = null;
        }

        if (mColorView != null)
        {
            mColorView->Release();
            mColorView = null;
        }

        if (mColorTexture != null)
        {
            mColorTexture->Release();
            mColorTexture = null;
        }

        for (int i = 0; i < mImages.Length; i++)
        {
            if (mMutexes[i] != null)
            {
                mMutexes[i]->Release();
                mMutexes[i] = null;
            }

            if (mImages[i] != null)
            {
                mImages[i]->Release();
                mImages[i] = null;
            }

            mHandles[i] = IntPtr.Zero;
        }
    }
}
