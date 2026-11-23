using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Obsidian.App.ViewModels;

public sealed record PropertyRowViewModel(
    string Label,
    string Value,
    bool IsMono = false,
    string? ToolTip = null);

public sealed record PropertyGroupViewModel(
    string Title,
    IReadOnlyList<PropertyRowViewModel> Rows);

/// <summary>Composite payload for header sub-trees.</summary>
public sealed record PropertyBag(
    string Title,
    IReadOnlyList<PropertyRowViewModel> Rows);

/// <summary>Explicit unavailable state for data that is not present in the image.</summary>
public sealed record NotAvailable(
    string Title,
    string Detail,
    string Phase);

public sealed partial class PropertiesViewModel : ObservableObject
{
    private const string DefaultEmptyMessage =
        "Select an item in Solution Explorer to inspect its properties.";

    public ObservableCollection<PropertyGroupViewModel> Groups { get; } = [];

    [ObservableProperty]
    private bool _hasContent;

    [ObservableProperty]
    private string _emptyMessage = DefaultEmptyMessage;

    public string? Subject { get; private set; }

    public void Show(IEnumerable<PropertyGroupViewModel> groups, string? subject = null)
    {
        Groups.Clear();
        foreach (var group in groups)
        {
            if (group.Rows.Count > 0)
            {
                Groups.Add(group);
            }
        }

        Subject = subject;
        HasContent = Groups.Count > 0;
        EmptyMessage = DefaultEmptyMessage;
    }

    public void Clear(string? message = null)
    {
        Groups.Clear();
        Subject = null;
        HasContent = false;
        EmptyMessage = message ?? DefaultEmptyMessage;
    }

    public string GetText()
    {
        if (Groups.Count == 0)
        {
            return EmptyMessage;
        }

        var lines = new List<string>();
        if (!string.IsNullOrEmpty(Subject))
        {
            lines.Add(Subject);
        }

        foreach (var group in Groups)
        {
            lines.Add(string.Empty);
            lines.Add(group.Title);
            lines.AddRange(group.Rows.Select(row => $"{row.Label}: {row.Value}"));
        }

        return string.Join(Environment.NewLine, lines).Trim();
    }
}
