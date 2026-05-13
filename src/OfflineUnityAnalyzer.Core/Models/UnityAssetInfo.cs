namespace OfflineUnityAnalyzer.Core.Models;

public sealed record UnityAssetInfo
{
    public string Path { get; init; } = string.Empty;

    public string Kind { get; init; } = string.Empty;

    public string? Guid { get; init; }

    public int ScriptReferenceCount { get; init; }
}
