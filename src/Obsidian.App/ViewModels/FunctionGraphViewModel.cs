using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Obsidian.Core.Formatting;
using Obsidian.Core.Model;

namespace Obsidian.App.ViewModels;

public sealed class CfgNodeViewModel
{
    public CfgNodeViewModel(
        FunctionAnalysis function,
        FunctionBasicBlock block,
        BinaryImage image,
        double x,
        double y,
        int layer)
    {
        Function = function;
        Block = block;
        Layer = layer;
        X = x;
        Y = y;
        Width = 240;

        var instructions = function.Instructions
            .Skip(block.InstructionOffset)
            .Take(block.InstructionCount)
            .Take(3)
            .Select(instruction => Trim($"{instruction.Mnemonic} {instruction.Operands}".Trim(), 32))
            .ToArray();

        Lines = instructions;
        Height = 38 + instructions.Length * 15 + 6;

        Title = $"loc_{image.ImageBase + block.StartRva:X}";
        Subtitle = $"{Hex.Rva(block.StartRva)} · {block.InstructionCount} instr";
        IsEntry = block.IsEntry;
        IsExit = block.IsExit;
        IsInvalid = block.Kind == BasicBlockKind.Invalid;
        ToolTip =
            $"{Title}\n{Subtitle}\n{block.KindText}\n" +
            $"Predecessors: {block.PredecessorStarts.Count} · Successors: {block.SuccessorStarts.Count}";
    }

    public FunctionAnalysis Function { get; }

    public FunctionBasicBlock Block { get; }

    public int Layer { get; }

    public double X { get; }

    public double Y { get; }

    public double Width { get; }

    public double Height { get; }

    public string Title { get; }

    public string Subtitle { get; }

    public IReadOnlyList<string> Lines { get; }

    public bool IsEntry { get; }

    public bool IsExit { get; }

    public bool IsInvalid { get; }

    public string ToolTip { get; }

    private static string Trim(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}

public sealed class CfgEdgeViewModel
{
    public CfgEdgeViewModel(FunctionBasicBlock source, FunctionBasicBlock target, Geometry geometry, bool isBackEdge)
    {
        Source = source;
        Target = target;
        Geometry = geometry;
        IsBackEdge = isBackEdge;
        ToolTip = $"{Hex.Rva(source.StartRva)} → {Hex.Rva(target.StartRva)}";
    }

    public FunctionBasicBlock Source { get; }

    public FunctionBasicBlock Target { get; }

    public Geometry Geometry { get; }

    public bool IsBackEdge { get; }

    public string ToolTip { get; }
}

/// <summary>
/// Interactive control-flow graph model. Layout is a deterministic layered
/// BFS: entry at layer 0, successors on later layers, back edges drawn in a
/// warning colour. No external graph library is required.
/// </summary>
public sealed partial class FunctionGraphViewModel : ObservableObject
{
    private readonly BinaryImage _image;
    private readonly AnalysisResult _analysis;
    private IReadOnlyDictionary<uint, AddressAnnotation> _labels;

    public FunctionGraphViewModel(
        BinaryImage image,
        AnalysisResult analysis,
        IReadOnlyDictionary<uint, AddressAnnotation>? labels = null)
    {
        _image = image;
        _analysis = analysis;
        _labels = labels ?? new Dictionary<uint, AddressAnnotation>();
        Functions = analysis.Functions
            .OrderBy(function => function.Function.Rva)
            .ToArray();

        var initial = analysis.FindFunction(image.EntryPointRva) ?? Functions.FirstOrDefault();
        SelectedFunction = initial;
    }

    public void UpdateLabels(IReadOnlyDictionary<uint, AddressAnnotation> labels)
    {
        _labels = labels;
        OnPropertyChanged(nameof(FunctionHeader));
    }

    public IReadOnlyList<FunctionAnalysis> Functions { get; }

    public ObservableCollection<CfgNodeViewModel> Nodes { get; } = [];

    public ObservableCollection<CfgEdgeViewModel> Edges { get; } = [];

    public event Action<uint>? NavigateToRvaRequested;

    [ObservableProperty]
    private FunctionAnalysis? _selectedFunction;

    [ObservableProperty]
    private double _graphWidth = 1;

    [ObservableProperty]
    private double _graphHeight = 1;

    public string FunctionHeader =>
        SelectedFunction is { } selected
            ? _labels.TryGetValue(selected.Function.Rva, out var annotation) &&
              !string.IsNullOrWhiteSpace(annotation.UserName)
                ? annotation.UserName!
                : selected.Function.Name
            : "No function";

    public string GraphSummary => SelectedFunction is { } function
        ? $"{function.Blocks.Count:N0} blocks · {function.EdgeCount:N0} edges · complexity {function.CyclomaticComplexity}"
        : "No basic blocks";

    public string FunctionDetails => SelectedFunction is { } function
        ? $"{Hex.Rva(function.Function.Rva)} – {Hex.Rva(function.Function.EndRva)} · {function.Instructions.Count:N0} instructions"
        : string.Empty;

    public void SelectFunction(FunctionAnalysis function)
    {
        if (ReferenceEquals(SelectedFunction, function))
        {
            return;
        }

        SelectedFunction = function;
    }

