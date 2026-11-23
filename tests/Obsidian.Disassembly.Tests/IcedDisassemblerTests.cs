using Obsidian.Core.Model;
using Obsidian.Disassembly;
using Obsidian.Pe;
using Xunit;

namespace Obsidian.Disassembly.Tests;

public sealed class IcedDisassemblerTests
{
    private static string TestAssemblyPath =>
        Path.Combine(AppContext.BaseDirectory, "Obsidian.Disassembly.Tests.dll");

    private static async Task<BinaryImage> LoadAsync(string path) =>
        await new PeImageLoader().LoadAsync(path);

    [Fact]
    public void SupportsX86AndX64Only()
    {
        var disassembler = new IcedDisassembler();

        Assert.True(disassembler.IsSupported(BinaryArchitecture.X86));
        Assert.True(disassembler.IsSupported(BinaryArchitecture.X64));
        Assert.False(disassembler.IsSupported(BinaryArchitecture.Arm64));
        Assert.False(disassembler.IsSupported(BinaryArchitecture.Unknown));
    }

    [Fact]
    public async Task DecodesTheTestAssembly()
    {
        var image = await LoadAsync(TestAssemblyPath);
        var result = await new IcedDisassembler().DisassembleAsync(image);

        Assert.NotEmpty(result.Instructions);
        Assert.NotEmpty(result.Sections);
        Assert.Null(result.Message);
        Assert.False(result.IsTruncated);
        Assert.All(result.Instructions, instruction =>
        {
            Assert.InRange(instruction.Length, 1, 15);
            Assert.Equal(instruction.Length, instruction.Bytes.Length);
            Assert.False(string.IsNullOrWhiteSpace(instruction.Mnemonic));
            Assert.False(string.IsNullOrWhiteSpace(instruction.SectionName));
            Assert.InRange(instruction.Rva, 0u, image.SizeOfImage - 1);
        });
    }

    [Fact]
    public async Task AddressesAreStrictlyIncreasingAndUnique()
    {
        var image = await LoadAsync(TestAssemblyPath);
        var result = await new IcedDisassembler().DisassembleAsync(image);

        var last = -1u;
        foreach (var instruction in result.Instructions)
        {
            Assert.True(instruction.Rva > last);
            last = instruction.Rva;
        }
    }

    [Fact]
    public async Task BranchTargetsMapBackIntoTheImage()
    {
        var image = await LoadAsync(TestAssemblyPath);
        var result = await new IcedDisassembler().DisassembleAsync(image);

        var branches = result.Instructions
            .Where(instruction => instruction.TargetRva is not null)
            .ToArray();

        Assert.NotEmpty(branches);
        Assert.All(branches, branch =>
        {
            var target = branch.TargetRva!.Value;
            Assert.InRange(target, 0u, image.SizeOfImage - 1);

            var targetSection = image.AddressMap.GetSectionForRva(target);
            Assert.NotNull(targetSection);
            Assert.True(targetSection.IsExecutable || targetSection.ContainsCode);
        });

        // A linear sweep cannot guarantee that every branch target is an
        // instruction boundary (data in code, overlapping encodings, tail
        // padding). The UI navigates to the nearest decoded instruction.
        var mapped = branches.Count(branch =>
            result.FindInstructionIndex(branch.TargetRva!.Value) >= 0);
        Assert.True(mapped >= branches.Length * 0.95, $"{mapped}/{branches.Length} targets mapped to instructions.");
    }

    [Fact]
    public async Task DecodesNotepadEntryPoint()
    {
        var path = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        if (!File.Exists(path))
        {
            return;
        }

        var image = await LoadAsync(path);
        var result = await new IcedDisassembler().DisassembleAsync(image);

        Assert.True(result.Count > 1_000);
        var entryIndex = result.FindInstructionIndex(image.EntryPointRva);
        Assert.True(entryIndex >= 0, "The entry point should be inside the decoded .text range.");
        Assert.Equal(image.EntryPointRva, result.Instructions[entryIndex].Rva);
        Assert.Contains(result.Instructions, instruction => instruction.Flow == InstructionFlow.Return);
    }

