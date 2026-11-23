using Obsidian.Analysis;
using Obsidian.Core.Model;
using Obsidian.Disassembly;
using Obsidian.Pe;
using Xunit;

namespace Obsidian.Analysis.Tests;

public sealed class BinaryAnalysisEngineTests
{
    private static string NotepadPath => Path.Combine(Environment.SystemDirectory, "notepad.exe");

    private static async Task<(BinaryImage Image, DisassemblyImage Disassembly, AnalysisResult Analysis)> AnalyzeAsync(
        string path)
    {
        var image = await new PeImageLoader().LoadAsync(path);
        var disassembly = await new IcedDisassembler().DisassembleAsync(image);
        var analysis = await new BinaryAnalysisEngine().AnalyzeAsync(image, disassembly);
        return (image, disassembly, analysis);
    }

    private static async Task<(BinaryImage Image, DisassemblyImage Disassembly, AnalysisResult Analysis)> AnalyzeNotepadAsync()
    {
        if (!File.Exists(NotepadPath))
        {
            throw new FileNotFoundException("notepad.exe is not available for analysis tests.", NotepadPath);
        }

        return await AnalyzeAsync(NotepadPath);
    }

    [Fact]
    public async Task DiscoversFunctionsAndBlocks()
    {
        var (image, _, analysis) = await AnalyzeNotepadAsync();

        Assert.NotEmpty(analysis.Functions);
        Assert.NotNull(analysis.FindFunction(image.EntryPointRva));
        Assert.Contains(analysis.Functions, function => function.Function.IsEntryPoint);
        Assert.All(analysis.Functions, function =>
        {
            Assert.NotEmpty(function.Function.Name);
            Assert.NotEmpty(function.Instructions);
            Assert.NotEmpty(function.Blocks);
            Assert.Equal(function.Blocks.Count, function.Function.BasicBlockCount);
            Assert.Equal(function.Instructions.Count, function.Function.InstructionCount);
            Assert.True(function.Function.EndRva > function.Function.Rva);
            Assert.True(function.Function.Size > 0);
        });
    }

    [Fact]
    public async Task BasicBlocksCoverFunctionInstructionsWithoutOverlap()
    {
        var (_, _, analysis) = await AnalyzeNotepadAsync();

        foreach (var function in analysis.Functions)
        {
            var covered = new HashSet<uint>();
            foreach (var block in function.Blocks)
            {
                Assert.True(block.InstructionCount > 0);
                Assert.True(block.EndRva > block.StartRva);
                Assert.InRange(block.InstructionOffset, 0, function.Instructions.Count - 1);
                Assert.InRange(
                    block.InstructionOffset + block.InstructionCount,
                    block.InstructionOffset,
                    function.Instructions.Count);

                for (var i = 0; i < block.InstructionCount; i++)
                {
                    var instruction = function.Instructions[block.InstructionOffset + i];
                    Assert.True(covered.Add(instruction.Rva), $"Instruction {instruction.Rva:X} is in two blocks.");
                    Assert.InRange(instruction.Rva, block.StartRva, block.EndRva - 1);
                }

                Assert.All(block.SuccessorStarts, start => Assert.NotNull(function.FindBlockByStart(start)));
                Assert.All(block.PredecessorStarts, start => Assert.NotNull(function.FindBlockByStart(start)));
            }

            Assert.Equal(function.Instructions.Count, covered.Count);
        }
    }

    [Fact]
    public async Task CollectsCallAndDataCrossReferences()
    {
        var (image, disassembly, analysis) = await AnalyzeNotepadAsync();

        Assert.NotEmpty(analysis.CrossReferences);
        Assert.Contains(analysis.CrossReferences, reference => reference.Kind == CrossReferenceKind.Call);
        Assert.Contains(analysis.CrossReferences, reference => reference.Kind == CrossReferenceKind.Data);
        Assert.All(analysis.CrossReferences, reference =>
        {
            Assert.NotEqual(0u, reference.FromRva);
            Assert.InRange(reference.FromRva, 0u, image.SizeOfImage - 1);
            Assert.InRange(reference.ToRva, 0u, image.SizeOfImage - 1);
            Assert.NotNull(disassembly.FindInstruction(reference.FromRva));
        });
    }

    [Fact]
    public async Task ExtractsPrintableStringsAndLinksKnownReferences()
    {
        var (_, _, analysis) = await AnalyzeNotepadAsync();

        Assert.NotEmpty(analysis.Strings);
        Assert.Contains(analysis.Strings, value => value.ReferenceCount > 0);
        Assert.All(analysis.Strings, value =>
        {
            Assert.True(value.Value.Length >= BinaryAnalysisEngine.MinStringLength);
            Assert.True(value.ByteLength > 0);
            Assert.NotEmpty(value.SectionName);
            Assert.InRange(value.References.Count, 0, analysis.CrossReferences.Count);
        });
    }

    [Fact]
    public async Task CallGraphIsSymmetric()
    {
        var (_, _, analysis) = await AnalyzeNotepadAsync();

        foreach (var function in analysis.Functions)
        {
            foreach (var callee in analysis.CalleesOf(function.Function.Rva))
            {
                Assert.Contains(function.Function.Rva, analysis.CallersOf(callee));
            }

            foreach (var caller in analysis.CallersOf(function.Function.Rva))
            {
                Assert.Contains(function.Function.Rva, analysis.CalleesOf(caller));
            }
        }
    }

    [Fact]
    public async Task IsDeterministic()
    {
        var (_, _, first) = await AnalyzeNotepadAsync();
        var (_, _, second) = await AnalyzeNotepadAsync();

        Assert.Equal(
            first.Functions.Select(function => (function.Function.Rva, function.Function.Name, function.Blocks.Count)),
            second.Functions.Select(function => (function.Function.Rva, function.Function.Name, function.Blocks.Count)));
        Assert.Equal(first.CrossReferences.Count, second.CrossReferences.Count);
        Assert.Equal(first.Strings.Count, second.Strings.Count);
    }

    [Fact]
    public async Task HonorsCancellation()
    {
        var image = await new PeImageLoader().LoadAsync(NotepadPath);
        var disassembly = await new IcedDisassembler().DisassembleAsync(image);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await new BinaryAnalysisEngine().AnalyzeAsync(image, disassembly, cancellationToken: cts.Token));
    }
}
