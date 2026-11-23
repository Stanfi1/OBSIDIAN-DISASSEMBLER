using Obsidian.Core.Formatting;
using Obsidian.Core.Model;

namespace Obsidian.App.ViewModels;

public sealed record MemorySegmentViewModel(
    string Name,
    double Left,
    double Width,
    bool IsExecutable,
    bool IsWritable,
    bool IsUninitialized,
    string ToolTip);

public sealed record SectionRowViewModel(
    string Name,
    string Rva,
    string Va,
    string RawSize,
    string VirtualSize,
    string Permissions,
    bool IsExecutable,
    bool IsWritable,
    string ToolTip);

public sealed record DirectoryRowViewModel(
    string Index,
    string Name,
    string Location,
    string Size,
    bool IsPresent);

public sealed record ImportModuleRowViewModel(
    string Name,
    string Count,
    string Sample);

public sealed record ExportRowViewModel(
    string Name,
    string Ordinal,
    string Location,
    string Kind);

/// <summary>Document-level projection of a loaded image for the Overview tab.</summary>
public sealed class OverviewViewModel
{
    private const double MapWidth = 520;

    public OverviewViewModel(BinaryImage image)
    {
        Image = image;
        FileName = image.FileName;
        FilePath = image.FilePath;
        Sha256 = image.Sha256;
        MapHeader = $"{image.Sections.Count} sections · {image.Imports.Count} modules · {image.Exports.Count} exports";

        var badges = new List<string>
        {
            image.FormatText,
            image.ArchitectureText,
            image.Subsystem.ToDisplayName(),
        };
        if (image.IsManaged)
        {
            badges.Add(".NET");
        }

        if (image.IsSigned)
        {
            badges.Add("Signed");
        }

        if (image.WarningCount > 0)
        {
            badges.Add($"{image.WarningCount} warning{(image.WarningCount == 1 ? string.Empty : "s")}");
        }

        Badges = badges;

        Groups =
        [
            new PropertyGroupViewModel("Image",
            [
                new PropertyRowViewModel("Format", image.FormatText),
                new PropertyRowViewModel("Architecture", image.Architecture.ToDisplayName()),
                new PropertyRowViewModel("Subsystem", image.Subsystem.ToDisplayName()),
                new PropertyRowViewModel("Image base", Hex.Va(image.ImageBase), true),
                new PropertyRowViewModel("Entry point", Hex.Rva(image.EntryPointRva), true),
                new PropertyRowViewModel("Size of image", Hex.UInt32(image.SizeOfImage), true),
                new PropertyRowViewModel("Timestamp", Hex.Timestamp(image.TimeDateStampUtc)),
                new PropertyRowViewModel("SHA-256", image.Sha256[..32] + "…", true, image.Sha256),
            ]),
            new PropertyGroupViewModel("Security posture",
            [
                new PropertyRowViewModel("ASLR", Has(image, PeDllCharacteristics.DynamicBase) ? "Enabled" : "Not requested"),
                new PropertyRowViewModel("DEP/NX", Has(image, PeDllCharacteristics.NxCompat) ? "Enabled" : "Not requested"),
                new PropertyRowViewModel("Control Flow Guard", Has(image, PeDllCharacteristics.ControlFlowGuard) ? "Enabled" : "Not requested"),
                new PropertyRowViewModel("High-entropy VA", Has(image, PeDllCharacteristics.HighEntropyVa) ? "Enabled" : "Not requested"),
                new PropertyRowViewModel("Authenticode", image.IsSigned ? "Table present" : "Absent"),
            ]),
        ];

        var maxSectionEnd = image.Sections.Select(section => section.VirtualEnd).DefaultIfEmpty(0UL).Max();
        var span = Math.Max((double)image.SizeOfImage, (double)maxSectionEnd);
        span = Math.Max(span, 1);
        MapSpan = span;

        Segments = image.Sections
            .Select(section =>
            {
                var left = section.VirtualAddress / span * MapWidth;
                var width = Math.Max(section.MappedSize / span * MapWidth, 3);
                return new MemorySegmentViewModel(
                    section.Name,
                    left,
                    width,
                    section.IsExecutable,
                    section.IsWritable,
                    !section.HasRawData && section.VirtualSize > 0,
                    $"{section.Name}\n{section.Kind} · {section.Permissions}\n" +
                    $"RVA {Hex.Rva(section.VirtualAddress)} – 0x{(uint)Math.Min(section.VirtualEnd, uint.MaxValue):X8}\n" +
                    $"{Hex.Size(section.MappedSize)} mapped");
            })
            .ToArray();

        EntryMarkerLeft = Math.Clamp(image.EntryPointRva / span * MapWidth, 0, MapWidth - 1);

        SectionRows = image.Sections.Select(section => new SectionRowViewModel(
                section.Name,
                Hex.Rva(section.VirtualAddress),
                Hex.Va(image.ImageBase + section.VirtualAddress),
                Hex.Size(section.RawSize),
                Hex.Size(section.VirtualSize),
                section.Permissions,
                section.IsExecutable,
                section.IsWritable,
                $"{section.Name} · {section.Kind}\n{Hex.Va(image.ImageBase + section.VirtualAddress)} · " +
                $"{Hex.Size(section.MappedSize)} mapped\nRaw {Hex.Offset(section.RawOffset)} · " +
                $"{section.RawSize:N0} bytes\nVirtual {section.VirtualSize:N0} bytes · {section.Permissions}"))
            .ToArray();

        DirectoryRows = image.DataDirectories
            .Where(directory => directory.IsPresent)
            .Select(directory => new DirectoryRowViewModel(
                directory.Index.ToString("D2"),
                directory.Name,
                Hex.Rva(directory.VirtualAddress),
                Hex.Size(directory.Size),
                true))
            .ToArray();

        Findings = image.Findings;
        WarningCount = image.Findings.Count(f => f.Severity == PeFindingSeverity.Warning);
        InformationCount = image.Findings.Count(f => f.Severity == PeFindingSeverity.Information);

        ImportRows = image.Imports
            .OrderByDescending(module => module.FunctionCount)
            .ThenBy(module => module.Name, StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .Select(module => new ImportModuleRowViewModel(
                module.Name,
                module.FunctionCount.ToString("N0"),
                string.Join(", ", module.Symbols.Take(4).Select(symbol => symbol.DisplayName)) +
                (module.FunctionCount > 4 ? ", …" : string.Empty)))
            .ToArray();

        MoreImportsText = image.Imports.Count > 12
            ? $"{image.Imports.Count - 12} more modules are listed in Solution Explorer."
            : string.Empty;

        ExportRows = image.Exports
            .Take(12)
            .Select(export => new ExportRowViewModel(
                export.DisplayName,
                export.Ordinal.ToString(),
                export.IsForwarder
                    ? export.ForwarderName ?? "forwarded"
                    : export.Rva is { } rva ? Hex.Rva(rva) : "not file-backed",
                export.IsForwarder ? "Forwarded" : "Local"))
            .ToArray();

        MoreExportsText = image.Exports.Count > 12
            ? $"{image.Exports.Count - 12} more exports are listed in Solution Explorer."
            : string.Empty;
    }

    public BinaryImage Image { get; }

    public string FileName { get; }

    public string FilePath { get; }

    public string Sha256 { get; }

    public string MapHeader { get; }

    public IReadOnlyList<string> Badges { get; }

    public IReadOnlyList<PropertyGroupViewModel> Groups { get; }

    public double MapSpan { get; }

    public double MapWidthValue => MapWidth;

    public IReadOnlyList<MemorySegmentViewModel> Segments { get; }

    public double EntryMarkerLeft { get; }

    public string EntryPointText => Hex.Rva(Image.EntryPointRva);

    public string EntryPointSection => Image.EntryPointSection?.Name ?? "unmapped";

    public string EntryPointFileOffset =>
        Image.EntryPointFileOffset is { } offset ? Hex.Offset(offset) : "not file-backed";

    public IReadOnlyList<SectionRowViewModel> SectionRows { get; }

    public IReadOnlyList<DirectoryRowViewModel> DirectoryRows { get; }

    public bool HasDirectories => DirectoryRows.Count > 0;

    public IReadOnlyList<PeFinding> Findings { get; }

    public int WarningCount { get; }

    public int InformationCount { get; }

    public bool HasFindings => Findings.Count > 0;

    public IReadOnlyList<ImportModuleRowViewModel> ImportRows { get; }

    public string MoreImportsText { get; }

    public bool HasImports => ImportRows.Count > 0;

    public IReadOnlyList<ExportRowViewModel> ExportRows { get; }

    public string MoreExportsText { get; }

    public bool HasExports => ExportRows.Count > 0;

    private static bool Has(BinaryImage image, PeDllCharacteristics flag) =>
        (image.DllCharacteristics & flag) == flag;
}
