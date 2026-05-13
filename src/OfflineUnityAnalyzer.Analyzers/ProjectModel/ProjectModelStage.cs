using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using OfflineUnityAnalyzer.Core.Models;
using OfflineUnityAnalyzer.Core.Pipeline;

namespace OfflineUnityAnalyzer.Analyzers.ProjectModel;

public sealed class ProjectModelStage : IAnalyzerStage
{
    private static readonly Regex SolutionProjectRegex = new(@"Project\(""\{[^}]+\}""\)\s*=\s*""[^""]+"",\s*""([^""]+\.csproj)""", RegexOptions.Compiled);

    public AnalysisStageKind Kind => AnalysisStageKind.ProjectModel;

    public Task<AnalysisStageResult> RunAsync(AnalysisContext context, CancellationToken cancellationToken)
    {
        var solutions = new List<SolutionInfo>();
        var csharpProjects = new List<CSharpProjectInfo>();
        var asmdefs = new List<AssemblyDefinitionInfo>();
        var packages = new List<PackageInfo>();
        var warnings = 0;

        foreach (var file in context.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                switch (file.Kind)
                {
                    case ProjectFileKind.Solution:
                        solutions.Add(ParseSolution(context, file));
                        break;
                    case ProjectFileKind.CSharpProject:
                        csharpProjects.Add(ParseCSharpProject(context, file));
                        break;
                    case ProjectFileKind.AssemblyDefinition:
                        asmdefs.Add(ParseAssemblyDefinition(context, file));
                        break;
                    case ProjectFileKind.PackageManifest:
                        packages.AddRange(ParsePackageManifest(context, file));
                        break;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or System.Xml.XmlException)
            {
                warnings++;
                context.AddWarning($"Failed to parse project model file '{file.RelativePath}': {exception.Message}");
                context.AddDiagnostic(new DiagnosticInfo
                {
                    Severity = "warning",
                    Category = "project-model",
                    Message = $"Failed to parse {file.Kind}: {exception.Message}",
                    Path = file.FullPath
                });
            }
        }

        context.SetProjectModel(new ProjectModelInfo
        {
            Solutions = solutions.OrderBy(item => item.Path).ToArray(),
            CSharpProjects = csharpProjects.OrderBy(item => item.Path).ToArray(),
            AssemblyDefinitions = asmdefs.OrderBy(item => item.Path).ToArray(),
            Packages = packages.OrderBy(item => item.Name).ToArray()
        });

        var status = warnings == 0
            ? AnalysisStageStatus.Completed
            : AnalysisStageStatus.CompletedWithWarnings;

        return Task.FromResult(new AnalysisStageResult(
            Kind,
            status,
            $"Parsed project model: {solutions.Count} solutions, {csharpProjects.Count} csproj, {asmdefs.Count} asmdef, {packages.Count} packages.",
            WarningCount: warnings));
    }

    private static SolutionInfo ParseSolution(AnalysisContext context, ProjectFile file)
    {
        var text = context.FileSystem.ReadAllText(file.FullPath);
        var baseDirectory = Path.GetDirectoryName(file.FullPath) ?? file.RootPath;
        var projectPaths = SolutionProjectRegex
            .Matches(text)
            .Select(match => Path.GetFullPath(Path.Combine(baseDirectory, match.Groups[1].Value.Replace('\\', Path.DirectorySeparatorChar))))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path)
            .ToArray();

        return new SolutionInfo
        {
            Path = file.FullPath,
            ProjectPaths = projectPaths
        };
    }

    private static CSharpProjectInfo ParseCSharpProject(AnalysisContext context, ProjectFile file)
    {
        var text = context.FileSystem.ReadAllText(file.FullPath);
        var document = XDocument.Parse(text);
        var root = document.Root;
        var name = Path.GetFileNameWithoutExtension(file.FullPath);
        var assemblyName = FindElements(root, "AssemblyName").Select(item => item.Value.Trim()).FirstOrDefault();
        var constants = FindElements(root, "DefineConstants")
            .SelectMany(item => item.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item)
            .ToArray();
        var projectReferences = FindElements(root, "ProjectReference")
            .Select(item => item.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file.FullPath) ?? file.RootPath, value!.Replace('\\', Path.DirectorySeparatorChar))))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path)
            .ToArray();
        var packageReferences = FindElements(root, "PackageReference")
            .Select(item => item.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value)
            .ToArray();

        return new CSharpProjectInfo
        {
            Path = file.FullPath,
            Name = name,
            AssemblyName = string.IsNullOrWhiteSpace(assemblyName) ? name : assemblyName,
            ProjectReferences = projectReferences,
            PackageReferences = packageReferences!,
            DefineConstants = constants
        };
    }

    private static AssemblyDefinitionInfo ParseAssemblyDefinition(AnalysisContext context, ProjectFile file)
    {
        using var document = JsonDocument.Parse(context.FileSystem.ReadAllText(file.FullPath));
        var root = document.RootElement;
        var name = root.TryGetProperty("name", out var nameProperty)
            ? nameProperty.GetString()
            : Path.GetFileNameWithoutExtension(file.FullPath);

        return new AssemblyDefinitionInfo
        {
            Path = file.FullPath,
            Name = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(file.FullPath) : name,
            References = ReadStringArray(root, "references"),
            IncludePlatforms = ReadStringArray(root, "includePlatforms"),
            ExcludePlatforms = ReadStringArray(root, "excludePlatforms"),
            DefineConstraints = ReadStringArray(root, "defineConstraints")
        };
    }

    private static IEnumerable<PackageInfo> ParsePackageManifest(AnalysisContext context, ProjectFile file)
    {
        using var document = JsonDocument.Parse(context.FileSystem.ReadAllText(file.FullPath));
        if (!document.RootElement.TryGetProperty("dependencies", out var dependencies)
            || dependencies.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var dependency in dependencies.EnumerateObject())
        {
            var value = dependency.Value.GetString() ?? string.Empty;
            yield return new PackageInfo
            {
                Name = dependency.Name,
                VersionOrSource = value,
                Source = InferPackageSource(value),
                Path = file.FullPath
            };
        }
    }

    private static IEnumerable<XElement> FindElements(XElement? root, string localName)
    {
        if (root is null)
        {
            return Array.Empty<XElement>();
        }

        return root.Descendants().Where(element => element.Name.LocalName == localName);
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return property
            .EnumerateArray()
            .Select(item => item.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value)
            .ToArray();
    }

    private static string InferPackageSource(string versionOrSource)
    {
        if (versionOrSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            return "local";
        }

        if (versionOrSource.StartsWith("http:", StringComparison.OrdinalIgnoreCase)
            || versionOrSource.StartsWith("https:", StringComparison.OrdinalIgnoreCase)
            || versionOrSource.Contains(".git", StringComparison.OrdinalIgnoreCase))
        {
            return "git-or-url";
        }

        return "registry";
    }
}
