using Obsidian.Core.Formatting;
using Obsidian.Core.Model;

namespace Obsidian.App.ViewModels;

/// <summary>Builds consistent property sheets for every selectable object.</summary>
public static class PropertySheetFactory
{
    public static IReadOnlyList<PropertyGroupViewModel> ForImage(BinaryImage image) =>
    [
        new PropertyGroupViewModel("Identity",
        [
            new PropertyRowViewModel("File name", image.FileName),
            new PropertyRowViewModel("Full path", image.FilePath, false, image.FilePath),
            new PropertyRowViewModel("File size", $"{Hex.Size(image.FileSize)} ({image.FileSize:N0} bytes)"),
            new PropertyRowViewModel("SHA-256", image.Sha256, true, image.Sha256),
            new PropertyRowViewModel("Loaded", Hex.Timestamp(image.LoadedAtUtc)),
        ]),
        new PropertyGroupViewModel("PE headers",
        [
            new PropertyRowViewModel("Format", image.FormatText),
            new PropertyRowViewModel("Architecture", image.Architecture.ToDisplayName()),
            new PropertyRowViewModel("Machine", $"0x{image.Machine:X4}", true),
            new PropertyRowViewModel("Subsystem", image.Subsystem.ToDisplayName()),
            new PropertyRowViewModel("File characteristics", $"0x{image.Characteristics:X4}", true),
            new PropertyRowViewModel("DLL characteristics", image.DllCharacteristics.ToString()),
            new PropertyRowViewModel("PE header offset", Hex.UInt32(image.PeHeaderOffset), true),
            new PropertyRowViewModel("Timestamp", Hex.Timestamp(image.TimeDateStampUtc)),
            new PropertyRowViewModel("Linker version", $"{image.MajorLinkerVersion}.{image.MinorLinkerVersion}"),
            new PropertyRowViewModel("Subsystem version", $"{image.MajorSubsystemVersion}.{image.MinorSubsystemVersion}"),
            new PropertyRowViewModel("Checksum", image.CheckSum == 0 ? "0x00000000 (absent)" : Hex.UInt32(image.CheckSum), true),
        ]),
        new PropertyGroupViewModel("Addresses",
        [
            new PropertyRowViewModel("Image base", Hex.Va(image.ImageBase), true),
            new PropertyRowViewModel("Entry point RVA", Hex.Rva(image.EntryPointRva), true),
            new PropertyRowViewModel("Entry point VA", Hex.Va(image.EntryPointVa), true),
            new PropertyRowViewModel("Entry point section", image.EntryPointSection?.Name ?? "unmapped"),
            new PropertyRowViewModel("Entry point offset", image.EntryPointFileOffset is { } offset ? Hex.Offset(offset) : "not file-backed", true),
            new PropertyRowViewModel("Size of image", Hex.UInt32(image.SizeOfImage), true),
            new PropertyRowViewModel("Size of headers", Hex.UInt32(image.SizeOfHeaders), true),
            new PropertyRowViewModel("Section alignment", Hex.UInt32(image.SectionAlignment), true),
            new PropertyRowViewModel("File alignment", Hex.UInt32(image.FileAlignment), true),
        ]),
        new PropertyGroupViewModel("Security and features",
        [
            new PropertyRowViewModel("ASLR (DYNAMICBASE)", HasFlag(image, PeDllCharacteristics.DynamicBase) ? "Enabled" : "Not requested"),
            new PropertyRowViewModel("DEP/NX (NX_COMPAT)", HasFlag(image, PeDllCharacteristics.NxCompat) ? "Enabled" : "Not requested"),
            new PropertyRowViewModel("Control Flow Guard", HasFlag(image, PeDllCharacteristics.ControlFlowGuard) ? "Enabled" : "Not requested"),
            new PropertyRowViewModel("High-entropy VA", HasFlag(image, PeDllCharacteristics.HighEntropyVa) ? "Enabled" : "Not requested"),
            new PropertyRowViewModel("Authenticode table", image.IsSigned ? "Present" : "Absent"),
            new PropertyRowViewModel("Managed image", image.IsManaged ? $"Yes ({image.ClrRuntimeVersion ?? "CLR"})" : "No"),
            new PropertyRowViewModel("Relocations", image.HasRelocations ? "Present" : "Absent"),
            new PropertyRowViewModel("TLS directory", image.HasTls ? "Present" : "Absent"),
            new PropertyRowViewModel("Debug directory", image.HasDebugData ? "Present" : "Absent"),
            new PropertyRowViewModel("Resources", image.HasResources ? "Present" : "Absent"),
            new PropertyRowViewModel("Exception directory", image.HasExceptions ? "Present" : "Absent"),
            new PropertyRowViewModel("Load configuration", image.HasLoadConfiguration ? "Present" : "Absent"),
        ]),
    ];

