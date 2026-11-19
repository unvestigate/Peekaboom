using System.ComponentModel;
using System.Text;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.VisualTree;

using Peekaboom.Docking;

namespace Peekaboom.UI;

public partial class PropertiesPanel : UserControl
{
    private PropertiesViewModel? mViewModel;

    public PropertiesPanel()
    {
        InitializeComponent();

        DataContextChanged += (_, _) => AttachViewModel();
        Rows.Tapped += (_, e) =>
        {
            if (RowFrom(e.Source) is PropertyRow row && row.RefId != 0)
            {
                mViewModel?.ReportRefClicked(row.RefId);
            }
        };
        Rows.KeyDown += OnRowsKeyDown;

        var menu = new ContextMenu();
        menu.Opening += OnMenuOpening;
        Rows.ContextMenu = menu;
    }

    private void AttachViewModel()
    {
        if (mViewModel != null)
        {
            mViewModel.HeadingChanged -= OnHeadingChanged;
        }

        mViewModel = DataContext as PropertiesViewModel;

        if (mViewModel != null)
        {
            mViewModel.HeadingChanged += OnHeadingChanged;
        }

        OnHeadingChanged();
    }

    private void OnHeadingChanged()
    {
        HeadingText.Text = mViewModel?.Heading ?? "";
    }

    private void OnRowsKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.C && e.KeyModifiers == KeyModifiers.Control && Rows.SelectedItem is PropertyRow row)
        {
            Copy(row.Name + " = " + row.Value);
            e.Handled = true;
        }
    }

    private void OnMenuOpening(object? sender, CancelEventArgs e)
    {
        ContextMenu? menu = Rows.ContextMenu;
        PropertiesViewModel? viewModel = mViewModel;
        if (menu == null || viewModel == null || viewModel.Rows.Count == 0)
        {
            e.Cancel = true;
            return;
        }

        var items = new List<Control>();
        if (Rows.SelectedItem is PropertyRow row && !row.IsHeader)
        {
            items.Add(MenuItemFor("Copy Value", () => Copy(row.Value)));
            items.Add(MenuItemFor("Copy Name and Value", () => Copy(row.Name + " = " + row.Value)));
            items.Add(new Separator());
        }

        items.Add(MenuItemFor("Copy All", () => CopyAll(viewModel)));
        menu.ItemsSource = items;
    }

    private void CopyAll(PropertiesViewModel viewModel)
    {
        var builder = new StringBuilder();
        builder.AppendLine(viewModel.Heading);
        foreach (PropertyRow row in viewModel.Rows)
        {
            if (row.IsHeader)
            {
                builder.AppendLine();
                builder.AppendLine(row.Name);
            }
            else
            {
                builder.Append("    ").Append(row.Name).Append(" = ").AppendLine(row.Value);
            }
        }

        Copy(builder.ToString());
    }

    private void Copy(string text)
    {
        IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null && text.Length > 0)
        {
            _ = clipboard.SetTextAsync(text);
        }
    }

    private static MenuItem MenuItemFor(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private static PropertyRow? RowFrom(object? source)
    {
        for (Visual? visual = source as Visual; visual != null; visual = visual.GetVisualParent())
        {
            if (visual is ListBoxItem item)
            {
                return item.DataContext as PropertyRow;
            }
        }

        return null;
    }
}
