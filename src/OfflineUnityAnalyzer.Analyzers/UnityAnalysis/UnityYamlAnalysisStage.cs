using System.Text.RegularExpressions;
using OfflineUnityAnalyzer.Core.Models;
using OfflineUnityAnalyzer.Core.Pipeline;

namespace OfflineUnityAnalyzer.Analyzers.UnityAnalysis;

public sealed class UnityYamlAnalysisStage : IAnalyzerStage
{
    private static readonly Regex GuidRegex = new(@"^\s*guid:\s*([a-fA-F0-9]{32})\s*$", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex ScriptRefRegex = new(@"m_Script:\s*\{fileID:\s*([^,}]+),\s*guid:\s*([a-fA-F0-9]{32}),\s*type:\s*([^}]+)\}", RegexOptions.Compiled);
    private static readonly Regex EditorClassIdentifierRegex = new(@"m_EditorClassIdentifier:\s*(.+)", RegexOptions.Compiled);

    public AnalysisStageKind Kind => AnalysisStageKind.UnityYamlRawIndex;

    public Task<AnalysisStageResult> RunAsync(AnalysisContext context, CancellationToken cancellationToken)
    {
        var guidToAssetPath = BuildGuidMap(context, cancellationToken);
        var parsedAssets = 0;
        var refs = 0;
        var warnings = 0;

        foreach (var file in context.Files.Where(IsUnityYamlAsset))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var text = context.FileSystem.ReadAllText(file.FullPath);
                var scriptRefs = ParseScriptReferences(file, text, guidToAssetPath).ToArray();

                context.AddUnityAsset(new UnityAssetInfo
                {
                    Path = file.FullPath,
                    Kind = file.Kind.ToString(),
                    Guid = TryGetMetaGuid(file.FullPath, guidToAssetPath),
                    ScriptReferenceCount = scriptRefs.Length
                });

                foreach (var reference in scriptRefs)
                {
                    context.AddUnityScriptReference(reference);
                }

                parsedAssets++;
                refs += scriptRefs.Length;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                context.AddWarning($"Failed to parse Unity asset '{file.RelativePath}': {exception.Message}");
                warnings++;
            }
        }

        AddMissingScriptDiagnostics(context);

        var status = warnings == 0
            ? AnalysisStageStatus.Completed
            : AnalysisStageStatus.CompletedWithWarnings;

        return Task.FromResult(new AnalysisStageResult(
            Kind,
            status,
            $"Parsed {parsedAssets} Unity YAML assets and found {refs} script references.",
            WarningCount: warnings));
    }

    private static Dictionary<string, string> BuildGuidMap(AnalysisContext context, CancellationToken cancellationToken)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var meta in context.Files.Where(file => file.Kind == ProjectFileKind.UnityMeta))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var text = context.FileSystem.ReadAllText(meta.FullPath);
                var match = GuidRegex.Match(text);
                if (!match.Success)
                {
                    continue;
                }

                var assetPath = meta.FullPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
                    ? meta.FullPath[..^5]
                    : meta.FullPath;
                map[match.Groups[1].Value] = assetPath;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                context.AddWarning($"Failed to read meta file '{meta.RelativePath}': {exception.Message}");
            }
        }

        return map;
    }

    private static IEnumerable<UnityScriptReferenceInfo> ParseScriptReferences(
        ProjectFile file,
        string text,
        IReadOnlyDictionary<string, string> guidToAssetPath)
    {
        foreach (Match match in ScriptRefRegex.Matches(text))
        {
            var fileId = match.Groups[1].Value.Trim();
            var guid = match.Groups[2].Value.Trim();
            var type = match.Groups[3].Value.Trim();
            var resolvedPath = guidToAssetPath.TryGetValue(guid, out var path)
                ? path
                : null;

            yield return new UnityScriptReferenceInfo
            {
                AssetPath = file.FullPath,
                AssetKind = file.Kind.ToString(),
                ScriptGuid = guid,
                FileId = fileId,
                Type = type,
                EditorClassIdentifier = FindNearestEditorClassIdentifier(text, match.Index),
                ResolvedScriptPath = resolvedPath,
                ResolvedType = resolvedPath is not null && resolvedPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                    ? Path.GetFileNameWithoutExtension(resolvedPath)
                    : null,
                Confidence = resolvedPath is null ? "low" : "medium"
            };
        }
    }

    private static string? FindNearestEditorClassIdentifier(string text, int startIndex)
    {
        var searchEnd = Math.Min(text.Length, startIndex + 400);
        var fragment = text[startIndex..searchEnd];
        var match = EditorClassIdentifierRegex.Match(fragment);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static string? TryGetMetaGuid(string assetPath, IReadOnlyDictionary<string, string> guidToAssetPath)
    {
        foreach (var pair in guidToAssetPath)
        {
            if (pair.Value.Equals(assetPath, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Key;
            }
        }

        return null;
    }

    private static bool IsUnityYamlAsset(ProjectFile file)
    {
        return file.Kind is ProjectFileKind.UnityPrefab
            or ProjectFileKind.UnityScene
            or ProjectFileKind.UnityAsset
            or ProjectFileKind.UnityController
            or ProjectFileKind.UnityOverrideController
            or ProjectFileKind.UnityPlayable
            or ProjectFileKind.UnityAnimation
            or ProjectFileKind.UnityMaterial
            or ProjectFileKind.UnityProjectSettings;
    }

    private static void AddMissingScriptDiagnostics(AnalysisContext context)
    {
        foreach (var reference in context.UnityScriptReferences.Where(reference => reference.ResolvedScriptPath is null))
        {
            context.AddDiagnostic(new DiagnosticInfo
            {
                Severity = "warning",
                Category = "unity",
                Message = $"Unresolved Unity script reference: guid={reference.ScriptGuid}, fileID={reference.FileId}",
                Path = reference.AssetPath
            });
        }
    }
}
