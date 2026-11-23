namespace Obsidian.Core.Model;

/// <summary>
/// Immutable section metadata. All address translations are derived from these
/// values; the class deliberately does not depend on AsmResolver.
/// </summary>
public sealed class PeSection
{
    public required string Name { get; init; }

    /// <summary>Relative virtual address the section is mapped to.</summary>
    public required uint VirtualAddress { get; init; }

    /// <summary>Size the loader commits in memory (VirtualSize).</summary>
    public required uint VirtualSize { get; init; }

    /// <summary>File offset of the raw section data; 0 for pure virtual sections.</summary>
    public required uint RawOffset { get; init; }

    /// <summary>Number of bytes backed by the file (SizeOfRawData).</summary>
    public required uint RawSize { get; init; }

    public required uint Characteristics { get; init; }
    public required bool IsReadable { get; init; }
    public required bool IsWritable { get; init; }
    public required bool IsExecutable { get; init; }
    public required bool ContainsCode { get; init; }
    public required bool ContainsInitializedData { get; init; }
    public required bool IsDiscardable { get; init; }

    /// <summary>Largest of raw and virtual size, used as the mapped span.</summary>
    public uint MappedSize => Math.Max(VirtualSize, RawSize);

    public ulong VirtualEnd => (ulong)VirtualAddress + MappedSize;

    public bool HasRawData => RawSize > 0;

    public string Permissions =>
        $"{(IsReadable ? 'R' : '-')}{(IsWritable ? 'W' : '-')}{(IsExecutable ? 'X' : '-')}";

    /// <summary>Human-readable section role inferred from flags and conventional names.</summary>
    public string Kind
    {
        get
        {
            if (IsExecutable || ContainsCode)
            {
                return "Code";
            }

            return Name switch
            {
                ".rsrc" => "Resources",
                ".reloc" => "Relocations",
                ".idata" => "Imports",
                ".edata" => "Exports",
                ".tls" => "TLS",
                ".pdata" => "Exceptions",
                ".debug" => "Debug",
                _ => !HasRawData && VirtualSize > 0 ? "Uninitialized data" : "Data",
            };
        }
    }

    public bool ContainsRva(uint rva) =>
        MappedSize > 0 && rva >= VirtualAddress && (ulong)rva < VirtualEnd;

    public bool ContainsFileOffset(long offset) =>
        HasRawData && offset >= RawOffset && (ulong)offset < (ulong)RawOffset + RawSize;

    public bool TryRvaToFileOffset(uint rva, out long fileOffset)
    {
        fileOffset = 0;
        if (!HasRawData || rva < VirtualAddress)
        {
            return false;
        }

        var delta = rva - VirtualAddress;
        if (delta >= RawSize)
        {
            return false;
        }

        fileOffset = RawOffset + (long)delta;
        return true;
    }

    public bool TryFileOffsetToRva(long fileOffset, out uint rva)
    {
        rva = 0;
        if (!HasRawData || fileOffset < RawOffset)
        {
            return false;
        }

        var delta = (ulong)(fileOffset - RawOffset);
        if (delta >= RawSize)
        {
            return false;
        }

        var result = (ulong)VirtualAddress + delta;
        if (result > uint.MaxValue)
        {
            return false;
        }

        rva = (uint)result;
        return true;
    }
}
