using Obsidian.Core.Model;

namespace Obsidian.Core.Services;

/// <summary>
/// Function/xref/string/CFG analysis boundary. Implementations consume an
/// already decoded disassembly and must not decode instructions themselves.
/// </summary>
public interface IBinaryAnalysisEngine
{
    Task<AnalysisResult> AnalyzeAsync(
        BinaryImage image,
        DisassemblyImage disassembly,
        IProgress<LoadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
