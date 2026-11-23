using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using AsmResolver.PE;
using AsmResolver.PE.Debug;
using AsmResolver.PE.Exceptions;
using AsmResolver.PE.File;
using AsmResolver.PE.Relocations;
using AsmResolver.PE.Win32Resources;
using Obsidian.Core.Model;
using Obsidian.Core.Services;
using PeModule = AsmResolver.PE.Imports.ImportedModule;

namespace Obsidian.Pe;

/// <summary>
/// Reads the PE container with AsmResolver and projects it into the
/// parser-independent Obsidian.Core model. Nothing is executed and no file is
/// written.
/// </summary>
public sealed class PeImageLoader : IBinaryImageLoader
{
    public async Task<BinaryImage> LoadAsync(
        string filePath,
        IProgress<LoadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new BinaryLoadException(BinaryLoadFailure.FileNotFound, "No file path was supplied.");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(filePath);
        }
        catch (Exception ex)
        {
            throw new BinaryLoadException(
                BinaryLoadFailure.Unknown,
                $"The path '{filePath}' is not a valid file path.",
                ex);
        }

        var info = new FileInfo(fullPath);
        if (!info.Exists)
        {
            throw new BinaryLoadException(
                BinaryLoadFailure.FileNotFound,
                $"The file '{info.Name}' does not exist.");
        }

        if (info.Length == 0)
        {
            throw new BinaryLoadException(
                BinaryLoadFailure.EmptyFile,
                $"The file '{info.Name}' is empty.");
        }

        if (info.Length > int.MaxValue)
        {
            throw new BinaryLoadException(
                BinaryLoadFailure.FileTooLarge,
                "Files larger than 2 GB are not supported by the current in-memory loader.");
        }

