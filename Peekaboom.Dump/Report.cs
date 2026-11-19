using System.Globalization;

using Peekaboom.Protocol;

namespace Peekaboom.Dump;

internal static class Report
{
    private const int MAX_CLASS_ROWS = 60;

    public static void PrintSummary(TextWriter output, StreamParser parser, DumpSink sink, double seconds)
    {
        StreamSession session = parser.Session;
        ProtocolStats stats = session.Stats;

        output.WriteLine();
        output.WriteLine("== Summary");
        output.WriteLine("Bytes parsed " + parser.BytesConsumed + " in " + seconds.ToString("0.00", CultureInfo.InvariantCulture)
            + " s, " + stats.Groups + " groups, " + stats.Sessions + " stream initializations, " + stats.StreamIds.Count + " sub-streams.");

        if (parser.PendingBytes > 0)
        {
            output.WriteLine("Incomplete trailing group of " + parser.PendingBytes + " bytes ignored.");
        }

        StreamInitialization? initialization = session.Initialization;
        if (initialization != null && stats.LastTimestamp > stats.FirstTimestamp)
        {
            double duration = initialization.TicksToSeconds((long)(stats.LastTimestamp - stats.FirstTimestamp));
            output.WriteLine("Stream time span " + duration.ToString("0.000", CultureInfo.InvariantCulture) + " s.");
        }

        output.WriteLine("Decode errors " + stats.DecodeErrors + ", warnings " + stats.Warnings + ", game error messages " + sink.ErrorMessages + ".");

        output.WriteLine();
        output.WriteLine("== Events");
        for (int i = 1; i < (int)EventType.Last; i++)
        {
            if (stats.EventCounts[i] == 0)
            {
                continue;
            }

            output.WriteLine("  " + ((EventType)i).ToString().PadRight(30) + stats.EventCounts[i].ToString(CultureInfo.InvariantCulture).PadLeft(12)
                + FormatBytes(stats.EventBytes[i]).PadLeft(14));
        }

        output.WriteLine();
        output.WriteLine("== Values");
        output.WriteLine("  Property values delivered " + stats.PropertyValues + ", from messages " + stats.MessageValues + ".");
        output.WriteLine("  Profile and memory blobs dropped " + stats.DroppedBlobs + ", " + FormatBytes(stats.DroppedBlobBytes) + ".");
        output.WriteLine("  Render blobs " + stats.RenderBlobs + ", " + FormatBytes(stats.RenderBlobBytes) + ".");
        output.WriteLine("  Object refs added " + sink.ObjectRefsAdded + ", removed " + sink.ObjectRefsRemoved + ".");
        output.WriteLine("  Camera updates " + sink.CameraUpdates + ", origin shifts " + sink.OriginShifts + ".");

        if (!session.HasStarted)
        {
            return;
        }

        MetadataDb metadata = session.Metadata;
        int properties = 0;
        foreach (ClassDef cls in metadata.Classes)
        {
            properties += cls.Properties.Count;
        }

        output.WriteLine();
        output.WriteLine("== Metadata");
        output.WriteLine("  Strings " + session.Strings.Count + ", classes " + metadata.Classes.Count + ", properties " + properties
            + ", property messages " + metadata.Messages.Count + ".");

        output.WriteLine();
        output.WriteLine("== Sections");
        foreach (KeyValuePair<string, int> pair in SortedByCount(sink.SectionCounts))
        {
            output.WriteLine("  \"" + pair.Key + "\" " + pair.Value);
        }

        foreach (KeyValuePair<ulong, int> pair in sink.FrameCounts)
        {
            ClassDef? cls = session.FindInstanceClass(pair.Key);
            output.WriteLine("  Frames of " + ValueFormatter.FormatId(pair.Key) + " (" + (cls?.Name.Name ?? "destroyed") + ") " + pair.Value);
        }

        output.WriteLine();
        output.WriteLine("== Instances");
        output.WriteLine("  Alive at end " + session.Instances.Count + ", top level " + sink.TopLevel.Count + ".");

        var alive = new Dictionary<string, int>();
        foreach (KeyValuePair<ulong, ClassDef> pair in session.Instances)
        {
            string name = pair.Value.Name.ToString();
            alive.TryGetValue(name, out int count);
            alive[name] = count + 1;
        }

        int rows = 0;
        foreach (KeyValuePair<string, int> pair in SortedByCount(sink.CreatedByClass))
        {
            if (++rows > MAX_CLASS_ROWS)
            {
                output.WriteLine("  ...");
                break;
            }

            sink.DestroyedByClass.TryGetValue(pair.Key, out int destroyed);
            alive.TryGetValue(pair.Key, out int living);
            output.WriteLine("  " + pair.Key.PadRight(48) + " created " + pair.Value.ToString(CultureInfo.InvariantCulture).PadLeft(7)
                + " destroyed " + destroyed.ToString(CultureInfo.InvariantCulture).PadLeft(7)
                + " alive " + living.ToString(CultureInfo.InvariantCulture).PadLeft(7));
        }
    }

    public static void PrintMetadata(TextWriter output, StreamSession session, bool includeBaseTypes)
    {
        if (!session.HasStarted)
        {
            return;
        }

        MetadataDb metadata = session.Metadata;

        output.WriteLine();
        output.WriteLine("== Classes");
        foreach (ClassDef cls in metadata.Classes)
        {
            if (cls.IsBaseType && !includeBaseTypes)
            {
                continue;
            }

            string header = "class " + cls.Name;
            if (cls.Parent != null)
            {
                header += " from " + cls.Parent.Name;
            }

            header += ", size " + cls.ByteSize + ", align " + cls.Alignment;
            if (cls.StringOffsets.Count > 0)
            {
                header += ", " + cls.StringOffsets.Count + " strings";
            }

            output.WriteLine(header);

            foreach (PropertyDef property in cls.Properties)
            {
                string line = "    " + property.Name.PadRight(44) + " " + (property.DataType.Name.Name + (property.IsArray ? "[]" : "")).PadRight(18)
                    + " @" + property.Offset;
                if (property.Semantic.Length > 0)
                {
                    line += "  " + property.Semantic;
                }

                if (property.Values.Length > 0)
                {
                    line += "  {" + JoinValues(property.Values) + "}";
                }

                output.WriteLine(line);
            }
        }

        output.WriteLine();
        output.WriteLine("== Property messages");
        foreach (PropertyMessageDef message in metadata.Messages)
        {
            output.WriteLine("message " + message.Name + " on " + message.Class.Name + ", size " + message.ByteSize);
            foreach (MessageEntry entry in message.Entries)
            {
                output.WriteLine("    " + entry.Property.Name.PadRight(44) + " " + entry.DataType.Name.Name.PadRight(18)
                    + " @" + entry.Offset + " size " + entry.ByteSize);
            }
        }
    }

    private static string JoinValues(NamedValue[] values)
    {
        var parts = new string[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            parts[i] = values[i].Name + "=" + values[i].Value;
        }

        return string.Join(", ", parts);
    }

    private static List<KeyValuePair<TKey, int>> SortedByCount<TKey>(Dictionary<TKey, int> counts)
        where TKey : notnull
    {
        var list = new List<KeyValuePair<TKey, int>>(counts);
        list.Sort((a, b) => b.Value.CompareTo(a.Value));
        return list;
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes >= 1L << 20)
        {
            return (bytes / (double)(1L << 20)).ToString("0.0", CultureInfo.InvariantCulture) + " MiB";
        }

        if (bytes >= 1L << 10)
        {
            return (bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KiB";
        }

        return bytes + " B";
    }
}
