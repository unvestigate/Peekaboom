namespace Peekaboom.Protocol;

public sealed class StringTable
{
    private readonly Dictionary<uint, string> mStrings = new();

    public int Count => mStrings.Count;

    public void Set(uint handle, string value)
    {
        mStrings[handle] = value;
    }

    public bool TryGet(uint handle, out string value)
    {
        if (handle == 0)
        {
            value = "";
            return true;
        }

        if (mStrings.TryGetValue(handle, out string? found))
        {
            value = found;
            return true;
        }

        value = "<string " + handle + ">";
        return false;
    }

    public string Get(uint handle)
    {
        TryGet(handle, out string value);
        return value;
    }

    public NamespacedName Get(NameHandles handles)
    {
        return new NamespacedName(Get(handles.Namespace), Get(handles.Name));
    }
}
