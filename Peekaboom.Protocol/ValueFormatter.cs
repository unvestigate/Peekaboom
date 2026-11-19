using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Peekaboom.Protocol;

public static class ValueFormatter
{
    public static string Format(PropertyValue value, PropertyDef? property, int maxItems)
    {
        bool isArray = property != null ? property.IsArray : value.Count != 1;
        if (!isArray && value.Count == 1)
        {
            return FormatItem(value, 0, property);
        }

        var builder = new StringBuilder();
        builder.Append('[').Append(value.Count.ToString(CultureInfo.InvariantCulture)).Append("] {");

        int shown = Math.Min(value.Count, maxItems);
        for (int i = 0; i < shown; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            AppendValue(builder, value, i, value.Type, 0, property);
        }

        if (value.Count > shown)
        {
            builder.Append(", ...");
        }

        builder.Append('}');
        return builder.ToString();
    }

    public static string FormatItem(PropertyValue value, int item, PropertyDef? property)
    {
        var builder = new StringBuilder();
        AppendValue(builder, value, item, value.Type, 0, property);
        return builder.ToString();
    }

    private static void AppendValue(StringBuilder builder, PropertyValue value, int item, ClassDef type, int offset, PropertyDef? property)
    {
        ReadOnlySpan<byte> data = value.Item(item).Slice(offset);

        switch (type.BaseType)
        {
            case BaseType.I8:
                AppendInteger(builder, (ulong)(long)(sbyte)data[0], true, property);
                break;
            case BaseType.U8:
                AppendInteger(builder, data[0], false, property);
                break;
            case BaseType.Bool:
                builder.Append(data[0] != 0 ? "true" : "false");
                break;
            case BaseType.I16:
                AppendInteger(builder, (ulong)(long)BinaryPrimitives.ReadInt16LittleEndian(data), true, property);
                break;
            case BaseType.U16:
                AppendInteger(builder, BinaryPrimitives.ReadUInt16LittleEndian(data), false, property);
                break;
            case BaseType.I32:
                AppendInteger(builder, (ulong)(long)BinaryPrimitives.ReadInt32LittleEndian(data), true, property);
                break;
            case BaseType.U32:
                AppendInteger(builder, BinaryPrimitives.ReadUInt32LittleEndian(data), false, property);
                break;
            case BaseType.I64:
                AppendInteger(builder, (ulong)BinaryPrimitives.ReadInt64LittleEndian(data), true, property);
                break;
            case BaseType.U64:
                AppendInteger(builder, BinaryPrimitives.ReadUInt64LittleEndian(data), false, property);
                break;
            case BaseType.F32:
                builder.Append(FormatFloat(BinaryPrimitives.ReadSingleLittleEndian(data)));
                break;
            case BaseType.F64:
                builder.Append(BinaryPrimitives.ReadDoubleLittleEndian(data).ToString("G9", CultureInfo.InvariantCulture));
                break;
            case BaseType.String:
            case BaseType.StringHandle:
                builder.Append('"').Append(value.GetString(item, value.Type.StringIndexAt(offset))).Append('"');
                break;
            case BaseType.VoidPtr:
            case BaseType.ObjectRef:
                builder.Append(FormatId(value.ReadPointer(item, offset)));
                break;
            case BaseType.Color:
                builder.Append('#').Append(data[0].ToString("X2")).Append(data[1].ToString("X2"))
                    .Append(data[2].ToString("X2")).Append(data[3].ToString("X2"));
                break;
            case BaseType.ArrayData:
                builder.Append("<array>");
                break;
            default:
                AppendStruct(builder, value, item, type, offset);
                break;
        }
    }

    private static void AppendStruct(StringBuilder builder, PropertyValue value, int item, ClassDef type, int offset)
    {
        var properties = new List<PropertyDef>();
        type.CollectProperties(properties);

        bool allFloats = properties.Count > 0;
        foreach (PropertyDef property in properties)
        {
            if (property.IsArray || property.DataType.BaseType != BaseType.F32)
            {
                allFloats = false;
                break;
            }
        }

        if (allFloats)
        {
            builder.Append('(');
            for (int i = 0; i < properties.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(FormatFloat(value.ReadF32(item, offset + properties[i].Offset)));
            }

            builder.Append(')');
            return;
        }

        builder.Append('{');
        for (int i = 0; i < properties.Count; i++)
        {
            PropertyDef property = properties[i];
            if (i > 0)
            {
                builder.Append(", ");
            }

            builder.Append(property.Name).Append('=');
            if (property.IsArray)
            {
                builder.Append("<array>");
            }
            else
            {
                AppendValue(builder, value, item, property.DataType, offset + property.Offset, property);
            }
        }

        builder.Append('}');
    }

    private static void AppendInteger(StringBuilder builder, ulong raw, bool signed, PropertyDef? property)
    {
        string number = signed ? ((long)raw).ToString(CultureInfo.InvariantCulture) : raw.ToString(CultureInfo.InvariantCulture);

        if (property == null || property.Values.Length == 0)
        {
            builder.Append(number);
            return;
        }

        if (property.IsBitflag)
        {
            AppendFlags(builder, raw, property.Values, number);
            return;
        }

        foreach (NamedValue named in property.Values)
        {
            if (named.Value == raw)
            {
                builder.Append(named.Name);
                return;
            }
        }

        builder.Append(number);
    }

    private static void AppendFlags(StringBuilder builder, ulong raw, NamedValue[] values, string number)
    {
        if (raw == 0)
        {
            foreach (NamedValue named in values)
            {
                if (named.Value == 0)
                {
                    builder.Append(named.Name);
                    return;
                }
            }

            builder.Append('0');
            return;
        }

        ulong remaining = raw;
        bool first = true;
        foreach (NamedValue named in values)
        {
            if (named.Value != 0 && (raw & named.Value) == named.Value)
            {
                if (!first)
                {
                    builder.Append('|');
                }

                builder.Append(named.Name);
                remaining &= ~(ulong)named.Value;
                first = false;
            }
        }

        if (first)
        {
            builder.Append(number);
            return;
        }

        if (remaining != 0)
        {
            builder.Append("|0x").Append(remaining.ToString("X", CultureInfo.InvariantCulture));
        }
    }

    public static string FormatFloat(float value)
    {
        return value.ToString("G7", CultureInfo.InvariantCulture);
    }

    public static string FormatId(ulong id)
    {
        return id == 0 ? "null" : "0x" + id.ToString("X", CultureInfo.InvariantCulture);
    }
}
