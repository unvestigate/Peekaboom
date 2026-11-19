using System.Numerics;

namespace Peekaboom.Protocol;

public readonly record struct NameHandles(uint Namespace, uint Name);

public readonly record struct NameHandleValue(uint Name, uint Value);

public readonly record struct MessageArg(uint Property, NameHandles DataType, uint Offset, uint ByteSize);

public sealed class StreamInitialization
{
    public uint Magic { get; init; }

    public uint Version { get; init; }

    public ulong TimestampNumerator { get; init; }

    public ulong TimestampDenominator { get; init; }

    public uint Flags { get; init; }

    public long Offset { get; init; }

    public int PointerSize => (Flags & LegacyFormat.FLAG_64BIT_POINTERS) != 0 ? 8 : 4;

    public double TicksToSeconds(long ticks)
    {
        if (TimestampDenominator == 0)
        {
            return 0.0;
        }

        return ticks * ((double)TimestampNumerator / TimestampDenominator) * 1e-9;
    }
}

public readonly record struct GroupHeader(uint DataSize, uint NumEvents, ulong StreamId, ulong Timestamp);

public abstract class PvdEvent
{
    public abstract EventType Type { get; }

    public ulong StreamId { get; set; }

    public ulong Timestamp { get; set; }

    public long Offset { get; set; }
}

public sealed class StringHandleEvent : PvdEvent
{
    public override EventType Type => EventType.StringHandle;

    public string Value { get; init; } = "";

    public uint Handle { get; init; }
}

public sealed class CreateClassEvent : PvdEvent
{
    public override EventType Type => EventType.CreateClass;

    public NameHandles Class { get; init; }
}

public sealed class DeriveClassEvent : PvdEvent
{
    public override EventType Type => EventType.DeriveClass;

    public NameHandles Parent { get; init; }

    public NameHandles Child { get; init; }
}

public sealed class CreatePropertyEvent : PvdEvent
{
    public override EventType Type => EventType.CreateProperty;

    public NameHandles Class { get; init; }

    public uint Name { get; init; }

    public uint Semantic { get; init; }

    public NameHandles DataType { get; init; }

    public byte PropertyType { get; init; }

    public NameHandleValue[] Values { get; init; } = Array.Empty<NameHandleValue>();

    public bool IsArray => PropertyType == 2;
}

public sealed class CreatePropertyMessageEvent : PvdEvent
{
    public override EventType Type => EventType.CreatePropertyMessage;

    public NameHandles Class { get; init; }

    public NameHandles Message { get; init; }

    public MessageArg[] Entries { get; init; } = Array.Empty<MessageArg>();

    public uint ByteSize { get; init; }
}

public sealed class CreateInstanceEvent : PvdEvent
{
    public override EventType Type => EventType.CreateInstance;

    public NameHandles Class { get; init; }

    public ulong Instance { get; init; }
}

public sealed class SetPropertyValueEvent : PvdEvent
{
    public override EventType Type => EventType.SetPropertyValue;

    public ulong Instance { get; init; }

    public uint Property { get; init; }

    public NameHandles IncomingType { get; init; }

    public uint NumItems { get; init; }

    public ReadOnlyMemory<byte> Data { get; init; }
}

public sealed class BeginSetPropertyValueEvent : PvdEvent
{
    public override EventType Type => EventType.BeginSetPropertyValue;

    public ulong Instance { get; init; }

    public uint Property { get; init; }

    public NameHandles IncomingType { get; init; }
}

public sealed class AppendPropertyValueDataEvent : PvdEvent
{
    public override EventType Type => EventType.AppendPropertyValueData;

    public ReadOnlyMemory<byte> Data { get; init; }

    public uint NumItems { get; init; }
}

public sealed class EndSetPropertyValueEvent : PvdEvent
{
    public override EventType Type => EventType.EndSetPropertyValue;
}

public sealed class SetPropertyMessageEvent : PvdEvent
{
    public override EventType Type => EventType.SetPropertyMessage;

    public ulong Instance { get; init; }

    public NameHandles Message { get; init; }

    public ReadOnlyMemory<byte> Data { get; init; }
}

public sealed class BeginPropertyMessageGroupEvent : PvdEvent
{
    public override EventType Type => EventType.BeginPropertyMessageGroup;

    public NameHandles Message { get; init; }
}

public sealed class SendPropertyMessageFromGroupEvent : PvdEvent
{
    public override EventType Type => EventType.SendPropertyMessageFromGroup;

    public ulong Instance { get; init; }

    public ReadOnlyMemory<byte> Data { get; init; }
}

public sealed class EndPropertyMessageGroupEvent : PvdEvent
{
    public override EventType Type => EventType.EndPropertyMessageGroup;
}

public sealed class DestroyInstanceEvent : PvdEvent
{
    public override EventType Type => EventType.DestroyInstance;

    public ulong Instance { get; init; }
}

public sealed class ObjectRefEvent : PvdEvent
{
    public ObjectRefEvent(bool isPushBack)
    {
        IsPushBack = isPushBack;
    }

    public override EventType Type => IsPushBack ? EventType.PushBackObjectRef : EventType.RemoveObjectRef;

    public bool IsPushBack { get; }

    public ulong Instance { get; init; }

    public uint Property { get; init; }

    public ulong ObjectRef { get; init; }
}

public sealed class SectionEvent : PvdEvent
{
    public SectionEvent(bool isBegin)
    {
        IsBegin = isBegin;
    }

    public override EventType Type => IsBegin ? EventType.BeginSection : EventType.EndSection;

    public bool IsBegin { get; }

    public ulong SectionId { get; init; }

    public uint Name { get; init; }

    public ulong SectionTimestamp { get; init; }
}

public sealed class SetPickableEvent : PvdEvent
{
    public override EventType Type => EventType.SetPickable;

    public ulong Instance { get; init; }

    public bool Pickable { get; init; }
}

public sealed class SetColorEvent : PvdEvent
{
    public override EventType Type => EventType.SetColor;

    public ulong Instance { get; init; }

    public uint Rgba { get; init; }
}

public sealed class SetIsTopLevelEvent : PvdEvent
{
    public override EventType Type => EventType.SetIsTopLevel;

    public ulong Instance { get; init; }

    public bool IsTopLevel { get; init; }
}

public sealed class SetCameraEvent : PvdEvent
{
    public override EventType Type => EventType.SetCamera;

    public string Name { get; init; } = "";

    public Vector3 Position { get; init; }

    public Vector3 Up { get; init; }

    public Vector3 Target { get; init; }
}

public sealed class AddProfileZoneEvent : PvdEvent
{
    public override EventType Type => EventType.AddProfileZone;

    public ulong Instance { get; init; }

    public string Name { get; init; } = "";
}

public sealed class AddProfileZoneNameEvent : PvdEvent
{
    public override EventType Type => EventType.AddProfileZoneEvent;

    public ulong Zone { get; init; }

    public string Name { get; init; } = "";

    public ushort EventId { get; init; }

    public bool CompileTimeEnabled { get; init; }
}

public sealed class StreamEndEvent : PvdEvent
{
    public override EventType Type => EventType.StreamEnd;

    public string Name { get; init; } = "";
}

public sealed class ErrorMessageEvent : PvdEvent
{
    public override EventType Type => EventType.ErrorMessage;

    public uint Code { get; init; }

    public string Message { get; init; } = "";

    public string File { get; init; } = "";

    public uint Line { get; init; }
}

public sealed class OriginShiftEvent : PvdEvent
{
    public override EventType Type => EventType.OriginShift;

    public ulong Scene { get; init; }

    public Vector3 Shift { get; init; }
}
