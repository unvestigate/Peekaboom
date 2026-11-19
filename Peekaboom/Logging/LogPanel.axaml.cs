using System.Collections.Specialized;
using System.ComponentModel;
using System.Text;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Peekaboom.Logging;

public partial class LogPanel : UserControl
{
    private const double FOLLOW_THRESHOLD = 24.0;

    private static readonly TimeSpan ScrollInterval = TimeSpan.FromMilliseconds(50.0);

    private readonly DispatcherTimer mScrollTimer;

    private LogViewModel? mViewModel;
    private ScrollViewer? mScroller;
    private bool mFollowing = true;
    private bool mUserMovedView;

    public LogPanel()
    {
        InitializeComponent();

        DataContextChanged += (_, _) => AttachViewModel();

        mScrollTimer = new DispatcherTimer(DispatcherPriority.Loaded) { Interval = ScrollInterval };
        mScrollTimer.Tick += (_, _) => ScrollToNewest();

        Rows.AddHandler(ContextRequestedEvent, OnContextRequested, RoutingStrategies.Tunnel);
        Rows.KeyDown += OnKeyDown;

        Rows.AddHandler(PointerWheelChangedEvent, OnUserMovedView, RoutingStrategies.Tunnel);
        Rows.AddHandler(PointerPressedEvent, OnUserMovedView, RoutingStrategies.Tunnel);
        Rows.AddHandler(KeyDownEvent, OnUserMovedView, RoutingStrategies.Tunnel);

        var menu = new ContextMenu();
        menu.Opening += OnMenuOpening;
        Rows.ContextMenu = menu;
    }

    private void AttachViewModel()
    {
        if (mViewModel != null)
        {
            mViewModel.Entries.CollectionChanged -= OnEntriesChanged;
        }

        mViewModel = DataContext as LogViewModel;

        if (mViewModel != null)
        {
            mViewModel.Entries.CollectionChanged += OnEntriesChanged;
        }
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add)
        {
            return;
        }

        if (mUserMovedView)
        {
            mFollowing = IsAtBottom();
        }

        if (mFollowing && !mScrollTimer.IsEnabled)
        {
            mScrollTimer.Start();
        }
    }

    private void OnUserMovedView(object? sender, RoutedEventArgs e) => mUserMovedView = true;

    private void ScrollToNewest()
    {
        mScrollTimer.Stop();
        FindScroller()?.ScrollToEnd();
        mUserMovedView = false;
    }

    private bool IsAtBottom()
    {
        ScrollViewer? scroller = FindScroller();
        if (scroller == null)
        {
            return true;
        }

        return scroller.Extent.Height - scroller.Viewport.Height - scroller.Offset.Y <= FOLLOW_THRESHOLD;
    }

    private ScrollViewer? FindScroller()
    {
        if (mScroller != null)
        {
            return mScroller;
        }

        foreach (Visual visual in Rows.GetVisualDescendants())
        {
            if (visual is ScrollViewer scroller)
            {
                mScroller = scroller;
                break;
            }
        }

        return mScroller;
    }

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        LogEntry? entry = EntryFrom(e.Source);
        if (entry == null || Rows.SelectedItems == null)
        {
            return;
        }

        if (!Rows.SelectedItems.Contains(entry))
        {
            Rows.SelectedItems.Clear();
            Rows.SelectedItems.Add(entry);
        }
    }

    private void OnMenuOpening(object? sender, CancelEventArgs e)
    {
        ContextMenu? menu = Rows.ContextMenu;
        LogViewModel? viewModel = mViewModel;
        if (menu == null || viewModel == null)
        {
            e.Cancel = true;
            return;
        }

        var items = new List<Control>();
        List<LogEntry> selected = SelectedEntries();

        if (selected.Count > 0)
        {
            string header = selected.Count == 1 ? "Copy Line" : "Copy " + selected.Count + " Lines";
            items.Add(MenuItemFor(header, () => Copy(selected)));
        }

        items.Add(MenuItemFor("Copy All", () => Copy(viewModel.Entries)));
        items.Add(new Separator());
        items.Add(MenuItemFor("Clear", () => viewModel.Clear()));

        menu.ItemsSource = items;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.C && e.KeyModifiers == KeyModifiers.Control)
        {
            Copy(SelectedEntries());
            e.Handled = true;
        }
    }

    private List<LogEntry> SelectedEntries()
    {
        var result = new List<LogEntry>();
        if (mViewModel == null || Rows.SelectedItems == null || Rows.SelectedItems.Count == 0)
        {
            return result;
        }

        var chosen = new HashSet<LogEntry>();
        foreach (object? item in Rows.SelectedItems)
        {
            if (item is LogEntry entry)
            {
                chosen.Add(entry);
            }
        }

        foreach (LogEntry entry in mViewModel.Entries)
        {
            if (chosen.Contains(entry))
            {
                result.Add(entry);
            }
        }

        return result;
    }

    private void Copy(IEnumerable<LogEntry> entries)
    {
        var builder = new StringBuilder();

        foreach (LogEntry entry in entries)
        {
            builder.AppendLine(entry.Text);
        }

        IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (builder.Length > 0 && clipboard != null)
        {
            _ = clipboard.SetTextAsync(builder.ToString());
        }
    }

    private static MenuItem MenuItemFor(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private static LogEntry? EntryFrom(object? source)
    {
        for (Visual? visual = source as Visual; visual != null; visual = visual.GetVisualParent())
        {
            if (visual is ListBoxItem item && item.DataContext is LogEntry entry)
            {
                return entry;
            }
        }

        return null;
    }
}
