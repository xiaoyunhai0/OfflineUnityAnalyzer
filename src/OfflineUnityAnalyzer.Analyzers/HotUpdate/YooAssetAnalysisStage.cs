using System.Text.Json;
using System.Text.RegularExpressions;
using OfflineUnityAnalyzer.Core.Models;
using OfflineUnityAnalyzer.Core.Pipeline;

namespace OfflineUnityAnalyzer.Analyzers.HotUpdate;

public sealed class YooAssetAnalysisStage : IAnalyzerStage
{
    private static readonly Regex YooAssetCallRegex = new(@"\b(?<api>LoadAssetAsync|LoadSceneAsync|LoadRawFileAsync|LoadSubAssetsAsync|LoadAllAssetsAsync)\s*(?:<[^>]+>)?\s*\(\s*""(?<address>[^""]+)""", RegexOptions.Compiled);

    public AnalysisStageKind Kind => AnalysisStageKind.YooAssetIndex;

    public Task<AnalysisStageResult> RunAsync(AnalysisContext context, CancellationToken cancellationToken)
    {
        var evidence = new List<string>();
        var manifests = new List<string>();
        var codeRefs = new List<string>();
        var structuredCodeRefs = new List<YooAssetCodeReferenceInfo>();
        var manifestAssets = new List<YooAssetManifestAssetInfo>();

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
                manifestAssets.AddRange(ParseManifestAssets(context, file));
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
                        structuredCodeRefs.AddRange(ParseCodeReferences(file, text));
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
            CodeReferences = codeRefs.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path).ToArray(),
            Assets = manifestAssets
                .DistinctBy(asset => $"{asset.ManifestPath}|{asset.Address}|{asset.AssetPath}")
                .OrderBy(asset => asset.Address)
                .ToArray(),
            StructuredCodeReferences = structuredCodeRefs
                .DistinctBy(reference => $"{reference.SourceFile}|{reference.ApiName}|{reference.AddressLiteral}")
                .OrderBy(reference => reference.SourceFile)
                .ToArray()
        });

        return Task.FromResult(new AnalysisStageResult(
            Kind,
            AnalysisStageStatus.Completed,
            context.YooAsset.Detected
                ? $"YooAsset evidence found: {context.YooAsset.Evidence.Count + context.YooAsset.ManifestFiles.Count + context.YooAsset.CodeReferences.Count} items."
                : "YooAsset evidence not found."));
    }

    private static IEnumerable<YooAssetManifestAssetInfo> ParseManifestAssets(AnalysisContext context, ProjectFile file)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(context.FileSystem.ReadAllText(file.FullPath));
        }
        catch (JsonException exception)
        {
            context.AddWarning($"Failed to parse YooAsset manifest '{file.RelativePath}': {exception.Message}");
            yield break;
        }

        using (document)
        {
            var root = document.RootElement;
            var packageName = ReadString(root, "packageName")
                ?? ReadString(root, "PackageName")
                ?? Path.GetFileNameWithoutExtension(file.FullPath);

            foreach (var asset in EnumerateAssetElements(root))
            {
                var address = ReadString(asset, "address")
                    ?? ReadString(asset, "Address")
                    ?? ReadString(asset, "assetPath")
                    ?? ReadString(asset, "AssetPath")
                    ?? string.Empty;
                var assetPath = ReadString(asset, "assetPath")
                    ?? ReadString(asset, "AssetPath")
                    ?? ReadString(asset, "path")
                    ?? ReadString(asset, "Path")
                    ?? string.Empty;

                if (string.IsNullOrWhiteSpace(address) && string.IsNullOrWhiteSpace(assetPath))
                {
                    continue;
                }

                yield return new YooAssetManifestAssetInfo
                {
                    ManifestPath = file.FullPath,
                    PackageName = packageName,
                    Address = address,
                    AssetPath = assetPath,
                    BundleName = ReadString(asset, "bundleName")
                        ?? ReadString(asset, "BundleName")
                        ?? ReadString(asset, "bundle")
                        ?? ReadString(asset, "Bundle")
                        ?? string.Empty,
                    Tags = ReadStringArray(asset, "tags")
                        .Concat(ReadStringArray(asset, "Tags"))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray()
                };
            }
        }
    }

    private static IEnumerable<YooAssetCodeReferenceInfo> ParseCodeReferences(ProjectFile file, string text)
    {
        foreach (Match match in YooAssetCallRegex.Matches(text))
        {
            yield return new YooAssetCodeReferenceInfo
            {
                SourceFile = file.FullPath,
                ApiName = match.Groups["api"].Value,
                AddressLiteral = match.Groups["address"].Value,
                Confidence = "high"
            };
        }

        if (!YooAssetCallRegex.IsMatch(text))
        {
            yield return new YooAssetCodeReferenceInfo
            {
                SourceFile = file.FullPath,
                ApiName = "YooAsset usage",
                Confidence = "low"
            };
        }
    }

    private static IEnumerable<JsonElement> EnumerateAssetElements(JsonElement root)
    {
        foreach (var propertyName in new[] { "assetList", "AssetList", "assets", "Assets" })
        {
            if (!root.TryGetProperty(propertyName, out var array) || array.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var element in array.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.Object)
                {
                    yield return element;
                }
            }
        }
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
    }

    private static IEnumerable<string> ReadStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return property
            .EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!);
    }
}
