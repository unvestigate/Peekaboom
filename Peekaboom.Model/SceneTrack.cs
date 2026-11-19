namespace Peekaboom.Model;

public sealed class FrameRecord
{
    public FrameRecord(int index, long beginOffset, ulong beginTimestamp)
    {
        Index = index;
        BeginOffset = beginOffset;
        BeginTimestamp = beginTimestamp;
    }

    public int Index { get; }

    public long BeginOffset { get; }

    public ulong BeginTimestamp { get; }

    public long PreOffset { get; internal set; } = -1;

    public long PostOffset { get; internal set; } = -1;

    public ulong EndTimestamp { get; internal set; }
}

public sealed class SceneTrack
{
    private readonly List<FrameRecord> mFrames = new();

    public SceneTrack(ulong id, long serial, int ordinal)
    {
        Id = id;
        Serial = serial;
        Ordinal = ordinal;
    }

    public ulong Id { get; }

    public long Serial { get; }

    public int Ordinal { get; }

    public string Name { get; internal set; } = "";

    public bool IsAlive { get; internal set; } = true;

    public string DisplayName => Name.Length > 0 ? Name : "Scene " + (Ordinal + 1);

    public IReadOnlyList<FrameRecord> Frames => mFrames;

    internal FrameRecord? OpenFrame => mFrames.Count > 0 && mFrames[mFrames.Count - 1].PostOffset < 0 ? mFrames[mFrames.Count - 1] : null;

    internal FrameRecord BeginFrame(long offset, ulong timestamp)
    {
        CloseFrame(offset);
        var frame = new FrameRecord(mFrames.Count, offset, timestamp);
        mFrames.Add(frame);
        return frame;
    }

    internal void EndFrame(long offset, ulong timestamp)
    {
        FrameRecord? frame = OpenFrame;
        if (frame != null && frame.PreOffset < 0)
        {
            frame.PreOffset = offset + 1;
            frame.EndTimestamp = timestamp;
        }
    }

    internal void CloseFrame(long offset)
    {
        FrameRecord? frame = OpenFrame;
        if (frame == null)
        {
            return;
        }

        if (frame.PreOffset < 0)
        {
            frame.PreOffset = offset;
        }

        frame.PostOffset = offset;
    }
}

public sealed class Keyframe
{
    public Keyframe(long offset, ObjectDb state)
    {
        Offset = offset;
        State = state;
    }

    public long Offset { get; }

    public ObjectDb State { get; }
}
