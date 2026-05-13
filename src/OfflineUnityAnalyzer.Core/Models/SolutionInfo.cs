namespace OfflineUnityAnalyzer.Core.Models;

public sealed record SolutionInfo
{
    public string Path { get; init; } = string.Empty;

    public IReadOnlyList<string> ProjectPaths { get; init; } = Array.Empty<string>();
}
