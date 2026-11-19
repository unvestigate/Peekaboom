using Dock.Model.Mvvm.Controls;

using Peekaboom.Rendering;

namespace Peekaboom.Docking;

public sealed class DisplayViewModel : Tool
{
    public DisplayOptions Options { get; private set; } = new();

    public event Action? OptionsChanged;

    public event Action? Reloaded;

    public void Load(DisplayOptions options)
    {
        Options = options.Clone();
        Reloaded?.Invoke();
    }

    public void NotifyChanged()
    {
        OptionsChanged?.Invoke();
    }
}
