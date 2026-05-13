using System.Text.RegularExpressions;
using OfflineUnityAnalyzer.Core.Models;
using OfflineUnityAnalyzer.Core.Pipeline;

namespace OfflineUnityAnalyzer.Analyzers.UnityAnalysis;

public sealed class UnityYamlAnalysisStage : IAnalyzerStage
{
    private static readonly Regex GuidRegex = new(@"^\s*guid:\s*([a-fA-F0-9]{32})\s*$", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex DocumentHeaderRegex = new(@"^---\s*!u!(\d+)\s*&(-?\d+)", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex ScriptRefRegex = new(@"m_Script:\s*\{fileID:\s*([^,}]+),\s*guid:\s*([a-fA-F0-9]{32}),\s*type:\s*([^}]+)\}", RegexOptions.Compiled);
    private static readonly Regex ObjectRefRegex = new(@"(?<field>[A-Za-z_][\w.]*)\s*:\s*\{fileID:\s*(?<fileId>[^,}]+)(?:,\s*guid:\s*(?<guid>[a-fA-F0-9]{32}),\s*type:\s*(?<type>[^}]+))?\}", RegexOptions.Compiled);
    private static readonly Regex EditorClassIdentifierRegex = new(@"m_EditorClassIdentifier:\s*(.+)", RegexOptions.Compiled);
    private static readonly Regex NameRegex = new(@"^\s*m_Name:\s*(.*)$", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex GameObjectRefRegex = new(@"m_GameObject:\s*\{fileID:\s*([^,}]+)", RegexOptions.Compiled);
    private static readonly Regex ComponentRefRegex = new(@"component:\s*\{fileID:\s*([^,}]+)", RegexOptions.Compiled);

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
                var documents = ParseDocuments(file, text).ToArray();
                var scriptRefs = documents
                    .SelectMany(document => ParseScriptReferences(file, document, guidToAssetPath))
                    .ToArray();
                var gameObjects = documents.Where(document => document.TypeName == "GameObject").ToArray();
                var gameObjectNameById = gameObjects.ToDictionary(
                    document => document.LocalId,
                    document => document.Name ?? "(unnamed)",
                    StringComparer.Ordinal);

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

                foreach (var document in documents)
                {
                    context.AddUnityObject(new UnityObjectInfo
                    {
                        AssetPath = file.FullPath,
                        AssetKind = file.Kind.ToString(),
                        LocalId = document.LocalId,
                        ClassId = document.ClassId,
                        TypeName = document.TypeName,
                        Name = document.Name,
                        GameObjectLocalId = document.GameObjectLocalId
                    });

                    if (document.TypeName == "GameObject")
                    {
                        context.AddUnityGameObject(new UnityGameObjectInfo
                        {
                            AssetPath = file.FullPath,
                            LocalId = document.LocalId,
                            Name = document.Name ?? "(unnamed)",
                            ComponentLocalIds = document.ComponentLocalIds
                        });
                    }
                    else if (document.GameObjectLocalId is not null || document.ScriptGuid is not null)
                    {
                        context.AddUnityComponent(new UnityComponentInfo
                        {
                            AssetPath = file.FullPath,
                            LocalId = document.LocalId,
                            TypeName = document.TypeName,
                            GameObjectLocalId = document.GameObjectLocalId,
                            GameObjectName = document.GameObjectLocalId is not null
                                && gameObjectNameById.TryGetValue(document.GameObjectLocalId, out var gameObjectName)
                                    ? gameObjectName
                                    : null,
                            ScriptGuid = document.ScriptGuid,
                            ScriptFileId = document.ScriptFileId,
                            ResolvedScriptPath = document.ScriptGuid is not null
                                && guidToAssetPath.TryGetValue(document.ScriptGuid, out var scriptPath)
                                    ? scriptPath
                                    : null,
                            ResolvedType = document.ScriptGuid is not null
                                && guidToAssetPath.TryGetValue(document.ScriptGuid, out var resolved)
                                && resolved.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                                    ? Path.GetFileNameWithoutExtension(resolved)
                                    : null
                        });
                    }

                    foreach (var assetReference in ParseAssetReferences(file, document, guidToAssetPath))
                    {
                        context.AddUnityAssetReference(assetReference);
                    }
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

    private static IEnumerable<UnityYamlDocument> ParseDocuments(ProjectFile file, string text)
    {
        var matches = DocumentHeaderRegex.Matches(text).Cast<Match>().ToArray();
        for (var index = 0; index < matches.Length; index++)
        {
            var match = matches[index];
            var start = match.Index;
            var end = index + 1 < matches.Length ? matches[index + 1].Index : text.Length;
            var content = text[start..end];
            var classId = int.Parse(match.Groups[1].Value);
            var localId = match.Groups[2].Value;
            var typeName = FindTypeName(content) ?? ClassIdToName(classId);
            var scriptMatch = ScriptRefRegex.Match(content);

            yield return new UnityYamlDocument(
                LocalId: localId,
                ClassId: classId,
                TypeName: typeName,
                Content: content,
                Name: ReadName(content),
                GameObjectLocalId: ReadFirstGroup(GameObjectRefRegex, content),
                ComponentLocalIds: ComponentRefRegex.Matches(content)
                    .Select(componentMatch => componentMatch.Groups[1].Value.Trim())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct()
                    .ToArray(),
                ScriptGuid: scriptMatch.Success ? scriptMatch.Groups[2].Value.Trim() : null,
                ScriptFileId: scriptMatch.Success ? scriptMatch.Groups[1].Value.Trim() : null);
        }
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
        UnityYamlDocument document,
        IReadOnlyDictionary<string, string> guidToAssetPath)
    {
        foreach (Match match in ScriptRefRegex.Matches(document.Content))
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
                EditorClassIdentifier = FindNearestEditorClassIdentifier(document.Content, match.Index),
                ResolvedScriptPath = resolvedPath,
                ResolvedType = resolvedPath is not null && resolvedPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                    ? Path.GetFileNameWithoutExtension(resolvedPath)
                    : null,
                Confidence = resolvedPath is null ? "low" : "medium"
            };
        }
    }

    private static IEnumerable<UnityAssetReferenceInfo> ParseAssetReferences(
        ProjectFile file,
        UnityYamlDocument document,
        IReadOnlyDictionary<string, string> guidToAssetPath)
    {
        foreach (Match match in ObjectRefRegex.Matches(document.Content))
        {
            var field = match.Groups["field"].Value;
            var fileId = match.Groups["fileId"].Value.Trim();
            var guid = match.Groups["guid"].Success ? match.Groups["guid"].Value.Trim() : null;
            var type = match.Groups["type"].Success ? match.Groups["type"].Value.Trim() : null;

            if (field == "m_Script")
            {
                continue;
            }

            yield return new UnityAssetReferenceInfo
            {
                AssetPath = file.FullPath,
                OwnerLocalId = document.LocalId,
                OwnerType = document.TypeName,
                FieldName = field,
                FileId = fileId,
                Guid = guid,
                Type = type,
                ResolvedPath = guid is not null && guidToAssetPath.TryGetValue(guid, out var resolvedPath)
                    ? resolvedPath
                    : null,
                ReferenceKind = guid is null ? "local" : "external"
            };
        }
    }

    private static string? FindTypeName(string content)
    {
        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.TrimEnd();
            if (trimmed.EndsWith(":", StringComparison.Ordinal)
                && !trimmed.StartsWith("---", StringComparison.Ordinal)
                && !trimmed.StartsWith("  ", StringComparison.Ordinal)
                && !trimmed.StartsWith("-", StringComparison.Ordinal))
            {
                return trimmed.TrimEnd(':').Trim();
            }
        }

        return null;
    }

    private static string? ReadName(string content)
    {
        var match = NameRegex.Match(content);
        if (!match.Success)
        {
            return null;
        }

        var value = match.Groups[1].Value.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? ReadFirstGroup(Regex regex, string content)
    {
        var match = regex.Match(content);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static string ClassIdToName(int classId)
    {
        return classId switch
        {
            1 => "GameObject",
            4 => "Transform",
            20 => "Camera",
            23 => "MeshRenderer",
            33 => "MeshFilter",
            114 => "MonoBehaviour",
            115 => "MonoScript",
            128 => "Font",
            213 => "SpriteRenderer",
            224 => "RectTransform",
            225 => "CanvasGroup",
            _ => $"Class{classId}"
        };
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

    private sealed record UnityYamlDocument(
        string LocalId,
        int ClassId,
        string TypeName,
        string Content,
        string? Name,
        string? GameObjectLocalId,
        IReadOnlyList<string> ComponentLocalIds,
        string? ScriptGuid,
        string? ScriptFileId);
}
