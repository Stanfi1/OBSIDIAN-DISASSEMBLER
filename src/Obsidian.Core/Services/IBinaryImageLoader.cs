using Obsidian.Core.Model;

namespace Obsidian.Core.Services;

/// <summary>Loads a binary into the immutable analysis model.</summary>
public interface IBinaryImageLoader
{
    Task<BinaryImage> LoadAsync(
        string filePath,
        IProgress<LoadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
