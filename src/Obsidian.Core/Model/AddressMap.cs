namespace Obsidian.Core.Model;

/// <summary>
/// Converts between file offsets, relative virtual addresses and virtual
/// addresses using the image's section table and PE header mapping rules.
/// </summary>
public sealed class AddressMap
{
    private readonly IReadOnlyList<PeSection> _sections;
    private readonly uint _sizeOfHeaders;
    private readonly ulong _imageBase;
    private readonly uint _sizeOfImage;
    private readonly long _fileLength;

    public AddressMap(
        IReadOnlyList<PeSection> sections,
        uint sizeOfHeaders,
        ulong imageBase,
        uint sizeOfImage,
        long fileLength)
    {
        ArgumentNullException.ThrowIfNull(sections);
        if (fileLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fileLength));
        }

        _sections = sections;
        _sizeOfHeaders = sizeOfHeaders;
        _imageBase = imageBase;
        _sizeOfImage = sizeOfImage;
        _fileLength = fileLength;
    }

    public IReadOnlyList<PeSection> Sections => _sections;

    public uint SizeOfHeaders => _sizeOfHeaders;

    public ulong ImageBase => _imageBase;

    public uint SizeOfImage => _sizeOfImage;

    /// <summary>Returns the section whose mapped span contains the RVA, if any.</summary>
    public PeSection? GetSectionForRva(uint rva)
    {
        foreach (var section in _sections)
        {
            if (section.ContainsRva(rva))
            {
                return section;
            }
        }

        return null;
    }

    /// <summary>Returns the section whose file-backed data contains the offset, if any.</summary>
    public PeSection? GetSectionForFileOffset(long fileOffset)
    {
        foreach (var section in _sections)
        {
            if (section.ContainsFileOffset(fileOffset))
            {
                return section;
            }
        }

        return null;
    }

    /// <summary>
    /// Translates an RVA to a file offset. Header RVAs map one-to-one; section
    /// RVAs are only translatable while they fall inside the raw data.
    /// </summary>
    public bool TryRvaToFileOffset(uint rva, out long fileOffset)
    {
        if (rva < _sizeOfHeaders && rva < _fileLength)
        {
            fileOffset = rva;
            return true;
        }

        var section = GetSectionForRva(rva);
        if (section is not null
            && section.TryRvaToFileOffset(rva, out fileOffset)
            && fileOffset < _fileLength)
        {
            return true;
        }

        fileOffset = 0;
        return false;
    }

    /// <summary>Translates a file offset back to an RVA.</summary>
    public bool TryFileOffsetToRva(long fileOffset, out uint rva)
    {
        if (fileOffset >= 0 && fileOffset < _sizeOfHeaders && fileOffset < _fileLength)
        {
            rva = (uint)fileOffset;
            return true;
        }

        var section = GetSectionForFileOffset(fileOffset);
        if (section is not null && section.TryFileOffsetToRva(fileOffset, out rva))
        {
            return true;
        }

        rva = 0;
        return false;
    }

    public bool TryRvaToVa(uint rva, out ulong va)
    {
        if (rva >= _sizeOfImage && GetSectionForRva(rva) is null)
        {
            va = 0;
            return false;
        }

        va = _imageBase + rva;
        return true;
    }

    public bool TryVaToRva(ulong va, out uint rva)
    {
        if (va < _imageBase)
        {
            rva = 0;
            return false;
        }

        var delta = va - _imageBase;
        if (delta > uint.MaxValue || (delta >= _sizeOfImage && GetSectionForRva((uint)delta) is null))
        {
            rva = 0;
            return false;
        }

        rva = (uint)delta;
        return true;
    }

    public bool TryVaToFileOffset(ulong va, out long fileOffset)
    {
        if (TryVaToRva(va, out var rva) && TryRvaToFileOffset(rva, out fileOffset))
        {
            return true;
        }

        fileOffset = 0;
        return false;
    }

    public bool TryFileOffsetToVa(long fileOffset, out ulong va)
    {
        if (TryFileOffsetToRva(fileOffset, out var rva))
        {
            va = _imageBase + rva;
            return true;
        }

        va = 0;
        return false;
    }

    /// <summary>
    /// Resolves an address given in any of the three coordinate spaces. The
    /// caller states which space the value belongs to; no guessing is done.
    /// </summary>
    public bool TryResolve(
        AddressSpace space,
        ulong value,
        out AddressResolution resolution)
    {
        resolution = default;
        switch (space)
        {
            case AddressSpace.Rva when value <= uint.MaxValue:
                if (!TryRvaToFileOffset((uint)value, out var rvaOffset))
                {
                    return false;
                }

                resolution = new AddressResolution((uint)value, _imageBase + value, rvaOffset, GetSectionForRva((uint)value));
                return true;

            case AddressSpace.VirtualAddress:
                if (!TryVaToRva(value, out var vaRva) || !TryRvaToFileOffset(vaRva, out var vaOffset))
                {
                    return false;
                }

                resolution = new AddressResolution(vaRva, value, vaOffset, GetSectionForRva(vaRva));
                return true;

            case AddressSpace.FileOffset when value <= long.MaxValue:
                if (!TryFileOffsetToRva((long)value, out var offsetRva))
                {
                    return false;
                }

                resolution = new AddressResolution(offsetRva, _imageBase + offsetRva, (long)value, GetSectionForRva(offsetRva));
                return true;

            default:
                return false;
        }
    }
}

public enum AddressSpace
{
    Rva = 0,
    VirtualAddress,
    FileOffset,
}

public readonly record struct AddressResolution(
    uint Rva,
    ulong VirtualAddress,
    long FileOffset,
    PeSection? Section);
