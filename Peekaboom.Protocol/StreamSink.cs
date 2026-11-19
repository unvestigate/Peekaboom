using System.Numerics;

namespace Peekaboom.Protocol;

public abstract class StreamSink
{
    public virtual void OnStreamStart(StreamSession session, StreamInitialization initialization)
    {
    }

    public virtual void OnCreateInstance(ulong instance, ClassDef cls)
    {
    }

    public virtual void OnDestroyInstance(ulong instance, ClassDef? cls)
    {
    }

    public virtual void OnSetProperty(ulong instance, ClassDef? cls, string name, PropertyDef? property, PropertyValue value)
    {
    }

    public virtual void OnSetPropertyMessage(ulong instance, ClassDef? cls, PropertyMessageDef message, ReadOnlyMemory<byte> raw,
        string[]? strings, int pointerSize)
    {
        for (int i = 0; i < message.Entries.Length; i++)
        {
            PropertyValue value = message.CreateEntryValue(i, raw, strings, pointerSize);
            PropertyDef property = message.Entries[i].Property;
            OnSetProperty(instance, cls, property.Name, property, value);
        }
    }

    public virtual void OnObjectRef(ulong instance, ClassDef? cls, string name, ulong objectRef, bool added)
    {
    }

    public virtual void OnSection(ulong section, string name, ulong timestamp, bool begin)
    {
    }

    public virtual void OnSetIsTopLevel(ulong instance, bool isTopLevel)
    {
    }

    public virtual void OnSetCamera(string name, Vector3 position, Vector3 up, Vector3 target)
    {
    }

    public virtual void OnErrorMessage(uint code, string message, string file, uint line)
    {
    }

    public virtual void OnOriginShift(ulong scene, Vector3 shift)
    {
    }

    public virtual void OnWarning(string message)
    {
    }
}
