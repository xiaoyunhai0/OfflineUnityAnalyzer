using OfflineUnityAnalyzer.Core.Models;

namespace OfflineUnityAnalyzer.Core.Pipeline;

public sealed class NullProgressReporter : IProgressReporter
{
    public static readonly NullProgressReporter Instance = new();

    private NullProgressReporter()
    {
    }

    public void StageStarted(AnalysisStageKind stage, string message)
    {
    }

    public void StageFinished(AnalysisStageResult result)
    {
    }

    public void Message(string message)
    {
    }
}
