namespace OfflineUnityAnalyzer.Core.Models;

public enum ProjectFileKind
{
    Unknown,
    CSharpSource,
    CSharpProject,
    Solution,
    AssemblyDefinition,
    AssemblyDefinitionReference,
    Dll,
    DllBytes,
    UnityPrefab,
    UnityScene,
    UnityAsset,
    UnityController,
    UnityOverrideController,
    UnityPlayable,
    UnityAnimation,
    UnityMaterial,
    UnityMeta,
    UnityProjectSettings,
    PackageManifest,
    PackageLock,
    YooAssetManifest,
    Json,
    Csv,
    Xml,
    TextBytes
}
