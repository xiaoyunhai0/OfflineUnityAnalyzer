namespace OfflineUnityAnalyzer.Core.Models;

public sealed record HybridClrInfo
{
    public bool Detected { get; init; }

    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> HotUpdateAssemblies { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> AotMetadataAssemblies { get; init; } = Array.Empty<string>();
}
