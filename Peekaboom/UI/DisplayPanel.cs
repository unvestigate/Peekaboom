using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using Peekaboom.Docking;
using Peekaboom.Rendering;

namespace Peekaboom.UI;

public sealed class DisplayPanel : UserControl
{
    private static readonly IBrush HeaderBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0xC8, 0xFF));
    private static readonly string[] ModeNames = { "None", "Selected", "All" };

    private readonly List<Action> mRefreshers = new();
    private DisplayViewModel? mViewModel;
    private bool mUpdating;

    public DisplayPanel()
    {
        var stack = new StackPanel { Margin = new Thickness(8, 6, 8, 8), Spacing = 2 };

        AddHeader(stack, "Actors");
        AddCheck(stack, "Static", o => o.ShowStatic, (o, v) => o.ShowStatic = v);
        AddCheck(stack, "Dynamic", o => o.ShowDynamic, (o, v) => o.ShowDynamic = v);
        AddCheck(stack, "Kinematic", o => o.ShowKinematic, (o, v) => o.ShowKinematic = v);
        AddCheck(stack, "Articulation links", o => o.ShowArticulationLinks, (o, v) => o.ShowArticulationLinks = v);
        AddCheck(stack, "Awake", o => o.ShowAwake, (o, v) => o.ShowAwake = v);
        AddCheck(stack, "Sleeping", o => o.ShowSleeping, (o, v) => o.ShowSleeping = v);

        AddHeader(stack, "Geometry");
        AddCheck(stack, "Boxes", o => o.ShowBoxes, (o, v) => o.ShowBoxes = v);
        AddCheck(stack, "Spheres", o => o.ShowSpheres, (o, v) => o.ShowSpheres = v);
        AddCheck(stack, "Capsules", o => o.ShowCapsules, (o, v) => o.ShowCapsules = v);
        AddCheck(stack, "Planes", o => o.ShowPlanes, (o, v) => o.ShowPlanes = v);
        AddCheck(stack, "Convex meshes", o => o.ShowConvexMeshes, (o, v) => o.ShowConvexMeshes = v);
        AddCheck(stack, "Triangle meshes", o => o.ShowTriangleMeshes, (o, v) => o.ShowTriangleMeshes = v);
        AddCheck(stack, "Heightfields", o => o.ShowHeightFields, (o, v) => o.ShowHeightFields = v);
        AddCheck(stack, "Tetrahedron meshes", o => o.ShowTetrahedronMeshes, (o, v) => o.ShowTetrahedronMeshes = v);

        AddHeader(stack, "Shapes");
        AddCheck(stack, "Simulation", o => o.ShowSimulationShapes, (o, v) => o.ShowSimulationShapes = v);
        AddCheck(stack, "Query only", o => o.ShowQueryShapes, (o, v) => o.ShowQueryShapes = v);
        AddCheck(stack, "Trigger", o => o.ShowTriggerShapes, (o, v) => o.ShowTriggerShapes = v);

        AddHeader(stack, "Name");
        AddNameFilter(stack);

        AddHeader(stack, "Overlays");
        AddMode(stack, "Contacts", o => o.Contacts, (o, v) => o.Contacts = v);
        AddMode(stack, "Joints", o => o.Joints, (o, v) => o.Joints = v);
        AddMode(stack, "Scene queries", o => o.SceneQueries, (o, v) => o.SceneQueries = v);
        AddMode(stack, "Debug render", o => o.DebugRender, (o, v) => o.DebugRender = v);
        AddMode(stack, "Bounds", o => o.Bounds, (o, v) => o.Bounds = v);
        AddMode(stack, "Axes", o => o.Axes, (o, v) => o.Axes = v);
        AddMode(stack, "Velocities", o => o.Velocities, (o, v) => o.Velocities = v);

        var reset = new Button { Content = "Reset to Defaults", Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        reset.Click += (_, _) =>
        {
            if (mViewModel == null)
            {
                return;
            }

            mViewModel.Load(new DisplayOptions());
            mViewModel.NotifyChanged();
        };
        stack.Children.Add(reset);

        Content = new ScrollViewer { Content = stack, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };

        DataContextChanged += (_, _) => AttachViewModel();
    }

    private void AttachViewModel()
    {
        if (mViewModel != null)
        {
            mViewModel.Reloaded -= Refresh;
        }

        mViewModel = DataContext as DisplayViewModel;

        if (mViewModel != null)
        {
            mViewModel.Reloaded += Refresh;
        }

        Refresh();
    }

    private void Refresh()
    {
        mUpdating = true;
        foreach (Action refresher in mRefreshers)
        {
            refresher();
        }

        mUpdating = false;
    }

    private void Changed(Action<DisplayOptions> apply)
    {
        if (mUpdating || mViewModel == null)
        {
            return;
        }

        apply(mViewModel.Options);
        mViewModel.NotifyChanged();
    }

    private static void AddHeader(StackPanel stack, string text)
    {
        stack.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = HeaderBrush,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, stack.Children.Count == 0 ? 0 : 10, 0, 2),
        });
    }

    private void AddCheck(StackPanel stack, string label, Func<DisplayOptions, bool> get, Action<DisplayOptions, bool> set)
    {
        var box = new CheckBox { Content = label, MinHeight = 24 };
        box.Click += (_, _) => Changed(options => set(options, box.IsChecked == true));
        mRefreshers.Add(() => box.IsChecked = mViewModel != null && get(mViewModel.Options));
        stack.Children.Add(box);
    }

    private void AddMode(StackPanel stack, string label, Func<DisplayOptions, OverlayMode> get, Action<DisplayOptions, OverlayMode> set)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,110"), Margin = new Thickness(0, 1, 0, 1) };
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        var box = new ComboBox { ItemsSource = ModeNames, HorizontalAlignment = HorizontalAlignment.Stretch };
        Grid.SetColumn(box, 1);
        grid.Children.Add(text);
        grid.Children.Add(box);

        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedIndex >= 0)
            {
                Changed(options => set(options, (OverlayMode)box.SelectedIndex));
            }
        };
        mRefreshers.Add(() => box.SelectedIndex = mViewModel != null ? (int)get(mViewModel.Options) : 0);
        stack.Children.Add(grid);
    }

    private void AddNameFilter(StackPanel stack)
    {
        var box = new TextBox { Watermark = "Actor or shape name, or id" };
        box.TextChanged += (_, _) => Changed(options => options.NameFilter = (box.Text ?? "").Trim());
        mRefreshers.Add(() => box.Text = mViewModel?.Options.NameFilter ?? "");
        stack.Children.Add(box);
    }
}
