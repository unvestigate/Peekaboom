using System.Numerics;

using Peekaboom.Protocol;

namespace Peekaboom.Dump;

internal sealed class DumpSink : StreamSink
{
    private const int MAX_ERROR_MESSAGES = 20;

    private readonly TextWriter mOutput;
    private readonly DumpOptions mOptions;
    private readonly HashSet<string> mSeenErrors = new();

    public DumpSink(TextWriter output, DumpOptions options)
    {
        mOutput = output;
        mOptions = options;
    }

    public Dictionary<ulong, int> FrameCounts { get; } = new();

    public Dictionary<string, int> SectionCounts { get; } = new();

    public Dictionary<string, int> CreatedByClass { get; } = new();

    public Dictionary<string, int> DestroyedByClass { get; } = new();

    public List<ulong> TopLevel { get; } = new();

    public long ErrorMessages { get; private set; }

    public long CameraUpdates { get; private set; }

    public long OriginShifts { get; private set; }

    public long ObjectRefsAdded { get; private set; }

    public long ObjectRefsRemoved { get; private set; }

    public override void OnStreamStart(StreamSession session, StreamInitialization initialization)
    {
        mOutput.WriteLine("Stream at offset " + initialization.Offset + ", version " + initialization.Version
            + ", " + (initialization.PointerSize * 8) + "-bit pointers, clock " + initialization.TimestampNumerator
            + "/" + initialization.TimestampDenominator + ".");
    }

    public override void OnCreateInstance(ulong instance, ClassDef cls)
    {
        Increment(CreatedByClass, cls.Name.ToString());
    }

    public override void OnDestroyInstance(ulong instance, ClassDef? cls)
    {
        Increment(DestroyedByClass, cls?.Name.ToString() ?? "<unknown>");
    }

    public override void OnSetProperty(ulong instance, ClassDef? cls, string name, PropertyDef? property, PropertyValue value)
    {
        if (mOptions.PrintValues)
        {
            mOutput.WriteLine("      " + name + " = " + ValueFormatter.Format(value, property, mOptions.MaxItems));
        }
    }

    public override void OnObjectRef(ulong instance, ClassDef? cls, string name, ulong objectRef, bool added)
    {
        if (added)
        {
            ObjectRefsAdded++;
        }
        else
        {
            ObjectRefsRemoved++;
        }
    }

    public override void OnSection(ulong section, string name, ulong timestamp, bool begin)
    {
        if (!begin)
        {
            return;
        }

        Increment(SectionCounts, name);
        if (name == LegacyFormat.FRAME_SECTION)
        {
            FrameCounts.TryGetValue(section, out int count);
            FrameCounts[section] = count + 1;
        }
    }

    public override void OnSetIsTopLevel(ulong instance, bool isTopLevel)
    {
        if (isTopLevel && !TopLevel.Contains(instance))
        {
            TopLevel.Add(instance);
        }
    }

    public override void OnSetCamera(string name, Vector3 position, Vector3 up, Vector3 target)
    {
        CameraUpdates++;
    }

    public override void OnErrorMessage(uint code, string message, string file, uint line)
    {
        ErrorMessages++;
        string text = code + ". " + message + " (" + file + " line " + line + ")";
        if (mSeenErrors.Count < MAX_ERROR_MESSAGES && mSeenErrors.Add(text))
        {
            mOutput.WriteLine("game error " + text);
        }
    }

    public override void OnOriginShift(ulong scene, Vector3 shift)
    {
        OriginShifts++;
    }

    public override void OnWarning(string message)
    {
        if (!mOptions.Quiet)
        {
            mOutput.WriteLine("warning. " + message);
        }
    }

    private static void Increment(Dictionary<string, int> counts, string key)
    {
        counts.TryGetValue(key, out int count);
        counts[key] = count + 1;
    }
}
