using System.Numerics;

using Silk.NET.Direct3D11;

namespace Peekaboom.Rendering;

internal sealed unsafe class GpuMesh : IDisposable
{
    private ID3D11Buffer* mVertices;
    private ID3D11Buffer* mIndices;

    private GpuMesh(ID3D11Buffer* vertices, ID3D11Buffer* indices, uint indexCount)
    {
        mVertices = vertices;
        mIndices = indices;
        IndexCount = indexCount;
    }

    public ID3D11Buffer* Vertices => mVertices;

    public ID3D11Buffer* Indices => mIndices;

    public uint IndexCount { get; }

    public long LastUsed { get; set; }

    public static GpuMesh? Create(ID3D11Device* device, Vector4[] vertices, uint[] indices)
    {
        if (vertices.Length == 0 || indices.Length == 0)
        {
            return null;
        }

        ID3D11Buffer* vertexBuffer;
        ID3D11Buffer* indexBuffer;

        fixed (Vector4* vertexData = vertices)
        {
            vertexBuffer = GpuBuffers.CreateImmutable(device, vertexData, (uint)(vertices.Length * sizeof(Vector4)), BindFlag.VertexBuffer);
        }

        if (vertexBuffer == null)
        {
            return null;
        }

        fixed (uint* indexData = indices)
        {
            indexBuffer = GpuBuffers.CreateImmutable(device, indexData, (uint)(indices.Length * sizeof(uint)), BindFlag.IndexBuffer);
        }

        if (indexBuffer == null)
        {
            vertexBuffer->Release();
            return null;
        }

        return new GpuMesh(vertexBuffer, indexBuffer, (uint)indices.Length);
    }

    public void Dispose()
    {
        if (mVertices != null)
        {
            mVertices->Release();
            mVertices = null;
        }

        if (mIndices != null)
        {
            mIndices->Release();
            mIndices = null;
        }
    }
}

internal static unsafe class GpuBuffers
{
    public static ID3D11Buffer* CreateImmutable(ID3D11Device* device, void* data, uint byteWidth, BindFlag bind)
    {
        var desc = new BufferDesc
        {
            ByteWidth = byteWidth,
            Usage = Usage.Immutable,
            BindFlags = (uint)bind,
            CPUAccessFlags = 0,
            MiscFlags = 0,
            StructureByteStride = 0,
        };

        var initial = new SubresourceData { PSysMem = data, SysMemPitch = 0, SysMemSlicePitch = 0 };
        ID3D11Buffer* buffer = null;
        int hr = device->CreateBuffer(&desc, &initial, &buffer);
        return hr < 0 ? null : buffer;
    }

    public static ID3D11Buffer* CreateDynamic(ID3D11Device* device, uint byteWidth, BindFlag bind)
    {
        var desc = new BufferDesc
        {
            ByteWidth = byteWidth,
            Usage = Usage.Dynamic,
            BindFlags = (uint)bind,
            CPUAccessFlags = (uint)CpuAccessFlag.Write,
            MiscFlags = 0,
            StructureByteStride = 0,
        };

        ID3D11Buffer* buffer = null;
        int hr = device->CreateBuffer(&desc, (SubresourceData*)null, &buffer);
        return hr < 0 ? null : buffer;
    }

    public static bool Upload<T>(ID3D11DeviceContext* context, ID3D11Buffer* buffer, ReadOnlySpan<T> data)
        where T : unmanaged
    {
        MappedSubresource mapped = default;
        int hr = context->Map((ID3D11Resource*)buffer, 0, Map.WriteDiscard, 0, &mapped);
        if (hr < 0)
        {
            return false;
        }

        data.CopyTo(new Span<T>(mapped.PData, data.Length));
        context->Unmap((ID3D11Resource*)buffer, 0);
        return true;
    }
}
