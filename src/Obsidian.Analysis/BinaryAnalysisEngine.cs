using System.Text;
using Obsidian.Core.Model;
using Obsidian.Core.Services;

namespace Obsidian.Analysis;

/// <summary>
/// Seed-based function discovery, CFG reconstruction, xref collection and
/// ASCII/UTF-16 string extraction. It consumes the linear disassembly and never
/// decodes instructions itself.
/// </summary>
public sealed class BinaryAnalysisEngine : IBinaryAnalysisEngine
{
    public const int MaxFunctionInstructions = 20_000;
    public const int MaxCrossReferences = 250_000;
    public const int MaxStrings = 50_000;
    public const int MaxStringLength = 512;
    public const int MinStringLength = 4;
    public const long MaxScannedBytes = 256L * 1024 * 1024;

    public Task<AnalysisResult> AnalyzeAsync(
        BinaryImage image,
        DisassemblyImage disassembly,
        IProgress<LoadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(disassembly);
        return Task.Run(() => AnalyzeCore(image, disassembly, progress, cancellationToken), cancellationToken);
    }

    private static AnalysisResult AnalyzeCore(
        BinaryImage image,
        DisassemblyImage disassembly,
        IProgress<LoadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<string>();
        var instructions = disassembly.Instructions;

        if (instructions.Count == 0)
        {
            return Empty(image, diagnostics, "Disassembly is empty; function analysis cannot run.");
        }

        progress?.Report(new LoadProgress(LoadStage.Analyzing, "Collecting function seeds…", 2));

        var indexByRva = new Dictionary<uint, int>(instructions.Count);
        for (var index = 0; index < instructions.Count; index++)
        {
            indexByRva[instructions[index].Rva] = index;
        }

        var seeds = new Dictionary<uint, SeedInfo>();
        void AddSeed(uint rva, FunctionSource source, string? nameHint = null)
        {
            if (!indexByRva.ContainsKey(rva))
            {
                return;
            }

            if (!seeds.TryGetValue(rva, out var seed))
            {
                seed = new SeedInfo(rva);
                seeds[rva] = seed;
            }

            seed.Sources.Add(source);
            if (!string.IsNullOrWhiteSpace(nameHint))
            {
                seed.NameHint ??= nameHint;
            }
        }

        AddSeed(image.EntryPointRva, FunctionSource.EntryPoint, "entry");
        var exportNames = new Dictionary<uint, string>();

        foreach (var export in image.Exports)
        {
            if (export.Rva is not { } exportRva || export.IsForwarder)
            {
                continue;
            }

            exportNames.TryAdd(exportRva, export.DisplayName);
            AddSeed(exportRva, FunctionSource.Export, export.DisplayName);
        }

        foreach (var exception in image.Exceptions?.Functions ?? [])
        {
            AddSeed(exception.BeginRva, FunctionSource.ExceptionRuntimeFunction);
        }

        foreach (var callback in image.Tls?.Callbacks ?? [])
        {
            if (callback.Rva > 0)
            {
                AddSeed(callback.Rva, FunctionSource.TlsCallback);
            }
        }

        foreach (var instruction in instructions)
        {
            if (instruction.Flow == InstructionFlow.Call && instruction.TargetRva is { } target)
            {
                AddSeed(target, FunctionSource.CallTarget);
            }
        }

        if (seeds.Count == 0)
        {
            diagnostics.Add("No function seed maps to a decoded instruction.");
            return Empty(image, diagnostics, "No function seed could be mapped to the decoded code.");
        }

        var seedRvas = seeds.Keys.ToHashSet();
        var orderedSeeds = seeds.Values
            .OrderBy(seed => SeedPriority(seed))
            .ThenBy(seed => seed.Rva)
            .ToArray();

        var functions = new List<FunctionDraft>();
        var ownerByInstruction = new Dictionary<int, uint>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var seedIndex = 0; seedIndex < orderedSeeds.Length; seedIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var seed = orderedSeeds[seedIndex];
            var entryIndex = indexByRva[seed.Rva];

            if (ownerByInstruction.ContainsKey(entryIndex))
            {
                continue;
            }

            if (functions.Count >= 20_000)
            {
                diagnostics.Add("Function cap reached; remaining seeds were skipped.");
                break;
            }

            var visited = DiscoverFunction(
                entryIndex,
                seed.Rva,
                seedRvas,
                instructions,
                indexByRva,
                cancellationToken,
                out var hitLimit);

            if (visited.Count == 0)
            {
                continue;
            }

            if (hitLimit)
            {
                diagnostics.Add($"Function at {HexRva(seed.Rva)} hit the {MaxFunctionInstructions:N0}-instruction limit.");
            }

            var functionInstructions = visited
                .Select(index => instructions[index])
                .OrderBy(instruction => instruction.Index)
                .ToArray();

            var blocks = BuildBlocks(functionInstructions, seed.Rva, out var edgeCount, diagnostics);
            var endRva = functionInstructions.Max(instruction => instruction.RvaEnd);
            var section = image.AddressMap.GetSectionForRva(seed.Rva);
            var name = MakeName(seed, exportNames, image, usedNames);

            var discovered = new DiscoveredFunction(
                seed.Rva,
                image.ImageBase + seed.Rva,
                name,
                section?.Name ?? functionInstructions[0].SectionName,
                endRva,
                functionInstructions.Length,
                blocks.Count,
                seed.Sources.OrderBy(source => source).ToArray());

            functions.Add(new FunctionDraft(discovered, functionInstructions, blocks, edgeCount));

            foreach (var instruction in functionInstructions)
            {
                ownerByInstruction.TryAdd(instruction.Index, seed.Rva);
            }

            if (seedIndex % 64 == 0)
            {
                var percent = 2 + (int)Math.Clamp((double)seedIndex / orderedSeeds.Length * 38, 0, 38);
                progress?.Report(new LoadProgress(
                    LoadStage.Analyzing,
                    $"Discovered {functions.Count:N0} functions…",
                    percent));
            }
        }