        progress?.Report(new LoadProgress(LoadStage.ReadingFile, $"Reading {info.Name}…", 5));

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new BinaryLoadException(
                BinaryLoadFailure.AccessDenied,
                $"Access to '{info.Name}' was denied.",
                ex);
        }
        catch (FileNotFoundException ex)
        {
            throw new BinaryLoadException(
                BinaryLoadFailure.FileNotFound,
                "The file disappeared while it was being opened.",
                ex);
        }
        catch (IOException ex)
        {
            throw new BinaryLoadException(
                BinaryLoadFailure.Unknown,
                $"The file '{info.Name}' could not be read: {ex.Message}",
                ex);
        }

        cancellationToken.ThrowIfCancellationRequested();

        ValidateDosAndPeSignature(bytes, info.Name);

        progress?.Report(new LoadProgress(LoadStage.Hashing, "Computing SHA-256…", 15));
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes));

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new LoadProgress(LoadStage.ParsingHeaders, "Parsing PE headers…", 25));

        PEImage image;
        try
        {
            image = PEImage.FromBytes(bytes);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new BinaryLoadException(
                BinaryLoadFailure.NotAPortableExecutable,
                $"'{info.Name}' is not a readable PE image: {ex.Message}",
                ex);
        }

        var pe = image.PEFile
            ?? throw new BinaryLoadException(
                BinaryLoadFailure.CorruptImage,
                "The PE image does not contain a section table.");

        var optionalHeader = pe.OptionalHeader
            ?? throw new BinaryLoadException(
                BinaryLoadFailure.CorruptImage,
                "The PE image does not contain an optional header.");

        var fileHeader = pe.FileHeader
            ?? throw new BinaryLoadException(
                BinaryLoadFailure.CorruptImage,
                "The PE image does not contain a COFF file header.");

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new LoadProgress(LoadStage.ReadingSections, "Reading section table…", 40));

        var sections = ReadSections(pe);
        var format = ReadFormat(image);
        var architecture = ReadArchitecture(fileHeader.Machine);

        if (architecture == BinaryArchitecture.Unknown)
        {
            // The file is a valid PE, but the disassembly stage cannot proceed
            // with an unknown ISA. Loading is still useful for inspection.
            // The finding below communicates the limitation explicitly.
        }

        var addressMap = new AddressMap(
            sections,
            optionalHeader.SizeOfHeaders,
            optionalHeader.ImageBase,
            optionalHeader.SizeOfImage,
            bytes.LongLength);

        var dataDirectories = ReadDataDirectories(optionalHeader);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new LoadProgress(LoadStage.ReadingImports, "Reading import table…", 55));

        var findings = new List<PeFinding>();
        var imports = ReadImports(image, findings);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new LoadProgress(LoadStage.ReadingExports, "Reading export table…", 70));

        var exports = ReadExports(image, pe, findings);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new LoadProgress(LoadStage.ReadingDirectories, "Reading resources…", 76));

        var resources = ReadResources(image, findings);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new LoadProgress(LoadStage.ReadingDirectories, "Reading base relocations…", 81));

        var relocations = ReadRelocations(image, pe, findings);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new LoadProgress(LoadStage.ReadingDirectories, "Reading TLS directory…", 85));

        var tls = ReadTls(image, pe, optionalHeader.ImageBase, addressMap, findings);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new LoadProgress(LoadStage.ReadingDirectories, "Reading exception directory…", 88));

        var exceptions = ReadExceptions(image, pe, findings);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new LoadProgress(LoadStage.ReadingDirectories, "Reading debug directory…", 91));

        var debugEntries = ReadDebugEntries(bytes, optionalHeader, addressMap, findings);
        var loadConfiguration = ReadLoadConfiguration(image, findings);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new LoadProgress(LoadStage.ReadingDirectories, "Inspecting CLR runtime header…", 94));

        var dotNetDirectory = dataDirectories[(int)DataDirectoryKind.ClrRuntime].IsPresent
            ? TryReadDotNetDirectory(image, findings)
            : null;

        var isManaged = dotNetDirectory is not null
            || dataDirectories[(int)DataDirectoryKind.ClrRuntime].IsPresent;

        if (isManaged && dotNetDirectory is null)
        {
            findings.Add(new PeFinding(
                PeFindingSeverity.Information,
                "MANAGED_IMAGE",
                "Managed image",
                "The CLR runtime header directory is present. This is a .NET assembly; native disassembly covers only its bootstrapping stub."));
        }

        progress?.Report(new LoadProgress(LoadStage.Analyzing, "Checking structural properties…", 95));

        findings.AddRange(AnalyzeSections(sections, bytes.LongLength));
        findings.AddRange(AnalyzeSecurity(optionalHeader, format, sections, imports, dataDirectories));

        var entrySection = addressMap.GetSectionForRva(optionalHeader.AddressOfEntryPoint);
        if (optionalHeader.AddressOfEntryPoint == 0)
        {
            findings.Add(new PeFinding(
                PeFindingSeverity.Information,
                "NO_ENTRY_POINT",
                "No entry point",
                "AddressOfEntryPoint is zero. This is common for resource-only DLLs."));
        }
        else if (entrySection is null)
        {
            findings.Add(new PeFinding(
                PeFindingSeverity.Warning,
                "ENTRY_POINT_UNMAPPED",
                "Entry point is outside every section",
                $"RVA 0x{optionalHeader.AddressOfEntryPoint:X8} does not fall inside a mapped section.",
                optionalHeader.AddressOfEntryPoint));
        }
        else if (!entrySection.IsExecutable && !entrySection.ContainsCode)
        {
            findings.Add(new PeFinding(
                PeFindingSeverity.Warning,
                "ENTRY_POINT_NOT_CODE",
                "Entry point is not in an executable section",
                $"RVA 0x{optionalHeader.AddressOfEntryPoint:X8} points into {entrySection.Name}.",
                optionalHeader.AddressOfEntryPoint));
        }

        if (!addressMap.TryRvaToFileOffset(optionalHeader.AddressOfEntryPoint, out _)
            && optionalHeader.AddressOfEntryPoint != 0)
        {
            findings.Add(new PeFinding(
                PeFindingSeverity.Information,
                "ENTRY_POINT_NOT_FILE_BACKED",
                "Entry point has no file-backed bytes",
                "The entry point RVA does not map to raw data. The bytes may be created at load time."));
        }

        if (format == PeFormat.Pe32Plus
            && (optionalHeader.DllCharacteristics & DllCharacteristics.HighEntropyVA) == 0
            && !isManaged)
        {
            findings.Add(new PeFinding(
                PeFindingSeverity.Information,
                "NO_HIGH_ENTROPY_VA",
                "High-entropy ASLR is not enabled",
                "The image does not advertise IMAGE_DLLCHARACTERISTICS_HIGH_ENTROPY_VA."));
        }

        var sortedFindings = findings
            .OrderByDescending(f => f.Severity)
            .ThenBy(f => f.Code, StringComparer.Ordinal)
            .ToArray();

        cancellationToken.ThrowIfCancellationRequested();

        var timeStamp = ToUtc(image.TimeDateStamp);

        var result = new BinaryImage
        {
            FilePath = fullPath,
            FileName = info.Name,
            FileSize = info.Length,
            Bytes = bytes,
            Sha256 = sha256,
            LoadedAtUtc = DateTimeOffset.UtcNow,
            Architecture = architecture,
            Format = format,
            Machine = (ushort)fileHeader.Machine,
            Subsystem = MapSubsystem(optionalHeader.SubSystem),
            DllCharacteristics = (PeDllCharacteristics)(ushort)optionalHeader.DllCharacteristics,
            Characteristics = (ushort)fileHeader.Characteristics,
            ImageBase = optionalHeader.ImageBase,
            PeHeaderOffset = pe.DosHeader?.NextHeaderOffset ?? 0,
            EntryPointRva = optionalHeader.AddressOfEntryPoint,
            SizeOfImage = optionalHeader.SizeOfImage,
            SizeOfHeaders = optionalHeader.SizeOfHeaders,
            SectionAlignment = optionalHeader.SectionAlignment,
            FileAlignment = optionalHeader.FileAlignment,
            CheckSum = optionalHeader.CheckSum,
            MajorLinkerVersion = optionalHeader.MajorLinkerVersion,
            MinorLinkerVersion = optionalHeader.MinorLinkerVersion,
            MajorSubsystemVersion = optionalHeader.MajorSubsystemVersion,
            MinorSubsystemVersion = optionalHeader.MinorSubsystemVersion,
            TimeDateStampUtc = timeStamp,
            IsManaged = isManaged,
            ClrRuntimeVersion = dotNetDirectory is null
                ? null
                : $"{dotNetDirectory.MajorRuntimeVersion}.{dotNetDirectory.MinorRuntimeVersion}",
            HasRelocations = dataDirectories[(int)DataDirectoryKind.BaseRelocation].IsPresent,
            HasTls = dataDirectories[(int)DataDirectoryKind.Tls].IsPresent,
            HasDebugData = dataDirectories[(int)DataDirectoryKind.Debug].IsPresent,
            HasResources = dataDirectories[(int)DataDirectoryKind.Resource].IsPresent,
            HasExceptions = dataDirectories[(int)DataDirectoryKind.Exception].IsPresent,
            IsSigned = dataDirectories[(int)DataDirectoryKind.Certificate].IsPresent,
            HasLoadConfiguration = dataDirectories[(int)DataDirectoryKind.LoadConfiguration].IsPresent,
            Sections = sections,
            DataDirectories = dataDirectories,
            Imports = imports,
            Exports = exports,
            Resources = resources,
            Relocations = relocations,
            Tls = tls,
            Exceptions = exceptions,
            DebugEntries = debugEntries,
            LoadConfiguration = loadConfiguration,
            Findings = sortedFindings,
            AddressMap = addressMap,
        };

        progress?.Report(new LoadProgress(LoadStage.Completed, "Binary loaded.", 100));
        return result;
    }

    private static void ValidateDosAndPeSignature(byte[] bytes, string fileName)
    {
        if (bytes.Length < 0x40 || bytes[0] != (byte)'M' || bytes[1] != (byte)'Z')
        {
            throw new BinaryLoadException(
                BinaryLoadFailure.NotAPortableExecutable,
                $"'{fileName}' does not start with a DOS MZ header.");
        }

        var peOffset = BitConverter.ToUInt32(bytes, 0x3C);
        if (peOffset > bytes.Length - 4L
            || peOffset < 0x40
            || BitConverter.ToUInt32(bytes, (int)peOffset) != 0x00004550)
        {
            throw new BinaryLoadException(
                BinaryLoadFailure.NotAPortableExecutable,
                $"'{fileName}' does not contain a valid PE signature.");
        }
    }

    private static IReadOnlyList<PeSection> ReadSections(PEFile pe)
    {
        var result = new List<PeSection>(pe.Sections.Count);
        foreach (var section in pe.Sections)
        {
            var offset = section.Offset <= uint.MaxValue ? (uint)section.Offset : 0u;
            var rawSize = section.GetPhysicalSize();
            result.Add(new PeSection
            {
                Name = section.Name?.ToString() ?? string.Empty,
                VirtualAddress = section.Rva,
                VirtualSize = section.GetVirtualSize(),
                RawOffset = offset,
                RawSize = rawSize,
                Characteristics = (uint)section.Characteristics,
                IsReadable = section.IsMemoryRead || section.IsReadable,
                IsWritable = section.IsMemoryWrite,
                IsExecutable = section.IsMemoryExecute,
                ContainsCode = section.IsContentCode,
                ContainsInitializedData = section.IsContentInitializedData,
                IsDiscardable = section.IsMemoryDiscardable,
            });
        }

        return result;
    }

    private static PeFormat ReadFormat(PEImage image) => image.PEKind switch
    {
        OptionalHeaderMagic.PE32 => PeFormat.Pe32,
        OptionalHeaderMagic.PE32Plus => PeFormat.Pe32Plus,
        _ => PeFormat.Unknown,
    };

    private static BinaryArchitecture ReadArchitecture(MachineType machine) => (ushort)machine switch
    {
        0x014C => BinaryArchitecture.X86,
        0x8664 => BinaryArchitecture.X64,
        0xAA64 => BinaryArchitecture.Arm64,
        _ => BinaryArchitecture.Unknown,
    };

    private static PeSubsystem MapSubsystem(SubSystem subsystem) =>
        Enum.IsDefined(typeof(PeSubsystem), (int)subsystem)
            ? (PeSubsystem)(int)subsystem
            : PeSubsystem.Unknown;

    private static IReadOnlyList<DataDirectoryInfo> ReadDataDirectories(OptionalHeader optionalHeader)
    {
        var result = new List<DataDirectoryInfo>(DataDirectoryInfo.StandardNames.Length);
        for (var i = 0; i < DataDirectoryInfo.StandardNames.Length; i++)
        {
            var name = DataDirectoryInfo.StandardNames[i];
            if (i < optionalHeader.DataDirectories.Count)
            {
                var directory = optionalHeader.DataDirectories[i];
                result.Add(new DataDirectoryInfo(i, name, directory.VirtualAddress, directory.Size));
            }
            else
            {
                result.Add(new DataDirectoryInfo(i, name, 0, 0));
            }
        }

        return result;
    }

    private static IReadOnlyList<ImportedModule> ReadImports(PEImage image, List<PeFinding> findings)
    {
        try
        {
            var modules = image.Imports
                .Select(module => new ImportedModule(
                    string.IsNullOrEmpty(module.Name) ? "(unnamed module)" : module.Name,
                    module.Symbols
                        .Select(symbol => new ImportedSymbol(
                            symbol.Name ?? string.Empty,
                            symbol.Ordinal,
                            symbol.Hint,
                            symbol.IsImportByOrdinal))
                        .ToArray()))
                .OrderBy(module => module.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return modules;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            findings.Add(new PeFinding(
                PeFindingSeverity.Warning,
                "IMPORT_TABLE_UNREADABLE",
                "Import table could not be read",
                "The import directory is malformed or uses an unsupported layout. " + ex.Message));
            return [];
        }
    }

    private static IReadOnlyList<ExportedSymbol> ReadExports(
        PEImage image,
        PEFile pe,
        List<PeFinding> findings)
    {
        try
        {
            var directory = image.Exports;
            if (directory is null)
            {
                return [];
            }

            return directory.Entries
                .Select(entry => new ExportedSymbol(
                    entry.Name ?? string.Empty,
                    entry.Ordinal,
                    TryGetReferenceRva(entry.Address, pe),
                    entry.ForwarderName))
                .OrderBy(symbol => symbol.Ordinal)
                .ThenBy(symbol => symbol.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            findings.Add(new PeFinding(
                PeFindingSeverity.Warning,
                "EXPORT_TABLE_UNREADABLE",
                "Export table could not be read",
                "The export directory is malformed or uses an unsupported layout. " + ex.Message));
            return [];
        }
    }

    private static uint? TryGetReferenceRva(AsmResolver.ISegmentReference? reference, PEFile pe)
    {
        if (reference is null)
        {
            return null;
        }

        // Export table entries are normally section-relative references that
        // carry their RVA directly. Forwarder entries have no local address.
        if (reference is PESegmentReference segmentReference && segmentReference.IsValidAddress)
        {
            return segmentReference.Rva;
        }

        try
        {
            var reader = reference.CreateReader();
            return pe.FileOffsetToRva((ulong)reader.Offset);
        }
        catch
        {
            // Forwarder entries and virtual-only segments have no file offset.
            // The ordinal and name are still valid metadata.
            return null;
        }
    }

    private static IReadOnlyList<PeResourceEntry> ReadResources(
        PEImage image,
        List<PeFinding> findings)
    {
        if (image.Resources is null)
        {
            return [];
        }

        var result = new List<PeResourceEntry>();

        try
        {
            foreach (var entry in image.Resources.Entries)
            {
                if (entry is ResourceDirectory typeDirectory)
                {
                    var typeName = string.IsNullOrWhiteSpace(typeDirectory.Name)
                        ? PeResourceNames.TypeName(typeDirectory.Id)
                        : typeDirectory.Name;
                    WalkResourceDirectory(
                        typeDirectory,
                        typeName,
                        typeDirectory.Id,
                        name: null,
                        result);
                }
                else if (entry is ResourceData directData)
                {
                    AddResource(result, directData, "Unknown", 0, null);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            findings.Add(new PeFinding(
                PeFindingSeverity.Warning,
                "RESOURCE_DIRECTORY_UNREADABLE",
                "Resource directory could not be read",
                "The resource directory is malformed or uses an unsupported layout. " + ex.Message));
            return [];
        }

        return result
            .OrderBy(resource => resource.TypeName, StringComparer.Ordinal)
            .ThenBy(resource => resource.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(resource => resource.LanguageId)
            .ToArray();
    }

    private static void WalkResourceDirectory(
        ResourceDirectory directory,
        string typeName,
        uint typeId,
        string? name,
        List<PeResourceEntry> result)
    {
        foreach (var entry in directory.Entries)
        {
            if (entry is ResourceDirectory childDirectory)
            {
                var childName = name ?? ResourceDirectoryLabel(childDirectory);
                WalkResourceDirectory(childDirectory, typeName, typeId, childName, result);
            }
            else if (entry is ResourceData data)
            {
                AddResource(result, data, typeName, typeId, name);
            }
        }
    }

    private static void AddResource(
        List<PeResourceEntry> result,
        ResourceData data,
        string typeName,
        uint typeId,
        string? name)
    {
        uint size = 0;
        uint? rva = null;
        long? fileOffset = null;

        try
        {
            var reader = data.CreateReader();
            rva = reader.Rva;
            fileOffset = reader.Offset <= long.MaxValue ? (long)reader.Offset : null;
            size = data.Contents?.GetVirtualSize() ?? 0;
        }
        catch
        {
            // The tree structure is still meaningful even if the payload bytes
            // cannot be read. Leave the size/address unknown rather than
            // dropping the resource.
        }

        var languageId = data.Id;
        var language = string.IsNullOrWhiteSpace(data.Name)
            ? PeResourceNames.LanguageName(languageId)
            : data.Name;
        var resourceName = name
                           ?? (string.IsNullOrWhiteSpace(data.Name) ? "(default)" : data.Name);

        result.Add(new PeResourceEntry(
            typeName,
            typeId,
            resourceName,
            0,
            languageId,
            language,
            size,
            rva,
            fileOffset,
            data.CodePage));
    }

    private static string ResourceDirectoryLabel(ResourceDirectory directory) =>
        string.IsNullOrWhiteSpace(directory.Name) ? $"#{directory.Id}" : directory.Name;

    private static PeRelocationInfo ReadRelocations(
        PEImage image,
        PEFile pe,
        List<PeFinding> findings)
    {
        try
        {
            var entries = new List<PeRelocationEntry>(image.Relocations.Count);

            foreach (var relocation in image.Relocations)
            {
                var rva = TryGetReferenceRva(relocation.Location, pe) ?? 0;
                entries.Add(new PeRelocationEntry(
                    rva & 0xFFFFF000u,
                    rva,
                    relocation.Type.ToString()));
            }

            entries.Sort((left, right) =>
            {
                var page = left.PageRva.CompareTo(right.PageRva);
                return page != 0 ? page : left.Rva.CompareTo(right.Rva);
            });

            return new PeRelocationInfo(entries);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            findings.Add(new PeFinding(
                PeFindingSeverity.Warning,
                "RELOCATION_DIRECTORY_UNREADABLE",
                "Base relocation directory could not be read",
                "The relocation directory is malformed or uses an unsupported layout. " + ex.Message));
            return new PeRelocationInfo([]);
        }
    }

    private static PeTlsInfo? ReadTls(
        PEImage image,
        PEFile pe,
        ulong imageBase,
        AddressMap addressMap,
        List<PeFinding> findings)
    {
        try
        {
            var tls = image.TlsDirectory;
            if (tls is null)
            {
                return null;
            }

            var callbacks = new List<PeTlsCallback>();

            if (tls.CallbackFunctions is { } table)
            {
                foreach (var reference in table)
                {
                    var rva = reference is null ? null : TryGetReferenceRva(reference, pe);
                    if (rva is not { } callbackRva)
                    {
                        callbacks.Add(new PeTlsCallback(0, 0, null));
                        continue;
                    }

                    callbacks.Add(new PeTlsCallback(
                        imageBase + callbackRva,
                        callbackRva,
                        addressMap.TryRvaToFileOffset(callbackRva, out var offset) ? offset : null));
                }
            }

            return new PeTlsInfo(
                tls.Index is null ? null : TryGetReferenceRva(tls.Index, pe),
                tls.SizeOfZeroFill,
                tls.Characteristics.ToString(),
                tls.TemplateData?.GetVirtualSize() ?? 0,
                callbacks);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            findings.Add(new PeFinding(
                PeFindingSeverity.Warning,
                "TLS_DIRECTORY_UNREADABLE",
                "TLS directory could not be read",
                "The TLS directory is malformed or uses an unsupported layout. " + ex.Message));
            return null;
        }
    }

    private static PeExceptionInfo? ReadExceptions(
        PEImage image,
        PEFile pe,
        List<PeFinding> findings)
    {
        try
        {
            var directory = image.Exceptions;
            if (directory is null)
            {
                return null;
            }

            var functions = new List<PeRuntimeFunction>();

            foreach (var function in directory.GetFunctions())
            {
                var begin = TryGetReferenceRva(function.Begin, pe) ?? 0;
                var end = TryGetReferenceRva(function.End, pe) ?? 0;
                functions.Add(new PeRuntimeFunction(begin, end, function.UnwindInfo is not null));
            }

            return new PeExceptionInfo(image.MachineType == MachineType.Amd64, functions);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            findings.Add(new PeFinding(
                PeFindingSeverity.Warning,
                "EXCEPTION_DIRECTORY_UNREADABLE",
                "Exception directory could not be read",
                "The exception/runtime-function directory is malformed or uses an unsupported layout. " + ex.Message));
            return null;
        }
    }

    private static IReadOnlyList<PeDebugEntry> ReadDebugEntries(
        byte[] bytes,
        OptionalHeader optionalHeader,
        AddressMap addressMap,
        List<PeFinding> findings)
    {
        if (optionalHeader.DataDirectories.Count <= (int)DataDirectoryKind.Debug)
        {
            return [];
        }

        var directory = optionalHeader.DataDirectories[(int)DataDirectoryKind.Debug];
        if (directory.VirtualAddress == 0 || directory.Size == 0)
        {
            return [];
        }

        var result = new List<PeDebugEntry>();
        var entryCount = directory.Size / 28;

        try
        {
            for (var index = 0u; index < entryCount; index++)
            {
                var entryRva = (ulong)directory.VirtualAddress + index * 28;
                if (entryRva > uint.MaxValue
                    || !addressMap.TryRvaToFileOffset((uint)entryRva, out var entryOffset)
                    || entryOffset < 0
                    || entryOffset + 28 > bytes.Length)
                {
                    findings.Add(new PeFinding(
                        PeFindingSeverity.Information,
                        "DEBUG_DIRECTORY_TRUNCATED",
                        "Debug directory is truncated",
                        $"Only {index} of {entryCount} entries are inside the file."));
                    break;
                }

                var span = bytes.AsSpan((int)entryOffset, 28);
                var timeDateStamp = BinaryPrimitives.ReadUInt32LittleEndian(span[4..]);
                var typeValue = BinaryPrimitives.ReadUInt32LittleEndian(span[12..]);
                var sizeOfData = BinaryPrimitives.ReadUInt32LittleEndian(span[16..]);
                var addressOfRawData = BinaryPrimitives.ReadUInt32LittleEndian(span[20..]);
                var pointerToRawData = BinaryPrimitives.ReadUInt32LittleEndian(span[24..]);

                DateTimeOffset? timestamp = null;
                if (timeDateStamp > 0)
                {
                    try
                    {
                        timestamp = DateTimeOffset.FromUnixTimeSeconds(timeDateStamp);
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        timestamp = null;
                    }
                }

                var dataOffset = ResolveDebugDataOffset(
                    pointerToRawData,
                    addressOfRawData,
                    addressMap);

                string? signature = null;
                string? pdbPath = null;

                if (typeValue == 2
                    && dataOffset is { } dataStart
                    && dataStart >= 0
                    && dataStart < bytes.Length
                    && sizeOfData > 0)
                {
                    var available = (int)Math.Min(
                        (ulong)sizeOfData,
                        (ulong)(bytes.Length - dataStart));
                    if (available >= 4)
                    {
                        var dataSpan = bytes.AsSpan((int)dataStart, available);
                        signature = Encoding.ASCII.GetString(dataSpan[..4]);
                        var pathStart = signature switch
                        {
                            "RSDS" => 24,
                            "NB10" => 16,
                            _ => -1,
                        };

                        if (pathStart > 0 && dataSpan.Length > pathStart)
                        {
                            var tail = dataSpan[pathStart..];
                            var terminator = tail.IndexOf((byte)0);
                            if (terminator >= 0)
                            {
                                tail = tail[..terminator];
                            }

                            if (!tail.IsEmpty)
                            {
                                pdbPath = Encoding.ASCII.GetString(tail);
                            }
                        }
                    }
                }

                result.Add(new PeDebugEntry(
                    DebugTypeName(typeValue),
                    typeValue,
                    timestamp,
                    sizeOfData,
                    addressOfRawData,
                    dataOffset,
                    signature,
                    pdbPath));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            findings.Add(new PeFinding(
                PeFindingSeverity.Warning,
                "DEBUG_DIRECTORY_UNREADABLE",
                "Debug directory could not be read",
                "The debug directory is malformed or uses an unsupported layout. " + ex.Message));
            return [];
        }

        return result;
    }

    private static long? ResolveDebugDataOffset(
        uint pointerToRawData,
        uint addressOfRawData,
        AddressMap addressMap)
    {
        if (pointerToRawData != 0)
        {
            return pointerToRawData;
        }

        if (addressOfRawData != 0
            && addressMap.TryRvaToFileOffset(addressOfRawData, out var mappedOffset))
        {
            return mappedOffset;
        }

        return null;
    }

    private static string DebugTypeName(uint type) => type switch
    {
        1 => "COFF",
        2 => "CodeView",
        3 => "FPO",
        4 => "Misc",
        5 => "Exception",
        6 => "Fixup",
        7 => "OMAP to source",
        8 => "OMAP from source",
        9 => "Borland",
        10 => "Reserved",
        11 => "CLSID",
        12 => "VC feature",
        13 => "POGO",
        14 => "ILTCG",
        16 => "Embedded Portable PDB",
        17 => "SPGO",
        19 => "PDB checksum",
        20 => "Repro",
        _ => $"Type 0x{type:X}",
    };

    private static PeLoadConfigurationInfo? ReadLoadConfiguration(
        PEImage image,
        List<PeFinding> findings)
    {
        try
        {
            var configuration = image.LoadConfiguration;
            if (configuration is null)
            {
                return null;
            }

            return new PeLoadConfigurationInfo(
                configuration.Size,
                configuration.Is32Bit,
                $"0x{(uint)configuration.GuardFlags:X8}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            findings.Add(new PeFinding(
                PeFindingSeverity.Warning,
                "LOAD_CONFIG_UNREADABLE",
                "Load configuration could not be read",
                "The load configuration directory is malformed or uses an unsupported layout. " + ex.Message));
            return null;
        }
    }

    private static AsmResolver.PE.DotNet.DotNetDirectory? TryReadDotNetDirectory(
        PEImage image,
        List<PeFinding> findings)
    {
        try
        {
            return image.DotNetDirectory;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            findings.Add(new PeFinding(
                PeFindingSeverity.Warning,
                "CLR_DIRECTORY_UNREADABLE",
                "CLR runtime header could not be read",
                ex.Message));
            return null;
        }
    }

    private static IEnumerable<PeFinding> AnalyzeSections(
        IReadOnlyList<PeSection> sections,
        long fileLength)
    {
        if (sections.Count == 0)
        {
            yield return new PeFinding(
                PeFindingSeverity.Warning,
                "NO_SECTIONS",
                "No sections",
                "The section table is empty. The image cannot be mapped.");
            yield break;
        }

        if (!sections.Any(s => s.IsExecutable || s.ContainsCode))
        {
            yield return new PeFinding(
                PeFindingSeverity.Warning,
                "NO_CODE_SECTIONS",
                "No executable sections",
                "No section is marked executable, so there is no native code to analyze.");
        }

        foreach (var section in sections)
        {
            if (section.IsWritable && section.IsExecutable)
            {
                yield return new PeFinding(
                    PeFindingSeverity.Warning,
                    "WRITABLE_EXECUTABLE_SECTION",
                    $"Section {section.Name} is writable and executable",
                    "W+X sections are unusual in normal compiler output and are worth examining. This is not by itself evidence of malicious behaviour.",
                    section.VirtualAddress);
            }

            if (section.HasRawData
                && (ulong)section.RawOffset + section.RawSize > (ulong)fileLength)
            {
                yield return new PeFinding(
                    PeFindingSeverity.Warning,
                    "SECTION_DATA_OUT_OF_BOUNDS",
                    $"Section {section.Name} extends past end of file",
                    $"Raw data ends at offset 0x{(ulong)section.RawOffset + section.RawSize:X}, but the file is {fileLength:N0} bytes.",
                    section.VirtualAddress);
            }
        }

        for (var i = 0; i < sections.Count; i++)
        {
            for (var j = i + 1; j < sections.Count; j++)
            {
                var a = sections[i];
                var b = sections[j];
                if (a.MappedSize == 0 || b.MappedSize == 0)
                {
                    continue;
                }

                if (a.VirtualAddress < b.VirtualEnd && b.VirtualAddress < a.VirtualEnd)
                {
                    yield return new PeFinding(
                        PeFindingSeverity.Warning,
                        "OVERLAPPING_SECTIONS",
                        $"Sections {a.Name} and {b.Name} overlap",
                        $"Mapped ranges 0x{a.VirtualAddress:X8}–0x{a.VirtualEnd:X} and 0x{b.VirtualAddress:X8}–0x{b.VirtualEnd:X} intersect.",
                        Math.Min(a.VirtualAddress, b.VirtualAddress));
                    yield break;
                }
            }
        }
    }

    private static IEnumerable<PeFinding> AnalyzeSecurity(
        OptionalHeader optionalHeader,
        PeFormat format,
        IReadOnlyList<PeSection> sections,
        IReadOnlyList<ImportedModule> imports,
        IReadOnlyList<DataDirectoryInfo> directories)
    {
        if ((optionalHeader.DllCharacteristics & DllCharacteristics.DynamicBase) == 0)
        {
            yield return new PeFinding(
                PeFindingSeverity.Warning,
                "ASLR_DISABLED",
                "ASLR is not requested",
                "IMAGE_DLLCHARACTERISTICS_DYNAMIC_BASE is not set, so the image asks to load at a fixed base address.");
        }

        if ((optionalHeader.DllCharacteristics & DllCharacteristics.NxCompat) == 0)
        {
            yield return new PeFinding(
                PeFindingSeverity.Information,
                "DEP_NOT_REQUESTED",
                "DEP/NX compatibility is not requested",
                "IMAGE_DLLCHARACTERISTICS_NX_COMPAT is not set.");
        }

        if (optionalHeader.CheckSum == 0)
        {
            yield return new PeFinding(
                PeFindingSeverity.Information,
                "CHECKSUM_ABSENT",
                "PE checksum is zero",
                "The optional header checksum is not populated. This is common for development builds.");
        }

        if (imports.Count == 0 && directories[(int)DataDirectoryKind.Import].IsPresent)
        {
            yield return new PeFinding(
                PeFindingSeverity.Information,
                "IMPORTS_EMPTY",
                "Import directory is present but empty",
                "The import table was declared but no imported modules were read.");
        }

        if (directories[(int)DataDirectoryKind.Tls].IsPresent)
        {
            yield return new PeFinding(
                PeFindingSeverity.Information,
                "TLS_PRESENT",
                "TLS directory is present",
                "Thread-local-storage callbacks run before the entry point and are worth reviewing.");
        }

        if (directories[(int)DataDirectoryKind.Certificate].IsPresent)
        {
            yield return new PeFinding(
                PeFindingSeverity.Information,
                "CERTIFICATE_TABLE_PRESENT",
                "Certificate table is present",
                "An Authenticode certificate directory is declared. This is not a validation of the signature.");
        }

        if (format == PeFormat.Pe32 && optionalHeader.ImageBase > uint.MaxValue)
        {
            yield return new PeFinding(
                PeFindingSeverity.Warning,
                "PE32_IMAGE_BASE_OUT_OF_RANGE",
                "PE32 image base exceeds 4 GB",
                "A 32-bit image cannot load at the declared image base.");
        }

        var lastEnd = sections
            .Select(s => s.VirtualEnd)
            .DefaultIfEmpty(optionalHeader.SizeOfHeaders)
            .Max();
        if (optionalHeader.SizeOfImage < lastEnd)
        {
            yield return new PeFinding(
                PeFindingSeverity.Warning,
                "SIZE_OF_IMAGE_TOO_SMALL",
                "SizeOfImage does not cover the section table",
                $"SizeOfImage is 0x{optionalHeader.SizeOfImage:X}, but mapped sections end at 0x{lastEnd:X}.");
        }

        var unusualNames = sections
            .Where(s => s.Name.Length is 0 or > 8
                        || s.Name.Any(c => c is < ' ' or > '~')
                        || (!s.Name.StartsWith('.') && s.Name is not "CODE" and not "DATA" and not "BSS"))
            .Select(s => s.Name)
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (unusualNames.Length > 0)
        {
            yield return new PeFinding(
                PeFindingSeverity.Information,
                "UNUSUAL_SECTION_NAMES",
                "Unconventional section names",
                "Unrecognized section names can result from custom linkers or packers: " + string.Join(", ", unusualNames));
        }
    }

    private static DateTimeOffset ToUtc(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

        return new DateTimeOffset(utc);
    }
}
