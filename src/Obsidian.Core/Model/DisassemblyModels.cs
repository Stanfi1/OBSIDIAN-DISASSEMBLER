namespace Obsidian.Core.Model;

/// <summary>Control-flow classification of a decoded instruction.</summary>
public enum InstructionFlow
{
    Next = 0,
    Call,
    IndirectCall,
    UnconditionalBranch,
    ConditionalBranch,
    IndirectBranch,
    Return,
    Interrupt,
    Invalid,
    Other,
}

/// <summary>
/// One decoded instruction in a linear sweep. Presentation text is stored as
/// mnemonic + operands so the UI can style them separately without re-parsing.
/// </summary>
public sealed record DisassembledInstruction
{
    public required int Index { get; init; }
    public required uint Rva { get; init; }
    public required ulong VirtualAddress { get; init; }
    public long? FileOffset { get; init; }
    public required int Length { get; init; }
    public required byte[] Bytes { get; init; }
    public required string SectionName { get; init; }
    public required string Mnemonic { get; init; }
    public required string Operands { get; init; }
    public required InstructionFlow Flow { get; init; }
    public uint? TargetRva { get; init; }
    public ulong? TargetVirtualAddress { get; init; }
    public uint? DataTargetRva { get; init; }
    public ulong? DataTargetVirtualAddress { get; init; }
    public bool IsValid { get; init; } = true;

    public bool HasStaticTarget => TargetRva.HasValue;

    public bool IsControlFlow => Flow is
        InstructionFlow.Call or
        InstructionFlow.IndirectCall or
        InstructionFlow.UnconditionalBranch or
        InstructionFlow.ConditionalBranch or
        InstructionFlow.IndirectBranch or
        InstructionFlow.Return;

    public string Text => string.IsNullOrEmpty(Operands) ? Mnemonic : $"{Mnemonic} {Operands}";

    public uint RvaEnd => Rva + (uint)Math.Max(0, Length);
}

/// <summary>Executable section that contributed instructions to the sweep.</summary>
public sealed record DisassemblySection(
    string Name,
    uint Rva,
    ulong VirtualAddress,
    int StartIndex,
    int InstructionCount,
    uint CodeSize);

/// <summary>
/// Immutable linear disassembly result. A linear sweep is deliberately not a
/// function-aware analysis; embedded data can produce plausible but wrong
/// instructions. Consumers must not present it as verified control flow.
/// </summary>
public sealed class DisassemblyImage
{
    public required IReadOnlyList<DisassembledInstruction> Instructions { get; init; }
    public required IReadOnlyList<DisassemblySection> Sections { get; init; }
    public required BinaryArchitecture Architecture { get; init; }
    public required ulong ImageBase { get; init; }
    public bool IsTruncated { get; init; }
    public string? Message { get; init; }

    public int Count => Instructions.Count;

    public bool HasInstructions => Count > 0;

    public static DisassemblyImage Empty(
        BinaryArchitecture architecture,
        ulong imageBase,
        string? message = null) =>
        new()
        {
            Instructions = [],
            Sections = [],
            Architecture = architecture,
            ImageBase = imageBase,
            Message = message,
        };

    /// <summary>Finds the instruction whose start address equals or contains the RVA.</summary>
    public int FindInstructionIndex(uint rva)
    {
        var index = FindNearestIndex(rva);
        if (index < 0)
        {
            return -1;
        }

        var instruction = Instructions[index];
        return rva >= instruction.Rva && rva < instruction.RvaEnd ? index : -1;
    }

    /// <summary>Finds the closest instruction starting at or before the RVA.</summary>
    public int FindNearestIndex(uint rva)
    {
        if (Instructions.Count == 0)
        {
            return -1;
        }

        var low = 0;
        var high = Instructions.Count - 1;
        var best = -1;

        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            if (Instructions[middle].Rva <= rva)
            {
                best = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return best >= 0 ? best : 0;
    }

    public DisassembledInstruction? FindInstruction(uint rva)
    {
        var index = FindInstructionIndex(rva);
        return index >= 0 ? Instructions[index] : null;
    }
}
