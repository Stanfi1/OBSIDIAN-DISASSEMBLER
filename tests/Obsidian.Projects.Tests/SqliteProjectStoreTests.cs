using Microsoft.Data.Sqlite;
using Obsidian.Core.Model;
using Obsidian.Projects;
using Xunit;

namespace Obsidian.Projects.Tests;

public sealed class SqliteProjectStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "obsidian-project-tests-" + Guid.NewGuid().ToString("N"));

    public SqliteProjectStoreTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // SQLite may briefly keep a WAL handle on Windows; the temp folder
            // is still safe to leave for the OS cleanup.
        }
    }

    private string ProjectPath => Path.Combine(_directory, "sample.obsproj");

    private static ProjectMetadata Metadata() => new(
        "Sample project",
        @"C:\Windows\System32\notepad.exe",
        "abc123",
        new DateTimeOffset(2026, 11, 23, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 11, 23, 0, 5, 0, TimeSpan.Zero));

    [Fact]
    public async Task CreatesAndOpensEmptyProject()
    {
        var store = new SqliteProjectStore();
        await store.CreateAsync(ProjectPath, Metadata());

        var document = await store.OpenAsync(ProjectPath);

        Assert.Equal("Sample project", document.Metadata.Name);
        Assert.Equal("abc123", document.Metadata.Sha256);
        Assert.Empty(document.Annotations);
        Assert.Empty(document.Bookmarks);
    }

    [Fact]
    public async Task RoundTripsAnnotationsAndBookmarks()
    {
        var store = new SqliteProjectStore();
        var metadata = Metadata();
        var document = new ProjectDocument
        {
            Metadata = metadata with { ModifiedUtc = new DateTimeOffset(2026, 11, 23, 1, 0, 0, TimeSpan.Zero) },
            Annotations =
            [
                new AddressAnnotation(0x1008, "entry_helper", "Called during startup", DateTimeOffset.UtcNow),
                new AddressAnnotation(0x2000, null, "Needs review", DateTimeOffset.UtcNow),
            ],
            Bookmarks =
            [
                new ProjectBookmark(0, 0x1010, "Interesting call", "Observe the return value", DateTimeOffset.UtcNow),
            ],
            Patches =
            [
                new BinaryPatch(0, 0x1010, 0x410, [0x90], [0xCC], DateTimeOffset.UtcNow),
            ],
        };

        await store.CreateAsync(ProjectPath, metadata);
        await store.SaveAsync(ProjectPath, document);
        var loaded = await store.OpenAsync(ProjectPath);

        Assert.Equal(document.Metadata.Name, loaded.Metadata.Name);
        Assert.Equal(2, loaded.Annotations.Count);
        Assert.Equal("entry_helper", loaded.AnnotationFor(0x1008)?.UserName);
        Assert.Equal("Needs review", loaded.AnnotationFor(0x2000)?.Comment);
        Assert.Single(loaded.Bookmarks);
        Assert.Equal("Interesting call", loaded.Bookmarks[0].Title);
        Assert.Single(loaded.Patches);
        Assert.Equal(0x410, loaded.Patches[0].FileOffset);
        Assert.Equal(new byte[] { 0x90 }, loaded.Patches[0].OriginalBytes);
        Assert.Equal(new byte[] { 0xCC }, loaded.Patches[0].PatchedBytes);
    }

    [Fact]
    public async Task MigratesVersionOneSchemaAndReadsEmptyPatches()
    {
        await using (var connection = new SqliteConnection($"Data Source={ProjectPath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE SchemaInfo(Version INTEGER NOT NULL);
                CREATE TABLE Project(Id INTEGER PRIMARY KEY CHECK (Id = 1), Name TEXT NOT NULL, BinaryPath TEXT NOT NULL, Sha256 TEXT NOT NULL, CreatedUtc TEXT NOT NULL, ModifiedUtc TEXT NOT NULL);
                CREATE TABLE Annotation(Rva INTEGER PRIMARY KEY, UserName TEXT NULL, Comment TEXT NULL, UpdatedUtc TEXT NOT NULL);
                CREATE TABLE Bookmark(Id INTEGER PRIMARY KEY AUTOINCREMENT, Rva INTEGER NOT NULL, Title TEXT NOT NULL, Comment TEXT NULL, CreatedUtc TEXT NOT NULL);
                INSERT INTO SchemaInfo(Version) VALUES(1);
                INSERT INTO Project VALUES(1, 'legacy', 'C:\Windows\System32\notepad.exe', 'abc123', '2026-01-01T00:00:00.0000000+00:00', '2026-01-01T00:00:00.0000000+00:00');
                """;
            await command.ExecuteNonQueryAsync();
        }

        var store = new SqliteProjectStore();
        var document = await store.OpenAsync(ProjectPath);

        Assert.Equal("legacy", document.Metadata.Name);
        Assert.Empty(document.Patches);

        await using var verify = new SqliteConnection($"Data Source={ProjectPath};Pooling=False");
        await verify.OpenAsync();
        await using var versionCommand = verify.CreateCommand();
        versionCommand.CommandText = "SELECT MAX(Version) FROM SchemaInfo;";
        Assert.Equal(2L, Convert.ToInt64(await versionCommand.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task MissingProjectThrowsFriendlyError()
    {
        var store = new SqliteProjectStore();

        var exception = await Assert.ThrowsAsync<ProjectStoreException>(
            () => store.OpenAsync(Path.Combine(_directory, "missing.obsproj")));

        Assert.Contains("does not exist", exception.Message);
    }

    [Fact]
    public async Task CorruptProjectIsRejected()
    {
        await File.WriteAllTextAsync(ProjectPath, "this is not sqlite");
        var store = new SqliteProjectStore();

        await Assert.ThrowsAsync<ProjectStoreException>(() => store.OpenAsync(ProjectPath));
    }

    [Fact]
    public async Task DeleteThenSaveRemovesOldRows()
    {
        var store = new SqliteProjectStore();
        await store.CreateAsync(ProjectPath, Metadata());

        var withData = new ProjectDocument
        {
            Metadata = Metadata(),
            Annotations = [new AddressAnnotation(0x10, "old", "old comment", DateTimeOffset.UtcNow)],
            Bookmarks = [new ProjectBookmark(0, 0x10, "old", null, DateTimeOffset.UtcNow)],
            Patches = [new BinaryPatch(0, 0x10, 0x10, [0x90], [0xCC], DateTimeOffset.UtcNow)],
        };
        await store.SaveAsync(ProjectPath, withData);

        var empty = new ProjectDocument
        {
            Metadata = Metadata(),
            Annotations = [],
            Bookmarks = [],
            Patches = [],
        };
        await store.SaveAsync(ProjectPath, empty);
        var loaded = await store.OpenAsync(ProjectPath);

        Assert.Empty(loaded.Annotations);
        Assert.Empty(loaded.Bookmarks);
        Assert.Empty(loaded.Patches);
    }
}
