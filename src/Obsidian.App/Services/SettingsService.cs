using System.Text.Json;
using Serilog;

namespace Obsidian.App.Services;

public enum AppTheme
{
    Dark = 0,
    Light = 1,
}

/// <summary>User-editable application settings, persisted as JSON.</summary>
public sealed class AppSettings
{
    public string Theme { get; set; } = nameof(AppTheme.Dark);

    public bool RememberWindowSize { get; set; } = true;

    public double WindowWidth { get; set; } = 1600;

    public double WindowHeight { get; set; } = 1000;

    public double? WindowLeft { get; set; }

    public double? WindowTop { get; set; }
}

/// <summary>
/// Owns settings persistence. Failures are logged and never allowed to take the
/// application down; the UI still receives a usable settings object.
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly ILogger _logger;

    public SettingsService(ILogger logger)
    {
        _logger = logger.ForContext<SettingsService>();
        ShellPaths.EnsureCreated();
        Current = Load();
    }

    public AppSettings Current { get; private set; }

    public AppTheme CurrentTheme =>
        Enum.TryParse<AppTheme>(Current.Theme, ignoreCase: true, out var theme)
            ? theme
            : AppTheme.Dark;

    public event EventHandler? Changed;

    public void SetTheme(AppTheme theme)
    {
        Current.Theme = theme.ToString();
        Save();
    }

    public void UpdateWindowBounds(double width, double height, double left, double top)
    {
        Current.WindowWidth = width;
        Current.WindowHeight = height;
        Current.WindowLeft = left;
        Current.WindowTop = top;
        Save();
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(Current, JsonOptions);
            var tempPath = ShellPaths.SettingsFile + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, ShellPaths.SettingsFile, overwrite: true);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.Error(ex, "Could not persist settings to {Path}", ShellPaths.SettingsFile);
        }
    }

    private AppSettings Load()
    {
        if (!File.Exists(ShellPaths.SettingsFile))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(ShellPaths.SettingsFile);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (JsonException ex)
        {
            _logger.Warning(ex, "Settings file is malformed; falling back to defaults.");
            TryBackupCorruptFile();
            return new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Error(ex, "Settings file could not be read; using defaults.");
            return new AppSettings();
        }
    }

    private void TryBackupCorruptFile()
    {
        try
        {
            var backup = ShellPaths.SettingsFile + $".corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}";
            File.Copy(ShellPaths.SettingsFile, backup, overwrite: true);
            _logger.Information("Corrupt settings backed up to {Path}", backup);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Could not back up the corrupt settings file.");
        }
    }
}
