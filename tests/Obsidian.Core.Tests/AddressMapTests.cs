using Obsidian.Core.Formatting;
using Obsidian.Core.Model;
using Xunit;

namespace Obsidian.Core.Tests;

public sealed class AddressMapTests
{
    private static readonly PeSection Text = new()
    {
        Name = ".text",
        VirtualAddress = 0x1000,
        VirtualSize = 0x2000,
        RawOffset = 0x400,
        RawSize = 0x2000,
        Characteristics = 0x60000020,
        IsReadable = true,
        IsWritable = false,
        IsExecutable = true,
        ContainsCode = true,
        ContainsInitializedData = false,
        IsDiscardable = false,
    };

    private static readonly PeSection Data = new()
    {
        Name = ".data",
        VirtualAddress = 0x3000,
        VirtualSize = 0x1000,
        RawOffset = 0x2400,
        RawSize = 0x0E00,
        Characteristics = 0xC0000040,
        IsReadable = true,
        IsWritable = true,
        IsExecutable = false,
        ContainsCode = false,
        ContainsInitializedData = true,
        IsDiscardable = false,
    };

    private static readonly PeSection Bss = new()
    {
        Name = ".bss",
        VirtualAddress = 0x4000,
        VirtualSize = 0x2000,
        RawOffset = 0,
        RawSize = 0,
        Characteristics = 0xC0000080,
        IsReadable = true,
        IsWritable = true,
        IsExecutable = false,
        ContainsCode = false,
        ContainsInitializedData = false,
        IsDiscardable = false,
    };

    private static AddressMap CreateMap() => new(
        [Text, Data, Bss],
        sizeOfHeaders: 0x400,
        imageBase: 0x140000000,
        sizeOfImage: 0x6000,
        fileLength: 0x3200);

    [Theory]
    [InlineData(0x0000, 0x0000L)]
    [InlineData(0x0100, 0x0100L)]
    [InlineData(0x03FF, 0x03FFL)]
    [InlineData(0x1000, 0x0400L)]
    [InlineData(0x1FFF, 0x13FFL)]
    [InlineData(0x3000, 0x2400L)]
    [InlineData(0x3DFF, 0x31FFL)]
    public void RvaToFileOffsetMapsHeadersAndRawData(uint rva, long expectedOffset)
    {
        var map = CreateMap();

        Assert.True(map.TryRvaToFileOffset(rva, out var offset));
        Assert.Equal(expectedOffset, offset);
    }

    [Theory]
    [InlineData(0x3E00)] // inside .data virtual span but past its raw data
    [InlineData(0x4000)] // .bss has no file backing
    [InlineData(0x5FFF)] // still inside .bss virtual span
    [InlineData(0x9000)] // outside every section
    public void RvaToFileOffsetRejectsUnbackedAddresses(uint rva)
    {
        var map = CreateMap();

        Assert.False(map.TryRvaToFileOffset(rva, out var offset));
        Assert.Equal(0, offset);
    }

    [Theory]
    [InlineData(0x0400L, 0x1000u)]
    [InlineData(0x13FFL, 0x1FFFu)]
    [InlineData(0x2400L, 0x3000u)]
    [InlineData(0x0100L, 0x0100u)]
    public void FileOffsetToRvaRoundTrips(long fileOffset, uint expectedRva)
    {
        var map = CreateMap();

        Assert.True(map.TryFileOffsetToRva(fileOffset, out var rva));
        Assert.Equal(expectedRva, rva);
    }

    [Fact]
    public void FileOffsetOutsideImageIsRejected()
    {
        var map = CreateMap();

        Assert.False(map.TryFileOffsetToRva(-1, out _));
        Assert.False(map.TryFileOffsetToRva(0x3200, out _));
    }

    [Fact]
    public void VirtualAddressTranslationUsesImageBase()
    {
        var map = CreateMap();

        Assert.True(map.TryVaToRva(0x140001234, out var rva));
        Assert.Equal(0x1234u, rva);

        Assert.True(map.TryRvaToVa(0x1234, out var va));
        Assert.Equal(0x140001234ul, va);

        Assert.False(map.TryVaToRva(0x140009000, out _));
        Assert.False(map.TryVaToRva(0x1000, out _));
    }

    [Fact]
    public void ResolveDistinguishesAllThreeAddressSpaces()
    {
        var map = CreateMap();

        Assert.True(map.TryResolve(AddressSpace.Rva, 0x1234, out var byRva));
        Assert.Equal(0x140001234ul, byRva.VirtualAddress);
        Assert.Equal(0x634L, byRva.FileOffset);
        Assert.Equal(".text", byRva.Section?.Name);

        Assert.True(map.TryResolve(AddressSpace.VirtualAddress, 0x140001234, out var byVa));
        Assert.Equal(0x1234u, byVa.Rva);

        Assert.True(map.TryResolve(AddressSpace.FileOffset, 0x634, out var byOffset));
        Assert.Equal(0x1234u, byOffset.Rva);

        Assert.False(map.TryResolve(AddressSpace.Rva, 0x9000, out _));
    }

    [Fact]
    public void SectionBoundariesAreHalfOpen()
    {
        Assert.True(Text.ContainsRva(0x1000));
        Assert.True(Text.ContainsRva(0x2FFF));
        Assert.False(Text.ContainsRva(0x3000));
        Assert.False(Text.ContainsRva(0x0FFF));

        Assert.True(Text.ContainsFileOffset(0x0400));
        Assert.True(Text.ContainsFileOffset(0x23FF));
        Assert.False(Text.ContainsFileOffset(0x2400));
        Assert.False(Text.ContainsFileOffset(0x03FF));
    }

    [Fact]
    public void HeaderRegionIsNotASection()
    {
        var map = CreateMap();

        Assert.Null(map.GetSectionForRva(0x0100));
        Assert.True(map.TryRvaToFileOffset(0x0100, out var offset));
        Assert.Equal(0x0100, offset);
    }
}

public sealed class HexTests
{
    [Fact]
    public void AddressesUseStableWidths()
    {
        Assert.Equal("0x00001234", Hex.Rva(0x1234));
        Assert.Equal("0x0000000140001234", Hex.Va(0x140001234));
        Assert.Equal("0x00001234", Hex.Address(0x1234));
        Assert.Equal("0x0000000140001234", Hex.Address(0x140001234));
        Assert.Equal("0x000002A4", Hex.Offset(0x2A4));
    }

    [Fact]
    public void BytesAreUppercaseAndSpaceSeparated()
    {
        Assert.Equal("4D 5A 90 00", Hex.Bytes([0x4D, 0x5A, 0x90, 0x00]));
        Assert.Equal(string.Empty, Hex.Bytes([]));
    }

    [Fact]
    public void SizesUseBinaryUnits()
    {
        Assert.Equal("512 B", Hex.Size(512));
        Assert.Equal("1.5 KB", Hex.Size(1536));
        Assert.Equal("1 MB", Hex.Size(1024 * 1024));
    }
}
