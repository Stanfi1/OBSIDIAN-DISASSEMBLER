namespace Obsidian.Core.Model;

/// <summary>One leaf in the Win32 resource tree.</summary>
public sealed record PeResourceEntry(
    string TypeName,
    uint TypeId,
    string Name,
    uint NameId,
    uint LanguageId,
    string LanguageName,
    uint Size,
    uint? Rva,
    long? FileOffset,
    uint CodePage)
{
    public string DisplayName => Name;

    public string FullPath => $"{TypeName} / {Name} / {LanguageName}";

    public string DisplayValue => Size == 0 ? "empty" : $"{Size:N0} bytes";
}

public sealed record PeRelocationEntry(uint PageRva, uint Rva, string TypeName);

public sealed class PeRelocationInfo
{
    public PeRelocationInfo(IReadOnlyList<PeRelocationEntry> entries)
    {
        Entries = entries;
        TypeCounts = entries
            .GroupBy(entry => entry.TypeName, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
    }

    public IReadOnlyList<PeRelocationEntry> Entries { get; }

    public IReadOnlyDictionary<string, int> TypeCounts { get; }

    public int Count => Entries.Count;

    public int PageCount => Entries.Select(entry => entry.PageRva).Distinct().Count();
}

public sealed record PeTlsCallback(ulong VirtualAddress, uint Rva, long? FileOffset);

public sealed record PeTlsInfo(
    uint? IndexRva,
    uint SizeOfZeroFill,
    string Characteristics,
    uint TemplateSize,
    IReadOnlyList<PeTlsCallback> Callbacks)
{
    public int CallbackCount => Callbacks.Count;
}

public sealed record PeRuntimeFunction(
    uint BeginRva,
    uint EndRva,
    bool HasUnwindInfo)
{
    public uint Size => EndRva >= BeginRva ? EndRva - BeginRva : 0;
}

public sealed record PeExceptionInfo(
    bool IsX64,
    IReadOnlyList<PeRuntimeFunction> Functions)
{
    public int Count => Functions.Count;
}

public sealed record PeDebugEntry(
    string TypeName,
    uint TypeValue,
    DateTimeOffset? TimestampUtc,
    uint Size,
    uint Rva,
    long? FileOffset,
    string? Signature,
    string? PdbPath);

public sealed record PeLoadConfigurationInfo(
    uint Size,
    bool Is32Bit,
    string GuardFlags);

/// <summary>Conventional Win32 resource type and language names.</summary>
public static class PeResourceNames
{
    private static readonly Dictionary<uint, string> TypeNames = new()
    {
        [1] = "Cursor",
        [2] = "Bitmap",
        [3] = "Icon",
        [4] = "Menu",
        [5] = "Dialog",
        [6] = "String Table",
        [7] = "Font Directory",
        [8] = "Font",
        [9] = "Accelerator",
        [10] = "RC Data",
        [11] = "Message Table",
        [12] = "Group Cursor",
        [14] = "Group Icon",
        [16] = "Version",
        [17] = "Dialog Include",
        [19] = "Plug and Play",
        [20] = "VXD",
        [21] = "Animated Cursor",
        [22] = "Animated Icon",
        [23] = "HTML",
        [24] = "Manifest",
    };

    private static readonly Dictionary<uint, string> LanguageNames = new()
    {
        [0] = "Neutral",
        [1028] = "zh-TW",
        [1029] = "cs-CZ",
        [1030] = "da-DK",
        [1031] = "de-DE",
        [1033] = "en-US",
        [1035] = "fi-FI",
        [1036] = "fr-FR",
        [1038] = "hu-HU",
        [1040] = "it-IT",
        [1041] = "ja-JP",
        [1042] = "ko-KR",
        [1045] = "pl-PL",
        [1046] = "pt-BR",
        [1049] = "ru-RU",
        [1051] = "sk-SK",
        [1053] = "sv-SE",
        [2052] = "zh-CN",
        [2057] = "en-GB",
        [3082] = "es-ES",
    };

    public static string TypeName(uint id) =>
        TypeNames.TryGetValue(id, out var name) ? name : $"Type 0x{id:X4}";

    public static string LanguageName(uint id) =>
        LanguageNames.TryGetValue(id, out var name) ? $"{name} ({id})" : $"Language 0x{id:X4}";
}
