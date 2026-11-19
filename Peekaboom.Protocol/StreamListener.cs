using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Peekaboom.Protocol;

public enum ListenerItemKind
{
    Connected,
    Data,
    Disconnected,
    Error,
}

public sealed class ListenerItem
{
    public ListenerItem(ListenerItemKind kind, int connection, string text, byte[]? data)
    {
        Kind = kind;
        Connection = connection;
        Text = text;
        Data = data;
    }

    public ListenerItemKind Kind { get; }

    public int Connection { get; }

    public string Text { get; }

    public byte[]? Data { get; }
}

public sealed class StreamListener : IDisposable
{
    public const int DEFAULT_PORT = 5425;

    private const int RECEIVE_BUFFER_SIZE = 1 << 18;

    private readonly BlockingCollection<ListenerItem> mItems = new();
    private readonly object mLock = new();
    private TcpListener? mListener;
    private Thread? mThread;
    private Socket? mClient;
    private volatile bool mStopping;
    private int mConnection;
    private long mBytesReceived;

    public StreamListener(int port)
    {
        Port = port;
    }

    public int Port { get; }

    public long BytesReceived => Interlocked.Read(ref mBytesReceived);

    public int PendingItems => mItems.Count;

    public void Start()
    {
        mListener = new TcpListener(IPAddress.Any, Port);
        mListener.Start(1);

        mThread = new Thread(ThreadMain)
        {
            IsBackground = true,
            Name = "Stream listener",
        };
        mThread.Start();
    }

    public bool TryTake(out ListenerItem? item, int timeoutMs)
    {
        return mItems.TryTake(out item, timeoutMs);
    }

    public void Stop()
    {
        mStopping = true;

        try
        {
            mListener?.Stop();
        }
        catch (SocketException)
        {
        }

        lock (mLock)
        {
            mClient?.Close();
        }

        mThread?.Join(2000);
        mThread = null;
    }

    public void Dispose()
    {
        Stop();
        mItems.Dispose();
    }

    private void ThreadMain()
    {
        var buffer = new byte[RECEIVE_BUFFER_SIZE];

        while (!mStopping)
        {
            Socket client;
            try
            {
                client = mListener!.AcceptSocket();
            }
            catch (SocketException exception)
            {
                if (!mStopping)
                {
                    mItems.Add(new ListenerItem(ListenerItemKind.Error, mConnection, "Accept failed. " + exception.Message, null));
                }

                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            int connection = ++mConnection;
            lock (mLock)
            {
                mClient = client;
            }

            string remote = client.RemoteEndPoint?.ToString() ?? "unknown";
            mItems.Add(new ListenerItem(ListenerItemKind.Connected, connection, remote, null));

            string reason = "Connection closed.";
            try
            {
                while (true)
                {
                    int received = client.Receive(buffer);
                    if (received <= 0)
                    {
                        break;
                    }

                    var chunk = new byte[received];
                    Buffer.BlockCopy(buffer, 0, chunk, 0, received);
                    Interlocked.Add(ref mBytesReceived, received);
                    mItems.Add(new ListenerItem(ListenerItemKind.Data, connection, "", chunk));
                }
            }
            catch (SocketException exception)
            {
                reason = mStopping ? "Listener stopped." : "Connection lost. " + exception.Message;
            }
            catch (ObjectDisposedException)
            {
                reason = "Listener stopped.";
            }

            lock (mLock)
            {
                mClient = null;
            }

            client.Close();
            mItems.Add(new ListenerItem(ListenerItemKind.Disconnected, connection, reason, null));
        }
    }
}
