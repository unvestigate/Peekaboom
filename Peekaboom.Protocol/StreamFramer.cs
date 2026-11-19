using System.Buffers.Binary;

namespace Peekaboom.Protocol;

public enum FramedKind
{
    Initialization,
    Group,
}

public readonly struct FramedItem
{
    public FramedItem(StreamInitialization initialization)
    {
        Kind = FramedKind.Initialization;
        Initialization = initialization;
        Header = default;
        Body = Array.Empty<byte>();
        Offset = initialization.Offset;
    }

    public FramedItem(GroupHeader header, byte[] body, long offset)
    {
        Kind = FramedKind.Group;
        Initialization = null;
        Header = header;
        Body = body;
        Offset = offset;
    }

    public FramedKind Kind { get; }

    public StreamInitialization? Initialization { get; }

    public GroupHeader Header { get; }

    public byte[] Body { get; }

    public long Offset { get; }
}

public sealed class StreamFramer
{
    private const int INITIAL_CAPACITY = 1 << 16;

    private byte[] mBuffer = new byte[INITIAL_CAPACITY];
    private int mStart;
    private int mEnd;
    private long mStartOffset;
    private bool mExpectInit = true;

    public StreamFramer()
    {
    }

    public StreamFramer(long startOffset, bool expectInit)
    {
        mStartOffset = startOffset;
        mExpectInit = expectInit;
    }

    public long BytesConsumed => mStartOffset;

    public int PendingBytes => mEnd - mStart;

    public void Push(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
        {
            return;
        }

        int pending = mEnd - mStart;
        if (mBuffer.Length - mEnd < data.Length)
        {
            if (mStart > 0 && mBuffer.Length - pending >= data.Length)
            {
                Array.Copy(mBuffer, mStart, mBuffer, 0, pending);
            }
            else
            {
                Grow(pending + data.Length);
            }

            mStart = 0;
            mEnd = pending;
        }

        data.CopyTo(new Span<byte>(mBuffer, mEnd, data.Length));
        mEnd += data.Length;
    }

    public bool TryRead(out FramedItem item)
    {
        item = default;
        int available = mEnd - mStart;

        if (mExpectInit)
        {
            if (available < 4)
            {
                return false;
            }

            uint magic = ReadU32(0);
            if (magic == LegacyFormat.MAGIC_SWAPPED)
            {
                throw new ProtocolException("The stream is big-endian, which is not supported.");
            }

            if (magic != LegacyFormat.MAGIC)
            {
                throw new ProtocolException("The stream does not start with the expected magic, found 0x" + magic.ToString("X8") + ".");
            }

            return TryReadInitialization(out item);
        }

        if (available < LegacyFormat.GROUP_HEADER_SIZE)
        {
            return false;
        }

        uint dataSize = ReadU32(0);
        uint numEvents = ReadU32(4);

        if (dataSize == LegacyFormat.MAGIC && numEvents == LegacyFormat.STREAM_VERSION)
        {
            return TryReadInitialization(out item);
        }

        if (dataSize > LegacyFormat.MAX_GROUP_SIZE)
        {
            throw new ProtocolException("Group at " + mStartOffset + " claims " + dataSize + " bytes. The stream is out of sync.");
        }

        long total = LegacyFormat.GROUP_HEADER_SIZE + (long)dataSize;
        if (available < total)
        {
            if (mBuffer.Length < total)
            {
                Grow((int)total);
            }

            return false;
        }

        var header = new GroupHeader(dataSize, numEvents, ReadU64(8), ReadU64(16));
        var body = new byte[dataSize];
        Buffer.BlockCopy(mBuffer, mStart + LegacyFormat.GROUP_HEADER_SIZE, body, 0, (int)dataSize);

        item = new FramedItem(header, body, mStartOffset);
        Advance((int)total);
        return true;
    }

    private bool TryReadInitialization(out FramedItem item)
    {
        item = default;
        if (mEnd - mStart < LegacyFormat.INIT_SIZE)
        {
            return false;
        }

        var initialization = new StreamInitialization
        {
            Magic = ReadU32(0),
            Version = ReadU32(4),
            TimestampNumerator = ReadU64(8),
            TimestampDenominator = ReadU64(16),
            Flags = ReadU32(24),
            Offset = mStartOffset,
        };

        item = new FramedItem(initialization);
        mExpectInit = false;
        Advance(LegacyFormat.INIT_SIZE);
        return true;
    }

    private void Advance(int count)
    {
        mStart += count;
        mStartOffset += count;

        if (mStart == mEnd)
        {
            mStart = 0;
            mEnd = 0;
        }
    }

    private void Grow(int required)
    {
        int capacity = mBuffer.Length;
        while (capacity < required)
        {
            capacity = capacity <= int.MaxValue / 2 ? capacity * 2 : int.MaxValue;
        }

        if (capacity == mBuffer.Length)
        {
            return;
        }

        var buffer = new byte[capacity];
        int pending = mEnd - mStart;
        Buffer.BlockCopy(mBuffer, mStart, buffer, 0, pending);
        mBuffer = buffer;
        mStart = 0;
        mEnd = pending;
    }

    private uint ReadU32(int offset)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(new ReadOnlySpan<byte>(mBuffer, mStart + offset, 4));
    }

    private ulong ReadU64(int offset)
    {
        return BinaryPrimitives.ReadUInt64LittleEndian(new ReadOnlySpan<byte>(mBuffer, mStart + offset, 8));
    }
}
