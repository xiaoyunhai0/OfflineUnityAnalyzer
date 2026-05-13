namespace OfflineUnityAnalyzer.Core.Models;

public enum AnalysisStageStatus
{
    Pending,
    Running,
    Completed,
    CompletedWithWarnings,
    Skipped,
    Failed,
    Cancelled
}
