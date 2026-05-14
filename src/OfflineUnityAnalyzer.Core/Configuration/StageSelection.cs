using OfflineUnityAnalyzer.Core.Models;

namespace OfflineUnityAnalyzer.Core.Configuration;

public sealed record StageSelection
{
    public bool ProjectModel { get; init; } = true;

    public bool SourceSyntaxIndex { get; init; } = true;

    public bool DllIndex { get; init; } = true;

    public bool UnityYamlRawIndex { get; init; } = true;

    public bool HybridClrIndex { get; init; } = true;

    public bool YooAssetIndex { get; init; } = true;

    public bool ConfigShallowIndex { get; init; } = true;

    public bool ModuleInference { get; init; } = true;

    public bool ReportExport { get; init; } = true;

    public bool IsEnabled(AnalysisStageKind kind)
    {
        return kind switch
        {
            AnalysisStageKind.SafetyPreflight => true,
            AnalysisStageKind.FileScan => true,
            AnalysisStageKind.ProjectModel => ProjectModel,
            AnalysisStageKind.SourceSyntaxIndex => SourceSyntaxIndex,
            AnalysisStageKind.DllIndex => DllIndex,
            AnalysisStageKind.UnityYamlRawIndex => UnityYamlRawIndex,
            AnalysisStageKind.HybridClrIndex => HybridClrIndex,
            AnalysisStageKind.YooAssetIndex => YooAssetIndex,
            AnalysisStageKind.ConfigShallowIndex => ConfigShallowIndex,
            AnalysisStageKind.ModuleInference => ModuleInference,
            AnalysisStageKind.ReportExport => ReportExport,
            _ => true
        };
    }
}
