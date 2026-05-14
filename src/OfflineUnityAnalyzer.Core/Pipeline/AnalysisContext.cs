using OfflineUnityAnalyzer.Core.Configuration;
using OfflineUnityAnalyzer.Core.Models;
using OfflineUnityAnalyzer.Core.Safety;

namespace OfflineUnityAnalyzer.Core.Pipeline;

public sealed class AnalysisContext
{
    private readonly List<ProjectFile> _files = new();
    private readonly List<string> _warnings = new();
    private readonly List<string> _errors = new();
    private readonly List<SourceTypeInfo> _sourceTypes = new();
    private readonly List<SourceTypeRelationInfo> _sourceTypeRelations = new();
    private readonly List<CodeAssemblyBridgeInfo> _codeAssemblyBridges = new();
    private readonly List<AssemblyMetadataInfo> _assemblies = new();
    private readonly List<UnityAssetInfo> _unityAssets = new();
    private readonly List<UnityScriptReferenceInfo> _unityScriptReferences = new();
    private readonly List<UnityObjectInfo> _unityObjects = new();
    private readonly List<UnityGameObjectInfo> _unityGameObjects = new();
    private readonly List<UnityComponentInfo> _unityComponents = new();
    private readonly List<UnityAssetReferenceInfo> _unityAssetReferences = new();
    private readonly List<ConfigReferenceInfo> _configReferences = new();
    private readonly List<DiagnosticInfo> _diagnostics = new();
    private readonly List<ModuleInfo> _modules = new();

    public AnalysisContext(AnalyzerConfig config, PathGuard pathGuard, ISafeFileSystem fileSystem)
    {
        Config = config;
        PathGuard = pathGuard;
        FileSystem = fileSystem;
    }

    public AnalyzerConfig Config { get; }

    public PathGuard PathGuard { get; }

    public ISafeFileSystem FileSystem { get; }

    public IReadOnlyList<ProjectFile> Files => _files;

    public IReadOnlyList<string> Warnings => _warnings;

    public IReadOnlyList<string> Errors => _errors;

    public IReadOnlyList<SourceTypeInfo> SourceTypes => _sourceTypes;

    public IReadOnlyList<SourceTypeRelationInfo> SourceTypeRelations => _sourceTypeRelations;

    public IReadOnlyList<CodeAssemblyBridgeInfo> CodeAssemblyBridges => _codeAssemblyBridges;

    public IReadOnlyList<AssemblyMetadataInfo> Assemblies => _assemblies;

    public IReadOnlyList<UnityAssetInfo> UnityAssets => _unityAssets;

    public IReadOnlyList<UnityScriptReferenceInfo> UnityScriptReferences => _unityScriptReferences;

    public IReadOnlyList<UnityObjectInfo> UnityObjects => _unityObjects;

    public IReadOnlyList<UnityGameObjectInfo> UnityGameObjects => _unityGameObjects;

    public IReadOnlyList<UnityComponentInfo> UnityComponents => _unityComponents;

    public IReadOnlyList<UnityAssetReferenceInfo> UnityAssetReferences => _unityAssetReferences;

    public IReadOnlyList<ConfigReferenceInfo> ConfigReferences => _configReferences;

    public IReadOnlyList<DiagnosticInfo> Diagnostics => _diagnostics;

    public IReadOnlyList<ModuleInfo> Modules => _modules;

    public HybridClrInfo HybridClr { get; private set; } = new();

    public YooAssetInfo YooAsset { get; private set; } = new();

    public ProjectModelInfo ProjectModel { get; private set; } = new();

    public void AddFile(ProjectFile file)
    {
        _files.Add(file);
    }

    public void AddWarning(string warning)
    {
        _warnings.Add(warning);
    }

    public void AddError(string error)
    {
        _errors.Add(error);
    }

    public void AddSourceType(SourceTypeInfo type)
    {
        _sourceTypes.Add(type);
    }

    public void AddSourceTypeRelation(SourceTypeRelationInfo relation)
    {
        _sourceTypeRelations.Add(relation);
    }

    public void AddCodeAssemblyBridge(CodeAssemblyBridgeInfo bridge)
    {
        _codeAssemblyBridges.Add(bridge);
    }

    public void AddAssembly(AssemblyMetadataInfo assembly)
    {
        _assemblies.Add(assembly);
    }

    public void AddUnityAsset(UnityAssetInfo asset)
    {
        _unityAssets.Add(asset);
    }

    public void AddUnityScriptReference(UnityScriptReferenceInfo reference)
    {
        _unityScriptReferences.Add(reference);
    }

    public void AddUnityObject(UnityObjectInfo unityObject)
    {
        _unityObjects.Add(unityObject);
    }

    public void AddUnityGameObject(UnityGameObjectInfo gameObject)
    {
        _unityGameObjects.Add(gameObject);
    }

    public void AddUnityComponent(UnityComponentInfo component)
    {
        _unityComponents.Add(component);
    }

    public void AddUnityAssetReference(UnityAssetReferenceInfo reference)
    {
        _unityAssetReferences.Add(reference);
    }

    public void AddConfigReference(ConfigReferenceInfo reference)
    {
        _configReferences.Add(reference);
    }

    public void AddDiagnostic(DiagnosticInfo diagnostic)
    {
        _diagnostics.Add(diagnostic);
    }

    public void AddModule(ModuleInfo module)
    {
        _modules.Add(module);
    }

    public void SetHybridClr(HybridClrInfo hybridClr)
    {
        HybridClr = hybridClr;
    }

    public void SetYooAsset(YooAssetInfo yooAsset)
    {
        YooAsset = yooAsset;
    }

    public void SetProjectModel(ProjectModelInfo projectModel)
    {
        ProjectModel = projectModel;
    }
}
