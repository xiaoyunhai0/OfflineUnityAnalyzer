namespace OfflineUnityAnalyzer.Core.Configuration;

public sealed record AnalyzerConfig
{
    public IReadOnlyList<string> CodeRoots { get; init; } = Array.Empty<string>();

    public string? SolutionPath { get; init; }

    public string? UnityProject { get; init; }

    public IReadOnlyList<string> DllRoots { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> YooAssetManifestRoots { get; init; } = Array.Empty<string>();

    public string Output { get; init; } = string.Empty;

    public bool StrictReadonly { get; init; } = true;

    public bool AnalyzeCallGraph { get; init; } = true;

    public StageSelection Stages { get; init; } = new();

    public IReadOnlyList<string> ExcludePatterns { get; init; } = DefaultExcludePatterns.All;

    public ReportConfig Report { get; init; } = new();

    public static AnalyzerConfig Empty => new();
}
