namespace OfflineUnityAnalyzer.Core.Models;

public sealed record AnalysisStageResult(
    AnalysisStageKind Stage,
    AnalysisStageStatus Status,
    string Message,
    int WarningCount = 0,
    int ErrorCount = 0);
