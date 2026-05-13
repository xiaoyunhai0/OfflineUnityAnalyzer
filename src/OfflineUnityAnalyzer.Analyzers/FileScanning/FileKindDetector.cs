using OfflineUnityAnalyzer.Core.Models;

namespace OfflineUnityAnalyzer.Analyzers.FileScanning;

public static class FileKindDetector
{
    public static ProjectFileKind Detect(string fullPath)
    {
        var fileName = Path.GetFileName(fullPath);
        var extension = Path.GetExtension(fullPath).ToLowerInvariant();

        if (fileName.Equals("manifest.json", StringComparison.OrdinalIgnoreCase)
            && ContainsDirectory(fullPath, "Packages"))
        {
            return ProjectFileKind.PackageManifest;
        }

        if (fileName.Equals("packages-lock.json", StringComparison.OrdinalIgnoreCase))
        {
            return ProjectFileKind.PackageLock;
        }

        if (fileName.StartsWith("PackageManifest", StringComparison.OrdinalIgnoreCase))
        {
            return ProjectFileKind.YooAssetManifest;
        }

        if (extension == ".dll")
        {
            return ProjectFileKind.Dll;
        }

        if (fileName.EndsWith(".dll.bytes", StringComparison.OrdinalIgnoreCase))
        {
            return ProjectFileKind.DllBytes;
        }

        return extension switch
        {
            ".cs" => ProjectFileKind.CSharpSource,
            ".csproj" => ProjectFileKind.CSharpProject,
            ".sln" => ProjectFileKind.Solution,
            ".asmdef" => ProjectFileKind.AssemblyDefinition,
            ".asmref" => ProjectFileKind.AssemblyDefinitionReference,
            ".prefab" => ProjectFileKind.UnityPrefab,
            ".unity" => ProjectFileKind.UnityScene,
            ".asset" => ContainsDirectory(fullPath, "ProjectSettings")
                ? ProjectFileKind.UnityProjectSettings
                : ProjectFileKind.UnityAsset,
            ".controller" => ProjectFileKind.UnityController,
            ".overridecontroller" => ProjectFileKind.UnityOverrideController,
            ".playable" => ProjectFileKind.UnityPlayable,
            ".anim" => ProjectFileKind.UnityAnimation,
            ".mat" => ProjectFileKind.UnityMaterial,
            ".meta" => ProjectFileKind.UnityMeta,
            ".json" => ProjectFileKind.Json,
            ".csv" => ProjectFileKind.Csv,
            ".xml" => ProjectFileKind.Xml,
            ".bytes" => ProjectFileKind.TextBytes,
            _ => ProjectFileKind.Unknown
        };
    }

    private static bool ContainsDirectory(string fullPath, string directoryName)
    {
        var parts = fullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(part => part.Equals(directoryName, StringComparison.OrdinalIgnoreCase));
    }
}
