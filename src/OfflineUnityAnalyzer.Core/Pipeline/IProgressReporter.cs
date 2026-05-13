using OfflineUnityAnalyzer.Core.Models;

namespace OfflineUnityAnalyzer.Core.Pipeline;

public interface IProgressReporter
{
    void StageStarted(AnalysisStageKind stage, string message);

    void StageFinished(AnalysisStageResult result);

    void Message(string message);
}
