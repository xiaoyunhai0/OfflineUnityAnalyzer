using System.Text.RegularExpressions;
using System.Xml.Linq;
using OfflineUnityAnalyzer.Core.Models;
using OfflineUnityAnalyzer.Core.Pipeline;

namespace OfflineUnityAnalyzer.Analyzers.ConfigAnalysis;

public sealed class ConfigReferenceStage : IAnalyzerStage
{
    private static readonly Regex GuidRegex = new(@"\b[a-fA-F0-9]{32}\b", RegexOptions.Compiled);
    private static readonly Regex AssetLikeRegex = new(@"[A-Za-z0-9_\-/]+(?:\.prefab|\.unity|\.asset|\.mat|\.controller|\.bytes|\.dll\.bytes|\.png|\.jpg|\.wav|\.mp3)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TypeLikeRegex = new(@"\b[A-Z][A-Za-z0-9_]+(?:\.[A-Z][A-Za-z0-9_]+)+\b", RegexOptions.Compiled);

    public AnalysisStageKind Kind => AnalysisStageKind.ConfigShallowIndex;

    public Task<AnalysisStageResult> RunAsync(AnalysisContext context, CancellationToken cancellationToken)
    {
        var scanned = 0;
        var warnings = 0;

        foreach (var file in context.Files.Where(IsConfigCandidate))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var text = context.FileSystem.ReadAllText(file.FullPath);
                if (file.Kind == ProjectFileKind.Xml)
                {
                    ExtractXmlReferences(context, file, text);
                }
                else
                {
                    ExtractLineReferences(context, file, text);
                }

                scanned++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException)
            {
                warnings++;
                context.AddWarning($"Failed to scan config references '{file.RelativePath}': {exception.Message}");
            }
        }

        var status = warnings == 0
            ? AnalysisStageStatus.Completed
            : AnalysisStageStatus.CompletedWithWarnings;

        return Task.FromResult(new AnalysisStageResult(
            Kind,
            status,
            $"Scanned {scanned} config-like files and found {context.ConfigReferences.Count} references.",
            WarningCount: warnings));
    }

    private static bool IsConfigCandidate(ProjectFile file)
    {
        if (file.Kind is not (ProjectFileKind.Json or ProjectFileKind.Csv or ProjectFileKind.Xml or ProjectFileKind.TextBytes))
        {
            return false;
        }

        var path = file.FullPath.Replace('\\', '/');
        return path.Contains("/Config", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/GameConfig", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/StreamingAssets", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/Yoo", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(file.FullPath).Contains("config", StringComparison.OrdinalIgnoreCase)
            || file.Kind == ProjectFileKind.TextBytes;
    }

    private static void ExtractLineReferences(AnalysisContext context, ProjectFile file, string text)
    {
        var lineNumber = 0;
        foreach (var line in text.Split('\n'))
        {
            lineNumber++;
            ExtractMatches(context, file.FullPath, $"line {lineNumber}", GuessFieldName(line), line);
        }
    }

    private static void ExtractXmlReferences(AnalysisContext context, ProjectFile file, string text)
    {
        var document = XDocument.Parse(text);
        foreach (var element in document.Descendants())
        {
            ExtractMatches(context, file.FullPath, element.Name.LocalName, element.Name.LocalName, element.Value);
            foreach (var attribute in element.Attributes())
            {
                ExtractMatches(context, file.FullPath, $"{element.Name.LocalName}@{attribute.Name.LocalName}", attribute.Name.LocalName, attribute.Value);
            }
        }
    }

    private static void ExtractMatches(
        AnalysisContext context,
        string sourcePath,
        string location,
        string fieldName,
        string text)
    {
        foreach (Match match in GuidRegex.Matches(text))
        {
            Add(context, sourcePath, location, fieldName, match.Value, "guid", "high");
        }

        foreach (Match match in AssetLikeRegex.Matches(text))
        {
            Add(context, sourcePath, location, fieldName, match.Value, "asset_address", "high");
        }

        foreach (Match match in TypeLikeRegex.Matches(text))
        {
            Add(context, sourcePath, location, fieldName, match.Value, "type_name", "medium");
        }
    }

    private static string GuessFieldName(string line)
    {
        var colon = line.IndexOf(':');
        if (colon > 0 && colon < 80)
        {
            return line[..colon].Trim().Trim('"');
        }

        var comma = line.IndexOf(',');
        if (comma > 0 && comma < 80)
        {
            return "csv-field";
        }

        return "value";
    }

    private static void Add(
        AnalysisContext context,
        string sourcePath,
        string location,
        string fieldName,
        string value,
        string matchKind,
        string confidence)
    {
        context.AddConfigReference(new ConfigReferenceInfo
        {
            SourcePath = sourcePath,
            Location = location,
            FieldName = fieldName,
            Value = value,
            MatchKind = matchKind,
            Confidence = confidence
        });
    }
}
