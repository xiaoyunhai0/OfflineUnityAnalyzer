namespace OfflineUnityAnalyzer.Core.Models;

public sealed record ConfigReferenceInfo
{
    public string SourcePath { get; init; } = string.Empty;

    public string Location { get; init; } = string.Empty;

    public string FieldName { get; init; } = string.Empty;

    public string Value { get; init; } = string.Empty;

    public string MatchKind { get; init; } = string.Empty;

    public string Confidence { get; init; } = "medium";
}
