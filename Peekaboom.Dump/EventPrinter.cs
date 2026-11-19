using System.Globalization;
using System.Numerics;

using Peekaboom.Protocol;

namespace Peekaboom.Dump;

internal sealed class EventPrinter
{
    private readonly TextWriter mOutput;
    private readonly StreamSession mSession;

    public EventPrinter(TextWriter output, StreamSession session)
    {
        mOutput = output;
        mSession = session;
    }

    public void Print(PvdEvent evt)
    {
        string prefix = evt.Offset.ToString(CultureInfo.InvariantCulture).PadLeft(10) + " s" + evt.StreamId + " " + evt.Type + " ";
        mOutput.WriteLine(prefix + Describe(evt));
    }

    private string Describe(PvdEvent evt)
    {
        StringTable strings = mSession.Strings;

        switch (evt)
        {
            case StringHandleEvent e:
                return e.Handle + " = \"" + e.Value + "\"";
            case CreateClassEvent e:
                return Name(e.Class);
            case DeriveClassEvent e:
                return Name(e.Child) + " from " + Name(e.Parent);
            case CreatePropertyEvent e:
            {
                string text = Name(e.Class) + "." + strings.Get(e.Name) + " " + Name(e.DataType) + (e.IsArray ? "[]" : "");
                string semantic = strings.Get(e.Semantic);
                if (semantic.Length > 0)
                {
                    text += " (" + semantic + ")";
                }

                if (e.Values.Length > 0)
                {
                    text += " " + e.Values.Length + " named values";
                }

                return text;
            }
            case CreatePropertyMessageEvent e:
                return Name(e.Message) + " on " + Name(e.Class) + ", " + e.Entries.Length + " entries, " + e.ByteSize + " bytes";
            case CreateInstanceEvent e:
                return Id(e.Instance) + " " + Name(e.Class);
            case DestroyInstanceEvent e:
                return Id(e.Instance) + " " + InstanceClass(e.Instance);
            case SetPropertyValueEvent e:
                return Id(e.Instance) + " " + InstanceClass(e.Instance) + " " + strings.Get(e.Property) + " as "
                    + Name(e.IncomingType) + ", " + e.NumItems + " items, " + e.Data.Length + " bytes";
            case BeginSetPropertyValueEvent e:
                return Id(e.Instance) + " " + InstanceClass(e.Instance) + " " + strings.Get(e.Property) + " as " + Name(e.IncomingType);
            case AppendPropertyValueDataEvent e:
                return e.NumItems + " items, " + e.Data.Length + " bytes";
            case SetPropertyMessageEvent e:
                return Id(e.Instance) + " " + InstanceClass(e.Instance) + " " + Name(e.Message) + ", " + e.Data.Length + " bytes";
            case BeginPropertyMessageGroupEvent e:
                return Name(e.Message);
            case SendPropertyMessageFromGroupEvent e:
                return Id(e.Instance) + " " + InstanceClass(e.Instance) + ", " + e.Data.Length + " bytes";
            case ObjectRefEvent e:
                return Id(e.Instance) + " " + InstanceClass(e.Instance) + " " + strings.Get(e.Property) + " " + Id(e.ObjectRef);
            case SectionEvent e:
                return Id(e.SectionId) + " \"" + strings.Get(e.Name) + "\" at " + e.SectionTimestamp;
            case SetIsTopLevelEvent e:
                return Id(e.Instance) + " " + e.IsTopLevel;
            case SetPickableEvent e:
                return Id(e.Instance) + " " + e.Pickable;
            case SetColorEvent e:
                return Id(e.Instance) + " 0x" + e.Rgba.ToString("X8", CultureInfo.InvariantCulture);
            case SetCameraEvent e:
                return "\"" + e.Name + "\" position " + Vec(e.Position) + " up " + Vec(e.Up) + " target " + Vec(e.Target);
            case AddProfileZoneEvent e:
                return Id(e.Instance) + " \"" + e.Name + "\"";
            case AddProfileZoneNameEvent e:
                return Id(e.Zone) + " " + e.EventId + " \"" + e.Name + "\"" + (e.CompileTimeEnabled ? "" : " disabled");
            case StreamEndEvent e:
                return "\"" + e.Name + "\"";
            case ErrorMessageEvent e:
                return e.Code + " \"" + e.Message + "\" " + e.File + " line " + e.Line;
            case OriginShiftEvent e:
                return Id(e.Scene) + " " + Vec(e.Shift);
            default:
                return "";
        }
    }

    private string Name(NameHandles handles)
    {
        return mSession.Strings.Get(handles).ToString();
    }

    private string InstanceClass(ulong instance)
    {
        ClassDef? cls = mSession.FindInstanceClass(instance);
        return cls != null ? cls.Name.Name : "<unknown>";
    }

    private static string Id(ulong id)
    {
        return ValueFormatter.FormatId(id);
    }

    private static string Vec(Vector3 value)
    {
        return "(" + ValueFormatter.FormatFloat(value.X) + ", " + ValueFormatter.FormatFloat(value.Y) + ", "
            + ValueFormatter.FormatFloat(value.Z) + ")";
    }
}