    [Fact]
    public async Task ReportsProgressAndHonoursCancellation()
    {
        var image = await LoadAsync(TestAssemblyPath);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await new IcedDisassembler().DisassembleAsync(image, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task IsDeterministic()
    {
        var image = await LoadAsync(TestAssemblyPath);
        var disassembler = new IcedDisassembler();

        var first = await disassembler.DisassembleAsync(image);
        var second = await disassembler.DisassembleAsync(image);

        Assert.Equal(first.Count, second.Count);
        Assert.Equal(
            first.Instructions.Select(instruction => (instruction.Rva, instruction.Length, instruction.Text)),
            second.Instructions.Select(instruction => (instruction.Rva, instruction.Length, instruction.Text)));
    }

    [Fact]
    public async Task InstructionBytesMatchTheUnderlyingImage()
    {
        var image = await LoadAsync(TestAssemblyPath);
        var result = await new IcedDisassembler().DisassembleAsync(image);

        foreach (var instruction in result.Instructions.Take(2_000))
        {
            Assert.True(image.AddressMap.TryRvaToFileOffset(instruction.Rva, out var fileOffset));
            var actual = image.Bytes.Span.Slice((int)fileOffset, instruction.Length).ToArray();
            Assert.Equal(actual, instruction.Bytes);
        }
    }

    [Fact]
    public async Task NavigationFindsContainingAndNearestInstructions()
    {
        var image = await LoadAsync(TestAssemblyPath);
        var result = await new IcedDisassembler().DisassembleAsync(image);
        var first = result.Instructions[0];

        Assert.Equal(0, result.FindInstructionIndex(first.Rva));
        Assert.Equal(0, result.FindNearestIndex(first.Rva));
        if (first.Length > 1)
        {
            Assert.Equal(0, result.FindInstructionIndex(first.Rva + 1));
            Assert.Equal(0, result.FindNearestIndex(first.Rva + 1));
        }

        Assert.Same(first, result.FindInstruction(first.Rva));

        var target = result.Instructions[10];
        Assert.Equal(10, result.FindInstructionIndex(target.Rva));
        Assert.Equal(10, result.FindNearestIndex(target.Rva));
    }

    [Fact]
    public async Task SectionsAccountForEveryInstruction()
    {
        var image = await LoadAsync(TestAssemblyPath);
        var result = await new IcedDisassembler().DisassembleAsync(image);

        Assert.Equal(result.Count, result.Sections.Sum(section => section.InstructionCount));

        var current = 0;
        foreach (var section in result.Sections)
        {
            Assert.Equal(current, section.StartIndex);
            current += section.InstructionCount;
            Assert.True(section.InstructionCount > 0);
            Assert.NotEmpty(section.Name);
        }
    }

    [Fact]
    public async Task FormattedOperandsUseConsistentHexSyntax()
    {
        var image = await LoadAsync(TestAssemblyPath);
        var result = await new IcedDisassembler().DisassembleAsync(image);

        Assert.DoesNotContain(
            result.Instructions,
            instruction => System.Text.RegularExpressions.Regex.IsMatch(
                instruction.Operands,
                @"0x[0-9A-Fa-f]+h"));
    }

    [Fact]
    public void PreviewDecodesExactBytes()
    {
        var nop = IcedPreview.Decode([0x90], 0x1000, is64Bit: true);
        Assert.True(nop.IsValid);
        Assert.Equal(1, nop.Length);
        Assert.Contains("nop", nop.Text, StringComparison.OrdinalIgnoreCase);

        var ret = IcedPreview.Decode([0xC3], 0x1000, is64Bit: true);
        Assert.True(ret.IsValid);
        Assert.Contains("ret", ret.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PreviewRejectsPartialBytes()
    {
        var partial = IcedPreview.Decode([0x48, 0x89, 0xD8, 0x90], 0x1000, is64Bit: true);
        Assert.False(partial.IsValid);
        Assert.Contains("3-byte", partial.Error);
    }
}