    public static IReadOnlyList<PropertyGroupViewModel> ForSection(PeSection section, BinaryImage image) =>
    [
        new PropertyGroupViewModel("Section",
        [
            new PropertyRowViewModel("Name", section.Name),
            new PropertyRowViewModel("Role", section.Kind),
            new PropertyRowViewModel("Permissions", section.Permissions),
            new PropertyRowViewModel("Virtual address", Hex.Rva(section.VirtualAddress), true),
            new PropertyRowViewModel("Virtual address (VA)", Hex.Va(image.ImageBase + section.VirtualAddress), true),
            new PropertyRowViewModel("Virtual size", $"{section.VirtualSize:N0} bytes ({Hex.UInt32(section.VirtualSize)})"),
            new PropertyRowViewModel("Raw offset", Hex.Offset(section.RawOffset), true),
            new PropertyRowViewModel("Raw size", $"{section.RawSize:N0} bytes ({Hex.UInt32(section.RawSize)})"),
            new PropertyRowViewModel("Mapped size", $"{section.MappedSize:N0} bytes"),
            new PropertyRowViewModel("End RVA", Hex.Rva((uint)Math.Min(section.VirtualEnd, uint.MaxValue)), true),
            new PropertyRowViewModel("Characteristics", Hex.UInt32(section.Characteristics), true),
            new PropertyRowViewModel("Contains code", section.ContainsCode ? "Yes" : "No"),
            new PropertyRowViewModel("Read / write / execute", section.Permissions),
            new PropertyRowViewModel("File-backed", section.HasRawData ? "Yes" : "No"),
        ]),
    ];

    public static IReadOnlyList<PropertyGroupViewModel> ForImportModule(ImportedModule module) =>
    [
        new PropertyGroupViewModel("Imported module",
        [
            new PropertyRowViewModel("Module", module.Name),
            new PropertyRowViewModel("Imported symbols", module.FunctionCount.ToString("N0")),
            new PropertyRowViewModel("Sample", string.Join(", ", module.Symbols.Take(6).Select(s => s.DisplayName)) +
                                                 (module.Symbols.Count > 6 ? ", …" : string.Empty)),
        ]),
    ];

    public static IReadOnlyList<PropertyGroupViewModel> ForImportedSymbol(ImportedSymbol symbol) =>
    [
        new PropertyGroupViewModel("Imported symbol",
        [
            new PropertyRowViewModel("Name", string.IsNullOrEmpty(symbol.Name) ? "(by ordinal)" : symbol.Name),
            new PropertyRowViewModel("Ordinal", symbol.IsByOrdinal ? $"#{symbol.Ordinal}" : symbol.Ordinal.ToString()),
            new PropertyRowViewModel("Hint", symbol.Hint.ToString()),
            new PropertyRowViewModel("Import by", symbol.IsByOrdinal ? "Ordinal" : "Name"),
        ]),
    ];

