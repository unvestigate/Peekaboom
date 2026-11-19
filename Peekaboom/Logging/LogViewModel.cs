using System.Collections.ObjectModel;

using Avalonia.Media;

using Dock.Model.Mvvm.Controls;

namespace Peekaboom.Logging;

public enum LogSeverity
{
    Info,
    Warning,
    Error,
}

public sealed class LogEntry
{
    private static readonly IBrush InfoBrush = new SolidColorBrush(Color.FromRgb(0xDA, 0xDA, 0xDA));
    private static readonly IBrush WarningBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0xC2, 0x4A));
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0x6C, 0x6C));

    public LogEntry(LogSeverity severity, string text)
    {
        Severity = severity;
        Text = text;
        Display = "> " + text;
    }

    public LogSeverity Severity { get; }

    public string Text { get; }

    public string Display { get; }

    public IBrush Brush => Severity switch
    {
        LogSeverity.Warning => WarningBrush,
        LogSeverity.Error => ErrorBrush,
        _ => InfoBrush,
    };
}

public sealed class LogViewModel : Tool
{
    public ObservableCollection<LogEntry> Entries { get; } = new();

    public void Add(string text, LogSeverity severity) => Entries.Add(new LogEntry(severity, text));

    public void Info(string text) => Entries.Add(new LogEntry(LogSeverity.Info, text));

    public void Warning(string text) => Entries.Add(new LogEntry(LogSeverity.Warning, text));

    public void Error(string text) => Entries.Add(new LogEntry(LogSeverity.Error, text));

    public void Clear() => Entries.Clear();
}
