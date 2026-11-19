using System.Globalization;

using Peekaboom.Protocol;

namespace Peekaboom.Dump;

internal sealed class DumpOptions
{
    public string? CapturePath { get; private set; }

    public bool Listen { get; private set; }

    public int Port { get; private set; } = StreamListener.DEFAULT_PORT;

    public string? SavePath { get; private set; }

    public bool KeepListening { get; private set; }

    public bool PrintEvents { get; private set; }

    public bool PrintValues { get; private set; }

    public bool PrintMetadata { get; private set; }

    public bool PrintBaseTypes { get; private set; }

    public bool Quiet { get; private set; }

    public int MaxItems { get; private set; } = 4;

    public int? FrameIndex { get; private set; }

    public int SceneOrdinal { get; private set; }

    public int SegmentIndex { get; private set; } = -1;

    public bool PreSimulation { get; private set; }

    public bool Verify { get; private set; }

    public bool ListFrames { get; private set; }

    public string? ClassFilter { get; private set; }

    public bool CaptureMode => FrameIndex != null || Verify || ListFrames;

    public static DumpOptions? Parse(string[] args, out string? error)
    {
        var options = new DumpOptions();
        error = null;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "listen":
                    options.Listen = true;
                    break;
                case "--port":
                    if (!TryInt(args, ref i, out int port) || port <= 0 || port > 65535)
                    {
                        error = "--port needs a port number.";
                        return null;
                    }

                    options.Port = port;
                    break;
                case "--save":
                    if (i + 1 >= args.Length)
                    {
                        error = "--save needs a file name.";
                        return null;
                    }

                    options.SavePath = args[++i];
                    break;
                case "--keep-listening":
                    options.KeepListening = true;
                    break;
                case "--events":
                    options.PrintEvents = true;
                    break;
                case "--values":
                    options.PrintEvents = true;
                    options.PrintValues = true;
                    break;
                case "--metadata":
                    options.PrintMetadata = true;
                    break;
                case "--base-types":
                    options.PrintMetadata = true;
                    options.PrintBaseTypes = true;
                    break;
                case "--quiet":
                    options.Quiet = true;
                    break;
                case "--max-items":
                    if (!TryInt(args, ref i, out int maxItems) || maxItems < 0)
                    {
                        error = "--max-items needs a count.";
                        return null;
                    }

                    options.MaxItems = maxItems;
                    break;
                case "--frame":
                    if (!TryInt(args, ref i, out int frame))
                    {
                        error = "--frame needs a frame number, negative counts from the end.";
                        return null;
                    }

                    options.FrameIndex = frame;
                    break;
                case "--scene":
                    if (!TryInt(args, ref i, out int scene) || scene < 0)
                    {
                        error = "--scene needs a scene number.";
                        return null;
                    }

                    options.SceneOrdinal = scene;
                    break;
                case "--segment":
                    if (!TryInt(args, ref i, out int segment) || segment < 0)
                    {
                        error = "--segment needs a segment number.";
                        return null;
                    }

                    options.SegmentIndex = segment;
                    break;
                case "--pre":
                    options.PreSimulation = true;
                    break;
                case "--verify":
                    options.Verify = true;
                    break;
                case "--frames":
                    options.ListFrames = true;
                    break;
                case "--class":
                    if (i + 1 >= args.Length)
                    {
                        error = "--class needs a class name.";
                        return null;
                    }

                    options.ClassFilter = args[++i];
                    break;
                default:
                    if (arg.StartsWith("--", StringComparison.Ordinal) || options.CapturePath != null)
                    {
                        error = "Unexpected argument " + arg + ".";
                        return null;
                    }

                    options.CapturePath = arg;
                    break;
            }
        }

        if (options.Listen == (options.CapturePath != null))
        {
            error = options.Listen ? "Give either listen or a capture file, not both." : "Give a capture file or listen.";
            return null;
        }

        if (options.SavePath != null && !options.Listen)
        {
            error = "--save only applies to listen.";
            return null;
        }

        if (options.CaptureMode && options.Listen)
        {
            error = "--frame, --frames and --verify read a capture file. Save one with listen --save first.";
            return null;
        }

        if (options.CaptureMode && options.PrintEvents)
        {
            error = "--events and --values do not combine with --frame, --frames or --verify.";
            return null;
        }

        return options;
    }

    private static bool TryInt(string[] args, ref int index, out int value)
    {
        value = 0;
        if (index + 1 >= args.Length)
        {
            return false;
        }

        index++;
        return int.TryParse(args[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    public static void PrintUsage(TextWriter output)
    {
        output.WriteLine("Usage");
        output.WriteLine("  Peekaboom.Dump <capture file> [options]");
        output.WriteLine("  Peekaboom.Dump listen [--port N] [--save file] [--keep-listening] [options]");
        output.WriteLine();
        output.WriteLine("Options");
        output.WriteLine("  --events        Print every event.");
        output.WriteLine("  --values        Print every event and the decoded property values.");
        output.WriteLine("  --metadata      Print classes and property messages at the end.");
        output.WriteLine("  --base-types    Include the built-in base types in the metadata.");
        output.WriteLine("  --max-items N   Array items shown per value, default 4.");
        output.WriteLine("  --quiet         Do not print warnings.");
        output.WriteLine();
        output.WriteLine("Capture options, file only");
        output.WriteLine("  --frames        List the frames of every scene.");
        output.WriteLine("  --frame N       Print the state after frame N of the scene, negative counts from the end.");
        output.WriteLine("  --scene N       Scene for --frame in creation order, default 0.");
        output.WriteLine("  --segment N     Stream segment, default the last.");
        output.WriteLine("  --pre           State at the end of the frame section instead, before the pose updates.");
        output.WriteLine("  --class TEXT    Only print objects whose class name contains TEXT.");
        output.WriteLine("  --verify        Check keyframe seeks against full replays.");
        output.WriteLine();
        output.WriteLine("Listen accepts one game on port 5425 by default and stops when it disconnects.");
        output.WriteLine("--save writes the raw stream as received, which this tool reads back as a capture file.");
    }
}
