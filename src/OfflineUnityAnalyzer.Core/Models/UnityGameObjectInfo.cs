namespace OfflineUnityAnalyzer.Core.Models;

public sealed record UnityGameObjectInfo
{
    public string AssetPath { get; init; } = string.Empty;

    public string LocalId { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public IReadOnlyList<string> ComponentLocalIds { get; init; } = Array.Empty<string>();
}
