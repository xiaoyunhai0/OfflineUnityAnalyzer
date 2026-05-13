namespace OfflineUnityAnalyzer.Core.Models;

public sealed record AnalyzeResult
{
    public DateTime StartedAtUtc { get; init; }

    public DateTime FinishedAtUtc { get; init; }

    public string OutputRoot { get; init; } = string.Empty;

    public int FileCount { get; init; }

    public IReadOnlyList<AnalysisStageResult> Stages { get; init; } = Array.Empty<AnalysisStageResult>();

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public bool HasErrors => Errors.Count > 0 || Stages.Any(stage => stage.Status == AnalysisStageStatus.Failed);

    public bool HasWarnings => Warnings.Count > 0 || Stages.Any(stage => stage.WarningCount > 0);
}
