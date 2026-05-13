namespace OfflineUnityAnalyzer.Core.Models;

public sealed record PackageInfo
{
    public string Name { get; init; } = string.Empty;

    public string VersionOrSource { get; init; } = string.Empty;

    public string Source { get; init; } = "manifest";

    public string? Path { get; init; }
}