    public static IReadOnlyList<PropertyGroupViewModel> ForExport(ExportedSymbol export) =>
    [
        new PropertyGroupViewModel("Exported symbol",
        [
            new PropertyRowViewModel("Name", export.DisplayName),
            new PropertyRowViewModel("Ordinal", export.Ordinal.ToString()),
            new PropertyRowViewModel("RVA", export.Rva is { } rva ? Hex.Rva(rva) : "not file-backed", true),
            new PropertyRowViewModel("Forwarder", export.ForwarderName ?? "—"),
            new PropertyRowViewModel("Kind", export.IsForwarder ? "Forwarded export" : "Local export"),
        ]),
    ];

    public static IReadOnlyList<PropertyGroupViewModel> ForResource(
        PeResourceEntry resource,
        BinaryImage image)
    {
        var section = resource.Rva is { } rva ? image.AddressMap.GetSectionForRva(rva) : null;

        return
        [
            new PropertyGroupViewModel("Resource",
            [
                new PropertyRowViewModel("Type", resource.TypeName),
                new PropertyRowViewModel("Type ID", resource.TypeId == 0 ? "named" : Hex.UInt32(resource.TypeId), true),
                new PropertyRowViewModel("Name", resource.Name),
                new PropertyRowViewModel("Language", resource.LanguageName),
                new PropertyRowViewModel("Language ID", $"0x{resource.LanguageId:X4}", true),
                new PropertyRowViewModel("Size", $"{resource.Size:N0} bytes"),
                new PropertyRowViewModel("RVA", resource.Rva is { } resourceRva ? Hex.Rva(resourceRva) : "—", true),
                new PropertyRowViewModel("Virtual address", resource.Rva is { } resourceRva2
                    ? Hex.Va(image.ImageBase + resourceRva2)
                    : "—", true),
                new PropertyRowViewModel("File offset", resource.FileOffset is { } offset ? Hex.Offset(offset) : "—", true),
                new PropertyRowViewModel("Code page", $"0x{resource.CodePage:X8}", true),
                new PropertyRowViewModel("Section", section?.Name ?? "—"),
            ]),
        ];
    }

    public static IReadOnlyList<PropertyGroupViewModel> ForRelocations(PeRelocationInfo relocations)
    {
        var groups = new List<PropertyGroupViewModel>
        {
            new("Base relocations",
            [
                new PropertyRowViewModel("Entries", relocations.Count.ToString("N0")),
                new PropertyRowViewModel("Pages", relocations.PageCount.ToString("N0")),
                new PropertyRowViewModel("First RVA", relocations.Count > 0 ? Hex.Rva(relocations.Entries[0].Rva) : "—", true),
                new PropertyRowViewModel("Last RVA", relocations.Count > 0
                    ? Hex.Rva(relocations.Entries[^1].Rva)
                    : "—", true),
            ]),
        };

        if (relocations.TypeCounts.Count > 0)
        {
            groups.Add(new PropertyGroupViewModel(
                "Relocation types",
                relocations.TypeCounts
                    .Select(pair => new PropertyRowViewModel(pair.Key, pair.Value.ToString("N0")))
                    .ToArray()));
        }

        return groups;
    }

    public static IReadOnlyList<PropertyGroupViewModel> ForTls(PeTlsInfo tls, BinaryImage image) =>
    [
        new PropertyGroupViewModel("TLS directory",
        [
            new PropertyRowViewModel("Callbacks", tls.CallbackCount.ToString("N0")),
            new PropertyRowViewModel("Index RVA", tls.IndexRva is { } indexRva ? Hex.Rva(indexRva) : "—", true),
            new PropertyRowViewModel("Index VA", tls.IndexRva is { } indexRva2 ? Hex.Va(image.ImageBase + indexRva2) : "—", true),
            new PropertyRowViewModel("Template size", $"{tls.TemplateSize:N0} bytes"),
            new PropertyRowViewModel("Size of zero fill", $"{tls.SizeOfZeroFill:N0} bytes"),
            new PropertyRowViewModel("Characteristics", tls.Characteristics),
        ]),
    ];

