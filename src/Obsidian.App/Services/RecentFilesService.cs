using System.Collections.ObjectModel;
using System.Text.Json;
using Serilog;

namespace Obsidian.App.Services;

public sealed class RecentFileEntry
{
    public required string Path { get; init; }

    public required DateTimeOffset LastOpenedUtc { get; init; }

    public string FileName => System.IO.Path.GetFileName(Path);

    public string Directory => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;

    public string LastOpenedText => LastOpenedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    public bool Exists => File.Exists(Path);
}

/// <summary>Maintains the most recently opened binaries across sessions.</summary>
public sealed class RecentFilesService
{
    private const int MaximumEntries = 10;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly ILogger _logger;

    public RecentFilesService(ILogger logger)
    {
        _logger = logger.ForContext<RecentFilesService>();
        ShellPaths.EnsureCreated();
        Load();
    }

    public ObservableCollection<RecentFileEntry> Items { get; } = [];

    public event EventHandler? Changed;

    public void Add(string filePath)
    {
        var fullPath = System.IO.Path.GetFullPath(filePath);
        var existing = Items.FirstOrDefault(
            entry => string.Equals(entry.Path, fullPath, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            Items.Remove(existing);
        }

        Items.Insert(0, new RecentFileEntry
        {
            Path = fullPath,
            LastOpenedUtc = DateTimeOffset.UtcNow,
        });

        while (Items.Count > MaximumEntries)
        {
            Items.RemoveAt(Items.Count - 1);
        }

        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Remove(string filePath)
    {
        var existing = Items.FirstOrDefault(
            entry => string.Equals(entry.Path, filePath, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            return;
        }

        Items.Remove(existing);
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        Items.Clear();
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Load()
    {
        if (!File.Exists(ShellPaths.RecentFilesFile))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(ShellPaths.RecentFilesFile);
            var entries = JsonSerializer.Deserialize<List<RecentFileRecord>>(json, JsonOptions);
            if (entries is null)
            {
                return;
            }

            foreach (var entry in entries
                         .Where(e => !string.IsNullOrWhiteSpace(e.Path))
                         .Take(MaximumEntries))
            {
                Items.Add(new RecentFileEntry
                {
                    Path = entry.Path!,
                    LastOpenedUtc = entry.LastOpenedUtc,
                });
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.Warning(ex, "Recent files could not be loaded from {Path}", ShellPaths.RecentFilesFile);
        }
    }

    private void Save()
    {
        try
        {
            var records = Items
                .Select(entry => new RecentFileRecord(entry.Path, entry.LastOpenedUtc))
                .ToList();
            var json = JsonSerializer.Serialize(records, JsonOptions);
            var tempPath = ShellPaths.RecentFilesFile + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, ShellPaths.RecentFilesFile, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.Error(ex, "Recent files could not be saved to {Path}", ShellPaths.RecentFilesFile);
        }
    }

    private sealed record RecentFileRecord(string Path, DateTimeOffset LastOpenedUtc);
}
