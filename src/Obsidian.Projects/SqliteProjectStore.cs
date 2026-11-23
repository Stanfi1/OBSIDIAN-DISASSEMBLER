using System.Globalization;
using Microsoft.Data.Sqlite;
using Obsidian.Core.Model;

namespace Obsidian.Projects;

/// <summary>
/// SQLite-backed project store. Project metadata, address annotations and
/// bookmarks are written in one transaction; opening never mutates the file.
/// </summary>
public sealed class SqliteProjectStore : IProjectStore
{
    private const int SchemaVersion = 2;

    public async Task CreateAsync(
        string path,
        ProjectMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(metadata);

        try
        {
            EnsureDirectory(path);
            await using var connection = await OpenConnectionAsync(path, SqliteOpenMode.ReadWriteCreate, cancellationToken);
            await EnsureSchemaAsync(connection, cancellationToken);
            await WriteDocumentAsync(connection, new ProjectDocument
            {
                Metadata = metadata,
                Annotations = [],
                Bookmarks = [],
                Patches = [],
            }, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new ProjectStoreException($"Could not create project '{path}'.", exception);
        }
    }

    public async Task<ProjectDocument> OpenAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new ProjectStoreException($"Project file '{path}' does not exist.");
        }

        try
        {
            await using var connection = await OpenConnectionAsync(path, SqliteOpenMode.ReadWrite, cancellationToken);
            await EnsureSchemaAsync(connection, cancellationToken);
            return await ReadDocumentAsync(connection, cancellationToken);
        }
        catch (ProjectStoreException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new ProjectStoreException($"Project '{path}' is not a valid Obsidian project.", exception);
        }
    }

    public async Task SaveAsync(
        string path,
        ProjectDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(document);

        try
        {
            EnsureDirectory(path);
            await using var connection = await OpenConnectionAsync(path, SqliteOpenMode.ReadWriteCreate, cancellationToken);
            await EnsureSchemaAsync(connection, cancellationToken);
            await WriteDocumentAsync(connection, document, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new ProjectStoreException($"Could not save project '{path}'.", exception);
        }
    }

    private static async Task<SqliteConnection> OpenConnectionAsync(
        string path,
        SqliteOpenMode mode,
        CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(path),
            Mode = mode,
            Cache = SqliteCacheMode.Shared,
            Pooling = false,
        };

        var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS SchemaInfo (
                Version INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS Project (
                Id INTEGER PRIMARY KEY CHECK (Id = 1),
                Name TEXT NOT NULL,
                BinaryPath TEXT NOT NULL,
                Sha256 TEXT NOT NULL,
                CreatedUtc TEXT NOT NULL,
                ModifiedUtc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS Annotation (
                Rva INTEGER PRIMARY KEY,
                UserName TEXT NULL,
                Comment TEXT NULL,
                UpdatedUtc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS Bookmark (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Rva INTEGER NOT NULL,
                Title TEXT NOT NULL,
                Comment TEXT NULL,
                CreatedUtc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS Patch (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Rva INTEGER NOT NULL,
                FileOffset INTEGER NOT NULL,
                OriginalBytes BLOB NOT NULL,
                PatchedBytes BLOB NOT NULL,
                CreatedUtc TEXT NOT NULL
            );
            INSERT INTO SchemaInfo(Version)
            SELECT 2
            WHERE NOT EXISTS (SELECT 1 FROM SchemaInfo);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);

        await using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "SELECT MAX(Version) FROM SchemaInfo;";
        var version = Convert.ToInt32(await versionCommand.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        if (version > SchemaVersion)
        {
            throw new ProjectStoreException(
                $"Project schema version {version} is newer than supported version {SchemaVersion}.");
        }

        if (version < SchemaVersion)
        {
            await using var migrate = connection.CreateCommand();
            migrate.CommandText = "UPDATE SchemaInfo SET Version = $version;";
            migrate.Parameters.AddWithValue("$version", SchemaVersion);
            await migrate.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<ProjectDocument> ReadDocumentAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var projectCommand = connection.CreateCommand();
        projectCommand.CommandText =
            "SELECT Name, BinaryPath, Sha256, CreatedUtc, ModifiedUtc FROM Project WHERE Id = 1;";

        ProjectMetadata metadata;
        await using (var reader = await projectCommand.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new ProjectStoreException("Project database does not contain project metadata.");
            }

            metadata = new ProjectMetadata(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                ParseTimestamp(reader.GetString(3)),
                ParseTimestamp(reader.GetString(4)));
        }

        var annotations = new List<AddressAnnotation>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Rva, UserName, Comment, UpdatedUtc FROM Annotation ORDER BY Rva;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                annotations.Add(new AddressAnnotation(
                    checked((uint)reader.GetInt64(0)),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    ParseTimestamp(reader.GetString(3))));
            }
        }

        var bookmarks = new List<ProjectBookmark>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, Rva, Title, Comment, CreatedUtc FROM Bookmark ORDER BY Rva;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                bookmarks.Add(new ProjectBookmark(
                    reader.GetInt64(0),
                    checked((uint)reader.GetInt64(1)),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    ParseTimestamp(reader.GetString(4))));
            }
        }

        var patches = new List<BinaryPatch>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, Rva, FileOffset, OriginalBytes, PatchedBytes, CreatedUtc FROM Patch ORDER BY Id;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                patches.Add(new BinaryPatch(
                    reader.GetInt64(0),
                    checked((uint)reader.GetInt64(1)),
                    reader.GetInt64(2),
                    reader.GetFieldValue<byte[]>(3),
                    reader.GetFieldValue<byte[]>(4),
                    ParseTimestamp(reader.GetString(5))));
            }
        }

        return new ProjectDocument
        {
            Metadata = metadata,
            Annotations = annotations,
            Bookmarks = bookmarks,
            Patches = patches,
        };
    }

    private static async Task WriteDocumentAsync(
        SqliteConnection connection,
        ProjectDocument document,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText =
                """
                INSERT INTO Project(Id, Name, BinaryPath, Sha256, CreatedUtc, ModifiedUtc)
                VALUES (1, $name, $path, $sha, $created, $modified)
                ON CONFLICT(Id) DO UPDATE SET
                    Name = excluded.Name,
                    BinaryPath = excluded.BinaryPath,
                    Sha256 = excluded.Sha256,
                    CreatedUtc = excluded.CreatedUtc,
                    ModifiedUtc = excluded.ModifiedUtc;
                DELETE FROM Annotation;
                DELETE FROM Bookmark;
                DELETE FROM Patch;
                """;
            command.Parameters.AddWithValue("$name", document.Metadata.Name);
            command.Parameters.AddWithValue("$path", document.Metadata.BinaryPath);
            command.Parameters.AddWithValue("$sha", document.Metadata.Sha256);
            command.Parameters.AddWithValue("$created", FormatTimestamp(document.Metadata.CreatedUtc));
            command.Parameters.AddWithValue("$modified", FormatTimestamp(document.Metadata.ModifiedUtc));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var annotation in document.Annotations)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText =
                """
                INSERT INTO Annotation(Rva, UserName, Comment, UpdatedUtc)
                VALUES ($rva, $name, $comment, $updated);
                """;
            command.Parameters.AddWithValue("$rva", (long)annotation.Rva);
            command.Parameters.AddWithValue("$name", (object?)annotation.UserName ?? DBNull.Value);
            command.Parameters.AddWithValue("$comment", (object?)annotation.Comment ?? DBNull.Value);
            command.Parameters.AddWithValue("$updated", FormatTimestamp(annotation.UpdatedUtc));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var bookmark in document.Bookmarks)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText =
                """
                INSERT INTO Bookmark(Id, Rva, Title, Comment, CreatedUtc)
                VALUES ($id, $rva, $title, $comment, $created);
                """;
            command.Parameters.AddWithValue("$id", bookmark.Id == 0 ? DBNull.Value : bookmark.Id);
            command.Parameters.AddWithValue("$rva", (long)bookmark.Rva);
            command.Parameters.AddWithValue("$title", bookmark.Title);
            command.Parameters.AddWithValue("$comment", (object?)bookmark.Comment ?? DBNull.Value);
            command.Parameters.AddWithValue("$created", FormatTimestamp(bookmark.CreatedUtc));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var patch in document.Patches)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText =
                """
                INSERT INTO Patch(Id, Rva, FileOffset, OriginalBytes, PatchedBytes, CreatedUtc)
                VALUES ($id, $rva, $offset, $original, $patched, $created);
                """;
            command.Parameters.AddWithValue("$id", patch.Id == 0 ? DBNull.Value : patch.Id);
            command.Parameters.AddWithValue("$rva", (long)patch.Rva);
            command.Parameters.AddWithValue("$offset", patch.FileOffset);
            command.Parameters.AddWithValue("$original", patch.OriginalBytes);
            command.Parameters.AddWithValue("$patched", patch.PatchedBytes);
            command.Parameters.AddWithValue("$created", FormatTimestamp(patch.CreatedUtc));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static void EnsureDirectory(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
