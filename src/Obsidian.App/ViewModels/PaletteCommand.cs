using System.Windows.Input;

namespace Obsidian.App.ViewModels;

public sealed record PaletteCommand(
    string Title,
    string Category,
    string Shortcut,
    ICommand Command,
    object? Parameter = null)
{
    public string SearchText => $"{Title} {Category} {Shortcut}";
}

public sealed class RecentMenuEntry
{
    public required string Header { get; init; }

    public string? Path { get; init; }

    public ICommand? Command { get; init; }

    public bool IsEnabled => Command is not null;

    public string? ToolTip => Path;
}

public sealed record ShortcutInfo(string Description, string Gesture);
