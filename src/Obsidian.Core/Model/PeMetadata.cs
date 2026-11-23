namespace Obsidian.Core.Model;

/// <summary>One of the sixteen standard PE data directories.</summary>
public sealed record DataDirectoryInfo(
    int Index,
    string Name,
    uint VirtualAddress,
    uint Size)
{
    public bool IsPresent => VirtualAddress != 0 && Size != 0;

    public string DisplayValue => IsPresent
        ? $"0x{VirtualAddress:X8} · {Size:N0} bytes"
        : "not present";

    public static readonly string[] StandardNames =
    [
        "Export table",
        "Import table",
        "Resource table",
        "Exception table",
        "Certificate table",
        "Base relocation table",
        "Debug directory",
        "Architecture data",
        "Global pointer",
        "TLS directory",
        "Load configuration",
        "Bound import",
        "Import address table",
        "Delay import",
        "CLR runtime header",
        "Reserved",
    ];
}

public sealed record ImportedSymbol(
    string Name,
    ushort Ordinal,
    ushort Hint,
    bool IsByOrdinal)
{
    public string DisplayName => IsByOrdinal ? $"#{Ordinal}" : Name;
}

public sealed record ImportedModule(
    string Name,
    IReadOnlyList<ImportedSymbol> Symbols)
{
    public int FunctionCount => Symbols.Count;
}

public sealed record ExportedSymbol(
    string Name,
    uint Ordinal,
    uint? Rva,
    string? ForwarderName)
{
    public string DisplayName => string.IsNullOrEmpty(Name) ? $"Ordinal {Ordinal}" : Name;

    public bool IsForwarder => !string.IsNullOrEmpty(ForwarderName);
}

/// <summary>
/// A structural observation about the image. These are facts to investigate,
/// never an automated malware classification.
/// </summary>
public sealed record PeFinding(
    PeFindingSeverity Severity,
    string Code,
    string Title,
    string Detail,
    uint? Rva = null);

public sealed record LoadProgress(
    LoadStage Stage,
    string Message,
    int? Percent = null)
{
    /// <summary>Progress used when a phase cannot report a meaningful percentage.</summary>
    public static LoadProgress Indeterminate(LoadStage stage, string message) => new(stage, message);
}
