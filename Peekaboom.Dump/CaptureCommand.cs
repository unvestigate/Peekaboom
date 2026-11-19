using System.Diagnostics;
using System.Globalization;

using Peekaboom.Model;
using Peekaboom.Protocol;

namespace Peekaboom.Dump;

internal static class CaptureCommand
{
    private const int MAX_GAME_ERRORS = 20;
    private const int VERIFY_SAMPLES_PER_SCENE = 10;

    public static int Run(DumpOptions options, TextWriter output)
    {
        string path = options.CapturePath!;
        if (!File.Exists(path))
        {
            Console.Error.WriteLine("File not found. " + path);
            return 1;
        }

        using Capture capture = Capture.Open(path);
        int gameErrors = 0;
        var seenErrors = new HashSet<string>();

        capture.Warning += message =>
        {
            if (!options.Quiet)
            {
                output.WriteLine("warning. " + message);
            }
        };

        capture.GameError += message =>
        {
            gameErrors++;
            if (seenErrors.Count < MAX_GAME_ERRORS && seenErrors.Add(message))
            {
                output.WriteLine("game error " + message);
            }
        };

        Stopwatch watch = Stopwatch.StartNew();
        capture.IngestFile();
        double ingestSeconds = watch.Elapsed.TotalSeconds;

        PrintCapture(output, capture, options, ingestSeconds, gameErrors);

        if (capture.Segments.Count == 0)
        {
            output.WriteLine("No stream in the file.");
            return 1;
        }

        int segmentIndex = options.SegmentIndex >= 0 ? options.SegmentIndex : capture.Segments.Count - 1;
        if (segmentIndex >= capture.Segments.Count)
        {
            output.WriteLine("Segment " + segmentIndex + " does not exist.");
            return 1;
        }

        CaptureSegment segment = capture.Segments[segmentIndex];
        int exitCode = capture.FatalError != null ? 1 : 0;

        if (options.Verify && !Verify(output, capture, segment))
        {
            exitCode = 1;
        }

        if (options.FrameIndex != null && !PrintFrameState(output, capture, segment, options))
        {
            exitCode = 1;
        }

        return exitCode;
    }

    private static void PrintCapture(TextWriter output, Capture capture, DumpOptions options, double ingestSeconds, int gameErrors)
    {
        output.WriteLine();
        output.WriteLine("== Capture");
        output.WriteLine("Ingested " + capture.BytesParsed + " of " + capture.BytesWritten + " bytes in "
            + ingestSeconds.ToString("0.00", CultureInfo.InvariantCulture) + " s, " + capture.Segments.Count + " segments, "
            + gameErrors + " game errors.");

        if (capture.PendingBytes > 0)
        {
            output.WriteLine("Incomplete trailing group of " + capture.PendingBytes + " bytes ignored.");
        }

        if (capture.FatalError != null)
        {
            output.WriteLine("Parsing stopped early. " + capture.FatalError);
        }

        foreach (CaptureSegment segment in capture.Segments)
        {
            ProtocolStats stats = segment.Session.Stats;
            output.WriteLine("Segment " + segment.Index + " bytes " + segment.StartOffset + " to " + segment.EndOffset + ", "
                + stats.Groups + " groups, " + stats.DecodeErrors + " decode errors, " + stats.Warnings + " warnings, "
                + segment.Keyframes.Count + " keyframes holding " + segment.KeyframeSlots() + " slots, "
                + segment.Head.Count + " objects alive at the end.");

            foreach (SceneTrack scene in segment.Scenes)
            {
                output.WriteLine("  Scene " + scene.Ordinal + " " + ValueFormatter.FormatId(scene.Id) + " \"" + scene.DisplayName + "\", "
                    + scene.Frames.Count + " frames" + (scene.IsAlive ? "" : ", destroyed") + ".");

                if (options.ListFrames)
                {
                    PrintFrames(output, segment, scene);
                }
            }
        }
    }

    private static void PrintFrames(TextWriter output, CaptureSegment segment, SceneTrack scene)
    {
        StreamInitialization initialization = segment.Initialization;
        ulong firstTimestamp = scene.Frames.Count > 0 ? scene.Frames[0].BeginTimestamp : 0;

        foreach (FrameRecord frame in scene.Frames)
        {
            double time = initialization.TicksToSeconds((long)(frame.BeginTimestamp - firstTimestamp));
            output.WriteLine("    frame " + frame.Index.ToString(CultureInfo.InvariantCulture).PadLeft(6)
                + "  t " + time.ToString("0.0000", CultureInfo.InvariantCulture).PadLeft(10)
                + "  begin " + frame.BeginOffset.ToString(CultureInfo.InvariantCulture).PadLeft(10)
                + "  pre " + segment.PreOffsetOf(frame).ToString(CultureInfo.InvariantCulture).PadLeft(10)
                + "  post " + segment.PostOffsetOf(frame).ToString(CultureInfo.InvariantCulture).PadLeft(10));
        }
    }