    public static IReadOnlyList<PropertyGroupViewModel> ForTlsCallback(
        PeTlsCallback callback,
        BinaryImage image)
    {
        var section = callback.Rva == 0 ? null : image.AddressMap.GetSectionForRva(callback.Rva);

        return
        [
            new PropertyGroupViewModel("TLS callback",
            [
                new PropertyRowViewModel("Virtual address", callback.VirtualAddress == 0 ? "unresolved" : Hex.Va(callback.VirtualAddress), true),
                new PropertyRowViewModel("RVA", callback.Rva == 0 ? "—" : Hex.Rva(callback.Rva), true),
                new PropertyRowViewModel("File offset", callback.FileOffset is { } offset ? Hex.Offset(offset) : "—", true),
                new PropertyRowViewModel("Section", section?.Name ?? "—"),
            ]),
        ];
    }

    public static IReadOnlyList<PropertyGroupViewModel> ForExceptions(PeExceptionInfo exceptions)
    {
        var first = exceptions.Functions.FirstOrDefault();
        var last = exceptions.Functions.LastOrDefault();

        return
        [
            new PropertyGroupViewModel("Exception directory",
            [
                new PropertyRowViewModel("Architecture", exceptions.IsX64 ? "x64 RUNTIME_FUNCTION" : "generic runtime function table"),
                new PropertyRowViewModel("Functions", exceptions.Count.ToString("N0")),
                new PropertyRowViewModel("First RVA", first is null ? "—" : Hex.Rva(first.BeginRva), true),
                new PropertyRowViewModel("First end", first is null ? "—" : Hex.Rva(first.EndRva), true),
                new PropertyRowViewModel("Last RVA", last is null ? "—" : Hex.Rva(last.BeginRva), true),
                new PropertyRowViewModel("Last end", last is null ? "—" : Hex.Rva(last.EndRva), true),
                new PropertyRowViewModel("With unwind info", exceptions.Functions.Count(function => function.HasUnwindInfo).ToString("N0")),
            ]),
        ];
    }

    public static IReadOnlyList<PropertyGroupViewModel> ForDebugEntry(PeDebugEntry entry) =>
    [
        new PropertyGroupViewModel("Debug entry",
        [
            new PropertyRowViewModel("Type", entry.TypeName),
            new PropertyRowViewModel("Type value", Hex.UInt32(entry.TypeValue), true),
            new PropertyRowViewModel("Timestamp", entry.TimestampUtc is { } timestamp ? Hex.Timestamp(timestamp) : "—"),
            new PropertyRowViewModel("Size", $"{entry.Size:N0} bytes"),
            new PropertyRowViewModel("RVA", entry.Rva == 0 ? "—" : Hex.Rva(entry.Rva), true),
            new PropertyRowViewModel("File offset", entry.FileOffset is { } offset ? Hex.Offset(offset) : "—", true),
            new PropertyRowViewModel("Signature", entry.Signature ?? "—"),
            new PropertyRowViewModel("PDB path", entry.PdbPath ?? "—", false, entry.PdbPath),
        ]),
    ];

    public static IReadOnlyList<PropertyGroupViewModel> ForLoadConfiguration(PeLoadConfigurationInfo configuration) =>
    [
        new PropertyGroupViewModel("Load configuration",
        [
            new PropertyRowViewModel("Size", $"{configuration.Size:N0} bytes"),
            new PropertyRowViewModel("Bitness", configuration.Is32Bit ? "32-bit" : "64-bit"),
            new PropertyRowViewModel("Guard flags", configuration.GuardFlags, true),
        ]),
    ];

