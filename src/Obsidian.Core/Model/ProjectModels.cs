namespace Obsidian.Core.Model;

/// <summary>Metadata persisted with an Obsidian project database.</summary>
public sealed record ProjectMetadata(
    string Name,
    string BinaryPath,
    string Sha256,
    DateTimeOffset CreatedUtc,
    DateTimeOffset ModifiedUtc);

/// <summary>User label/comment attached to an address (function, string, instruction).</summary>
public sealed record AddressAnnotation(
    uint Rva,
    string? UserName,
    string? Comment,
    DateTimeOffset UpdatedUtc);

/// <summary>User bookmark at an RVA with a title and optional note.</summary>
public sealed record ProjectBookmark(
    long Id,
    uint Rva,
    string Title,
    string? Comment,
    DateTimeOffset CreatedUtc);

/// <summary>
/// A persisted in-memory instruction patch. Operations are replayed in order
/// against the original binary; the original file is never modified.
/// </summary>
public sealed record BinaryPatch(
    long Id,
    uint Rva,
    long FileOffset,
    byte[] OriginalBytes,
    byte[] PatchedBytes,
    DateTimeOffset CreatedUtc);

/// <summary>Complete immutable project snapshot; the store writes it transactionally.</summary>
public sealed class ProjectDocument
{
    public required ProjectMetadata Metadata { get; init; }
    public required IReadOnlyList<AddressAnnotation> Annotations { get; init; }
    public required IReadOnlyList<ProjectBookmark> Bookmarks { get; init; }
    public required IReadOnlyList<BinaryPatch> Patches { get; init; }

    public AddressAnnotation? AnnotationFor(uint rva) =>
        Annotations.FirstOrDefault(annotation => annotation.Rva == rva);

    public static ProjectDocument Empty(ProjectMetadata metadata) =>
        new()
        {
            Metadata = metadata,
            Annotations = [],
            Bookmarks = [],
            Patches = [],
        };
}

/// <summary>SQLite project database contract.</summary>
public interface IProjectStore
{
    Task CreateAsync(string path, ProjectMetadata metadata, CancellationToken cancellationToken = default);

    Task<ProjectDocument> OpenAsync(string path, CancellationToken cancellationToken = default);

    Task SaveAsync(string path, ProjectDocument document, CancellationToken cancellationToken = default);
}

/// <summary>Project storage failure with a user-facing message.</summary>
public sealed class ProjectStoreException : Exception
{
    public ProjectStoreException(string message)
        : base(message)
    {
    }

    public ProjectStoreException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
