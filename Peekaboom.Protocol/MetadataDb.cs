namespace Peekaboom.Protocol;

public sealed class MetadataDb
{
    private readonly List<ClassDef> mClasses = new();
    private readonly Dictionary<NamespacedName, ClassDef> mClassesByName = new();
    private readonly List<PropertyMessageDef> mMessages = new();
    private readonly Dictionary<NamespacedName, PropertyMessageDef> mMessagesByName = new();
    private readonly Dictionary<BaseType, ClassDef> mBaseTypes = new();

    public MetadataDb(int pointerSize)
    {
        PointerSize = pointerSize;
        LegacyFormat.CreateBaseTypes(this, pointerSize);
    }

    public int PointerSize { get; }

    public IReadOnlyList<ClassDef> Classes => mClasses;

    public IReadOnlyList<PropertyMessageDef> Messages => mMessages;

    public ClassDef? FindClass(NamespacedName name)
    {
        mClassesByName.TryGetValue(name, out ClassDef? cls);
        return cls;
    }

    public PropertyMessageDef? FindMessage(NamespacedName name)
    {
        mMessagesByName.TryGetValue(name, out PropertyMessageDef? message);
        return message;
    }

    public ClassDef FindBaseType(BaseType baseType)
    {
        return mBaseTypes[baseType];
    }

    public ClassDef GetOrCreateClass(NamespacedName name, out bool created)
    {
        ClassDef? existing = FindClass(name);
        if (existing != null)
        {
            created = false;
            return existing;
        }

        created = true;
        return AddClass(name, BaseType.None);
    }

    private ClassDef AddClass(NamespacedName name, BaseType baseType)
    {
        var cls = new ClassDef(mClasses.Count, name, baseType);
        mClasses.Add(cls);
        mClassesByName[name] = cls;
        return cls;
    }

    internal ClassDef CreateBaseType(string ns, string name, BaseType baseType, int byteSize, int alignment)
    {
        ClassDef cls = AddClass(new NamespacedName(ns, name), baseType);
        cls.ByteSize = byteSize;
        cls.DataByteSize = byteSize;
        cls.Alignment = alignment;
        mBaseTypes[baseType] = cls;
        return cls;
    }

    internal ClassDef CreateStructBaseType(string ns, string name, BaseType baseType)
    {
        ClassDef cls = AddClass(new NamespacedName(ns, name), baseType);
        mBaseTypes[baseType] = cls;
        return cls;
    }

    internal void AddBaseProperty(ClassDef cls, string name, ClassDef dataType)
    {
        CreateProperty(cls, name, "", dataType, false, Array.Empty<NamedValue>(), out _);
    }

    internal void LockAll()
    {
        foreach (ClassDef cls in mClasses)
        {
            cls.Locked = true;
        }
    }

    public string? DeriveClass(NamespacedName parentName, NamespacedName childName)
    {
        ClassDef parent = GetOrCreateClass(parentName, out _);
        ClassDef child = GetOrCreateClass(childName, out _);

        if (child.Parent != null)
        {
            return child.Parent == parent ? null : "Class " + childName + " already derives from " + child.Parent.Name + ", not " + parentName + ".";
        }

        if (child == parent || parent.IsA(child))
        {
            return "Class " + childName + " cannot derive from " + parentName + ".";
        }

        parent.Locked = true;
        child.Parent = parent;
        child.CopyLayoutFrom(parent);
        return null;
    }

    public PropertyDef? CreateProperty(ClassDef cls, string name, string semantic, ClassDef dataType, bool isArray,
        NamedValue[] values, out string? error)
    {
        if (cls.Locked)
        {
            error = "Class " + cls.Name + " is locked, property " + name + " rejected.";
            return null;
        }

        if (cls.FindOwnProperty(name) != null)
        {
            error = "Property " + cls.Name + "." + name + " is defined twice.";
            return null;
        }

        if (dataType.BaseType == BaseType.String)
        {
            error = "Property " + cls.Name + "." + name + " has datatype String.";
            return null;
        }

        if (dataType.RequiresDestruction)
        {
            error = "Property " + cls.Name + "." + name + " has datatype " + dataType.Name + ", which holds arrays.";
            return null;
        }

        bool requiresDestruction = cls.RequiresDestruction;
        ClassDef element = dataType;
        if (isArray)
        {
            element = mBaseTypes[BaseType.ArrayData];
            requiresDestruction = true;
        }

        int alignment = Math.Max(element.Alignment, 1);
        cls.Alignment = Math.Max(cls.Alignment, alignment);
        int offset = AlignUp(cls.DataByteSize, alignment);
        cls.DataByteSize = offset + element.ByteSize;
        cls.ByteSize = AlignUp(cls.DataByteSize, cls.Alignment);

        foreach (PtrOffset ptr in element.PtrOffsets)
        {
            cls.AddPtrOffset(ptr.Kind, ptr.Offset + offset);
        }

        element.Locked = true;
        cls.RequiresDestruction = requiresDestruction;

        var property = new PropertyDef(cls, name, semantic, dataType, isArray, offset, values);
        cls.AddProperty(property);
        error = null;
        return property;
    }

    public PropertyMessageDef? CreateMessage(NamespacedName className, NamespacedName messageName, MessageEntrySpec[] specs,
        int byteSize, out string? error)
    {
        if (mMessagesByName.ContainsKey(messageName))
        {
            error = "Property message " + messageName + " is defined twice.";
            return null;
        }

        ClassDef? cls = FindClass(className);
        if (cls == null)
        {
            error = "Property message " + messageName + " targets unknown class " + className + ".";
            return null;
        }

        var entries = new MessageEntry[specs.Length];
        int stringCount = 0;

        for (int i = 0; i < specs.Length; i++)
        {
            MessageEntrySpec spec = specs[i];
            ClassDef? dataType = FindClass(spec.DataType);
            if (dataType == null)
            {
                error = "Property message " + messageName + " entry " + spec.Property + " has unknown datatype " + spec.DataType + ".";
                return null;
            }

            if (spec.ByteSize < dataType.ByteSize || spec.Offset < 0 || spec.Offset + spec.ByteSize > byteSize)
            {
                error = "Property message " + messageName + " entry " + spec.Property + " does not fit, offset " + spec.Offset
                    + " size " + spec.ByteSize + " of " + byteSize + ".";
                return null;
            }

            PropertyDef? property = cls.FindProperty(spec.Property);
            if (property == null)
            {
                error = "Property message " + messageName + " entry " + spec.Property + " is not a property of " + className + ".";
                return null;
            }

            int stringIndex = -1;
            if (dataType.BaseType == BaseType.String)
            {
                stringIndex = stringCount;
                stringCount++;
            }

            entries[i] = new MessageEntry(property, dataType, spec.Offset, spec.ByteSize, stringIndex);
        }

        var message = new PropertyMessageDef(messageName, cls, entries, byteSize, stringCount);
        mMessages.Add(message);
        mMessagesByName[messageName] = message;
        error = null;
        return message;
    }

    private static int AlignUp(int value, int alignment)
    {
        return (value + alignment - 1) / alignment * alignment;
    }
}
