using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Obsidian.Core.Formatting;
using Obsidian.Core.Model;

namespace Obsidian.App.ViewModels;

/// <summary>
/// Virtualized, searchable browser for the three analysis result collections.
/// It is intentionally a document, not another tree: thousands of rows remain
/// smooth and users can sort/scan columns without expanding nodes.
/// </summary>
public sealed partial class AnalysisBrowserViewModel : ObservableObject
{
    private readonly IReadOnlyList<FunctionAnalysis> _allFunctions;
    private readonly IReadOnlyList<StringLiteral> _allStrings;
    private readonly IReadOnlyList<CrossReference> _allReferences;
    private readonly ListCollectionView _functions;
    private readonly ListCollectionView _strings;
    private readonly ListCollectionView _references;

    public AnalysisBrowserViewModel(AnalysisResult analysis)
    {
        _allFunctions = analysis.Functions.OrderBy(function => function.Function.Rva).ToArray();
        _allStrings = analysis.Strings.OrderBy(value => value.Rva).ToArray();
        _allReferences = analysis.CrossReferences.OrderBy(reference => reference.FromRva).ToArray();

        _functions = new ListCollectionView(_allFunctions.ToList());
        _functions.Filter = FilterFunction;
        _strings = new ListCollectionView(_allStrings.ToList());
        _strings.Filter = FilterString;
        _references = new ListCollectionView(_allReferences.ToList());
        _references.Filter = FilterReference;

        Functions = _functions;
        Strings = _strings;
        References = _references;
        Refresh();
    }

    public ICollectionView Functions { get; }

    public ICollectionView Strings { get; }

    public ICollectionView References { get; }

    public string FunctionCountText => $"{_functions.Count:N0} functions";

    public string StringCountText => $"{_strings.Count:N0} strings";

    public string ReferenceCountText => $"{_references.Count:N0} references";

    public string SearchHint => "Filter name, RVA, section, kind or text";

    public event Action<object>? ActivateRequested;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private FunctionAnalysis? _selectedFunction;

    [ObservableProperty]
    private StringLiteral? _selectedString;

    [ObservableProperty]
    private CrossReference? _selectedReference;

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    public void Activate(object? item)
    {
        if (item is not null)
        {
            ActivateRequested?.Invoke(item);
        }
    }

    partial void OnSearchTextChanged(string value) => Refresh();

    partial void OnSelectedTabIndexChanged(int value) => Refresh();

    private void Refresh()
    {
        _functions.Refresh();
        _strings.Refresh();
        _references.Refresh();
        OnPropertyChanged(nameof(FunctionCountText));
        OnPropertyChanged(nameof(StringCountText));
        OnPropertyChanged(nameof(ReferenceCountText));
    }

    private bool FilterFunction(object item)
    {
        if (item is not FunctionAnalysis function || !HasSearch)
        {
            return item is FunctionAnalysis;
        }

        return Matches(function.Function.Name)
            || Matches(Hex.Rva(function.Function.Rva))
            || Matches(function.Function.SectionName)
            || Matches(function.Function.SourceText);
    }

    private bool FilterString(object item)
    {
        if (item is not StringLiteral value || !HasSearch)
        {
            return item is StringLiteral;
        }

        return Matches(value.Value)
            || Matches(value.SectionName)
            || Matches(value.EncodingText)
            || Matches(Hex.Rva(value.Rva));
    }

    private bool FilterReference(object item)
    {
        if (item is not CrossReference reference || !HasSearch)
        {
            return item is CrossReference;
        }

        return Matches(reference.KindText)
            || Matches(reference.FromSection)
            || Matches(reference.SourceText)
            || Matches(Hex.Rva(reference.FromRva))
            || Matches(Hex.Rva(reference.ToRva));
    }

    private bool HasSearch => !string.IsNullOrWhiteSpace(SearchText);

    private bool Matches(string value) =>
        value.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase);
}
