namespace Peekaboom.Core;

public sealed class Selection
{
    public ulong Id { get; private set; }

    public long Serial { get; private set; } = -1;

    public bool IsEmpty => Id == 0;

    public event Action? Changed;

    public void Select(ulong id, long serial)
    {
        if (id == Id && serial == Serial)
        {
            return;
        }

        Id = id;
        Serial = id == 0 ? -1 : serial;
        Changed?.Invoke();
    }

    public void Clear()
    {
        Select(0, -1);
    }
}
