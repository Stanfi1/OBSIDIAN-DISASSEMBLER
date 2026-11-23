namespace Obsidian.Core.Model;

/// <summary>Instruction-set family required to disassemble the image.</summary>
public enum BinaryArchitecture
{
    Unknown = 0,
    X86,
    X64,
    Arm64,
}

/// <summary>Portable Executable container flavour.</summary>
public enum PeFormat
{
    Unknown = 0,
    Pe32,
    Pe32Plus,
}

/// <summary>PE optional-header subsystem, kept independent of any parser library.</summary>
public enum PeSubsystem
{
    Unknown = 0,
    Native = 1,
    WindowsGui = 2,
    WindowsConsole = 3,
    PosixConsole = 7,
    NativeWindows = 8,
    WindowsCeGui = 9,
    EfiApplication = 10,
    EfiBootServiceDriver = 11,
    EfiRuntimeDriver = 12,
    EfiRom = 13,
    Xbox = 14,
    WindowsBootApplication = 16,
}

/// <summary>Security-relevant bits from IMAGE_DLLCHARACTERISTICS.</summary>
[Flags]
public enum PeDllCharacteristics : ushort
{
    None = 0,
    HighEntropyVa = 0x0020,
    DynamicBase = 0x0040,
    ForceIntegrity = 0x0080,
    NxCompat = 0x0100,
    NoIsolation = 0x0200,
    NoSeh = 0x0400,
    NoBind = 0x0800,
    AppContainer = 0x1000,
    WdmDriver = 0x2000,
    ControlFlowGuard = 0x4000,
    TerminalServerAware = 0x8000,
}

/// <summary>Severity of a structural observation. Never a malware verdict.</summary>
public enum PeFindingSeverity
{
    Information = 0,
    Warning = 1,
}

public enum BinaryLoadFailure
{
    Unknown = 0,
    FileNotFound,
    AccessDenied,
    EmptyFile,
    NotAPortableExecutable,
    UnsupportedArchitecture,
    CorruptImage,
    FileTooLarge,
}

public enum LoadStage
{
    ReadingFile = 0,
    Hashing,
    ParsingHeaders,
    ReadingSections,
    ReadingImports,
    ReadingExports,
    ReadingDirectories,
    Analyzing,
    Disassembling,
    Completed,
}

public static class PeEnumExtensions
{
    public static string ToShortName(this BinaryArchitecture architecture) => architecture switch
    {
        BinaryArchitecture.X86 => "x86",
        BinaryArchitecture.X64 => "x64",
        BinaryArchitecture.Arm64 => "ARM64",
        _ => "unknown",
    };

    public static string ToDisplayName(this BinaryArchitecture architecture) => architecture switch
    {
        BinaryArchitecture.X86 => "x86 (32-bit)",
        BinaryArchitecture.X64 => "x64 (64-bit)",
        BinaryArchitecture.Arm64 => "ARM64",
        _ => "Unknown",
    };

    public static string ToDisplayName(this PeFormat format) => format switch
    {
        PeFormat.Pe32 => "PE32",
        PeFormat.Pe32Plus => "PE32+",
        _ => "Unknown",
    };

    public static string ToDisplayName(this PeSubsystem subsystem) => subsystem switch
    {
        PeSubsystem.Native => "Native",
        PeSubsystem.WindowsGui => "Windows GUI",
        PeSubsystem.WindowsConsole => "Windows Console",
        PeSubsystem.PosixConsole => "POSIX Console",
        PeSubsystem.NativeWindows => "Native Windows",
        PeSubsystem.WindowsCeGui => "Windows CE GUI",
        PeSubsystem.EfiApplication => "EFI Application",
        PeSubsystem.EfiBootServiceDriver => "EFI Boot Service Driver",
        PeSubsystem.EfiRuntimeDriver => "EFI Runtime Driver",
        PeSubsystem.EfiRom => "EFI ROM",
        PeSubsystem.Xbox => "Xbox",
        PeSubsystem.WindowsBootApplication => "Windows Boot Application",
        _ => "Unknown",
    };
}
