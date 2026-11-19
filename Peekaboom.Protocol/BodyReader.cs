using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace Peekaboom.Protocol;

internal ref struct BodyReader
{
    private readonly byte[] mData;
    private readonly int mEnd;
    private int mPosition;

    public BodyReader(byte[] data, int start, int end)
    {
        mData = data;
        mPosition = start;
        mEnd = end;
    }

    public int Position => mPosition;

    public int Remaining => mEnd - mPosition;

    private ReadOnlySpan<byte> Take(int count)
    {
        if (count < 0 || count > mEnd - mPosition)
        {
            throw new ProtocolException("Event body overrun, wanted " + count + " bytes with " + (mEnd - mPosition) + " left.");
        }

        ReadOnlySpan<byte> span = new ReadOnlySpan<byte>(mData, mPosition, count);
        mPosition += count;
        return span;
    }

    public byte ReadU8() => Take(1)[0];

    public bool ReadBool() => Take(1)[0] != 0;

    public ushort ReadU16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));

    public uint ReadU32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));

    public ulong ReadU64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));

    public float ReadF32() => BinaryPrimitives.ReadSingleLittleEndian(Take(4));

    public NameHandles ReadNames()
    {
        uint ns = ReadU32();
        uint name = ReadU32();
        return new NameHandles(ns, name);
    }

    public Vector3 ReadVec3()
    {
        float x = ReadF32();
        float y = ReadF32();
        float z = ReadF32();
        return new Vector3(x, y, z);
    }

    public string ReadWireString()
    {
        uint length = ReadU32();
        if (length == 0)
        {
            return "";
        }

        if (length > (uint)Remaining)
        {
            throw new ProtocolException("String length " + length + " exceeds the event body.");
        }

        ReadOnlySpan<byte> bytes = Take((int)length);
        int count = bytes.IndexOf((byte)0);
        if (count < 0)
        {
            count = bytes.Length;
        }

        return Encoding.UTF8.GetString(bytes.Slice(0, count));
    }

    public ReadOnlyMemory<byte> ReadBlob()
    {
        uint length = ReadU32();
        if (length > (uint)Remaining)
        {
            throw new ProtocolException("Blob length " + length + " exceeds the event body.");
        }

        var blob = new ReadOnlyMemory<byte>(mData, mPosition, (int)length);
        mPosition += (int)length;
        return blob;
    }
}
