using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Obsidian.App.ViewModels;

public enum ExplorerIcon
{
    Binary,
    Folder,
    FolderOpen,
    Header,
    Section,
    Code,
    Data,
    Module,
    Function,
    Export,
    String,
    Resource,
    Symbol,
    CrossReference,
    Property,
    Info,
    Warning,
    Analysis,
}

public enum ExplorerNodeKind
{
    Binary,
    Folder,
    Property,
    Section,
    ImportModule,
    ImportFunction,
    Export,
    Finding,
    DataDirectory,
    Function,
    String,
    CrossReference,
    Analysis,
    Info,
}

public enum ExplorerAnnotationAction
{
    RenameFunction = 0,
    EditComment,
    AddBookmark,
    RemoveBookmark,
    OpenCallGraph,
}

/// <summary>
/// One item in the Solution Explorer tree. Payload is the domain object the
/// Properties window describes; it is never mutated here.
/// </summary>
public sealed partial class ExplorerNode : ObservableObject
{
    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string? _detail;

    public string? ToolTip { get; init; }

    public required ExplorerIcon Icon { get; init; }

    public required ExplorerNodeKind Kind { get; init; }

    [ObservableProperty]
    private object? _payload;

    public ExplorerNode? Parent { get; private set; }

    public ObservableCollection<ExplorerNode> Children { get; } = [];

    [ObservableProperty]
    private bool _isExpanded;

    public bool HasChildren => Children.Count > 0;

    public void AddChild(ExplorerNode child)
    {
        child.Parent = this;
        Children.Add(child);
    }

    public IEnumerable<ExplorerNode> Descendants()
    {
        foreach (var child in Children)
        {
            yield return child;
            foreach (var descendant in child.Descendants())
            {
                yield return descendant;
            }
        }
    }

    public bool Matches(string filter) =>
        Title.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || (Detail?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false);
}
