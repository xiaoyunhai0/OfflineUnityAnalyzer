namespace OfflineUnityAnalyzer.Core.Models;

public sealed record UnityObjectInfo
{
    public string AssetPath { get; init; } = string.Empty;

    public string AssetKind { get; init; } = string.Empty;

    public string LocalId { get; init; } = string.Empty;

    public int ClassId { get; init; }

    public string TypeName { get; init; } = string.Empty;

    public string? Name { get; init; }

    public string? GameObjectLocalId { get; init; }
}
