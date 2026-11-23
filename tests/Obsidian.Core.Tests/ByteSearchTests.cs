using Obsidian.Core.Model;
using Obsidian.Core.Searching;
using Xunit;

namespace Obsidian.Core.Tests;

public sealed class ByteSearchTests
{
    private static readonly byte[] Haystack = [0x01, 0x02, 0x03, 0x04, 0x01, 0x02, 0x03];

    [Fact]
    public void ForwardSearchFindsNextOccurrence()
    {
        Assert.Equal(4, ByteSearch.FindForward(Haystack, [0x01, 0x02], 0));
        Assert.Equal(4, ByteSearch.FindForward(Haystack, [0x01, 0x02], 3));
    }

    [Fact]
    public void ForwardSearchWrapsAround()
    {
        Assert.Equal(0, ByteSearch.FindForward(Haystack, [0x01, 0x02], 5));
        Assert.Equal(0, ByteSearch.FindForward([0x01, 0x02, 0x03], [0x01, 0x02], 0));
    }

    [Fact]
    public void BackwardSearchFindsPreviousOccurrence()
    {
        Assert.Equal(4, ByteSearch.FindBackward(Haystack, [0x01, 0x02], 5));
        Assert.Equal(0, ByteSearch.FindBackward(Haystack, [0x01, 0x02], 4));
    }

    [Fact]
    public void BackwardSearchWrapsAround()
    {
        Assert.Equal(4, ByteSearch.FindBackward(Haystack, [0x01, 0x02], 0));
    }

    [Fact]
    public void MissingOrEmptyPatternsReturnMinusOne()
    {
        Assert.Equal(-1, ByteSearch.FindForward(Haystack, [0xFF], 0));
        Assert.Equal(-1, ByteSearch.FindForward(Haystack, [], 0));
        Assert.Equal(-1, ByteSearch.FindBackward(Haystack, [0xFF], 0));
        Assert.Equal(-1, ByteSearch.FindBackward(Haystack, [], 0));
        Assert.Equal(-1, ByteSearch.FindForward([], [0x00], 0));
    }

    [Fact]
    public void PatternLongerThanBufferIsRejected()
    {
        Assert.Equal(
            -1,
            ByteSearch.FindForward(Haystack, [0x01, 0x02, 0x03, 0x04, 0x01, 0x02, 0x03, 0x04], 0));
    }
}

public sealed class PeResourceNamesTests
{
    [Fact]
    public void KnownTypesAndLanguagesAreNamed()
    {
        Assert.Equal("Icon", PeResourceNames.TypeName(3));
        Assert.Equal("Manifest", PeResourceNames.TypeName(24));
        Assert.Contains("en-US", PeResourceNames.LanguageName(1033));
        Assert.Contains("sk-SK", PeResourceNames.LanguageName(1051));
    }

    [Fact]
    public void UnknownIdsStayExplicit()
    {
        Assert.Equal("Type 0x1234", PeResourceNames.TypeName(0x1234));
        Assert.Equal("Language 0x1234", PeResourceNames.LanguageName(0x1234));
    }
}
