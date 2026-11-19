using Avalonia.Data.Converters;

namespace Peekaboom.Docking;

public static class DockConverters
{
    public static readonly FuncValueConverter<int, bool> CountGreaterThanOne =
        new(count => count > 1);
}
