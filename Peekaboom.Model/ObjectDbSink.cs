using Peekaboom.Protocol;

namespace Peekaboom.Model;

public class ObjectDbSink : StreamSink
{
    public const string RENDERER_REF = "Renderer";

    public ObjectDbSink(ObjectDb db)
    {
        Db = db;
    }

    public ObjectDb Db { get; set; }

    public StreamSession? Session { get; set; }

    protected long CurrentOffset => Session?.CurrentEvent?.Offset ?? 0;

    public override void OnCreateInstance(ulong instance, ClassDef cls)
    {
        ObjectInstance? previous = Db.Find(Db.LastCreatedId);
        ObjectInstance created = Db.Create(instance, cls, CurrentOffset);

        if (LegacyFormat.IsScene(cls) && previous != null && IsRenderer(previous.Class))
        {
            created.AddRef(RENDERER_REF, previous.Id);
        }

        Db.LastCreatedId = instance;
    }

    public override void OnDestroyInstance(ulong instance, ClassDef? cls)
    {
        Db.Destroy(instance);
    }

    public override void OnSetProperty(ulong instance, ClassDef? cls, string name, PropertyDef? property, PropertyValue value)
    {
        ObjectInstance? target = Db.Find(instance);
        if (target == null)
        {
            return;
        }

        if (property != null && property.IsArray && value.Type.IsObjectRef)
        {
            target.SetRefs(name, value);
            return;
        }

        target.SetValue(name, value);

        if (IsRenderer(target.Class) && name == LegacyFormat.EVENTS_PROPERTY && value.Count > 0)
        {
            target.AppendRenderChunk(value);
        }
    }

    public override void OnSetPropertyMessage(ulong instance, ClassDef? cls, PropertyMessageDef message, ReadOnlyMemory<byte> raw,
        string[]? strings, int pointerSize)
    {
        Db.Find(instance)?.SetMessage(new MessagePayload(message, raw, strings, pointerSize, CurrentOffset));
    }

    public override void OnObjectRef(ulong instance, ClassDef? cls, string name, ulong objectRef, bool added)
    {
        ObjectInstance? target = Db.Find(instance);
        if (target == null)
        {
            return;
        }

        if (added)
        {
            target.AddRef(name, objectRef);
        }
        else
        {
            target.RemoveRef(name, objectRef);
        }
    }

    public override void OnSetIsTopLevel(ulong instance, bool isTopLevel)
    {
        ObjectInstance? target = Db.Find(instance);
        if (target != null)
        {
            target.IsTopLevel = isTopLevel;
        }
    }

    public override void OnSection(ulong section, string name, ulong timestamp, bool begin)
    {
        if (!begin || name != LegacyFormat.FRAME_SECTION)
        {
            return;
        }

        ObjectInstance? scene = Db.Find(section);
        if (scene == null)
        {
            return;
        }

        foreach (ulong renderer in scene.GetRefs(RENDERER_REF))
        {
            Db.Find(renderer)?.ClearRenderChunks();
        }
    }

    private static bool IsRenderer(ClassDef cls)
    {
        return cls.Name.Namespace == LegacyFormat.DEBUGGER_NAMESPACE && cls.Name.Name == LegacyFormat.USER_RENDERER_CLASS;
    }
}