    public static IReadOnlyList<PropertyGroupViewModel> ForInstruction(
        DisassembledInstruction instruction,
        BinaryImage image)
    {
        var section = image.AddressMap.GetSectionForRva(instruction.Rva);
        var flow = instruction.Flow switch
        {
            InstructionFlow.Call => "Call",
            InstructionFlow.IndirectCall => "Indirect call",
            InstructionFlow.UnconditionalBranch => "Jump",
            InstructionFlow.ConditionalBranch => "Conditional jump",
            InstructionFlow.IndirectBranch => "Indirect jump",
            InstructionFlow.Return => "Return",
            InstructionFlow.Interrupt => "Interrupt",
            InstructionFlow.Invalid => "Invalid instruction",
            _ => "Next",
        };

        var target = instruction.TargetRva is { } targetRva
            ? $"{Hex.Rva(targetRva)} · {Hex.Va(image.ImageBase + targetRva)}"
            : instruction.TargetVirtualAddress is { } targetVa
                ? $"{Hex.Va(targetVa)} (no decoded executable code)"
                : "—";

        var dataTarget = instruction.DataTargetRva is { } dataRva
            ? $"{Hex.Rva(dataRva)} · {Hex.Va(image.ImageBase + dataRva)}"
            : instruction.DataTargetVirtualAddress is { } dataVa
                ? Hex.Va(dataVa)
                : "—";

        return
        [
            new PropertyGroupViewModel("Instruction",
            [
                new PropertyRowViewModel("RVA", Hex.Rva(instruction.Rva), true),
                new PropertyRowViewModel("Virtual address", Hex.Va(instruction.VirtualAddress), true),
                new PropertyRowViewModel("File offset", instruction.FileOffset is { } offset ? Hex.Offset(offset) : "—", true),
                new PropertyRowViewModel("Section", section?.Name ?? instruction.SectionName),
                new PropertyRowViewModel("Bytes", string.Join(' ', instruction.Bytes.Select(value => value.ToString("X2"))), true),
                new PropertyRowViewModel("Length", $"{instruction.Length} byte{(instruction.Length == 1 ? string.Empty : "s")}"),
                new PropertyRowViewModel("Mnemonic", instruction.Mnemonic, true),
                new PropertyRowViewModel("Operands", string.IsNullOrWhiteSpace(instruction.Operands) ? "—" : instruction.Operands, true),
                new PropertyRowViewModel("Flow", flow),
                new PropertyRowViewModel("Target", target, true),
                new PropertyRowViewModel("Data target", dataTarget, true),
                new PropertyRowViewModel("Valid", instruction.IsValid ? "Yes" : "No — undecodable byte(s)"),
            ]),
        ];
    }

