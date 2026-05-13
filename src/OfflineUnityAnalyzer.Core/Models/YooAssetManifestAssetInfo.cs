namespace OfflineUnityAnalyzer.Core.Models;

public sealed record YooAssetManifestAssetInfo
{
    public string ManifestPath { get; init; } = string.Empty;

    public string PackageName { get; init; } = string.Empty;

    public string Address { get; init; } = string.Empty;

    public string AssetPath { get; init; } = string.Empty;

    public string BundleName { get; init; } = string.Empty;

    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
}
