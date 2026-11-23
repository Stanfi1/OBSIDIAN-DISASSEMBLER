using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using Serilog.Core;
using Serilog.Events;

namespace Obsidian.App.Services;

public enum UiLogLevel
{
    Verbose = 0,
    Debug,
    Information,
    Warning,
    Error,
    Fatal,
}

public sealed record UiLogEntry(
    DateTimeOffset Timestamp,
    UiLogLevel Level,
    string Message,
    string? Exception)
{
    public string TimeText => Timestamp.ToString("HH:mm:ss");

    public string DisplayText => Exception is null
        ? Message
        : Message + Environment.NewLine + Environment.NewLine + Exception;
}

/// <summary>
/// Serilog sink that projects log events into an observable collection for the
/// Output tool window. Entries are added on the dispatcher thread and capped so
/// a long debugging session cannot exhaust memory.
/// </summary>
public sealed class UiLogSink : ILogEventSink
{
    private const int MaximumEntries = 3000;

    public ObservableCollection<UiLogEntry> Entries { get; } = [];

    public event EventHandler<UiLogEntry>? EntryAdded;

    public void Emit(LogEvent logEvent)
    {
        var entry = new UiLogEntry(
            logEvent.Timestamp.ToLocalTime(),
            Map(logEvent.Level),
            logEvent.RenderMessage(CultureInfo.InvariantCulture),
            logEvent.Exception?.ToString());

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            Add(entry);
        }
        else
        {
            dispatcher.BeginInvoke(new Action(() => Add(entry)));
        }
    }

    public void Clear()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            Entries.Clear();
        }
        else
        {
            dispatcher.BeginInvoke(new Action(() => Entries.Clear()));
        }
    }

    public string GetText()
    {
        return string.Join(
            Environment.NewLine,
            Entries.Select(e =>
                $"{e.Timestamp:HH:mm:ss.fff} [{e.Level.ToString().ToUpperInvariant()}] {e.Message}" +
                (e.Exception is null ? string.Empty : Environment.NewLine + e.Exception)));
    }

    private void Add(UiLogEntry entry)
    {
        Entries.Add(entry);
        while (Entries.Count > MaximumEntries)
        {
            Entries.RemoveAt(0);
        }

        EntryAdded?.Invoke(this, entry);
    }

    private static UiLogLevel Map(LogEventLevel level) => level switch
    {
        LogEventLevel.Verbose => UiLogLevel.Verbose,
        LogEventLevel.Debug => UiLogLevel.Debug,
        LogEventLevel.Information => UiLogLevel.Information,
        LogEventLevel.Warning => UiLogLevel.Warning,
        LogEventLevel.Error => UiLogLevel.Error,
        LogEventLevel.Fatal => UiLogLevel.Fatal,
        _ => UiLogLevel.Information,
    };
}