        progress?.Report(new LoadProgress(LoadStage.Analyzing, "Collecting cross references…", 45));

        var xrefs = CollectCrossReferences(image, instructions, diagnostics);
        var xrefsByTarget = xrefs.ToLookup(reference => reference.ToRva);

        progress?.Report(new LoadProgress(LoadStage.Analyzing, "Extracting strings…", 65));

        var strings = ExtractStrings(image, xrefsByTarget, diagnostics, cancellationToken, out var scanTruncated);

        progress?.Report(new LoadProgress(LoadStage.Analyzing, "Building analysis result…", 90));

        var functionResults = functions
            .OrderBy(function => function.Function.Rva)
            .Select(function => new FunctionAnalysis
            {
                Function = function.Function,
                Instructions = function.Instructions,
                Blocks = function.Blocks,
                EdgeCount = function.EdgeCount,
            })
            .ToArray();

        var truncated = scanTruncated
                        || diagnostics.Any(diagnostic => diagnostic.Contains("limit", StringComparison.OrdinalIgnoreCase)
                                                         || diagnostic.Contains("cap", StringComparison.OrdinalIgnoreCase));

        var message = diagnostics.Count == 0
            ? null
            : string.Join(" ", diagnostics.Take(4));

        progress?.Report(new LoadProgress(
            LoadStage.Analyzing,
            $"Analysis complete: {functionResults.Length:N0} functions, {strings.Count:N0} strings",
            100));

