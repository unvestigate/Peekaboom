namespace Peekaboom.Protocol;

public sealed class StreamParser
{
    private readonly StreamFramer mFramer = new();
    private readonly List<PvdEvent> mEvents = new();

    public StreamParser(StreamSink sink)
    {
        Session = new StreamSession(sink);
    }

    public StreamSession Session { get; }

    public long BytesConsumed => mFramer.BytesConsumed;

    public int PendingBytes => mFramer.PendingBytes;

    public event Action<PvdEvent>? EventDecoded;

    public void Feed(ReadOnlySpan<byte> data)
    {
        mFramer.Push(data);

        while (mFramer.TryRead(out FramedItem item))
        {
            if (item.Kind == FramedKind.Initialization)
            {
                Session.Begin(item.Initialization!);
                continue;
            }

            ProtocolStats stats = Session.Stats;
            stats.Groups++;
            stats.GroupBytes += LegacyFormat.GROUP_HEADER_SIZE + item.Body.Length;

            mEvents.Clear();
            string? error = EventDecoder.DecodeGroup(item.Header, item.Body, item.Offset, mEvents);

            for (int i = 0; i < mEvents.Count; i++)
            {
                PvdEvent evt = mEvents[i];
                if (i == 0)
                {
                    stats.EventBytes[(int)evt.Type] += item.Body.Length;
                }

                EventDecoded?.Invoke(evt);
                Session.Process(evt);
            }

            if (error != null)
            {
                Session.ReportDecodeError(error);
            }
        }
    }
}
