using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;

using Peekaboom.Logging;

namespace Peekaboom.Docking;

public sealed class PeekaboomDockFactory : Factory
{
    private const double LEFT_PROPORTION = 0.2;
    private const double RIGHT_PROPORTION = 0.25;
    private const double BOTTOM_PROPORTION = 0.22;

    public ViewportViewModel Viewport { get; private set; } = null!;

    public ObjectsViewModel Objects { get; private set; } = null!;

    public PropertiesViewModel Properties { get; private set; } = null!;

    public DisplayViewModel Display { get; private set; } = null!;

    public LogViewModel Log { get; private set; } = null!;

    public override IRootDock CreateLayout()
    {
        Log = new LogViewModel { Id = "Log", Title = "Log", CanClose = false };
        Viewport = new ViewportViewModel { Id = "Viewport", Title = "Viewport", CanClose = false, Log = Log };
        Objects = new ObjectsViewModel { Id = "Objects", Title = "Objects", CanClose = false };
        Properties = new PropertiesViewModel { Id = "Properties", Title = "Properties", CanClose = false };
        Display = new DisplayViewModel { Id = "Display", Title = "Display", CanClose = false };

        var leftDock = new ToolDock
        {
            Id = "LeftPane",
            Alignment = Alignment.Left,
            Proportion = LEFT_PROPORTION,
            VisibleDockables = CreateList<IDockable>(Objects),
            ActiveDockable = Objects,
        };

        var documentDock = new DocumentDock
        {
            Id = "Documents",
            Title = "Documents",
            IsCollapsable = false,
            VisibleDockables = CreateList<IDockable>(Viewport),
            ActiveDockable = Viewport,
            CanCreateDocument = false,
        };

        var rightDock = new ToolDock
        {
            Id = "RightPane",
            Alignment = Alignment.Right,
            Proportion = RIGHT_PROPORTION,
            VisibleDockables = CreateList<IDockable>(Properties, Display),
            ActiveDockable = Properties,
        };

        var mainRow = new ProportionalDock
        {
            Id = "MainRow",
            Orientation = Orientation.Horizontal,
            Proportion = 1.0 - BOTTOM_PROPORTION,
            VisibleDockables = CreateList<IDockable>(
                leftDock,
                new ProportionalDockSplitter(),
                documentDock,
                new ProportionalDockSplitter(),
                rightDock),
        };

        var bottomDock = new ToolDock
        {
            Id = "BottomPane",
            Alignment = Alignment.Bottom,
            Proportion = BOTTOM_PROPORTION,
            VisibleDockables = CreateList<IDockable>(Log),
            ActiveDockable = Log,
        };

        var outerColumn = new ProportionalDock
        {
            Id = "OuterColumn",
            Orientation = Orientation.Vertical,
            VisibleDockables = CreateList<IDockable>(
                mainRow,
                new ProportionalDockSplitter(),
                bottomDock),
        };

        IRootDock root = CreateRootDock();
        root.Title = "Default";
        root.VisibleDockables = CreateList<IDockable>(outerColumn);
        root.ActiveDockable = outerColumn;
        root.DefaultDockable = outerColumn;
        return root;
    }

    public override void InitLayout(IDockable layout)
    {
        HostWindowLocator = new Dictionary<string, Func<IHostWindow?>>
        {
            [nameof(IDockWindow)] = () => new HostWindow(),
        };

        base.InitLayout(layout);
    }
}
