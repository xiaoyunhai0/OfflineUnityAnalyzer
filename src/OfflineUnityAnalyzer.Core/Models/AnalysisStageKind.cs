namespace OfflineUnityAnalyzer.Core.Models;

public enum AnalysisStageKind
{
    SafetyPreflight,
    FileScan,
    ProjectModel,
    SourceSyntaxIndex,
    DllIndex,
    UnityYamlRawIndex,
    TypeMerge,
    SerializedFieldIndex,
    PrefabMerge,
    HybridClrIndex,
    YooAssetIndex,
    ConfigShallowIndex,
    SemanticAnalysis,
    CallGraph,
    ModuleInference,
    Diagnostics,
    SearchIndex,
    ReportExport
}
