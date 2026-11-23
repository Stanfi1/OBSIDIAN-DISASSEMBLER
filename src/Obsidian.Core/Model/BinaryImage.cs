namespace Obsidian.Core.Model;

using System.Diagnostics.CodeAnalysis;

/// <summary>
/// Fully parsed, immutable view of a loaded binary. The raw bytes are retained
/// for later phases (disassembly, hex view) and are never written back.
/// </summary>
public sealed class BinaryImage
{
    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public required long FileSize { get; init; }
    public required ReadOnlyMemory<byte> Bytes { get; init; }
    public required string Sha256 { get; init; }
    public required DateTimeOffset LoadedAtUtc { get; init; }

    public required BinaryArchitecture Architecture { get; init; }
    public required PeFormat Format { get; init; }
    public required ushort Machine { get; init; }
    public required PeSubsystem Subsystem { get; init; }
    public required PeDllCharacteristics DllCharacteristics { get; init; }
    public required ushort Characteristics { get; init; }

    public required ulong ImageBase { get; init; }
    public required uint PeHeaderOffset { get; init; }
    public required uint EntryPointRva { get; init; }
    public required uint SizeOfImage { get; init; }
    public required uint SizeOfHeaders { get; init; }
    public required uint SectionAlignment { get; init; }
    public required uint FileAlignment { get; init; }
    public required uint CheckSum { get; init; }
    public required byte MajorLinkerVersion { get; init; }
    public required byte MinorLinkerVersion { get; init; }
    public required ushort MajorSubsystemVersion { get; init; }
    public required ushort MinorSubsystemVersion { get; init; }
    public required DateTimeOffset TimeDateStampUtc { get; init; }

    public required bool IsManaged { get; init; }
    public string? ClrRuntimeVersion { get; init; }
    public required bool HasRelocations { get; init; }
    public required bool HasTls { get; init; }
    public required bool HasDebugData { get; init; }
    public required bool HasResources { get; init; }
    public required bool HasExceptions { get; init; }
    public required bool IsSigned { get; init; }
    public required bool HasLoadConfiguration { get; init; }

    public required IReadOnlyList<PeSection> Sections { get; init; }
    public required IReadOnlyList<DataDirectoryInfo> DataDirectories { get; init; }
    public required IReadOnlyList<ImportedModule> Imports { get; init; }
    public required IReadOnlyList<ExportedSymbol> Exports { get; init; }
    public required IReadOnlyList<PeResourceEntry> Resources { get; init; }
    public required PeRelocationInfo Relocations { get; init; }
    public PeTlsInfo? Tls { get; init; }
    public PeExceptionInfo? Exceptions { get; init; }
    public required IReadOnlyList<PeDebugEntry> DebugEntries { get; init; }
    public PeLoadConfigurationInfo? LoadConfiguration { get; init; }
    public required IReadOnlyList<PeFinding> Findings { get; init; }
    public required AddressMap AddressMap { get; init; }

    /// <summary>Creates an image through the required object initializer in the PE loader.</summary>
    public BinaryImage()
    {
    }

    /// <summary>
    /// Returns a copy of this image with different raw bytes and hash. The
    /// length must stay the same because every PE offset and AddressMap range
    /// remains tied to the original file layout.
    /// </summary>
    public BinaryImage WithBytes(ReadOnlyMemory<byte> bytes, string sha256)
    {
        if (bytes.Length != Bytes.Length)
        {
            throw new ArgumentException("Patched image length must match the original file.", nameof(bytes));
        }

        return new BinaryImage(this, bytes, sha256);
    }

    [SetsRequiredMembers]
    private BinaryImage(BinaryImage source, ReadOnlyMemory<byte> bytes, string sha256)
    {
        FilePath = source.FilePath;
        FileName = source.FileName;
        FileSize = source.FileSize;
        Bytes = bytes;
        Sha256 = sha256;
        LoadedAtUtc = source.LoadedAtUtc;
        Architecture = source.Architecture;
        Format = source.Format;
        Machine = source.Machine;
        Subsystem = source.Subsystem;
        DllCharacteristics = source.DllCharacteristics;
        Characteristics = source.Characteristics;
        ImageBase = source.ImageBase;
        PeHeaderOffset = source.PeHeaderOffset;
        EntryPointRva = source.EntryPointRva;
        SizeOfImage = source.SizeOfImage;
        SizeOfHeaders = source.SizeOfHeaders;
        SectionAlignment = source.SectionAlignment;
        FileAlignment = source.FileAlignment;
        CheckSum = source.CheckSum;
        MajorLinkerVersion = source.MajorLinkerVersion;
        MinorLinkerVersion = source.MinorLinkerVersion;
        MajorSubsystemVersion = source.MajorSubsystemVersion;
        MinorSubsystemVersion = source.MinorSubsystemVersion;
        TimeDateStampUtc = source.TimeDateStampUtc;
        IsManaged = source.IsManaged;
        ClrRuntimeVersion = source.ClrRuntimeVersion;
        HasRelocations = source.HasRelocations;
        HasTls = source.HasTls;
        HasDebugData = source.HasDebugData;
        HasResources = source.HasResources;
        HasExceptions = source.HasExceptions;
        IsSigned = source.IsSigned;
        HasLoadConfiguration = source.HasLoadConfiguration;
        Sections = source.Sections;
        DataDirectories = source.DataDirectories;
        Imports = source.Imports;
        Exports = source.Exports;
        Resources = source.Resources;
        Relocations = source.Relocations;
        Tls = source.Tls;
        Exceptions = source.Exceptions;
        DebugEntries = source.DebugEntries;
        LoadConfiguration = source.LoadConfiguration;
        Findings = source.Findings;
        AddressMap = source.AddressMap;
    }

    public ulong EntryPointVa => ImageBase + EntryPointRva;

    public bool IsDll => (Characteristics & 0x2000) != 0;

    public bool IsExe => (Characteristics & 0x0002) != 0;

    public int ImportedFunctionCount => Imports.Sum(module => module.FunctionCount);

    public int ResourceCount => Resources.Count;

    public int RelocationCount => Relocations.Count;

    public int ExceptionFunctionCount => Exceptions?.Count ?? 0;

    public int DebugEntryCount => DebugEntries.Count;

    public int WarningCount => Findings.Count(f => f.Severity == PeFindingSeverity.Warning);

    public IEnumerable<PeSection> ExecutableSections => Sections.Where(s => s.IsExecutable || s.ContainsCode);

    public PeSection? EntryPointSection => AddressMap.GetSectionForRva(EntryPointRva);

    public long? EntryPointFileOffset =>
        AddressMap.TryRvaToFileOffset(EntryPointRva, out var offset) ? offset : null;

    public string ArchitectureText => Architecture.ToShortName();

    public string FormatText => Format.ToDisplayName();
}
