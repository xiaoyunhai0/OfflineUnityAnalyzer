namespace OfflineUnityAnalyzer.Core.Models;

public sealed record SourceTypeRelationInfo
{
    public string SourceType { get; init; } = string.Empty;

    public string TargetType { get; init; } = string.Empty;

    public string TargetDisplayName { get; init; } = string.Empty;

    public string RelationKind { get; init; } = string.Empty;

    public string SourceFile { get; init; } = string.Empty;

    public string? SourceMember { get; init; }

    public bool IsResolved { get; init; }

    public string Confidence { get; init; } = "medium";
}
