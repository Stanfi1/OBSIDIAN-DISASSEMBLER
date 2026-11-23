namespace Obsidian.Core.Model;

/// <summary>Why an address was selected as a function seed.</summary>
public enum FunctionSource
{
    EntryPoint = 0,
    Export,
    CallTarget,
    ExceptionRuntimeFunction,
    TlsCallback,
}

public enum CrossReferenceKind
{
    Call = 0,
    Jump,
    ConditionalJump,
    Data,
}

public enum BasicBlockKind
{
    Normal = 0,
    Return,
    Jump,
    ConditionalBranch,
    IndirectBranch,
    Invalid,
}

public enum StringEncodingKind
{
    Ascii = 0,
    Utf16Le,
}

/// <summary>Stable metadata for one discovered function.</summary>
public sealed record DiscoveredFunction(
    uint Rva,
    ulong VirtualAddress,
    string Name,
    string SectionName,
    uint EndRva,
    int InstructionCount,
    int BasicBlockCount,
    IReadOnlyList<FunctionSource> Sources)
{
    public bool IsEntryPoint => Sources.Contains(FunctionSource.EntryPoint);

    public uint Size => EndRva > Rva ? EndRva - Rva : 0;

    public string SourceText => string.Join(", ", Sources.Select(source => source switch
    {
        FunctionSource.EntryPoint => "Entry point",
        FunctionSource.Export => "Export",
        FunctionSource.CallTarget => "Call target",
        FunctionSource.ExceptionRuntimeFunction => "Exception table",
        FunctionSource.TlsCallback => "TLS callback",
        _ => source.ToString(),
    }));
}

/// <summary>
/// One basic block inside a function. InstructionOffset/InstructionCount index
/// into the owning FunctionAnalysis.Instructions list.
/// </summary>
public sealed record FunctionBasicBlock(
    int Index,
    uint StartRva,
    uint EndRva,
    int InstructionOffset,
    int InstructionCount,
    BasicBlockKind Kind,
    IReadOnlyList<uint> SuccessorStarts,
    IReadOnlyList<uint> PredecessorStarts,
    bool IsEntry,
    bool IsExit)
{
    public string KindText => Kind switch
    {
        BasicBlockKind.Return => "Return",
        BasicBlockKind.Jump => "Jump",
        BasicBlockKind.ConditionalBranch => "Conditional branch",
        BasicBlockKind.IndirectBranch => "Indirect branch",
        BasicBlockKind.Invalid => "Invalid",
        _ => "Fall through",
    };
}

/// <summary>Function plus its reachable instructions and CFG blocks.</summary>
public sealed class FunctionAnalysis
{
    public required DiscoveredFunction Function { get; init; }
    public required IReadOnlyList<DisassembledInstruction> Instructions { get; init; }
    public required IReadOnlyList<FunctionBasicBlock> Blocks { get; init; }
    public required int EdgeCount { get; init; }

    public FunctionBasicBlock? EntryBlock => Blocks.FirstOrDefault(block => block.IsEntry) ?? Blocks.FirstOrDefault();

    public int CyclomaticComplexity => Blocks.Count == 0 ? 0 : EdgeCount - Blocks.Count + 2;

    public FunctionBasicBlock? FindBlock(uint rva) =>
        Blocks.FirstOrDefault(block => rva >= block.StartRva && rva < block.EndRva);

    public IEnumerable<FunctionBasicBlock> Successors(FunctionBasicBlock block) =>
        block.SuccessorStarts
            .Select(FindBlockByStart)
            .OfType<FunctionBasicBlock>();

    public FunctionBasicBlock? FindBlockByStart(uint startRva) =>
        Blocks.FirstOrDefault(block => block.StartRva == startRva);
}

/// <summary>A code or data reference discovered while walking instructions.</summary>
public sealed record CrossReference(
    uint FromRva,
    uint ToRva,
    CrossReferenceKind Kind,
    string FromSection,
    string? ToSection,
    string SourceText)
{
    public string KindText => Kind switch
    {
        CrossReferenceKind.Call => "Call",
        CrossReferenceKind.Jump => "Jump",
        CrossReferenceKind.ConditionalJump => "Conditional jump",
        CrossReferenceKind.Data => "Data reference",
        _ => Kind.ToString(),
    };
}

/// <summary>An ASCII or UTF-16LE candidate string found in file-backed data.</summary>
public sealed record StringLiteral(
    uint Rva,
    long FileOffset,
    string Value,
    StringEncodingKind Encoding,
    string SectionName,
    int ByteLength,
    IReadOnlyList<CrossReference> References)
{
    public int ReferenceCount => References.Count;

    public string EncodingText => Encoding == StringEncodingKind.Ascii ? "ASCII" : "UTF-16LE";

    public string Preview => Value.Length <= 80 ? Value : Value[..80] + "…";
}

