namespace OfflineUnityAnalyzer.Core.Models;

public sealed record YooAssetInfo
{
    public bool Detected { get; init; }

    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> ManifestFiles { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> CodeReferences { get; init; } = Array.Empty<string>();
}
