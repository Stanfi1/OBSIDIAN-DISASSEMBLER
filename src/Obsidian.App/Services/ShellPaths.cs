namespace Obsidian.App.Services;

/// <summary>Well-known application data locations, created on first use.</summary>
public static class ShellPaths
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Obsidian");

    public static string LogDirectory { get; } = Path.Combine(DataDirectory, "logs");

    public static string SettingsFile { get; } = Path.Combine(DataDirectory, "settings.json");

    public static string RecentFilesFile { get; } = Path.Combine(DataDirectory, "recent.json");

    public static string LayoutFile { get; } = Path.Combine(DataDirectory, "layout.xml");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogDirectory);
    }
}
