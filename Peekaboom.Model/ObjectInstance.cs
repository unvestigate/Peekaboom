using Peekaboom.Protocol;

namespace Peekaboom.Model;

public sealed class ObjectInstance
{
    private static readonly List<ulong> EmptyRefs = new();

    private readonly Dictionary<string, PropertySlot> mValues;
    private readonly Dictionary<string, List<ulong>> mRefs;
    private List<PropertyValue>? mRenderChunks;

    public ObjectInstance(ulong id, ClassDef cls, long serial, long createdOffset)
    {
        Id = id;
        Class = cls;
        Serial = serial;
        CreatedOffset = createdOffset;
        mValues = new Dictionary<string, PropertySlot>();
        mRefs = new Dictionary<string, List<ulong>>();
    }

    private ObjectInstance(ObjectInstance other)
    {
        Id = other.Id;
        Class = other.Class;
        Serial = other.Serial;
        CreatedOffset = other.CreatedOffset;
        IsTopLevel = other.IsTopLevel;
        mValues = new Dictionary<string, PropertySlot>(other.mValues);
        mRefs = new Dictionary<string, List<ulong>>(other.mRefs.Count);

        foreach (KeyValuePair<string, List<ulong>> pair in other.mRefs)
        {
            mRefs[pair.Key] = new List<ulong>(pair.Value);
        }

        if (other.mRenderChunks != null)
        {
            mRenderChunks = new List<PropertyValue>(other.mRenderChunks);
        }
    }

    public IReadOnlyList<PropertyValue> RenderChunks => (IReadOnlyList<PropertyValue>?)mRenderChunks ?? Array.Empty<PropertyValue>();

    internal void AppendRenderChunk(PropertyValue value)
    {
        mRenderChunks ??= new List<PropertyValue>();
        mRenderChunks.Add(value);
    }

    internal void ClearRenderChunks()
    {
        mRenderChunks?.Clear();
    }

    public ulong Id { get; }

    public ClassDef Class { get; }

    public long Serial { get; }

    public long CreatedOffset { get; }

    public bool IsTopLevel { get; internal set; }

    public IReadOnlyDictionary<string, PropertySlot> Values => mValues;

    public IEnumerable<string> RefNames => mRefs.Keys;

    public int SlotCount => mValues.Count + mRefs.Count;

    public bool TryGetSlot(string name, out PropertySlot slot)
    {
        return mValues.TryGetValue(name, out slot);
    }

    public PropertyValue? GetValue(string name)
    {
        return mValues.TryGetValue(name, out PropertySlot slot) ? slot.Resolve() : null;
    }

    public IReadOnlyList<ulong> GetRefs(string name)
    {
        return mRefs.TryGetValue(name, out List<ulong>? refs) ? refs : EmptyRefs;
    }

    public bool HasRefs(string name)
    {
        return mRefs.ContainsKey(name);
    }

    internal void SetValue(string name, PropertyValue value)
    {
        mValues[name] = new PropertySlot(value);
    }

    internal void SetMessage(MessagePayload payload)
    {
        MessageEntry[] entries = payload.Message.Entries;
        for (int i = 0; i < entries.Length; i++)
        {
            mValues[entries[i].Property.Name] = new PropertySlot(payload, i);
        }
    }

    internal void AddRef(string name, ulong objectRef)
    {
        List<ulong> refs = GetOrCreateRefs(name);
        if (!refs.Contains(objectRef))
        {
            refs.Add(objectRef);
        }
    }

    internal void RemoveRef(string name, ulong objectRef)
    {
        if (mRefs.TryGetValue(name, out List<ulong>? refs))
        {
            refs.RemoveAll(id => id == objectRef);
        }
    }

    internal void SetRefs(string name, PropertyValue value)
    {
        List<ulong> refs = GetOrCreateRefs(name);
        refs.Clear();

        for (int i = 0; i < value.Count; i++)
        {
            ulong objectRef = value.ReadPointer(i, 0);
            if (objectRef != 0 && !refs.Contains(objectRef))
            {
                refs.Add(objectRef);
            }
        }
    }

    private List<ulong> GetOrCreateRefs(string name)
    {
        if (!mRefs.TryGetValue(name, out List<ulong>? refs))
        {
            refs = new List<ulong>();
            mRefs[name] = refs;
        }

        return refs;
    }

    internal ObjectInstance Clone()
    {
        return new ObjectInstance(this);
    }
}