        return new AnalysisResult
        {
            Functions = functionResults,
            Strings = strings,
            CrossReferences = xrefs,
            Diagnostics = diagnostics,
            IsTruncated = truncated,
            Message = message,
        };
    }

    private static HashSet<int> DiscoverFunction(
        int entryIndex,
        uint functionRva,
        HashSet<uint> allSeeds,
        IReadOnlyList<DisassembledInstruction> instructions,
        IReadOnlyDictionary<uint, int> indexByRva,
        CancellationToken cancellationToken,
        out bool hitLimit)
    {
        hitLimit = false;
        var visited = new HashSet<int>();
        var stack = new Stack<int>();
        stack.Push(entryIndex);

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var index = stack.Pop();
            if (!visited.Add(index))
            {
                continue;
            }

            if (visited.Count > MaxFunctionInstructions)
            {
                visited.Remove(index);
                hitLimit = true;
                break;
            }

            var instruction = instructions[index];

            void PushFallthrough()
            {
                var next = index + 1;
                if (next < instructions.Count && IsFollowable(instructions[next], functionRva, allSeeds, indexByRva))
                {
                    stack.Push(next);
                }
            }

            void PushTarget()
            {
                if (instruction.TargetRva is { } target
                    && indexByRva.TryGetValue(target, out var targetIndex)
                    && IsFollowable(instructions[targetIndex], functionRva, allSeeds, indexByRva))
                {
                    stack.Push(targetIndex);
                }
            }

            switch (instruction.Flow)
            {
                case InstructionFlow.Next:
                case InstructionFlow.Call:
                case InstructionFlow.IndirectCall:
                case InstructionFlow.Other:
                    PushFallthrough();
                    break;

                case InstructionFlow.ConditionalBranch:
                    PushFallthrough();
                    PushTarget();
                    break;

                case InstructionFlow.UnconditionalBranch:
                    PushTarget();
                    break;

                case InstructionFlow.Return:
                case InstructionFlow.IndirectBranch:
                case InstructionFlow.Interrupt:
                case InstructionFlow.Invalid:
                    break;
            }
        }

        return visited;
    }

    private static bool IsFollowable(
        DisassembledInstruction instruction,
        uint functionRva,
        HashSet<uint> allSeeds,
        IReadOnlyDictionary<uint, int> indexByRva)
    {
        if (!instruction.IsValid)
        {
            return false;
        }

        if (instruction.Rva != functionRva && allSeeds.Contains(instruction.Rva))
        {
            return false;
        }

        return instruction.Flow != InstructionFlow.Invalid;
    }

    private static List<FunctionBasicBlock> BuildBlocks(
        IReadOnlyList<DisassembledInstruction> instructions,
        uint entryRva,
        out int edgeCount,
        List<string> diagnostics)
    {
        edgeCount = 0;
        if (instructions.Count == 0)
        {
            return [];
        }

        var indexByRva = instructions
            .Select((instruction, index) => (instruction.Rva, index))
            .ToDictionary(pair => pair.Rva, pair => pair.index);

        var leaders = new HashSet<int> { 0 };

        for (var index = 0; index < instructions.Count; index++)
        {
            var instruction = instructions[index];
            if (instruction.Flow is InstructionFlow.ConditionalBranch or InstructionFlow.UnconditionalBranch)
            {
                if (instruction.TargetRva is { } target && indexByRva.TryGetValue(target, out var targetIndex))
                {
                    leaders.Add(targetIndex);
                }

                if (index + 1 < instructions.Count)
                {
                    leaders.Add(index + 1);
                }
            }
        }

        var starts = leaders.OrderBy(index => index).ToArray();
        var blocks = new List<FunctionBasicBlock>(starts.Length);

        for (var blockIndex = 0; blockIndex < starts.Length; blockIndex++)
        {
            var start = starts[blockIndex];
            var endExclusive = blockIndex + 1 < starts.Length ? starts[blockIndex + 1] : instructions.Count;
            if (endExclusive <= start)
            {
                continue;
            }

            var first = instructions[start];
            var last = instructions[endExclusive - 1];
            var block = new FunctionBasicBlock(
                blocks.Count,
                first.Rva,
                last.RvaEnd,
                start,
                endExclusive - start,
                MapBlockKind(last),
                [],
                [],
                blocks.Count == 0 && first.Rva == entryRva,
                false);

            blocks.Add(block);
        }

        var byStart = blocks.ToDictionary(block => block.StartRva);
        var successors = new Dictionary<int, List<uint>>();
        var predecessors = new Dictionary<uint, HashSet<uint>>();

        foreach (var block in blocks)
        {
            var list = new List<uint>();
            var nextInstructionIndex = block.InstructionOffset + block.InstructionCount;
            var last = instructions[nextInstructionIndex - 1];

            void AddSuccessor(uint rva)
            {
                if (byStart.ContainsKey(rva) && !list.Contains(rva))
                {
                    list.Add(rva);
                }
            }

            if (nextInstructionIndex < instructions.Count)
            {
                var nextRva = instructions[nextInstructionIndex].Rva;
                switch (last.Flow)
                {
                    case InstructionFlow.Next:
                    case InstructionFlow.Call:
                    case InstructionFlow.IndirectCall:
                    case InstructionFlow.Other:
                        AddSuccessor(nextRva);
                        break;

                    case InstructionFlow.ConditionalBranch:
                        AddSuccessor(nextRva);
                        if (last.TargetRva is { } target)
                        {
                            AddSuccessor(target);
                        }

                        break;

                    case InstructionFlow.UnconditionalBranch:
                        if (last.TargetRva is { } jumpTarget)
                        {
                            AddSuccessor(jumpTarget);
                        }

                        break;
                }
            }
            else if (last.Flow == InstructionFlow.UnconditionalBranch && last.TargetRva is { } finalTarget)
            {
                AddSuccessor(finalTarget);
            }

            successors[block.Index] = list;
            foreach (var successor in list)
            {
                if (!predecessors.TryGetValue(successor, out var set))
                {
                    set = [];
                    predecessors[successor] = set;
                }

                set.Add(block.StartRva);
            }

            edgeCount += list.Count;
        }

        var result = new List<FunctionBasicBlock>(blocks.Count);
        foreach (var block in blocks)
        {
            var succ = successors.GetValueOrDefault(block.Index, []);
            var pred = predecessors.TryGetValue(block.StartRva, out var set)
                ? set.OrderBy(rva => rva).ToArray()
                : [];
            var isExit = succ.Count == 0
                         || block.Kind is BasicBlockKind.Return or BasicBlockKind.IndirectBranch or BasicBlockKind.Invalid;

            result.Add(block with
            {
                SuccessorStarts = succ,
                PredecessorStarts = pred,
                IsExit = isExit,
            });
        }

        if (result.Count == 0)
        {
            diagnostics.Add($"Function at {HexRva(entryRva)} has no basic blocks.");
        }

        return result;
    }

    private static BasicBlockKind MapBlockKind(DisassembledInstruction instruction) => instruction.Flow switch
    {
        InstructionFlow.Return => BasicBlockKind.Return,
        InstructionFlow.UnconditionalBranch => BasicBlockKind.Jump,
        InstructionFlow.ConditionalBranch => BasicBlockKind.ConditionalBranch,
        InstructionFlow.IndirectBranch => BasicBlockKind.IndirectBranch,
        InstructionFlow.Invalid => BasicBlockKind.Invalid,
        _ => BasicBlockKind.Normal,
    };

    private static List<CrossReference> CollectCrossReferences(
        BinaryImage image,
        IReadOnlyList<DisassembledInstruction> instructions,
        List<string> diagnostics)
    {
        var references = new List<CrossReference>();

        foreach (var instruction in instructions)
        {
            if (references.Count >= MaxCrossReferences)
            {
                diagnostics.Add($"Cross-reference cap reached ({MaxCrossReferences:N0}).");
                break;
            }

            if (instruction.TargetRva is not { } target)
            {
                continue;
            }

            var kind = instruction.Flow switch
            {
                InstructionFlow.Call => CrossReferenceKind.Call,
                InstructionFlow.UnconditionalBranch => CrossReferenceKind.Jump,
                InstructionFlow.ConditionalBranch => CrossReferenceKind.ConditionalJump,
                _ => (CrossReferenceKind?)null,
            };

            if (kind is null)
            {
                continue;
            }

            references.Add(new CrossReference(
                instruction.Rva,
                target,
                kind.Value,
                instruction.SectionName,
                image.AddressMap.GetSectionForRva(target)?.Name,
                instruction.Text));
        }

        foreach (var instruction in instructions)
        {
            if (references.Count >= MaxCrossReferences)
            {
                break;
            }

            if (instruction.DataTargetRva is { } dataTarget)
            {
                references.Add(new CrossReference(
                    instruction.Rva,
                    dataTarget,
                    CrossReferenceKind.Data,
                    instruction.SectionName,
                    image.AddressMap.GetSectionForRva(dataTarget)?.Name,
                    instruction.Text));
            }
        }

        return references;
    }

    private static List<StringLiteral> ExtractStrings(
        BinaryImage image,
        ILookup<uint, CrossReference> xrefsByTarget,
        List<string> diagnostics,
        CancellationToken cancellationToken,
        out bool truncated)
    {
        truncated = false;
        var results = new List<StringLiteral>();
        long scannedBytes = 0;

        foreach (var section in image.Sections)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!section.HasRawData)
            {
                continue;
            }

            // Executive code bytes are a notorious source of printable noise.
            // Phase 4 extracts literals from file-backed data/resource sections;
            // embedded code literals are a future enhancement.
            if (section.IsExecutable || section.ContainsCode)
            {
                continue;
            }

            var available = image.Bytes.Length - (long)section.RawOffset;
            if (available <= 0)
            {
                continue;
            }

            var length = (int)Math.Min((long)section.RawSize, available);
            if (section.VirtualSize > 0)
            {
                length = Math.Min(length, (int)Math.Min(section.VirtualSize, int.MaxValue));
            }

            if (length <= 0)
            {
                continue;
            }

            if (scannedBytes + length > MaxScannedBytes)
            {
                length = (int)Math.Max(0, MaxScannedBytes - scannedBytes);
                truncated = true;
                diagnostics.Add($"String scan limited to {MaxScannedBytes / (1024 * 1024)} MB.");
            }

            if (length <= 0)
            {
                break;
            }

            var data = image.Bytes.Span.Slice((int)section.RawOffset, length);
            var executable = section.IsExecutable || section.ContainsCode;
            ExtractAscii(data, section, executable, results, xrefsByTarget, diagnostics);
            ExtractUtf16(data, section, executable, results, xrefsByTarget, diagnostics);

            scannedBytes += length;

            if (results.Count >= MaxStrings)
            {
                truncated = true;
                diagnostics.Add($"String cap reached ({MaxStrings:N0}).");
                break;
            }
        }

        return results
            .OrderBy(value => value.Rva)
            .ThenBy(value => value.Encoding)
            .ToList();
    }

    private static void ExtractAscii(
        ReadOnlySpan<byte> data,
        PeSection section,
        bool executable,
        List<StringLiteral> results,
        ILookup<uint, CrossReference> xrefsByTarget,
        List<string> diagnostics)
    {
        var index = 0;
        while (index < data.Length && results.Count < MaxStrings)
        {
            if (!IsPrintable(data[index]))
            {
                index++;
                continue;
            }

            var start = index;
            while (index < data.Length && IsPrintable(data[index]) && index - start < MaxStringLength)
            {
                index++;
            }

            var length = index - start;
            if (length >= MinStringLength && !IsAllWhitespace(data.Slice(start, length)))
            {
                AddString(section, start, length, data.Slice(start, length), StringEncodingKind.Ascii, executable, results, xrefsByTarget);
            }

            if (index < data.Length && IsPrintable(data[index]))
            {
                // Reached MaxStringLength; skip the rest of the long run.
                while (index < data.Length && IsPrintable(data[index]))
                {
                    index++;
                }
            }
        }
    }

    private static void ExtractUtf16(
        ReadOnlySpan<byte> data,
        PeSection section,
        bool executable,
        List<StringLiteral> results,
        ILookup<uint, CrossReference> xrefsByTarget,
        List<string> diagnostics)
    {
        var index = 0;
        while (index + 1 < data.Length && results.Count < MaxStrings)
        {
            if (data[index + 1] != 0 || !IsPrintable(data[index]))
            {
                index += 2;
                continue;
            }

            var start = index;
            var chars = 0;
            var builder = new StringBuilder();
            while (index + 1 < data.Length
                   && data[index + 1] == 0
                   && IsPrintable(data[index])
                   && chars < MaxStringLength)
            {
                builder.Append((char)data[index]);
                index += 2;
                chars++;
            }

            if (chars >= MinStringLength && !IsAllWhitespace(builder))
            {
                AddString(
                    section,
                    start,
                    chars * 2,
                    Encoding.UTF8.GetBytes(builder.ToString()),
                    StringEncodingKind.Utf16Le,
                    executable,
                    results,
                    xrefsByTarget,
                    builder.ToString());
            }

            if (index + 1 < data.Length && data[index + 1] == 0 && IsPrintable(data[index]))
            {
                while (index + 1 < data.Length && data[index + 1] == 0 && IsPrintable(data[index]))
                {
                    index += 2;
                }
            }
        }
    }

    private static void AddString(
        PeSection section,
        int offset,
        int byteLength,
        ReadOnlySpan<byte> bytes,
        StringEncodingKind encoding,
        bool executable,
        List<StringLiteral> results,
        ILookup<uint, CrossReference> xrefsByTarget,
        string? valueOverride = null)
    {
        var value = valueOverride ?? Encoding.ASCII.GetString(bytes);
        if (value.Length > MaxStringLength)
        {
            value = value[..MaxStringLength];
        }

        var rva = section.VirtualAddress + (uint)offset;

        // Executable sections contain a lot of printable code bytes. Keep only
        // strings referenced by an instruction; non-executable sections keep
        // every candidate.
        if (executable && !xrefsByTarget[rva].Any())
        {
            return;
        }

        var fileOffset = section.RawOffset + (long)offset;
        results.Add(new StringLiteral(
            rva,
            fileOffset,
            value,
            encoding,
            section.Name,
            byteLength,
            xrefsByTarget[rva].ToArray()));
    }

    private static bool IsPrintable(byte value) => value is >= 0x20 and <= 0x7E;

    private static bool IsAllWhitespace(ReadOnlySpan<byte> value)
    {
        foreach (var character in value)
        {
            if (!char.IsWhiteSpace((char)character))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAllWhitespace(StringBuilder value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (!char.IsWhiteSpace(value[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static int SeedPriority(SeedInfo seed)
    {
        if (seed.Sources.Contains(FunctionSource.EntryPoint))
        {
            return 0;
        }

        if (seed.Sources.Contains(FunctionSource.Export))
        {
            return 1;
        }

        if (seed.Sources.Contains(FunctionSource.ExceptionRuntimeFunction))
        {
            return 2;
        }

        if (seed.Sources.Contains(FunctionSource.TlsCallback))
        {
            return 3;
        }

        return 4;
    }

    private static string MakeName(
        SeedInfo seed,
        IReadOnlyDictionary<uint, string> exportNames,
        BinaryImage image,
        HashSet<string> usedNames)
    {
        var candidate = seed.NameHint
                        ?? exportNames.GetValueOrDefault(seed.Rva)
                        ?? $"sub_{image.ImageBase + seed.Rva:X}";

        if (string.IsNullOrWhiteSpace(candidate))
        {
            candidate = $"sub_{image.ImageBase + seed.Rva:X}";
        }

        var unique = candidate;
        var suffix = 2;
        while (!usedNames.Add(unique))
        {
            unique = $"{candidate}_{suffix++}";
        }

        return unique;
    }

    private static AnalysisResult Empty(BinaryImage image, List<string> diagnostics, string message) =>
        new()
        {
            Functions = [],
            Strings = [],
            CrossReferences = [],
            Diagnostics = diagnostics,
            Message = message,
        };

    private static string HexRva(uint rva) => $"0x{rva:X8}";

    private sealed class SeedInfo(uint rva)
    {
        public uint Rva { get; } = rva;
        public HashSet<FunctionSource> Sources { get; } = [];
        public string? NameHint { get; set; }
    }

    private sealed record FunctionDraft(
        DiscoveredFunction Function,
        IReadOnlyList<DisassembledInstruction> Instructions,
        IReadOnlyList<FunctionBasicBlock> Blocks,
        int EdgeCount);
}
