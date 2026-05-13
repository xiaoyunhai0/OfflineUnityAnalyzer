namespace OfflineUnityAnalyzer.Core.Models;

public sealed record ModuleInfo
{
    public string Name { get; init; } = string.Empty;

    public string Source { get; init; } = string.Empty;

    public string Confidence { get; init; } = "medium";

    public int TypeCount { get; init; }

    public int AssetCount { get; init; }

    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();
}
