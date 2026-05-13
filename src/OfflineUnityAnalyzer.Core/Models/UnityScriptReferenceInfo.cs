namespace OfflineUnityAnalyzer.Core.Models;

public sealed record UnityScriptReferenceInfo
{
    public string AssetPath { get; init; } = string.Empty;

    public string AssetKind { get; init; } = string.Empty;

    public string? ScriptGuid { get; init; }

    public string? FileId { get; init; }

    public string? Type { get; init; }

    public string? EditorClassIdentifier { get; init; }

    public string? ResolvedScriptPath { get; init; }

    public string? ResolvedType { get; init; }

    public string Confidence { get; init; } = "low";
}
