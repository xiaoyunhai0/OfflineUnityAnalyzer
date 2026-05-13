using OfflineUnityAnalyzer.Analyzers.FileScanning;
using OfflineUnityAnalyzer.Core.Pipeline;

namespace OfflineUnityAnalyzer.Analyzers.Pipeline;

public static class AnalyzerPipelineFactory
{
    public static ProjectAnalyzer CreateDefault()
    {
        IAnalyzerStage[] stages =
        {
            new SafetyPreflightStage(),
            new FileScanStage()
        };

        return new ProjectAnalyzer(stages);
    }
}
