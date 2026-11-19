using Peekaboom.Protocol;

namespace Peekaboom.Model;

public sealed class ObjectDb
{
    private const ulong FNV_OFFSET = 14695981039346656037UL;
    private const ulong FNV_PRIME = 1099511628211UL;

    private readonly Dictionary<ulong, ObjectInstance> mObjects;
    private long mNextSerial;

    public ObjectDb()
    {
        mObjects = new Dictionary<ulong, ObjectInstance>();
    }

    private ObjectDb(ObjectDb other)
    {
        mObjects = new Dictionary<ulong, ObjectInstance>(other.mObjects.Count);
        foreach (KeyValuePair<ulong, ObjectInstance> pair in other.mObjects)
        {
            mObjects[pair.Key] = pair.Value.Clone();
        }

        mNextSerial = other.mNextSerial;
        LastCreatedId = other.LastCreatedId;
    }

    public ulong LastCreatedId { get; internal set; }

    public int Count => mObjects.Count;

    public long NextSerial => mNextSerial;

    public IEnumerable<ObjectInstance> Objects => mObjects.Values;

    public ObjectInstance? Find(ulong id)
    {
        mObjects.TryGetValue(id, out ObjectInstance? instance);
        return instance;
    }

    public ObjectInstance Create(ulong id, ClassDef cls, long offset)
    {
        var instance = new ObjectInstance(id, cls, mNextSerial++, offset);
        mObjects[id] = instance;
        return instance;
    }

    public ObjectInstance? Destroy(ulong id)
    {
        if (mObjects.TryGetValue(id, out ObjectInstance? instance))
        {
            mObjects.Remove(id);
        }

        return instance;
    }

    public ObjectDb Clone()
    {
        return new ObjectDb(this);
    }

    public long CountSlots()
    {
        long total = 0;
        foreach (ObjectInstance instance in mObjects.Values)
        {
            total += instance.SlotCount + 1;
        }

        return total;
    }

    public List<ObjectInstance> SortedBySerial()
    {
        var list = new List<ObjectInstance>(mObjects.Values);
        list.Sort((a, b) => a.Serial.CompareTo(b.Serial));
        return list;
    }

    public ulong ComputeDigest()
    {
        ulong hash = FNV_OFFSET;
        hash = Mix(hash, (ulong)mNextSerial);

        var names = new List<string>();
        foreach (ObjectInstance instance in SortedBySerial())
        {
            hash = Mix(hash, instance.Id);
            hash = Mix(hash, (ulong)instance.Serial);
            hash = Mix(hash, (ulong)instance.Class.Id);
            hash = Mix(hash, instance.IsTopLevel ? 1UL : 0UL);

            names.Clear();
            foreach (KeyValuePair<string, PropertySlot> pair in instance.Values)
            {
                names.Add(pair.Key);
            }

            names.Sort(StringComparer.Ordinal);
            foreach (string name in names)
            {
                hash = Mix(hash, name);
                instance.TryGetSlot(name, out PropertySlot slot);
                PropertyValue value = slot.Resolve();
                hash = Mix(hash, (ulong)value.Type.Id);
                hash = Mix(hash, (ulong)value.Count);
                foreach (byte b in value.Data.Span)
                {
                    hash = (hash ^ b) * FNV_PRIME;
                }

                if (value.Strings != null)
                {
                    foreach (string text in value.Strings)
                    {
                        hash = Mix(hash, text);
                    }
                }
            }

            names.Clear();
            foreach (string name in instance.RefNames)
            {
                names.Add(name);
            }

            names.Sort(StringComparer.Ordinal);
            foreach (string name in names)
            {
                hash = Mix(hash, name);
                foreach (ulong id in instance.GetRefs(name))
                {
                    hash = Mix(hash, id);
                }
            }
        }

        return hash;
    }

    private static ulong Mix(ulong hash, ulong value)
    {
        for (int i = 0; i < 8; i++)
        {
            hash = (hash ^ (value & 0xFF)) * FNV_PRIME;
            value >>= 8;
        }

        return hash;
    }

    private static ulong Mix(ulong hash, string text)
    {
        foreach (char c in text)
        {
            hash = (hash ^ c) * FNV_PRIME;
        }

        return (hash ^ 0xFF) * FNV_PRIME;
    }
}
