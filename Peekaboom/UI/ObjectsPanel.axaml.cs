using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Peekaboom.Docking;

namespace Peekaboom.UI;

public partial class ObjectsPanel : UserControl
{
    private const string EXPANDER_CLASS = "expander";

    private ObjectsViewModel? mViewModel;
    private bool mSelecting;

    public ObjectsPanel()
    {
        InitializeComponent();

        DataContextChanged += (_, _) => AttachViewModel();
        FilterBox.TextChanged += (_, _) => mViewModel?.SetFilter(FilterBox.Text ?? "");
        Tree.SelectionChanged += (_, _) =>
        {
            if (!mSelecting)
            {
                mViewModel?.ReportSelected(Tree.SelectedItem as ObjectNode);
            }
        };

        Tree.AddHandler(PointerPressedEvent, OnTreePointerPressed, RoutingStrategies.Tunnel);
        Tree.DoubleTapped += (_, e) =>
        {
            if (NodeFrom(e.Source) is ObjectNode node)
            {
                mViewModel?.Toggle(node);
            }
        };
        Tree.KeyDown += OnTreeKeyDown;
    }

    private void AttachViewModel()
    {
        if (mViewModel != null)
        {
            mViewModel.NodeToSelect -= OnNodeToSelect;
        }

        mViewModel = DataContext as ObjectsViewModel;

        if (mViewModel != null)
        {
            mViewModel.NodeToSelect += OnNodeToSelect;
        }
    }

    private void OnNodeToSelect(ObjectNode? node)
    {
        if (ReferenceEquals(Tree.SelectedItem, node))
        {
            return;
        }

        mSelecting = true;
        Tree.SelectedItem = node;
        mSelecting = false;

        if (node != null)
        {
            ScrollIntoViewLater(node);
        }
    }

    private void ScrollIntoViewLater(ObjectNode node)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (mViewModel != null && mViewModel.Rows.Contains(node))
            {
                Tree.ScrollIntoView(node);
            }
        }, DispatcherPriority.Background);
    }

    private void OnTreePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsOnExpander(e.Source) || NodeFrom(e.Source) is not ObjectNode node)
        {
            return;
        }

        mViewModel?.Toggle(node);
        e.Handled = true;
    }

    private void OnTreeKeyDown(object? sender, KeyEventArgs e)
    {
        if (mViewModel == null || Tree.SelectedItem is not ObjectNode node)
        {
            return;
        }

        if (e.Key == Key.Right)
        {
            mViewModel.SetExpanded(node, true);
            e.Handled = true;
        }
        else if (e.Key == Key.Left)
        {
            if (node.IsExpanded && node.HasChildren)
            {
                mViewModel.SetExpanded(node, false);
            }
            else if (node.Parent != null)
            {
                Tree.SelectedItem = node.Parent;
                ScrollIntoViewLater(node.Parent);
            }

            e.Handled = true;
        }
    }

    private static bool IsOnExpander(object? source)
    {
        for (Visual? visual = source as Visual; visual != null; visual = visual.GetVisualParent())
        {
            if (visual is Border border && border.Classes.Contains(EXPANDER_CLASS))
            {
                return true;
            }

            if (visual is ListBoxItem)
            {
                return false;
            }
        }

        return false;
    }

    private static ObjectNode? NodeFrom(object? source)
    {
        for (Visual? visual = source as Visual; visual != null; visual = visual.GetVisualParent())
        {
            if (visual is ListBoxItem item)
            {
                return item.DataContext as ObjectNode;
            }
        }

        return null;
    }
}
