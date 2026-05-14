using OfflineUnityAnalyzer.Analyzers.FileScanning;
using OfflineUnityAnalyzer.Analyzers.AssemblyAnalysis;
using OfflineUnityAnalyzer.Analyzers.ConfigAnalysis;
using OfflineUnityAnalyzer.Analyzers.HotUpdate;
using OfflineUnityAnalyzer.Analyzers.Modules;
using OfflineUnityAnalyzer.Analyzers.ProjectModel;
using OfflineUnityAnalyzer.Analyzers.Reporting;
using OfflineUnityAnalyzer.Analyzers.SourceAnalysis;
using OfflineUnityAnalyzer.Analyzers.UnityAnalysis;
using OfflineUnityAnalyzer.Core.Pipeline;

namespace OfflineUnityAnalyzer.Analyzers.Pipeline;

public static class AnalyzerPipelineFactory
{
    public static ProjectAnalyzer CreateDefault()
    {
        IAnalyzerStage[] stages =
        {
            new SafetyPreflightStage(),
            new FileScanStage(),
            new ProjectModelStage(),
            new SourceAnalysisStage(),
            new AssemblyAnalysisStage(),
            new CodeAssemblyBridgeStage(),
            new UnityYamlAnalysisStage(),
            new HybridClrAnalysisStage(),
            new YooAssetAnalysisStage(),
            new ConfigReferenceStage(),
            new ModuleInferenceStage(),
            new ReportGenerationStage()
        };

        return new ProjectAnalyzer(stages);
    }
}
