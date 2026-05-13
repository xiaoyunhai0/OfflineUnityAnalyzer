namespace OfflineUnityAnalyzer.Core.Models;

public sealed record UnityAssetReferenceInfo
{
    public string AssetPath { get; init; } = string.Empty;

    public string OwnerLocalId { get; init; } = string.Empty;

    public string OwnerType { get; init; } = string.Empty;

    public string FieldName { get; init; } = string.Empty;

    public string? FileId { get; init; }

    public string? Guid { get; init; }

    public string? Type { get; init; }

    public string? ResolvedPath { get; init; }

    public string ReferenceKind { get; init; } = "external";
}