/// <summary>
/// Immutable result of the Phase 4 analysis passes. Function discovery is
/// seed-based (entry point, exports, call targets, exception/TLS callbacks);
/// it is intentionally not a full signature/type analysis.
/// </summary>
public sealed class AnalysisResult
{
    private Dictionary<uint, FunctionAnalysis>? _functionsByStart;
    private Dictionary<uint, FunctionAnalysis>? _functionByInstruction;
    private ILookup<uint, CrossReference>? _xrefsTo;
    private ILookup<uint, CrossReference>? _xrefsFrom;
    private Dictionary<uint, List<uint>>? _callers;
    private Dictionary<uint, List<uint>>? _callees;

    public required IReadOnlyList<FunctionAnalysis> Functions { get; init; }
    public required IReadOnlyList<StringLiteral> Strings { get; init; }
    public required IReadOnlyList<CrossReference> CrossReferences { get; init; }
    public required IReadOnlyList<string> Diagnostics { get; init; }
    public bool IsTruncated { get; init; }
    public string? Message { get; init; }

    public int FunctionCount => Functions.Count;

    public int StringCount => Strings.Count;

    public int CrossReferenceCount => CrossReferences.Count;

    public bool HasFunctions => Functions.Count > 0;

    public IReadOnlyList<CrossReference> XrefsTo(uint rva) =>
        XrefsToLookup[rva].ToArray();

    public IReadOnlyList<CrossReference> XrefsFrom(uint rva) =>
        XrefsFromLookup[rva].ToArray();

    public FunctionAnalysis? FindFunction(uint startRva) =>
        FunctionsByStart.GetValueOrDefault(startRva);

    public FunctionAnalysis? FindFunctionContaining(uint rva)
    {
        foreach (var function in Functions)
        {
            if (rva >= function.Function.Rva && rva < function.Function.EndRva)
            {
                return function;
            }
        }

        return null;
    }

    public FunctionAnalysis? FindFunctionForInstruction(uint instructionRva) =>
        FunctionByInstruction.GetValueOrDefault(instructionRva);

    public IReadOnlyList<uint> CallersOf(uint functionRva) =>
        Callers.GetValueOrDefault(functionRva, []);

    public IReadOnlyList<uint> CalleesOf(uint functionRva) =>
        Callees.GetValueOrDefault(functionRva, []);

    private Dictionary<uint, FunctionAnalysis> FunctionsByStart =>
        _functionsByStart ??= Functions
            .GroupBy(function => function.Function.Rva)
            .ToDictionary(group => group.Key, group => group.First());

    private Dictionary<uint, FunctionAnalysis> FunctionByInstruction
    {
        get
        {
            if (_functionByInstruction is not null)
            {
                return _functionByInstruction;
            }

            _functionByInstruction = [];
            foreach (var function in Functions)
            {
                foreach (var instruction in function.Instructions)
                {
                    _functionByInstruction.TryAdd(instruction.Rva, function);
                }
            }

            return _functionByInstruction;
        }
    }

    private ILookup<uint, CrossReference> XrefsToLookup =>
        _xrefsTo ??= CrossReferences.ToLookup(reference => reference.ToRva);

    private ILookup<uint, CrossReference> XrefsFromLookup =>
        _xrefsFrom ??= CrossReferences.ToLookup(reference => reference.FromRva);

    private Dictionary<uint, List<uint>> Callers => _callers ??= BuildCallGraph().Callers;

    private Dictionary<uint, List<uint>> Callees => _callees ??= BuildCallGraph().Callees;

    private (Dictionary<uint, List<uint>> Callers, Dictionary<uint, List<uint>> Callees) BuildCallGraph()
    {
        var callers = new Dictionary<uint, List<uint>>();
        var callees = new Dictionary<uint, List<uint>>();
        var functions = FunctionByInstruction;

        foreach (var reference in CrossReferences)
        {
            if (reference.Kind != CrossReferenceKind.Call)
            {
                continue;
            }

            var target = FindFunction(reference.ToRva);
            if (target is null || !functions.TryGetValue(reference.FromRva, out var source))
            {
                continue;
            }

            if (!callers.TryGetValue(target.Function.Rva, out var callerList))
            {
                callerList = [];
                callers[target.Function.Rva] = callerList;
            }

            if (!callerList.Contains(source.Function.Rva))
            {
                callerList.Add(source.Function.Rva);
            }

            if (!callees.TryGetValue(source.Function.Rva, out var calleeList))
            {
                calleeList = [];
                callees[source.Function.Rva] = calleeList;
            }

            if (!calleeList.Contains(target.Function.Rva))
            {
                calleeList.Add(target.Function.Rva);
            }
        }

        return (callers, callees);
    }
}