    public void NavigateToBlock(CfgNodeViewModel node) =>
        NavigateToRvaRequested?.Invoke(node.Block.StartRva);

    public void NavigateToFunctionEntry()
    {
        if (SelectedFunction?.Function is { } function)
        {
            NavigateToRvaRequested?.Invoke(function.Rva);
        }
    }

    partial void OnSelectedFunctionChanged(FunctionAnalysis? value)
    {
        BuildGraph();
        OnPropertyChanged(nameof(FunctionHeader));
        OnPropertyChanged(nameof(GraphSummary));
        OnPropertyChanged(nameof(FunctionDetails));
    }

    private void BuildGraph()
    {
        Nodes.Clear();
        Edges.Clear();

        if (SelectedFunction is not { Blocks.Count: > 0 } function)
        {
            GraphWidth = 1;
            GraphHeight = 1;
            return;
        }

        var blocks = function.Blocks;
        var byStart = blocks.ToDictionary(block => block.StartRva);
        var layers = ComputeLayers(function);

        var nodesByBlock = new Dictionary<uint, CfgNodeViewModel>();
        const double margin = 36;
        const double nodeGapX = 24;
        const double rowGap = 34;
        const double nodeWidth = 240;

        var groups = blocks
            .GroupBy(block => layers.GetValueOrDefault(block.StartRva, 0))
            .OrderBy(group => group.Key)
            .ToArray();

        var maxRowWidth = groups
            .Select(group => group.Count() * nodeWidth + (group.Count() - 1) * nodeGapX)
            .DefaultIfEmpty(nodeWidth)
            .Max();

        var y = margin;
        foreach (var layerGroup in groups)
        {
            var layer = layerGroup.Key;
            var rowWidth = layerGroup.Count() * nodeWidth + (layerGroup.Count() - 1) * nodeGapX;
            var x = margin + (maxRowWidth - rowWidth) / 2;

            foreach (var block in layerGroup.OrderBy(block => block.Index))
            {
                var node = new CfgNodeViewModel(function, block, _image, x, y, layer);
                Nodes.Add(node);
                nodesByBlock[block.StartRva] = node;
                x += nodeWidth + nodeGapX;
            }

            y += layerGroup.Max(block => BlockHeight(function, block)) + rowGap;
        }

        foreach (var block in blocks)
        {
            var source = nodesByBlock.GetValueOrDefault(block.StartRva);
            if (source is null)
            {
                continue;
            }

            foreach (var successorStart in block.SuccessorStarts)
            {
                if (!nodesByBlock.TryGetValue(successorStart, out var target))
                {
                    continue;
                }

                var isBackEdge = target.Layer <= source.Layer;
                var geometry = CreateEdgeGeometry(source, target, isBackEdge);
                Edges.Add(new CfgEdgeViewModel(block, byStart[successorStart], geometry, isBackEdge));
            }
        }

        GraphWidth = Math.Max(1, maxRowWidth + margin * 2);
        GraphHeight = Math.Max(1, y + margin - rowGap);
    }

    private static Dictionary<uint, int> ComputeLayers(FunctionAnalysis function)
    {
        var layers = new Dictionary<uint, int>();
        var entry = function.EntryBlock;
        if (entry is null)
        {
            return layers;
        }

        layers[entry.StartRva] = 0;
        var queue = new Queue<FunctionBasicBlock>();
        queue.Enqueue(entry);

        while (queue.Count > 0)
        {
            var block = queue.Dequeue();
            var nextLayer = layers[block.StartRva] + 1;

            foreach (var successor in block.SuccessorStarts)
            {
                var target = function.FindBlockByStart(successor);
                if (target is null || layers.ContainsKey(successor))
                {
                    continue;
                }

                layers[successor] = nextLayer;
                queue.Enqueue(target);
            }
        }

        foreach (var block in function.Blocks)
        {
            layers.TryAdd(block.StartRva, 0);
        }

        return layers;
    }

    private static double BlockHeight(FunctionAnalysis function, FunctionBasicBlock block) =>
        38 + Math.Min(block.InstructionCount, 3) * 15 + 6;

    private static Geometry CreateEdgeGeometry(CfgNodeViewModel source, CfgNodeViewModel target, bool backEdge)
    {
        var sourceX = source.X + source.Width / 2;
        var sourceY = source.Y + source.Height;
        var targetX = target.X + target.Width / 2;
        var targetY = target.Y;

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(sourceX, sourceY), isFilled: false, isClosed: false);
            var control1 = new Point(sourceX + (backEdge ? 80 : 0), sourceY + 38);
            var control2 = new Point(targetX + (backEdge ? 80 : 0), targetY - 38);
            context.BezierTo(control1, control2, new Point(targetX, targetY), isStroked: true, isSmoothJoin: false);

            // Arrowhead pointing down into the target block.
            context.BeginFigure(new Point(targetX - 4, targetY - 9), isFilled: false, isClosed: false);
            context.LineTo(new Point(targetX, targetY), isStroked: true, isSmoothJoin: false);
            context.LineTo(new Point(targetX + 4, targetY - 9), isStroked: true, isSmoothJoin: false);
        }

        geometry.Freeze();
        return geometry;
    }
}
