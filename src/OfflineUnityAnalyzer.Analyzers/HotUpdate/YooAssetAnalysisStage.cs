using OfflineUnityAnalyzer.Core.Models;
using OfflineUnityAnalyzer.Core.Pipeline;

namespace OfflineUnityAnalyzer.Analyzers.HotUpdate;

public sealed class YooAssetAnalysisStage : IAnalyzerStage
{
    public AnalysisStageKind Kind => AnalysisStageKind.YooAssetIndex;

    public Task<AnalysisStageResult> RunAsync(AnalysisContext context, CancellationToken cancellationToken)
    {
        var evidence = new List<string>();
        var manifests = new List<string>();
        var codeRefs = new List<string>();

        foreach (var file in context.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = file.FullPath.Replace('\\', '/');

            if (path.Contains("YooAsset", StringComparison.OrdinalIgnoreCase))
            {
                evidence.Add(file.FullPath);
            }

            if (file.Kind == ProjectFileKind.YooAssetManifest
                || Path.GetFileName(file.FullPath).Contains("PackageManifest", StringComparison.OrdinalIgnoreCase))
            {
                manifests.Add(file.FullPath);
            }

            if (file.Kind == ProjectFileKind.CSharpSource)
            {
                try
                {
                    var text = context.FileSystem.ReadAllText(file.FullPath);
                    if (text.Contains("YooAssets.", StringComparison.Ordinal)
                        || text.Contains("LoadAssetAsync", StringComparison.Ordinal)
                        || text.Contains("LoadSceneAsync", StringComparison.Ordinal)
                        || text.Contains("LoadRawFileAsync", StringComparison.Ordinal))
                    {
                        codeRefs.Add(file.FullPath);
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    context.AddWarning($"Failed to scan YooAsset code reference '{file.RelativePath}': {exception.Message}");
                }
            }
        }

        var detected = evidence.Any(path =>
                path.Contains("Packages/manifest.json", StringComparison.OrdinalIgnoreCase)
                || path.Contains("Assets/YooAsset", StringComparison.OrdinalIgnoreCase)
                || path.Contains("YooAssetSettings", StringComparison.OrdinalIgnoreCase))
            || manifests.Count > 0;

        context.SetYooAsset(new YooAssetInfo
        {
            Detected = detected,
            Evidence = evidence.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path).ToArray(),
            ManifestFiles = manifests.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path).ToArray(),
            CodeReferences = codeRefs.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path).ToArray()
        });

        return Task.FromResult(new AnalysisStageResult(
            Kind,
            AnalysisStageStatus.Completed,
            context.YooAsset.Detected
                ? $"YooAsset evidence found: {context.YooAsset.Evidence.Count + context.YooAsset.ManifestFiles.Count + context.YooAsset.CodeReferences.Count} items."
                : "YooAsset evidence not found."));
    }
}