    private static bool Verify(TextWriter output, Capture capture, CaptureSegment segment)
    {
        output.WriteLine();
        output.WriteLine("== Verify segment " + segment.Index);

        bool ok = true;
        ulong head = segment.Head.ComputeDigest();
        ulong end = capture.StateAt(segment, segment.EndOffset).ComputeDigest();
        ulong endFull = capture.StateAt(segment, segment.EndOffset, false).ComputeDigest();

        if (end != head || endFull != head)
        {
            ok = false;
            output.WriteLine("MISMATCH at the end. Head " + head.ToString("X16") + ", keyframe seek " + end.ToString("X16")
                + ", full replay " + endFull.ToString("X16") + ".");
        }
        else
        {
            output.WriteLine("End state matches the ingested head, by keyframe seek and by full replay.");
        }

        var positions = new List<long>();
        foreach (SceneTrack scene in segment.Scenes)
        {
            int count = scene.Frames.Count;
            int samples = Math.Min(count, VERIFY_SAMPLES_PER_SCENE);
            for (int i = 0; i < samples; i++)
            {
                FrameRecord frame = scene.Frames[(int)((long)i * (count - 1) / Math.Max(1, samples - 1))];
                positions.Add(segment.PreOffsetOf(frame));
                positions.Add(segment.PostOffsetOf(frame));
            }
        }

        int mismatches = 0;
        double keyframeSeconds = 0.0;
        double fullSeconds = 0.0;
        var watch = new Stopwatch();

        foreach (long position in positions)
        {
            watch.Restart();
            ulong fast = capture.StateAt(segment, position).ComputeDigest();
            keyframeSeconds += watch.Elapsed.TotalSeconds;

            watch.Restart();
            ulong slow = capture.StateAt(segment, position, false).ComputeDigest();
            fullSeconds += watch.Elapsed.TotalSeconds;

            if (fast != slow)
            {
                mismatches++;
                ok = false;
                output.WriteLine("MISMATCH at offset " + position + ". Keyframe seek " + fast.ToString("X16")
                    + ", full replay " + slow.ToString("X16") + ".");
            }
        }

        if (positions.Count > 0)
        {
            output.WriteLine("Checked " + positions.Count + " frame positions, " + mismatches + " mismatches. Average keyframe seek "
                + (keyframeSeconds * 1000.0 / positions.Count).ToString("0.0", CultureInfo.InvariantCulture) + " ms, full replay "
                + (fullSeconds * 1000.0 / positions.Count).ToString("0.0", CultureInfo.InvariantCulture) + " ms.");
        }

        return ok;
    }

    private static bool PrintFrameState(TextWriter output, Capture capture, CaptureSegment segment, DumpOptions options)
    {
        if (options.SceneOrdinal >= segment.Scenes.Count)
        {
            output.WriteLine("Scene " + options.SceneOrdinal + " does not exist in segment " + segment.Index + ".");
            return false;
        }

        SceneTrack scene = segment.Scenes[options.SceneOrdinal];
        int index = options.FrameIndex!.Value;
        if (index < 0)
        {
            index += scene.Frames.Count;
        }

        if (index < 0 || index >= scene.Frames.Count)
        {
            output.WriteLine("Frame " + options.FrameIndex + " does not exist, scene " + scene.Ordinal + " has " + scene.Frames.Count + ".");
            return false;
        }

        FrameRecord frame = scene.Frames[index];
        long position = options.PreSimulation ? segment.PreOffsetOf(frame) : segment.PostOffsetOf(frame);

        Stopwatch watch = Stopwatch.StartNew();
        ObjectDb state = capture.StateAt(segment, position);
        double seekMs = watch.Elapsed.TotalMilliseconds;

        output.WriteLine();
        output.WriteLine("== State of scene " + scene.Ordinal + " \"" + scene.DisplayName + "\" frame " + index
            + (options.PreSimulation ? " before" : " after") + " the pose updates, offset " + position + ", "
            + state.Count + " objects, seek " + seekMs.ToString("0.0", CultureInfo.InvariantCulture) + " ms.");

        foreach (ObjectInstance instance in state.SortedBySerial())
        {
            if (options.ClassFilter != null && !instance.Class.Name.ToString().Contains(options.ClassFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            PrintInstance(output, instance, options.MaxItems);
        }

        return true;
    }

    private static void PrintInstance(TextWriter output, ObjectInstance instance, int maxItems)
    {
        output.WriteLine();
        output.WriteLine(ValueFormatter.FormatId(instance.Id) + " " + instance.Class.Name + " #" + instance.Serial
            + (instance.IsTopLevel ? " top level" : ""));

        var declared = new List<PropertyDef>();
        instance.Class.CollectProperties(declared);
        var printed = new HashSet<string>();

        foreach (PropertyDef property in declared)
        {
            if (instance.TryGetSlot(property.Name, out PropertySlot slot))
            {
                output.WriteLine("    " + property.Name + " = " + ValueFormatter.Format(slot.Resolve(), property, maxItems));
                printed.Add(property.Name);
            }
            else if (instance.HasRefs(property.Name))
            {
                output.WriteLine("    " + property.Name + " = " + FormatRefs(instance.GetRefs(property.Name), maxItems));
                printed.Add(property.Name);
            }
        }

        var extras = new List<string>();
        foreach (KeyValuePair<string, PropertySlot> pair in instance.Values)
        {
            if (!printed.Contains(pair.Key))
            {
                extras.Add(pair.Key);
            }
        }

        extras.Sort(StringComparer.Ordinal);
        foreach (string name in extras)
        {
            instance.TryGetSlot(name, out PropertySlot slot);
            output.WriteLine("    " + name + " = " + ValueFormatter.Format(slot.Resolve(), null, maxItems) + "  (undeclared)");
        }

        foreach (string name in instance.RefNames)
        {
            if (!printed.Contains(name))
            {
                output.WriteLine("    " + name + " = " + FormatRefs(instance.GetRefs(name), maxItems) + "  (undeclared)");
            }
        }
    }

    private static string FormatRefs(IReadOnlyList<ulong> refs, int maxItems)
    {
        int shown = Math.Min(refs.Count, maxItems);
        var parts = new string[shown];
        for (int i = 0; i < shown; i++)
        {
            parts[i] = ValueFormatter.FormatId(refs[i]);
        }

        return "[" + refs.Count + "] {" + string.Join(", ", parts) + (refs.Count > shown ? ", ...}" : "}");
    }
}
