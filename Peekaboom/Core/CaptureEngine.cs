using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Text;

using Avalonia.Threading;

using Peekaboom.Logging;
using Peekaboom.Model;
using Peekaboom.Model.Physics;
using Peekaboom.Protocol;
using Peekaboom.Rendering;

namespace Peekaboom.Core;

public sealed class CaptureEngine : IDisposable
{
    private const int IDLE_WAIT_MS = 10;
    private const long DRAIN_BUDGET_BYTES = 8L << 20;
    private const int PUBLISH_INTERVAL_MS = 100;
    private const int MAX_MESSAGES_PER_BATCH = 200;
    private const string LIVE_PREFIX = "live_";
    private const string CAPTURE_EXTENSION = ".pxd2";

    private readonly ConcurrentQueue<Action> mCommands = new();
    private readonly AutoResetEvent mWake = new(false);
    private readonly Thread mThread;
    private readonly SceneDrawListBuilder mBuilder = new();
    private readonly Dictionary<SceneTrack, int> mPublishedFrames = new();
    private readonly List<EngineMessage> mMessages = new();
    private readonly Stopwatch mClock = Stopwatch.StartNew();

    private FrameRequest? mPendingRequest;
    private volatile bool mStopping;

    private StreamListener? mListener;
    private int mListenPort;
    private string mLiveFolder = "";
    private int mKeepLiveCaptures;

    private Capture? mCapture;
    private string mSource = "";
    private bool mConnected;
    private string mRemote = "";
    private bool mLoading;
    private int mGeneration;
    private int mDroppedMessages;

    private CaptureSegment? mPublishedSegment;
    private bool mResetPending;
    private bool mHasTimeBase;
    private ulong mTimeBase;
    private long mLastPublish;
    private string mLastStatusSignature = "";
    private string mLastTimelineSignature = "";

    public CaptureEngine()
    {
        mThread = new Thread(ThreadMain)
        {
            IsBackground = true,
            Name = "Capture engine",
        };
        mThread.Start();
    }

    public event Action<TimelineUpdate>? TimelineChanged;

    public event Action<FrameResult>? FrameReady;

    public event Action<IReadOnlyList<EngineMessage>, int>? MessagesArrived;

    public event Action<EngineStatus>? StatusChanged;

    public void StartListening(int port, string liveFolder, int keepLiveCaptures)
    {
        Post(() => DoStartListening(port, liveFolder, keepLiveCaptures));
    }

    public void OpenFile(string path)
    {
        Post(() => DoOpenFile(path));
    }

    public void RequestFrame(FrameRequest request)
    {
        Interlocked.Exchange(ref mPendingRequest, request);
        mWake.Set();
    }

    public void Dispose()
    {
        mStopping = true;
        mWake.Set();
        mThread.Join(3000);
    }

    private void Post(Action command)
    {
        mCommands.Enqueue(command);
        mWake.Set();
    }

    private static void ToUi(Action action)
    {
        Dispatcher.UIThread.Post(action);
    }

    private void ThreadMain()
    {
        while (!mStopping)
        {
            bool worked = RunCommands();
            worked |= ServeFrameRequest();
            worked |= DrainListener(worked ? 0 : IDLE_WAIT_MS);
            Publish(false);
        }

        mListener?.Dispose();
        mListener = null;
        CloseCapture();
    }

    private bool RunCommands()
    {
        bool any = false;
        while (mCommands.TryDequeue(out Action? command))
        {
            any = true;
            command();
        }

        return any;
    }

    private void AddMessage(LogSeverity severity, string text)
    {
        if (mMessages.Count < MAX_MESSAGES_PER_BATCH)
        {
            mMessages.Add(new EngineMessage(severity, text));
        }
        else
        {
            mDroppedMessages++;
        }
    }

    private void DoStartListening(int port, string liveFolder, int keepLiveCaptures)
    {
        mListener?.Dispose();
        mListener = null;
        mListenPort = port;
        mLiveFolder = liveFolder;
        mKeepLiveCaptures = Math.Max(1, keepLiveCaptures);

        var listener = new StreamListener(port);
        try
        {
            listener.Start();
            mListener = listener;
            AddMessage(LogSeverity.Info, "Listening for games on port " + port + ".");
        }
        catch (SocketException exception)
        {
            listener.Dispose();
            AddMessage(LogSeverity.Error, "Cannot listen on port " + port + ". " + exception.Message);
        }

        Publish(true);
    }

