using Peekaboom.Protocol;

namespace Peekaboom.Model;

public sealed class CaptureSegment
{
    private const int KEYFRAME_FRAMES = 60;
    private const long KEYFRAME_BYTES = 8L << 20;

    private readonly List<SceneTrack> mScenes = new();
    private readonly Dictionary<ulong, SceneTrack> mLiveScenes = new();
    private readonly List<Keyframe> mKeyframes = new();
    private int mFramesSinceKeyframe;
    private long mLastKeyframeOffset;

    internal CaptureSegment(Capture capture, int index, StreamInitialization initialization)
    {
        Index = index;
        Initialization = initialization;
        StartOffset = initialization.Offset + LegacyFormat.INIT_SIZE;
        EndOffset = StartOffset;
        Head = new ObjectDb();

        var sink = new IngestSink(capture, this, Head);
        Session = new StreamSession(sink);
        sink.Session = Session;
        Session.Begin(initialization);

        mKeyframes.Add(new Keyframe(StartOffset, new ObjectDb()));
        mLastKeyframeOffset = StartOffset;
    }

    public int Index { get; }

    public StreamInitialization Initialization { get; }

    public StreamSession Session { get; }

    public ObjectDb Head { get; }

    public long StartOffset { get; }

    public long EndOffset { get; internal set; }

    public IReadOnlyList<SceneTrack> Scenes => mScenes;

    public IReadOnlyList<Keyframe> Keyframes => mKeyframes;

    public long PreOffsetOf(FrameRecord frame)
    {
        return frame.PreOffset >= 0 ? frame.PreOffset : EndOffset;
    }

    public long PostOffsetOf(FrameRecord frame)
    {
        return frame.PostOffset >= 0 ? frame.PostOffset : EndOffset;
    }

    public Keyframe FindKeyframe(long position)
    {
        int low = 0;
        int high = mKeyframes.Count - 1;

        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            if (mKeyframes[middle].Offset <= position)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return mKeyframes[low];
    }

    public long KeyframeSlots()
    {
        long total = 0;
        foreach (Keyframe keyframe in mKeyframes)
        {
            total += keyframe.State.CountSlots();
        }

        return total;
    }

    internal void BeforeGroup(long offset)
    {
        if (offset == mLastKeyframeOffset)
        {
            return;
        }

        bool due = mFramesSinceKeyframe >= KEYFRAME_FRAMES || offset - mLastKeyframeOffset >= KEYFRAME_BYTES;
        if (!due || !Session.IsQuiescent)
        {
            return;
        }

        mKeyframes.Add(new Keyframe(offset, Head.Clone()));
        mLastKeyframeOffset = offset;
        mFramesSinceKeyframe = 0;
    }

    private SceneTrack GetOrCreateScene(ulong id, long serial)
    {
        if (!mLiveScenes.TryGetValue(id, out SceneTrack? track))
        {
            track = new SceneTrack(id, serial, mScenes.Count);
            mScenes.Add(track);
            mLiveScenes[id] = track;
        }

        return track;
    }

    private void OnSceneCreated(ulong id, long serial)
    {
        if (mLiveScenes.TryGetValue(id, out SceneTrack? previous))
        {
            previous.IsAlive = false;
            mLiveScenes.Remove(id);
        }

        GetOrCreateScene(id, serial);
    }

    private void OnSceneDestroyed(ulong id, long offset)
    {
        if (mLiveScenes.TryGetValue(id, out SceneTrack? track))
        {
            track.CloseFrame(offset);
            track.IsAlive = false;
            mLiveScenes.Remove(id);
        }
    }

    private void OnSceneName(ulong id, string name)
    {
        if (mLiveScenes.TryGetValue(id, out SceneTrack? track))
        {
            track.Name = name;
        }
    }

    private void OnFrameSection(ulong id, bool begin, long offset, ulong timestamp)
    {
        SceneTrack track = GetOrCreateScene(id, -1);
        if (begin)
        {
            track.BeginFrame(offset, timestamp);
            mFramesSinceKeyframe++;
        }
        else
        {
            track.EndFrame(offset, timestamp);
        }
    }

    private sealed class IngestSink : ObjectDbSink
    {
        private readonly Capture mCapture;
        private readonly CaptureSegment mSegment;

        public IngestSink(Capture capture, CaptureSegment segment, ObjectDb db)
            : base(db)
        {
            mCapture = capture;
            mSegment = segment;
        }

        public override void OnCreateInstance(ulong instance, ClassDef cls)
        {
            base.OnCreateInstance(instance, cls);
            if (LegacyFormat.IsScene(cls))
            {
                mSegment.OnSceneCreated(instance, Db.Find(instance)!.Serial);
            }
        }

        public override void OnDestroyInstance(ulong instance, ClassDef? cls)
        {
            if (LegacyFormat.IsScene(cls))
            {
                mSegment.OnSceneDestroyed(instance, CurrentOffset);
            }

            base.OnDestroyInstance(instance, cls);
        }

        public override void OnSetProperty(ulong instance, ClassDef? cls, string name, PropertyDef? property, PropertyValue value)
        {
            base.OnSetProperty(instance, cls, name, property, value);
            if (name == LegacyFormat.NAME_PROPERTY && LegacyFormat.IsScene(cls) && value.Type.IsString && value.Count > 0)
            {
                mSegment.OnSceneName(instance, value.GetString(0, 0));
            }
        }

        public override void OnSection(ulong section, string name, ulong timestamp, bool begin)
        {
            base.OnSection(section, name, timestamp, begin);
            if (name == LegacyFormat.FRAME_SECTION)
            {
                mSegment.OnFrameSection(section, begin, CurrentOffset, timestamp);
            }
        }

        public override void OnErrorMessage(uint code, string message, string file, uint line)
        {
            mCapture.ReportGameError(code + ". " + message + " (" + file + " line " + line + ")");
        }

        public override void OnWarning(string message)
        {
            mCapture.ReportWarning(message);
        }
    }
}