    public static IReadOnlyList<PropertyGroupViewModel> ForFunction(
        FunctionAnalysis function,
        AnalysisResult analysis,
        BinaryImage image,
        AddressAnnotation? annotation = null)
    {
        var discovered = function.Function;
        var callers = analysis.CallersOf(discovered.Rva)
            .Select(rva => analysis.FindFunction(rva)?.Function.Name ?? Hex.Rva(rva))
            .ToArray();
        var callees = analysis.CalleesOf(discovered.Rva)
            .Select(rva => analysis.FindFunction(rva)?.Function.Name ?? Hex.Rva(rva))
            .ToArray();
        var entry = function.EntryBlock;
        var incoming = analysis.XrefsTo(discovered.Rva);

        var groups = new List<PropertyGroupViewModel>
        {
            new("Function",
            [
                new PropertyRowViewModel("Name", discovered.Name),
                new PropertyRowViewModel("RVA", Hex.Rva(discovered.Rva), true),
                new PropertyRowViewModel("Virtual address", Hex.Va(discovered.VirtualAddress), true),
                new PropertyRowViewModel("Section", discovered.SectionName),
                new PropertyRowViewModel("Size", $"{discovered.Size:N0} bytes"),
                new PropertyRowViewModel("Range", $"{Hex.Rva(discovered.Rva)} – {Hex.Rva(discovered.EndRva)}", true),
                new PropertyRowViewModel("Instructions", discovered.InstructionCount.ToString("N0")),
                new PropertyRowViewModel("Basic blocks", $"{discovered.BasicBlockCount:N0}"),
                new PropertyRowViewModel("CFG edges", function.EdgeCount.ToString("N0")),
                new PropertyRowViewModel("Cyclomatic complexity", function.CyclomaticComplexity.ToString()),
                new PropertyRowViewModel("Sources", discovered.SourceText),
                new PropertyRowViewModel("Entry block", entry is null ? "—" : Hex.Rva(entry.StartRva), true),
                new PropertyRowViewModel("Entry references", incoming.Count.ToString("N0")),
            ]),
            new PropertyGroupViewModel("Call graph",
            [
                new PropertyRowViewModel("Callers", $"{callers.Length:N0}" + (callers.Length == 0 ? string.Empty : " · " + JoinNames(callers))),
                new PropertyRowViewModel("Callees", $"{callees.Length:N0}" + (callees.Length == 0 ? string.Empty : " · " + JoinNames(callees))),
            ]),
        };

        if (annotation is not null)
        {
            groups.Add(new PropertyGroupViewModel(
                "Project annotation",
                [
                    new PropertyRowViewModel("User name", annotation.UserName ?? "—"),
                    new PropertyRowViewModel("Comment", annotation.Comment ?? "—", false),
                    new PropertyRowViewModel("Updated", annotation.UpdatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm")),
                ]));
        }

        return groups;
    }

    public static IReadOnlyList<PropertyGroupViewModel> ForBookmark(ProjectBookmark bookmark) =>
    [
        new PropertyGroupViewModel("Bookmark",
        [
            new PropertyRowViewModel("Title", bookmark.Title),
            new PropertyRowViewModel("RVA", Hex.Rva(bookmark.Rva), true),
            new PropertyRowViewModel("Comment", bookmark.Comment ?? "—"),
            new PropertyRowViewModel("Created", bookmark.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm")),
        ]),
    ];

    public static IReadOnlyList<PropertyGroupViewModel> ForString(
        StringLiteral value,
        BinaryImage image,
        AddressAnnotation? annotation = null)
    {
        var section = image.AddressMap.GetSectionForRva(value.Rva);
        var rows = new List<PropertyRowViewModel>
        {
            new("Value", value.Value, false),
            new("Encoding", value.EncodingText),
            new("Section", section?.Name ?? value.SectionName),
            new("RVA", Hex.Rva(value.Rva), true),
            new("Virtual address", Hex.Va(image.ImageBase + value.Rva), true),
            new("File offset", Hex.Offset(value.FileOffset), true),
            new("Byte length", value.ByteLength.ToString("N0")),
            new("Character count", value.Value.Length.ToString("N0")),
            new("References", value.ReferenceCount.ToString("N0")),
        };

        foreach (var reference in value.References.Take(8))
        {
            rows.Add(new PropertyRowViewModel(
                "Reference",
                $"{Hex.Rva(reference.FromRva)} · {reference.KindText} · {reference.SourceText}",
                true));
        }

        var groups = new List<PropertyGroupViewModel>
        {
            new("String", rows),
        };
        if (annotation is not null)
        {
            groups.Add(AnnotationGroup(annotation));
        }

        return groups;
    }

    public static IReadOnlyList<PropertyGroupViewModel> ForCrossReference(
        CrossReference reference,
        BinaryImage image,
        AddressAnnotation? annotation = null)
    {
        var groups = new List<PropertyGroupViewModel>
        {
            new("Cross reference",
            [
                new PropertyRowViewModel("Kind", reference.KindText),
                new PropertyRowViewModel("From", $"{Hex.Rva(reference.FromRva)} · {reference.FromSection}", true),
                new PropertyRowViewModel("From VA", Hex.Va(image.ImageBase + reference.FromRva), true),
                new PropertyRowViewModel("To", $"{Hex.Rva(reference.ToRva)} · {reference.ToSection ?? "external"}", true),
                new PropertyRowViewModel("To VA", Hex.Va(image.ImageBase + reference.ToRva), true),
                new PropertyRowViewModel("Instruction", reference.SourceText, true),
            ]),
        };

        if (annotation is not null)
        {
            groups.Add(AnnotationGroup(annotation));
        }

        return groups;
    }

    private static PropertyGroupViewModel AnnotationGroup(AddressAnnotation annotation) =>
        new(
            "Project annotation",
            [
                new PropertyRowViewModel("User name", annotation.UserName ?? "—"),
                new PropertyRowViewModel("Comment", annotation.Comment ?? "—"),
                new PropertyRowViewModel("Updated", annotation.UpdatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm")),
            ]);

    public static IReadOnlyList<PropertyGroupViewModel> ForAnalysisSummary(AnalysisResult analysis) =>
    [
        new PropertyGroupViewModel("Analysis",
        [
            new PropertyRowViewModel("Functions", analysis.FunctionCount.ToString("N0")),
            new PropertyRowViewModel("Basic blocks", analysis.Functions.Sum(function => function.Blocks.Count).ToString("N0")),
            new PropertyRowViewModel("Cross references", analysis.CrossReferenceCount.ToString("N0")),
            new PropertyRowViewModel("Strings", analysis.StringCount.ToString("N0")),
            new PropertyRowViewModel("Truncated", analysis.IsTruncated ? "Yes" : "No"),
            new PropertyRowViewModel("Diagnostics", analysis.Diagnostics.Count == 0 ? "None" : string.Join(" · ", analysis.Diagnostics.Take(4))),
        ]),
    ];

    private static string JoinNames(IReadOnlyList<string> names) =>
        names.Count <= 5
            ? string.Join(", ", names)
            : string.Join(", ", names.Take(5)) + $" +{names.Count - 5} more";

    public static IReadOnlyList<PropertyGroupViewModel> ForFinding(PeFinding finding) =>
    [
        new PropertyGroupViewModel("Observation",
        [
            new PropertyRowViewModel("Severity", finding.Severity.ToString()),
            new PropertyRowViewModel("Code", finding.Code),
            new PropertyRowViewModel("Title", finding.Title),
            new PropertyRowViewModel("RVA", finding.Rva is { } rva ? Hex.Rva(rva) : "—", true),
            new PropertyRowViewModel("Detail", finding.Detail),
        ]),
    ];

    public static IReadOnlyList<PropertyGroupViewModel> ForDataDirectory(DataDirectoryInfo directory) =>
    [
        new PropertyGroupViewModel("Data directory",
        [
            new PropertyRowViewModel("Index", directory.Index.ToString()),
            new PropertyRowViewModel("Name", directory.Name),
            new PropertyRowViewModel("RVA", directory.IsPresent ? Hex.Rva(directory.VirtualAddress) : "—", true),
            new PropertyRowViewModel("Size", directory.IsPresent ? $"{directory.Size:N0} bytes" : "—"),
            new PropertyRowViewModel("Present", directory.IsPresent ? "Yes" : "No"),
        ]),
    ];

    public static IReadOnlyList<PropertyGroupViewModel> ForPropertyBag(PropertyBag bag) =>
        [new PropertyGroupViewModel(bag.Title, bag.Rows)];

    public static IReadOnlyList<PropertyGroupViewModel> ForPropertyRow(PropertyRowViewModel row) =>
        [new PropertyGroupViewModel("Property", [row])];

    public static IReadOnlyList<PropertyGroupViewModel> ForNotAvailable(NotAvailable unavailable) =>
    [
        new PropertyGroupViewModel("Unavailable",
        [
            new PropertyRowViewModel("Item", unavailable.Title),
            new PropertyRowViewModel("Status", "Not available in this build"),
            new PropertyRowViewModel("Detail", unavailable.Detail),
        ]),
    ];

    private static bool HasFlag(BinaryImage image, PeDllCharacteristics flag) =>
        (image.DllCharacteristics & flag) == flag;
}