    private void DoOpenFile(string path)
    {
        if (mConnected)
        {
            AddMessage(LogSeverity.Warning, "A game is connected. Close it before opening " + Path.GetFileName(path) + ".");
            Publish(true);
            return;
        }

        mLoading = true;
        mSource = path;
        Publish(true);

        Stopwatch watch = Stopwatch.StartNew();
        try
        {
            Capture capture = Capture.Open(path);
            AttachCapture(capture, path);
            capture.IngestFile();

            int frames = 0;
            int scenes = 0;
            if (capture.Current != null)
            {
                scenes = capture.Current.Scenes.Count;
                foreach (SceneTrack scene in capture.Current.Scenes)
                {
                    frames += scene.Frames.Count;
                }
            }

            AddMessage(LogSeverity.Info, "Loaded " + Path.GetFileName(path) + ", " + scenes + " scenes, " + frames + " frames in "
                + watch.Elapsed.TotalSeconds.ToString("0.00", CultureInfo.InvariantCulture) + " s.");

            if (capture.FatalError != null)
            {
                AddMessage(LogSeverity.Error, "The stream could not be parsed to the end. " + capture.FatalError);
            }
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
        {
            AddMessage(LogSeverity.Error, "Opening " + path + " failed. " + exception.Message);
            CloseCapture();
        }

        mLoading = false;
        Publish(true);
    }

    private void AttachCapture(Capture capture, string source)
    {
        CloseCapture();
        mCapture = capture;
        mSource = source;
        capture.Warning += message => AddMessage(LogSeverity.Warning, message);
        capture.GameError += message => AddMessage(LogSeverity.Error, "Game error " + message);

        mGeneration++;
        mPublishedFrames.Clear();
        mPublishedSegment = null;
        mHasTimeBase = false;
        mResetPending = true;
        mBuilder.Clear();
    }

    private void CloseCapture()
    {
        mCapture?.Dispose();
        mCapture = null;
    }

    private bool DrainListener(int waitMs)
    {
        StreamListener? listener = mListener;
        if (listener == null)
        {
            if (waitMs > 0)
            {
                mWake.WaitOne(waitMs);
            }

            return false;
        }

        long bytes = 0;
        bool any = false;
        int timeout = waitMs;

        while (bytes < DRAIN_BUDGET_BYTES && listener.TryTake(out ListenerItem? item, timeout) && item != null)
        {
            timeout = 0;
            any = true;
            bytes += HandleListenerItem(item);
        }

        return any;
    }

    private long HandleListenerItem(ListenerItem item)
    {
        switch (item.Kind)
        {
            case ListenerItemKind.Connected:
                BeginLiveCapture(item.Text);
                return 0;

            case ListenerItemKind.Data:
                byte[] data = item.Data!;
                if (mCapture != null && mCapture.IsLive)
                {
                    mCapture.Append(data);
                }

                return data.Length;

            case ListenerItemKind.Disconnected:
                if (mCapture != null && mCapture.IsLive)
                {
                    mCapture.CloseWriter();
                }

                mConnected = false;
                AddMessage(LogSeverity.Info, "Game disconnected. " + item.Text);
                Publish(true);
                return 0;

            default:
                AddMessage(LogSeverity.Error, item.Text);
                Publish(true);
                return 0;
        }
    }

    private void BeginLiveCapture(string remote)
    {
        string path = Path.Combine(mLiveFolder, LIVE_PREFIX + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + CAPTURE_EXTENSION);
        mConnected = true;
        mRemote = remote;

        try
        {
            AttachCapture(Capture.CreateLive(path), path);
            AddMessage(LogSeverity.Info, "Game connected from " + remote + ". Recording to " + path + ".");
            PruneLiveCaptures(path);
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
        {
            AddMessage(LogSeverity.Error, "Cannot record to " + path + ". " + exception.Message);
            CloseCapture();
        }

        Publish(true);
    }

    private void PruneLiveCaptures(string current)
    {
        try
        {
            var files = new List<string>(Directory.GetFiles(mLiveFolder, LIVE_PREFIX + "*" + CAPTURE_EXTENSION));
            files.Sort(StringComparer.OrdinalIgnoreCase);

            int excess = files.Count - mKeepLiveCaptures;
            for (int i = 0; i < files.Count && excess > 0; i++)
            {
                if (string.Equals(files[i], current, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                File.Delete(files[i]);
                excess--;
            }
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
        {
            AddMessage(LogSeverity.Warning, "Removing old live captures failed. " + exception.Message);
        }
    }

    private bool ServeFrameRequest()
    {
        FrameRequest? request = Interlocked.Exchange(ref mPendingRequest, null);
        if (request == null)
        {
            return false;
        }

        FrameResult result = BuildFrame(request);
        ToUi(() => FrameReady?.Invoke(result));
        return true;
    }

    private FrameResult BuildFrame(FrameRequest request)
    {
        CaptureSegment? segment = mCapture?.Current;
        if (mCapture == null || segment == null || request.SceneOrdinal < 0 || request.SceneOrdinal >= segment.Scenes.Count)
        {
            return new FrameResult { Generation = mGeneration, SceneOrdinal = request.SceneOrdinal, FrameIndex = -1 };
        }

        SceneTrack scene = segment.Scenes[request.SceneOrdinal];
        int count = scene.Frames.Count;
        if (count == 0)
        {
            return new FrameResult { Generation = mGeneration, SceneOrdinal = request.SceneOrdinal, FrameIndex = -1 };
        }

        int index = request.Latest ? LatestFrame(scene, request.PreSimulation) : Math.Clamp(request.FrameIndex, 0, count - 1);
        FrameRecord frame = scene.Frames[index];
        long position = request.PreSimulation ? segment.PreOffsetOf(frame) : segment.PostOffsetOf(frame);

        try
        {
            ObjectDb state = mCapture.StateAt(segment, position);
            SceneSnapshot snapshot = SceneSnapshot.Build(state, scene.Id);
            SceneDrawList drawList = mBuilder.Build(state, snapshot);

            return new FrameResult
            {
                Generation = mGeneration,
                SceneOrdinal = request.SceneOrdinal,
                FrameIndex = index,
                PreSimulation = request.PreSimulation,
                State = state,
                Snapshot = snapshot,
                DrawList = drawList,
            };
        }
        catch (Exception exception) when (exception is IOException || exception is ProtocolException)
        {
            AddMessage(LogSeverity.Error, "Seeking to frame " + index + " failed. " + exception.Message);
            return new FrameResult { Generation = mGeneration, SceneOrdinal = request.SceneOrdinal, FrameIndex = -1 };
        }
    }

    private static int LatestFrame(SceneTrack scene, bool preSimulation)
    {
        for (int i = scene.Frames.Count - 1; i >= 0; i--)
        {
            FrameRecord frame = scene.Frames[i];
            if (preSimulation ? frame.PreOffset >= 0 : frame.PostOffset >= 0)
            {
                return i;
            }
        }

        return scene.Frames.Count - 1;
    }

    private void Publish(bool force)
    {
        long now = mClock.ElapsedMilliseconds;
        if (!force && !mResetPending && now - mLastPublish < PUBLISH_INTERVAL_MS)
        {
            return;
        }

        mLastPublish = now;
        PublishMessages();
        PublishStatus();
        PublishTimeline();
    }

    private void PublishMessages()
    {
        if (mMessages.Count == 0 && mDroppedMessages == 0)
        {
            return;
        }

        var batch = new List<EngineMessage>(mMessages);
        int dropped = mDroppedMessages;
        mMessages.Clear();
        mDroppedMessages = 0;
        ToUi(() => MessagesArrived?.Invoke(batch, dropped));
    }

    private void PublishStatus()
    {
        var status = new EngineStatus
        {
            Listening = mListener != null,
            Port = mListenPort,
            Connected = mConnected,
            Remote = mRemote,
            BytesReceived = mCapture != null && mCapture.IsLive ? mCapture.BytesWritten : 0,
            Source = mSource,
            Loading = mLoading,
        };

        string signature = status.Listening + "|" + status.Port + "|" + status.Connected + "|" + status.Remote + "|"
            + status.BytesReceived + "|" + status.Source + "|" + status.Loading;
        if (signature == mLastStatusSignature)
        {
            return;
        }

        mLastStatusSignature = signature;
        ToUi(() => StatusChanged?.Invoke(status));
    }

    private void PublishTimeline()
    {
        CaptureSegment? segment = mCapture?.Current;
        bool reset = mResetPending;
        mResetPending = false;

        if (segment != mPublishedSegment)
        {
            reset = true;
            mPublishedSegment = segment;
            mPublishedFrames.Clear();
            mHasTimeBase = false;
        }

        var update = new TimelineUpdate
        {
            Generation = mGeneration,
            Reset = reset,
            IsLive = mCapture != null && mCapture.IsLive,
            Source = mSource,
        };

        bool changed = reset;
        var signature = new StringBuilder();
        signature.Append(update.IsLive).Append('|').Append(mGeneration);

        if (segment != null)
        {
            StreamInitialization initialization = segment.Initialization;
            foreach (SceneTrack scene in segment.Scenes)
            {
                mPublishedFrames.TryGetValue(scene, out int sent);
                int count = scene.Frames.Count;

                if (!mHasTimeBase && count > 0)
                {
                    mTimeBase = scene.Frames[0].BeginTimestamp;
                    mHasTimeBase = true;
                }

                var times = new double[count - sent];
                for (int i = 0; i < times.Length; i++)
                {
                    long ticks = (long)scene.Frames[sent + i].BeginTimestamp - (long)mTimeBase;
                    times[i] = initialization.TicksToSeconds(ticks);
                }

                mPublishedFrames[scene] = count;
                changed |= times.Length > 0;
                signature.Append('|').Append(scene.DisplayName).Append(scene.IsAlive);
                update.Scenes.Add(new SceneUpdate
                {
                    Ordinal = scene.Ordinal,
                    Id = scene.Id,
                    Name = scene.DisplayName,
                    IsAlive = scene.IsAlive,
                    NewTimes = times,
                });
            }
        }

        string text = signature.ToString();
        if (!changed && text == mLastTimelineSignature)
        {
            return;
        }

        mLastTimelineSignature = text;
        ToUi(() => TimelineChanged?.Invoke(update));
    }
}
