using Obsidian.Core.Model;

namespace Obsidian.Core.Services;

/// <summary>Native instruction decoder boundary; implementations may support only some architectures.</summary>
public interface IDisassembler
{
    bool IsSupported(BinaryArchitecture architecture);

    Task<DisassemblyImage> DisassembleAsync(
        BinaryImage image,
        IProgress<LoadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
