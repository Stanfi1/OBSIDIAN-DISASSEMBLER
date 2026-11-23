using Obsidian.Core.Model;
using Obsidian.Pe;
using Xunit;

namespace Obsidian.Pe.Tests;

public sealed class PeImageLoaderTests
{
    private static string TestAssemblyPath => typeof(PeImageLoaderTests).Assembly.Location;

    [Fact]
    public async Task WithBytesKeepsMetadataAndReplacesOnlyTheBytes()
    {
        var loader = new PeImageLoader();
        var image = await loader.LoadAsync(TestAssemblyPath);
        var patchedBytes = image.Bytes.ToArray();
        patchedBytes[0] ^= 0xFF;

        var patched = image.WithBytes(patchedBytes, "patched-hash");

        Assert.Equal("patched-hash", patched.Sha256);
        Assert.Equal(image.FilePath, patched.FilePath);
        Assert.Equal(image.Sections.Count, patched.Sections.Count);
        Assert.Same(image.Sections, patched.Sections);
        Assert.NotEqual(image.Bytes.Span[0], patched.Bytes.Span[0]);
        Assert.Throws<ArgumentException>(() => image.WithBytes(patchedBytes.AsSpan(0, 1).ToArray(), "bad"));
    }

    [Fact]
    public async Task LoadsCurrentTestAssembly()
    {
        var loader = new PeImageLoader();

        var image = await loader.LoadAsync(TestAssemblyPath);

        Assert.Equal(Path.GetFileName(TestAssemblyPath), image.FileName);
        Assert.Equal(TestAssemblyPath, image.FilePath);
        Assert.True(image.FileSize > 0);
        Assert.Equal(64, image.Sha256.Length);
        Assert.NotEqual(PeFormat.Unknown, image.Format);
        Assert.NotEqual(BinaryArchitecture.Unknown, image.Architecture);
        Assert.True(image.IsManaged);
        Assert.NotNull(image.ClrRuntimeVersion);
        Assert.NotEmpty(image.Sections);
        Assert.Contains(image.Sections, s => s.Name == ".text");
        Assert.NotEmpty(image.DataDirectories);
        Assert.Equal(16, image.DataDirectories.Count);
        Assert.NotNull(image.AddressMap);
        Assert.NotEmpty(image.Findings);
    }

    [Fact]
    public async Task ImportTableIsParsedForManagedPe()
    {
        var loader = new PeImageLoader();

        var image = await loader.LoadAsync(TestAssemblyPath);

        Assert.True(image.Imports.Count > 0, "The CLR bootstrapping import should be present.");
        Assert.Contains(image.Imports, m => m.Name.Contains("mscoree", StringComparison.OrdinalIgnoreCase));
        Assert.All(image.Imports, m => Assert.NotEmpty(m.Symbols));
    }

    [Fact]
    public async Task EverySectionRoundTripsThroughAddressMap()
    {
        var loader = new PeImageLoader();
        var image = await loader.LoadAsync(TestAssemblyPath);

        foreach (var section in image.Sections.Where(s => s.HasRawData))
        {
            Assert.True(image.AddressMap.TryRvaToFileOffset(section.VirtualAddress, out var offset));
            Assert.True(offset >= section.RawOffset);
            Assert.True(image.AddressMap.TryFileOffsetToRva(offset, out var rva));
            Assert.Equal(section.VirtualAddress, rva);
        }
    }

    [Fact]
    public async Task ReportsProgressThroughCompletion()
    {
        var loader = new PeImageLoader();
        var reports = new List<LoadProgress>();

        var image = await loader.LoadAsync(
            TestAssemblyPath,
            new SynchronousProgress<LoadProgress>(reports.Add));

        Assert.NotNull(image);
        Assert.NotEmpty(reports);
        Assert.Equal(LoadStage.ReadingFile, reports[0].Stage);
        Assert.Equal(LoadStage.Completed, reports[^1].Stage);
        Assert.Equal(100, reports[^1].Percent);
    }

    [Fact]
    public async Task LoadsManagedDirectoryMetadata()
    {
        var loader = new PeImageLoader();

        var image = await loader.LoadAsync(TestAssemblyPath);

        Assert.NotEmpty(image.Resources);
        Assert.NotEmpty(image.DebugEntries);
        Assert.NotNull(image.Relocations);
        Assert.All(image.Resources, resource => Assert.False(string.IsNullOrWhiteSpace(resource.TypeName)));
        Assert.Contains(image.DebugEntries, entry => entry.TypeName == "CodeView");
    }

    [Fact]
    public async Task ParsesResourcesRelocationsExceptionsAndDebugFromNotepad()
    {
        var path = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        if (!File.Exists(path))
        {
            return;
        }

        var loader = new PeImageLoader();
        var image = await loader.LoadAsync(path);

        Assert.NotEmpty(image.Resources);
        Assert.Contains(image.Resources, resource => resource.Size > 0);
        Assert.True(image.Relocations.Count > 0);
        Assert.NotEmpty(image.Exceptions?.Functions ?? []);
        Assert.NotEmpty(image.DebugEntries);
        Assert.Contains(
            image.DebugEntries,
            entry => entry.TypeName == "CodeView"
                     && entry.PdbPath?.Contains("notepad.pdb", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task ParsesTlsDirectoryWhenPresent()
    {
        var path = Path.Combine(Environment.SystemDirectory, "wininet.dll");
        if (!File.Exists(path))
        {
            return;
        }

        var loader = new PeImageLoader();
        var image = await loader.LoadAsync(path);

        Assert.NotNull(image.Tls);
        Assert.NotNull(image.Tls!.IndexRva);
        Assert.True(image.Tls.TemplateSize > 0);
        Assert.All(image.Tls.Callbacks, callback => Assert.NotEqual(0UL, callback.VirtualAddress));
    }

    [Fact]
    public async Task RejectsNonPeFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"obsidian-not-pe-{Guid.NewGuid():N}.bin");
        await File.WriteAllTextAsync(path, "This is not a portable executable.");

        try
        {
            var loader = new PeImageLoader();

            var ex = await Assert.ThrowsAsync<BinaryLoadException>(() => loader.LoadAsync(path));
            Assert.Equal(BinaryLoadFailure.NotAPortableExecutable, ex.Failure);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task RejectsMissingFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"obsidian-missing-{Guid.NewGuid():N}.exe");
        var loader = new PeImageLoader();

        var ex = await Assert.ThrowsAsync<BinaryLoadException>(() => loader.LoadAsync(path));
        Assert.Equal(BinaryLoadFailure.FileNotFound, ex.Failure);
    }

    [Fact]
    public async Task RejectsEmptyFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"obsidian-empty-{Guid.NewGuid():N}.exe");
        await File.WriteAllBytesAsync(path, []);

        try
        {
            var loader = new PeImageLoader();
            var ex = await Assert.ThrowsAsync<BinaryLoadException>(() => loader.LoadAsync(path));
            Assert.Equal(BinaryLoadFailure.EmptyFile, ex.Failure);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task HonorsCancellation()
    {
        var loader = new PeImageLoader();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => loader.LoadAsync(TestAssemblyPath, cancellationToken: cts.Token));
    }

    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
