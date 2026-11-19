using Peekaboom.Protocol;

namespace Peekaboom.Model;

public sealed class Capture : IDisposable
{
    private const int READ_CHUNK_SIZE = 1 << 20;

    private readonly StreamFramer mFramer = new();
    private readonly List<PvdEvent> mEvents = new();
    private readonly List<CaptureSegment> mSegments = new();
    private FileStream? mWriter;
    private CaptureSegment? mCurrent;

    private Capture(string path, FileStream? writer)
    {
        FilePath = path;
        mWriter = writer;
    }

    public string FilePath { get; }

    public bool IsLive => mWriter != null;

    public IReadOnlyList<CaptureSegment> Segments => mSegments;

    public CaptureSegment? Current => mCurrent;

    public long BytesWritten { get; private set; }

    public long BytesParsed => mFramer.BytesConsumed;

    public int PendingBytes => mFramer.PendingBytes;

    public string? FatalError { get; private set; }

    public event Action<string>? Warning;

    public event Action<string>? GameError;

    public static Capture CreateLive(string path)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var writer = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16);
        return new Capture(path, writer);
    }

    public static Capture Open(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Capture not found.", path);
        }

        return new Capture(path, null);
    }

    public void Append(ReadOnlySpan<byte> data)
    {
        if (mWriter == null)
        {
            throw new InvalidOperationException("The capture is not live.");
        }

        mWriter.Write(data);
        BytesWritten += data.Length;
        Ingest(data);
    }

    public void Flush()
    {
        mWriter?.Flush();
    }

    public void CloseWriter()
    {
        mWriter?.Dispose();
        mWriter = null;
    }

    public void IngestFile(Action<long, long>? progress = null)
    {
        if (mWriter != null)
        {
            throw new InvalidOperationException("A live capture ingests what is appended.");
        }

        using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, READ_CHUNK_SIZE);
        long length = stream.Length;
        var buffer = new byte[READ_CHUNK_SIZE];
        int read;

        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            BytesWritten += read;
            Ingest(new ReadOnlySpan<byte>(buffer, 0, read));
            progress?.Invoke(BytesWritten, length);
        }
    }

    private void Ingest(ReadOnlySpan<byte> data)
    {
        if (FatalError != null)
        {
            return;
        }

        try
        {
            mFramer.Push(data);

            while (mFramer.TryRead(out FramedItem item))
            {
                if (item.Kind == FramedKind.Initialization)
                {
                    mCurrent = new CaptureSegment(this, mSegments.Count, item.Initialization!);
                    mSegments.Add(mCurrent);
                    continue;
                }

                ProcessGroup(mCurrent!, item);
            }
        }
        catch (ProtocolException exception)
        {
            FatalError = exception.Message;
            ReportWarning("Parsing stopped. " + exception.Message);
        }
    }

    private void ProcessGroup(CaptureSegment segment, FramedItem item)
    {
        segment.BeforeGroup(item.Offset);

        ProtocolStats stats = segment.Session.Stats;
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

            segment.Session.Process(evt);
        }

        if (error != null)
        {
            segment.Session.ReportDecodeError(error);
        }

        segment.EndOffset = item.Offset + LegacyFormat.GROUP_HEADER_SIZE + item.Body.Length;
    }

    public ObjectDb StateAt(CaptureSegment segment, long position, bool useKeyframes = true)
    {
        position = Math.Clamp(position, segment.StartOffset, segment.EndOffset);
        Keyframe keyframe = useKeyframes ? segment.FindKeyframe(position) : segment.Keyframes[0];
        ObjectDb db = keyframe.State.Clone();

        if (keyframe.Offset >= position)
        {
            return db;
        }

        Flush();

        var sink = new ObjectDbSink(db);
        var session = new StreamSession(sink, segment.Session);
        sink.Session = session;
        foreach (ObjectInstance instance in db.Objects)
        {
            session.SetInstance(instance.Id, instance.Class);
        }

        var framer = new StreamFramer(keyframe.Offset, false);
        var events = new List<PvdEvent>();
        var buffer = new byte[READ_CHUNK_SIZE];

        using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1);
        stream.Position = keyframe.Offset;
        long remaining = segment.EndOffset - keyframe.Offset;

        while (remaining > 0)
        {
            int read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (read <= 0)
            {
                break;
            }

            remaining -= read;
            framer.Push(new ReadOnlySpan<byte>(buffer, 0, read));

            while (framer.TryRead(out FramedItem item))
            {
                if (item.Kind != FramedKind.Group || item.Offset >= position)
                {
                    return db;
                }

                events.Clear();
                EventDecoder.DecodeGroup(item.Header, item.Body, item.Offset, events);
                foreach (PvdEvent evt in events)
                {
                    session.Process(evt);
                }
            }
        }

        return db;
    }

    internal void ReportWarning(string message)
    {
        Warning?.Invoke(message);
    }

    internal void ReportGameError(string message)
    {
        GameError?.Invoke(message);
    }

    public void Dispose()
    {
        mWriter?.Dispose();
        mWriter = null;
    }
}
