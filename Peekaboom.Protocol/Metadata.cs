namespace Peekaboom.Protocol;

public readonly record struct NamespacedName(string Namespace, string Name)
{
    public override string ToString()
    {
        return Namespace.Length == 0 ? Name : Namespace + "." + Name;
    }
}

public enum BaseType
{
    None,
    ArrayData,
    I8,
    U8,
    Bool,
    I16,
    U16,
    I32,
    U32,
    I64,
    U64,
    F32,
    F64,
    String,
    StringHandle,
    VoidPtr,
    ObjectRef,
    Color,
    Vec2,
    Vec3,
    Vec4,
    Quat,
    Bounds3,
    Transform,
    Mat33,
    Mat44,
    U32Array4,
}

public enum PtrOffsetKind
{
    String,
    VoidPtr,
}

public readonly record struct PtrOffset(PtrOffsetKind Kind, int Offset);

public readonly record struct NamedValue(string Name, uint Value);

public sealed class ClassDef
{
    private readonly List<PropertyDef> mProperties = new();
    private readonly List<PtrOffset> mPtrOffsets = new();
    private readonly List<int> mStringOffsets = new();

    public ClassDef(int id, NamespacedName name, BaseType baseType)
    {
        Id = id;
        Name = name;
        BaseType = baseType;
    }

    public int Id { get; }

    public NamespacedName Name { get; }

    public BaseType BaseType { get; }

    public ClassDef? Parent { get; internal set; }

    public IReadOnlyList<PropertyDef> Properties => mProperties;

    public IReadOnlyList<PtrOffset> PtrOffsets => mPtrOffsets;

    public IReadOnlyList<int> StringOffsets => mStringOffsets;

    public int ByteSize { get; internal set; }

    public int DataByteSize { get; internal set; }

    public int Alignment { get; internal set; }

    public bool Locked { get; internal set; }

    public bool RequiresDestruction { get; internal set; }

    public bool IsBaseType => BaseType != BaseType.None;

    public bool IsPrimitive => BaseType >= BaseType.I8 && BaseType <= BaseType.ObjectRef;

    public bool IsPointer => BaseType >= BaseType.String && BaseType <= BaseType.ObjectRef;

    public bool IsString => BaseType == BaseType.String || BaseType == BaseType.StringHandle;

    public bool IsObjectRef => BaseType == BaseType.VoidPtr || BaseType == BaseType.ObjectRef;

    internal void AddPtrOffset(PtrOffsetKind kind, int offset)
    {
        mPtrOffsets.Add(new PtrOffset(kind, offset));
        if (kind == PtrOffsetKind.String)
        {
            mStringOffsets.Add(offset);
        }
    }

    internal void CopyLayoutFrom(ClassDef parent)
    {
        ByteSize = parent.ByteSize;
        DataByteSize = parent.DataByteSize;
        Alignment = parent.Alignment;
        RequiresDestruction = parent.RequiresDestruction;
        mPtrOffsets.Clear();
        mStringOffsets.Clear();

        foreach (PtrOffset offset in parent.mPtrOffsets)
        {
            AddPtrOffset(offset.Kind, offset.Offset);
        }
    }

    internal void AddProperty(PropertyDef property)
    {
        mProperties.Add(property);
    }

    public PropertyDef? FindOwnProperty(string name)
    {
        foreach (PropertyDef property in mProperties)
        {
            if (property.Name == name)
            {
                return property;
            }
        }

        return null;
    }

    public PropertyDef? FindProperty(string name)
    {
        for (ClassDef? cls = this; cls != null; cls = cls.Parent)
        {
            PropertyDef? property = cls.FindOwnProperty(name);
            if (property != null)
            {
                return property;
            }
        }

        return null;
    }

    public bool IsA(ClassDef other)
    {
        for (ClassDef? cls = this; cls != null; cls = cls.Parent)
        {
            if (cls == other)
            {
                return true;
            }
        }

        return false;
    }

    public void CollectProperties(List<PropertyDef> output)
    {
        if (Parent != null)
        {
            Parent.CollectProperties(output);
        }

        output.AddRange(mProperties);
    }

    public int StringIndexAt(int offset)
    {
        for (int i = 0; i < mStringOffsets.Count; i++)
        {
            if (mStringOffsets[i] == offset)
            {
                return i;
            }
        }

        return -1;
    }

    public override string ToString()
    {
        return Name.ToString();
    }
}

public sealed class PropertyDef
{
    public PropertyDef(ClassDef owner, string name, string semantic, ClassDef dataType, bool isArray, int offset, NamedValue[] values)
    {
        Owner = owner;
        Name = name;
        Semantic = semantic;
        DataType = dataType;
        IsArray = isArray;
        Offset = offset;
        Values = values;
    }

    public ClassDef Owner { get; }

    public string Name { get; }

    public string Semantic { get; }

    public ClassDef DataType { get; }

    public bool IsArray { get; }

    public int Offset { get; }

    public NamedValue[] Values { get; }

    public bool IsEnumeration => Semantic == LegacyFormat.SEMANTIC_ENUMERATION;

    public bool IsBitflag => Semantic == LegacyFormat.SEMANTIC_BITFLAG;

    public override string ToString()
    {
        return Owner.Name + "." + Name;
    }
}

public readonly record struct MessageEntrySpec(string Property, NamespacedName DataType, int Offset, int ByteSize);

public sealed class MessageEntry
{
    public MessageEntry(PropertyDef property, ClassDef dataType, int offset, int byteSize, int stringIndex)
    {
        Property = property;
        DataType = dataType;
        Offset = offset;
        ByteSize = byteSize;
        StringIndex = stringIndex;
    }

    public PropertyDef Property { get; }

    public ClassDef DataType { get; }

    public int Offset { get; }

    public int ByteSize { get; }

    public int StringIndex { get; }
}

public sealed class PropertyMessageDef
{
    public PropertyMessageDef(NamespacedName name, ClassDef cls, MessageEntry[] entries, int byteSize, int stringCount)
    {
        Name = name;
        Class = cls;
        Entries = entries;
        ByteSize = byteSize;
        StringCount = stringCount;
    }

    public NamespacedName Name { get; }

    public ClassDef Class { get; }

    public MessageEntry[] Entries { get; }

    public int ByteSize { get; }

    public int StringCount { get; }

    public PropertyValue CreateEntryValue(int index, ReadOnlyMemory<byte> raw, string[]? strings, int pointerSize)
    {
        MessageEntry entry = Entries[index];
        ReadOnlyMemory<byte> slice = raw.Slice(entry.Offset, entry.DataType.ByteSize);
        string[]? entryStrings = entry.StringIndex >= 0 && strings != null ? new[] { strings[entry.StringIndex] } : null;
        return new PropertyValue(entry.DataType, 1, slice, entryStrings, pointerSize);
    }

    public override string ToString()
    {
        return Name.ToString();
    }
}
