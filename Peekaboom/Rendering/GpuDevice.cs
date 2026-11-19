using System.Runtime.InteropServices;
using System.Text;

using Peekaboom.Logging;

using Silk.NET.Core.Contexts;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D.Compilers;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;

namespace Peekaboom.Rendering;

public sealed unsafe class GpuDevice : IDisposable
{
    private const uint COMPILE_ENABLE_STRICTNESS = 1u << 11;
    private const uint COMPILE_OPTIMIZATION_LEVEL3 = 1u << 15;

    private static DXGI? mDxgi;
    private static D3D11? mD3D11;
    private static D3DCompiler? mCompiler;
    private static GpuDevice? mShared;

    private ID3D11Device* mDevice;
    private ID3D11DeviceContext* mContext;

    private GpuDevice(ID3D11Device* device, ID3D11DeviceContext* context, ulong luid, string adapterName, D3DFeatureLevel featureLevel)
    {
        mDevice = device;
        mContext = context;
        Luid = luid;
        AdapterName = adapterName;
        FeatureLevel = featureLevel;
    }

    public ID3D11Device* Device => mDevice;

    public ID3D11DeviceContext* Context => mContext;

    public ulong Luid { get; }

    public string AdapterName { get; }

    public D3DFeatureLevel FeatureLevel { get; }

    public bool IsRemoved => mDevice == null || mDevice->GetDeviceRemovedReason() != 0;

    public static GpuDevice? Acquire(byte[]? deviceLuid, LogViewModel? log)
    {
        ulong luid = deviceLuid != null && deviceLuid.Length >= 8 ? BitConverter.ToUInt64(deviceLuid, 0) : 0;

        if (mShared != null && (mShared.IsRemoved || (luid != 0 && mShared.Luid != luid)))
        {
            log?.Warning("Recreating the Direct3D 11 device.");
            mShared.Dispose();
            mShared = null;
        }

        if (mShared == null)
        {
            mShared = Create(luid, log);
        }

        return mShared;
    }

    private static GpuDevice? Create(ulong luid, LogViewModel? log)
    {
        try
        {
            mDxgi ??= DXGI.GetApi((INativeWindowSource)null!);
            mD3D11 ??= D3D11.GetApi((INativeWindowSource)null!);
        }
        catch (Exception exception)
        {
            log?.Error("Loading Direct3D 11 failed. " + exception.Message);
            return null;
        }

        IDXGIFactory1* factory = null;
        Guid factoryId = IDXGIFactory1.Guid;
        int hr = mDxgi.CreateDXGIFactory1(&factoryId, (void**)&factory);
        if (hr < 0)
        {
            log?.Error("CreateDXGIFactory1 failed with " + Hex(hr) + ".");
            return null;
        }

        IDXGIAdapter1* chosen = null;
        ulong chosenLuid = 0;
        string adapterName = "default adapter";

        for (uint i = 0; ; i++)
        {
            IDXGIAdapter1* adapter = null;
            if (factory->EnumAdapters1(i, &adapter) < 0)
            {
                break;
            }

            AdapterDesc1 desc = default;
            if (chosen == null && luid != 0 && adapter->GetDesc1(&desc) >= 0)
            {
                ulong adapterLuid = desc.AdapterLuid.Low | ((ulong)(uint)desc.AdapterLuid.High << 32);
                if (adapterLuid == luid)
                {
                    chosen = adapter;
                    chosenLuid = adapterLuid;
                    adapterName = new string((char*)desc.Description);
                    continue;
                }
            }

            adapter->Release();
        }

        factory->Release();

        if (chosen == null)
        {
            log?.Warning("No adapter matches the compositor's LUID " + luid.ToString("X16") + ". Using the default adapter, sharing may fail.");
        }

        D3DFeatureLevel* levels = stackalloc D3DFeatureLevel[4];
        levels[0] = D3DFeatureLevel.Level111;
        levels[1] = D3DFeatureLevel.Level110;
        levels[2] = D3DFeatureLevel.Level101;
        levels[3] = D3DFeatureLevel.Level100;

        ID3D11Device* device = null;
        ID3D11DeviceContext* context = null;
        D3DFeatureLevel featureLevel = 0;
        D3DDriverType driverType = chosen != null ? D3DDriverType.Unknown : D3DDriverType.Hardware;

        hr = mD3D11.CreateDevice((IDXGIAdapter*)chosen, driverType, IntPtr.Zero, (uint)CreateDeviceFlag.BgraSupport,
            levels, 4, (uint)D3D11.SdkVersion, &device, &featureLevel, &context);

        if (chosen != null)
        {
            chosen->Release();
        }

        if (hr < 0)
        {
            log?.Error("D3D11CreateDevice failed with " + Hex(hr) + ".");
            return null;
        }

        log?.Info("Direct3D 11 device on " + adapterName + ", feature level " + FeatureLevelName(featureLevel) + ".");
        return new GpuDevice(device, context, chosenLuid, adapterName, featureLevel);
    }

    public ID3D10Blob* CompileShader(string source, string name, string entryPoint, string profile, LogViewModel? log)
    {
        try
        {
            mCompiler ??= D3DCompiler.GetApi();
        }
        catch (Exception exception)
        {
            log?.Error("Loading the shader compiler failed. " + exception.Message);
            return null;
        }

        byte[] sourceBytes = Encoding.UTF8.GetBytes(source);
        byte[] nameBytes = Encoding.ASCII.GetBytes(name + "\0");
        byte[] entryBytes = Encoding.ASCII.GetBytes(entryPoint + "\0");
        byte[] profileBytes = Encoding.ASCII.GetBytes(profile + "\0");

        ID3D10Blob* code = null;
        ID3D10Blob* errors = null;
        int hr;

        fixed (byte* sourcePtr = sourceBytes)
        fixed (byte* namePtr = nameBytes)
        fixed (byte* entryPtr = entryBytes)
        fixed (byte* profilePtr = profileBytes)
        {
            hr = mCompiler.Compile(sourcePtr, (nuint)sourceBytes.Length, namePtr, (D3DShaderMacro*)null, (ID3DInclude*)null,
                entryPtr, profilePtr, COMPILE_ENABLE_STRICTNESS | COMPILE_OPTIMIZATION_LEVEL3, 0, &code, &errors);
        }

        if (errors != null)
        {
            string message = (Marshal.PtrToStringAnsi((IntPtr)errors->GetBufferPointer(), (int)errors->GetBufferSize()) ?? "").TrimEnd('\0', '\r', '\n');
            errors->Release();

            if (message.Length > 0)
            {
                if (hr < 0)
                {
                    log?.Error(name + " " + entryPoint + ". " + message);
                }
                else
                {
                    log?.Warning(name + " " + entryPoint + ". " + message);
                }
            }
        }

        if (hr < 0)
        {
            if (code != null)
            {
                code->Release();
            }

            log?.Error("Compiling " + name + " " + entryPoint + " failed with " + Hex(hr) + ".");
            return null;
        }

        return code;
    }

    public void Dispose()
    {
        if (mContext != null)
        {
            mContext->ClearState();
            mContext->Flush();
            mContext->Release();
            mContext = null;
        }

        if (mDevice != null)
        {
            mDevice->Release();
            mDevice = null;
        }
    }

    public static string Hex(int hr)
    {
        return "0x" + ((uint)hr).ToString("X8");
    }

    private static string FeatureLevelName(D3DFeatureLevel level)
    {
        int value = (int)level;
        return ((value >> 12) & 0xF) + "." + ((value >> 8) & 0xF);
    }
}
