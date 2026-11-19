using Peekaboom.Protocol;

namespace Peekaboom.Model;

public sealed class MessagePayload
{
    public MessagePayload(PropertyMessageDef message, ReadOnlyMemory<byte> raw, string[]? strings, int pointerSize, long origin)
    {
        Message = message;
        Raw = raw;
        Strings = strings;
        PointerSize = pointerSize;
        Origin = origin;
    }

    public long Origin { get; }

    public PropertyMessageDef Message { get; }

    public ReadOnlyMemory<byte> Raw { get; }

    public string[]? Strings { get; }

    public int PointerSize { get; }

    public PropertyValue GetEntryValue(int entry)
    {
        PropertyValue value = Message.CreateEntryValue(entry, Raw, Strings, PointerSize);
        value.Origin = Origin;
        return value;
    }
}

public readonly struct PropertySlot
{
    public PropertySlot(PropertyValue value)
    {
        Value = value;
        Payload = null;
        Entry = -1;
    }

    public PropertySlot(MessagePayload payload, int entry)
    {
        Value = null;
        Payload = payload;
        Entry = entry;
    }

    public PropertyValue? Value { get; }

    public MessagePayload? Payload { get; }

    public int Entry { get; }

    public ClassDef Type => Value != null ? Value.Type : Payload!.Message.Entries[Entry].DataType;

    public PropertyValue Resolve()
    {
        return Value ?? Payload!.GetEntryValue(Entry);
    }
}
