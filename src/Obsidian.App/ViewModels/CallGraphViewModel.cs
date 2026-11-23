using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Obsidian.Core.Formatting;
using Obsidian.Core.Model;

namespace Obsidian.App.ViewModels;

public enum CallGraphNodeRole
{
    Caller = 0,
    Target,
    Callee,
}

public sealed class CallGraphNodeViewModel
{
    public CallGraphNodeViewModel(
        FunctionAnalysis? function,
        string title,
        CallGraphNodeRole role,
        double x,
        double y)
    {
        Function = function;
        Title = title;
        Role = role;
        X = x;
        Y = y;
        Width = 220;
        Height = 56;
        Subtitle = function is null
            ? "external or unresolved"
            : $"{Hex.Rva(function.Function.Rva)} · {function.Function.BasicBlockCount} blocks";
        RoleText = role switch
        {
            CallGraphNodeRole.Caller => "CALLER",
            CallGraphNodeRole.Target => "FUNCTION",
            _ => "CALLEE",
        };
        ToolTip = function is null
            ? title
            : $"{title}\n{Hex.Rva(function.Function.Rva)} · {function.Function.SourceText}";
    }

    public FunctionAnalysis? Function { get; }

    public string Title { get; }

    public string Subtitle { get; }

    public string RoleText { get; }

    public string ToolTip { get; }

    public CallGraphNodeRole Role { get; }

    public double X { get; }

    public double Y { get; }

    public double Width { get; }

    public double Height { get; }

    public bool IsTarget => Role == CallGraphNodeRole.Target;
}

public sealed class CallGraphEdgeViewModel
{
    public CallGraphEdgeViewModel(CallGraphNodeViewModel source, CallGraphNodeViewModel target)
    {
        Source = source;
        Target = target;
        var startX = source.X + source.Width / 2;
        var startY = source.Y + source.Height;
        var endX = target.X + target.Width / 2;
        var endY = target.Y;
        var midY = (startY + endY) / 2;

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(startX, startY), isFilled: false, isClosed: false);
            context.BezierTo(
                new Point(startX, midY),
                new Point(endX, midY),
                new Point(endX, endY),
                isStroked: true,
                isSmoothJoin: false);
        }

        geometry.Freeze();
        Geometry = geometry;
        ArrowPoints =
        [
            new Point(endX, endY),
            new Point(endX - 5, endY - 9),
            new Point(endX + 5, endY - 9),
        ];
        ToolTip = $"{source.Title} → {target.Title}";
    }

    public CallGraphNodeViewModel Source { get; }

    public CallGraphNodeViewModel Target { get; }

    public Geometry Geometry { get; }

    public PointCollection ArrowPoints { get; }

    public string ToolTip { get; }
}

/// <summary>
/// Focused call-neighborhood graph: direct callers above the selected function,
/// direct callees below. It is intentionally not a whole-program graph; that
/// keeps the view readable and deterministic for large binaries.
/// </summary>
public sealed partial class CallGraphViewModel : ObservableObject
{
    private const double NodeWidth = 220;
    private const double NodeHeight = 56;
    private const double HorizontalGap = 24;
    private const double RowGap = 48;
    private const double Margin = 24;
    private const int MaxCallers = 10;
    private const int MaxCallees = 16;

    private readonly BinaryImage _image;
    private readonly AnalysisResult _analysis;
    private IReadOnlyDictionary<uint, AddressAnnotation> _labels;

    public CallGraphViewModel(
        BinaryImage image,
        AnalysisResult analysis,
        IReadOnlyDictionary<uint, AddressAnnotation>? labels = null)
    {
        _image = image;
        _analysis = analysis;
        _labels = labels ?? new Dictionary<uint, AddressAnnotation>();
        var initial = analysis.FindFunction(image.EntryPointRva) ?? analysis.Functions.FirstOrDefault();
        if (initial is not null)
        {
            SelectFunction(initial);
        }
    }

    public ObservableCollection<CallGraphNodeViewModel> Nodes { get; } = [];

    public ObservableCollection<CallGraphEdgeViewModel> Edges { get; } = [];

