using System.Diagnostics;

namespace Obsidian.App.Services;

/// <summary>Small shell operations, kept in one place for error handling.</summary>
public static class ShellHelper
{
    public static void OpenFolder(string path)
    {
        ShellPaths.EnsureCreated();
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
        });
    }

    public static void ShowInExplorer(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("The file no longer exists.", filePath);
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{filePath}\"",
            UseShellExecute = true,
        });
    }
}
