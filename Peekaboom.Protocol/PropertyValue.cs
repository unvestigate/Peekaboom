using System.Buffers.Binary;
using System.Text;

namespace Peekaboom.Protocol;

public sealed class PropertyValue
{
    public PropertyValue(ClassDef type, int count, ReadOnlyMemory<byte> data, string[]? strings, int pointerSize)
    {
        Type = type;
        Count = count;
        Data = data;
        Strings = strings;
        PointerSize = pointerSize;
    }

    public ClassDef Type { get; }

    public int Count { get; }

    public ReadOnlyMemory<byte> Data { get; }

    public string[]? Strings { get; }

    public int PointerSize { get; }

    public long Origin { get; set; }

    public int Stride => Type.ByteSize;

    public ReadOnlySpan<byte> Item(int index)
    {
        return Data.Span.Slice(index * Stride, Stride);
    }

    public string GetString(int item, int stringIndex)
    {
        if (Strings == null || stringIndex < 0)
        {
            return "";
        }

        int index = item * Type.StringOffsets.Count + stringIndex;
        return index < Strings.Length ? Strings[index] : "";
    }

    public ulong ReadPointer(int item, int offset)
    {
        ReadOnlySpan<byte> span = Item(item).Slice(offset);
        return PointerSize == 8 ? BinaryPrimitives.ReadUInt64LittleEndian(span) : BinaryPrimitives.ReadUInt32LittleEndian(span);
    }

    public float ReadF32(int item, int offset)
    {
        return BinaryPrimitives.ReadSingleLittleEndian(Item(item).Slice(offset));
    }

    public uint ReadU32(int item, int offset)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(Item(item).Slice(offset));
    }

    public static PropertyValue? Decode(ClassDef type, uint numItems, ReadOnlyMemory<byte> blob, int pointerSize, out string? error)
    {
        error = null;
        int stride = type.ByteSize;
        if (stride <= 0)
        {
            error = "Type " + type.Name + " has no size.";
            return null;
        }

        int stringCount = type.StringOffsets.Count;
        if (stringCount == 0)
        {
            long expected = (long)numItems * stride;
            int count = (int)numItems;
            if (blob.Length != expected)
            {
                error = "Value of type " + type.Name + " has " + blob.Length + " bytes for " + numItems + " items of " + stride + ".";
                count = blob.Length / stride;
            }

            return new PropertyValue(type, count, blob.Slice(0, count * stride), null, pointerSize);
        }

        ReadOnlySpan<byte> span = blob.Span;
        if ((long)numItems * stride > span.Length)
        {
            error = "Value of type " + type.Name + " has " + span.Length + " bytes, too few for " + numItems + " items.";
            return null;
        }

        var raw = new byte[numItems * stride];
        var strings = new string[numItems * stringCount];
        int position = 0;

        for (int item = 0; item < numItems; item++)
        {
            if (position + stride > span.Length)
            {
                error = "Value of type " + type.Name + " ends inside item " + item + ".";
                return null;
            }

            span.Slice(position, stride).CopyTo(new Span<byte>(raw, item * stride, stride));
            position += stride;

            for (int s = 0; s < stringCount; s++)
            {
                int length = span.Slice(position).IndexOf((byte)0);
                if (length < 0)
                {
                    error = "Value of type " + type.Name + " has an unterminated string in item " + item + ".";
                    return null;
                }

                strings[item * stringCount + s] = Encoding.UTF8.GetString(span.Slice(position, length));
                position += length + 1;
            }
        }

        if (position != span.Length)
        {
            error = "Value of type " + type.Name + " has " + (span.Length - position) + " bytes left over.";
        }

        return new PropertyValue(type, (int)numItems, raw, strings, pointerSize);
    }

    public static PropertyValue Concat(ClassDef type, List<PropertyValue> parts, int pointerSize)
    {
        if (parts.Count == 1)
        {
            return parts[0];
        }

        int count = 0;
        int bytes = 0;
        int stringTotal = 0;
        foreach (PropertyValue part in parts)
        {
            count += part.Count;
            bytes += part.Data.Length;
            stringTotal += part.Strings?.Length ?? 0;
        }

        var data = new byte[bytes];
        string[]? strings = stringTotal > 0 ? new string[stringTotal] : null;
        int byteOffset = 0;
        int stringOffset = 0;

        foreach (PropertyValue part in parts)
        {
            part.Data.Span.CopyTo(new Span<byte>(data, byteOffset, part.Data.Length));
            byteOffset += part.Data.Length;

            if (strings != null && part.Strings != null)
            {
                Array.Copy(part.Strings, 0, strings, stringOffset, part.Strings.Length);
                stringOffset += part.Strings.Length;
            }
        }

        return new PropertyValue(type, count, data, strings, pointerSize) { Origin = parts[0].Origin };
    }
}
