namespace OfflineUnityAnalyzer.Core.Models;

public sealed record UnityComponentInfo
{
    public string AssetPath { get; init; } = string.Empty;

    public string LocalId { get; init; } = string.Empty;

    public string TypeName { get; init; } = string.Empty;

    public string? GameObjectLocalId { get; init; }

    public string? GameObjectName { get; init; }

    public string? ScriptGuid { get; init; }

    public string? ScriptFileId { get; init; }

    public string? ResolvedScriptPath { get; init; }

    public string? ResolvedType { get; init; }
}
