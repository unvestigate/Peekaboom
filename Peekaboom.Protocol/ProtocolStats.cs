namespace Peekaboom.Protocol;

public sealed class ProtocolStats
{
    public long[] EventCounts { get; } = new long[(int)EventType.Last];

    public long[] EventBytes { get; } = new long[(int)EventType.Last];

    public long Groups { get; set; }

    public long GroupBytes { get; set; }

    public long Sessions { get; set; }

    public long DecodeErrors { get; set; }

    public long Warnings { get; set; }

    public long DroppedBlobs { get; set; }

    public long DroppedBlobBytes { get; set; }

    public long RenderBlobs { get; set; }

    public long RenderBlobBytes { get; set; }

    public long PropertyValues { get; set; }

    public long MessageValues { get; set; }

    public HashSet<ulong> StreamIds { get; } = new();

    public ulong FirstTimestamp { get; set; }

    public ulong LastTimestamp { get; set; }
}