    [ObservableProperty]
    private FunctionAnalysis? _selectedFunction;

    [ObservableProperty]
    private double _graphWidth = 1;

    [ObservableProperty]
    private double _graphHeight = 1;

    [ObservableProperty]
    private string _statusText = "No function selected";

    [ObservableProperty]
    private bool _hasNeighborhood;

    public string FunctionHeader =>
        SelectedFunction is { } selected
            ? Label(selected.Function)
            : "No function";

    public void SelectNode(CallGraphNodeViewModel node)
    {
        if (node.Function is not null)
        {
            SelectFunction(node.Function);
        }
    }

    public void SelectFunction(FunctionAnalysis function)
    {
        SelectedFunction = function;
        Nodes.Clear();
        Edges.Clear();

        var callerIds = _analysis.CallersOf(function.Function.Rva)
            .Where(rva => rva != function.Function.Rva)
            .Distinct()
            .ToArray();
        var calleeIds = _analysis.CalleesOf(function.Function.Rva)
            .Where(rva => rva != function.Function.Rva)
            .Distinct()
            .ToArray();

        var callers = callerIds
            .Select(_analysis.FindFunction)
            .OfType<FunctionAnalysis>()
            .Take(MaxCallers)
            .ToArray();
        var callees = calleeIds
            .Select(_analysis.FindFunction)
            .OfType<FunctionAnalysis>()
            .Take(MaxCallees)
            .ToArray();

        var rowCount = Math.Max(callers.Length, callees.Length);
        var width = Math.Max(NodeWidth, rowCount * (NodeWidth + HorizontalGap) - HorizontalGap) + Margin * 2;
        var targetX = (width - NodeWidth) / 2;
        var callersHeight = callers.Length == 0 ? 0 : NodeHeight + RowGap;
        var targetY = Margin + callersHeight;
        var calleeY = targetY + NodeHeight + RowGap;

        var target = new CallGraphNodeViewModel(function, Label(function.Function), CallGraphNodeRole.Target, targetX, targetY);
        Nodes.Add(target);

        AddRow(callers, CallGraphNodeRole.Caller, Margin, width, target);
        AddRow(callees, CallGraphNodeRole.Callee, calleeY, width, target);

        GraphWidth = width;
        GraphHeight = callees.Length == 0
            ? targetY + NodeHeight + Margin
            : calleeY + NodeHeight + Margin;
        HasNeighborhood = callers.Length > 0 || callees.Length > 0;
        StatusText = $"{callerIds.Length:N0} caller(s) · {calleeIds.Length:N0} callee(s)" +
                     (callerIds.Length > callers.Length || calleeIds.Length > callees.Length
                         ? " · capped for readability"
                         : string.Empty);
        OnPropertyChanged(nameof(FunctionHeader));
    }

    public void UpdateLabels(IReadOnlyDictionary<uint, AddressAnnotation> labels)
    {
        _labels = labels;
        if (SelectedFunction is not null)
        {
            SelectFunction(SelectedFunction);
        }
    }

    private void AddRow(
        IReadOnlyList<FunctionAnalysis> functions,
        CallGraphNodeRole role,
        double y,
        double graphWidth,
        CallGraphNodeViewModel target)
    {
        if (functions.Count == 0)
        {
            return;
        }

        var totalWidth = functions.Count * (NodeWidth + HorizontalGap) - HorizontalGap;
        var startX = (graphWidth - totalWidth) / 2;
        for (var index = 0; index < functions.Count; index++)
        {
            var function = functions[index];
            var node = new CallGraphNodeViewModel(
                function,
                Label(function.Function),
                role,
                startX + index * (NodeWidth + HorizontalGap),
                y);
            Nodes.Add(node);
            Edges.Add(role == CallGraphNodeRole.Caller
                ? new CallGraphEdgeViewModel(node, target)
                : new CallGraphEdgeViewModel(target, node));
        }
    }

    private string Label(DiscoveredFunction function) =>
        _labels.TryGetValue(function.Rva, out var annotation) &&
        !string.IsNullOrWhiteSpace(annotation.UserName)
            ? annotation.UserName!
            : function.Name;
}
