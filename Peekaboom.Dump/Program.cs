using System.Diagnostics;
using System.Net.Sockets;
using System.Text;

using Peekaboom.Protocol;

namespace Peekaboom.Dump;

internal static class Program
{
    private const int FILE_CHUNK_SIZE = 1 << 20;
    private const int TAKE_TIMEOUT_MS = 200;

    public static int Main(string[] args)
    {
        DumpOptions? options = DumpOptions.Parse(args, out string? error);
        if (options == null)
        {
            if (args.Length > 0 && error != null)
            {
                Console.Error.WriteLine(error);
                Console.Error.WriteLine();
            }

            DumpOptions.PrintUsage(Console.Error);
            return 2;
        }

        var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false), 1 << 16);
        try
        {
            if (options.CaptureMode)
            {
                return CaptureCommand.Run(options, output);
            }

            return options.Listen ? RunListen(options, output) : RunFile(options, output);
        }
        finally
        {
            output.Flush();
        }
    }

    private static StreamParser CreateParser(DumpOptions options, TextWriter output, out DumpSink sink)
    {
        sink = new DumpSink(output, options);
        var parser = new StreamParser(sink);

        if (options.PrintEvents)
        {
            var printer = new EventPrinter(output, parser.Session);
            parser.EventDecoded += printer.Print;
        }

        return parser;
    }

    private static void Finish(DumpOptions options, TextWriter output, StreamParser parser, DumpSink sink, Stopwatch watch)
    {
        Report.PrintSummary(output, parser, sink, watch.Elapsed.TotalSeconds);
        if (options.PrintMetadata)
        {
            Report.PrintMetadata(output, parser.Session, options.PrintBaseTypes);
        }

        output.Flush();
    }

    private static int RunFile(DumpOptions options, TextWriter output)
    {
        string path = options.CapturePath!;
        if (!File.Exists(path))
        {
            Console.Error.WriteLine("File not found. " + path);
            return 1;
        }

        StreamParser parser = CreateParser(options, output, out DumpSink sink);
        Stopwatch watch = Stopwatch.StartNew();
        var buffer = new byte[FILE_CHUNK_SIZE];
        bool failed = false;

        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, FILE_CHUNK_SIZE))
        {
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                try
                {
                    parser.Feed(new ReadOnlySpan<byte>(buffer, 0, read));
                }
                catch (ProtocolException exception)
                {
                    output.WriteLine("fatal. " + exception.Message);
                    failed = true;
                    break;
                }
            }
        }

        Finish(options, output, parser, sink, watch);
        return failed || parser.Session.Stats.DecodeErrors > 0 ? 1 : 0;
    }

    private static FileStream? OpenSaveFile(string path)
    {
        try
        {
            string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            return new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16);
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
        {
            Console.Error.WriteLine("Cannot create " + path + ". " + exception.Message);
            return null;
        }
    }

    private static int RunListen(DumpOptions options, TextWriter output)
    {
        FileStream? save = null;
        if (options.SavePath != null)
        {
            save = OpenSaveFile(options.SavePath);
            if (save == null)
            {
                return 1;
            }
        }

        using var listener = new StreamListener(options.Port);
        try
        {
            listener.Start();
        }
        catch (SocketException exception)
        {
            Console.Error.WriteLine("Cannot listen on port " + options.Port + ". " + exception.Message);
            save?.Dispose();
            return 1;
        }

        output.WriteLine("Listening on port " + options.Port + ". Press Ctrl+C to stop.");
        output.Flush();

        bool cancelled = false;
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancelled = true;
        };

        StreamParser? parser = null;
        DumpSink? sink = null;
        var watch = new Stopwatch();
        bool failed = false;
        bool done = false;
        int exitCode = 0;

        void Handle(ListenerItem item)
        {
            switch (item.Kind)
            {
                case ListenerItemKind.Connected:
                    output.WriteLine("Connection " + item.Connection + " from " + item.Text + ".");
                    parser = CreateParser(options, output, out DumpSink newSink);
                    sink = newSink;
                    failed = false;
                    watch.Restart();
                    break;

                case ListenerItemKind.Data:
                    byte[] data = item.Data!;
                    save?.Write(data, 0, data.Length);
                    if (parser != null && !failed)
                    {
                        try
                        {
                            parser.Feed(data);
                        }
                        catch (ProtocolException exception)
                        {
                            output.WriteLine("fatal. " + exception.Message + " The rest of the connection is drained without parsing.");
                            failed = true;
                            exitCode = 1;
                        }
                    }

                    break;

                case ListenerItemKind.Disconnected:
                    output.WriteLine("Connection " + item.Connection + " ended. " + item.Text);
                    if (parser != null && sink != null)
                    {
                        Finish(options, output, parser, sink, watch);
                        if (parser.Session.Stats.DecodeErrors > 0)
                        {
                            exitCode = 1;
                        }
                    }

                    parser = null;
                    sink = null;
                    if (!options.KeepListening)
                    {
                        done = true;
                    }

                    break;

                case ListenerItemKind.Error:
                    output.WriteLine(item.Text);
                    exitCode = 1;
                    done = true;
                    break;
            }
        }

        try
        {
            while (!cancelled && !done)
            {
                if (!listener.TryTake(out ListenerItem? item, TAKE_TIMEOUT_MS) || item == null)
                {
                    output.Flush();
                    continue;
                }

                Handle(item);
            }

            listener.Stop();

            while (listener.TryTake(out ListenerItem? item, 0) && item != null)
            {
                if (item.Kind == ListenerItemKind.Data || item.Kind == ListenerItemKind.Disconnected)
                {
                    Handle(item);
                }
            }

            if (parser != null && sink != null)
            {
                output.WriteLine("Stopped while connected.");
                Finish(options, output, parser, sink, watch);
            }
        }
        finally
        {
            save?.Dispose();
        }

        if (options.SavePath != null)
        {
            output.WriteLine("Saved " + listener.BytesReceived + " bytes to " + options.SavePath + ".");
        }

        return exitCode;
    }
}
