namespace Obsidian.Pe;

/// <summary>Standard PE data-directory indices.</summary>
internal enum DataDirectoryKind
{
    Export = 0,
    Import = 1,
    Resource = 2,
    Exception = 3,
    Certificate = 4,
    BaseRelocation = 5,
    Debug = 6,
    Architecture = 7,
    GlobalPointer = 8,
    Tls = 9,
    LoadConfiguration = 10,
    BoundImport = 11,
    ImportAddressTable = 12,
    DelayImport = 13,
    ClrRuntime = 14,
    Reserved = 15,
}
