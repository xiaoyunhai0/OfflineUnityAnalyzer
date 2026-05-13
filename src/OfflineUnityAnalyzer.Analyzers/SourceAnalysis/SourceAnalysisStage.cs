using System.Text.RegularExpressions;
using OfflineUnityAnalyzer.Core.Models;
using OfflineUnityAnalyzer.Core.Pipeline;

namespace OfflineUnityAnalyzer.Analyzers.SourceAnalysis;

public sealed class SourceAnalysisStage : IAnalyzerStage
{
    private static readonly Regex NamespaceRegex = new(@"^\s*namespace\s+([A-Za-z_][\w.]*)", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex TypeRegex = new(@"^\s*(?:\[.*\]\s*)*(?:(?:public|internal|private|protected|sealed|abstract|static|partial)\s+)*(class|interface|struct|enum|record)\s+([A-Za-z_]\w*)\s*(?::\s*([^{;\r\n]+))?", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex MemberRegex = new(@"^\s*(?:(public|private|protected|internal)\s+)?(?:(static|readonly|virtual|override|sealed|async)\s+)*([A-Za-z_][\w<>,\s\[\].?]*)\s+([A-Za-z_]\w*)\s*(\(|[;=])", RegexOptions.Compiled | RegexOptions.Multiline);

    public AnalysisStageKind Kind => AnalysisStageKind.SourceSyntaxIndex;

    public Task<AnalysisStageResult> RunAsync(AnalysisContext context, CancellationToken cancellationToken)
    {
        var parsed = 0;
        var warnings = 0;

        foreach (var file in context.Files.Where(file => file.Kind == ProjectFileKind.CSharpSource))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var text = context.FileSystem.ReadAllText(file.FullPath);
                foreach (var type in ParseTypes(file, text))
                {
                    context.AddSourceType(type);
                }

                parsed++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                context.AddWarning($"Failed to parse C# source '{file.RelativePath}': {exception.Message}");
                warnings++;
            }
        }

        var status = warnings == 0
            ? AnalysisStageStatus.Completed
            : AnalysisStageStatus.CompletedWithWarnings;

        return Task.FromResult(new AnalysisStageResult(
            Kind,
            status,
            $"Parsed {parsed} C# source files and found {context.SourceTypes.Count} types.",
            WarningCount: warnings));
    }

    private static IEnumerable<SourceTypeInfo> ParseTypes(ProjectFile file, string text)
    {
        var namespaceName = NamespaceRegex.Match(text) is { Success: true } namespaceMatch
            ? namespaceMatch.Groups[1].Value
            : string.Empty;

        foreach (Match match in TypeRegex.Matches(text))
        {
            var kind = match.Groups[1].Value;
            var name = match.Groups[2].Value;
            var baseList = SplitBaseList(match.Groups[3].Value);
            var members = ParseMembers(text).ToArray();
            var fullName = string.IsNullOrWhiteSpace(namespaceName)
                ? name
                : $"{namespaceName}.{name}";

            yield return new SourceTypeInfo
            {
                FullName = fullName,
                Namespace = namespaceName,
                Name = name,
                Kind = kind,
                SourceFile = file.FullPath,
                AssemblyName = InferAssemblyName(file),
                BaseTypes = baseList,
                Interfaces = baseList.Where(item => item.StartsWith("I", StringComparison.Ordinal)).ToArray(),
                Members = members,
                IsMonoBehaviour = baseList.Any(IsMonoBehaviourName) || text.Contains(": MonoBehaviour", StringComparison.Ordinal),
                IsScriptableObject = baseList.Any(item => item.Contains("ScriptableObject", StringComparison.Ordinal)),
                IsEditorType = file.RelativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Any(part => part.Equals("Editor", StringComparison.OrdinalIgnoreCase))
                    || text.Contains("UnityEditor", StringComparison.Ordinal)
            };
        }
    }

    private static IReadOnlyList<string> SplitBaseList(string baseList)
    {
        if (string.IsNullOrWhiteSpace(baseList))
        {
            return Array.Empty<string>();
        }

        return baseList
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => item.Split('<')[0].Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
    }

    private static IEnumerable<SourceMemberInfo> ParseMembers(string text)
    {
        foreach (Match match in MemberRegex.Matches(text))
        {
            var marker = match.Groups[5].Value;
            var memberKind = marker == "(" ? "method" : "field";
            var visibility = string.IsNullOrWhiteSpace(match.Groups[1].Value)
                ? "private"
                : match.Groups[1].Value;
            var typeName = Regex.Replace(match.Groups[3].Value, @"\s+", " ").Trim();
            var name = match.Groups[4].Value;

            if (name is "if" or "for" or "foreach" or "while" or "switch" or "catch" or "using")
            {
                continue;
            }

            yield return new SourceMemberInfo
            {
                Name = name,
                Kind = memberKind,
                Signature = $"{visibility} {typeName} {name}",
                Visibility = visibility,
                IsSerializedField = IsSerializedField(text, match.Index, visibility, memberKind)
            };
        }
    }

    private static bool IsSerializedField(string text, int memberIndex, string visibility, string memberKind)
    {
        if (memberKind != "field")
        {
            return false;
        }

        if (visibility == "public")
        {
            return true;
        }

        var prefixStart = Math.Max(0, memberIndex - 160);
        var prefix = text[prefixStart..memberIndex];
        return prefix.Contains("[SerializeField]", StringComparison.Ordinal)
            || prefix.Contains("[field: SerializeField]", StringComparison.Ordinal);
    }

    private static bool IsMonoBehaviourName(string name)
    {
        return name.Contains("MonoBehaviour", StringComparison.Ordinal)
            || name.EndsWith("Behaviour", StringComparison.Ordinal);
    }

    private static string InferAssemblyName(ProjectFile file)
    {
        var parts = file.RelativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(part => part.Equals("Editor", StringComparison.OrdinalIgnoreCase))
            ? "Assembly-CSharp-Editor"
            : "Assembly-CSharp";
    }
}
