using OfflineUnityAnalyzer.Core.Models;
using OfflineUnityAnalyzer.Core.Pipeline;

namespace OfflineUnityAnalyzer.Analyzers.SourceAnalysis;

public sealed class CodeAssemblyBridgeStage : IAnalyzerStage
{
    public AnalysisStageKind Kind => AnalysisStageKind.TypeMerge;

    public Task<AnalysisStageResult> RunAsync(AnalysisContext context, CancellationToken cancellationToken)
    {
        var typeToAssemblies = BuildTypeAssemblyMap(context);
        var assemblyNameToAssemblies = context.Assemblies
            .Where(assembly => !string.IsNullOrWhiteSpace(assembly.AssemblyName))
            .GroupBy(assembly => NormalizeAssemblyName(assembly.AssemblyName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var bridges = new List<CodeAssemblyBridgeInfo>();

        foreach (var sourceType in context.SourceTypes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (typeToAssemblies.TryGetValue(sourceType.FullName, out var exactAssemblies))
            {
                foreach (var assembly in exactAssemblies)
                {
                    bridges.Add(CreateBridge(sourceType, assembly, "full-type", "high"));
                }
            }
            else if (typeToAssemblies.TryGetValue(sourceType.Name, out var shortAssemblies))
            {
                foreach (var assembly in shortAssemblies)
                {
                    bridges.Add(CreateBridge(sourceType, assembly, "short-type", "medium"));
                }
            }
            else if (assemblyNameToAssemblies.TryGetValue(NormalizeAssemblyName(sourceType.AssemblyName), out var namedAssemblies))
            {
                foreach (var assembly in namedAssemblies)
                {
                    bridges.Add(CreateBridge(sourceType, assembly, "assembly-name", "medium"));
                }
            }
        }

        foreach (var bridge in bridges
            .DistinctBy(bridge => $"{bridge.SourceType}|{bridge.AssemblyPath}|{bridge.MatchKind}")
            .OrderBy(bridge => bridge.SourceType, StringComparer.Ordinal)
            .ThenBy(bridge => bridge.AssemblyName, StringComparer.OrdinalIgnoreCase))
        {
            context.AddCodeAssemblyBridge(bridge);
        }

        AddDiagnostics(context);

        return Task.FromResult(new AnalysisStageResult(
            Kind,
            AnalysisStageStatus.Completed,
            $"Linked {context.CodeAssemblyBridges.Count} source types to compiled assemblies."));
    }

    private static Dictionary<string, AssemblyMetadataInfo[]> BuildTypeAssemblyMap(AnalysisContext context)
    {
        var pairs = context.Assemblies
            .SelectMany(assembly => assembly.TypeNames.Select(type => new { Type = type, Assembly = assembly })
                .Concat(assembly.TypeNames.Select(type => new { Type = ShortName(type), Assembly = assembly })))
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Type))
            .GroupBy(pair => pair.Type, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(pair => pair.Assembly).DistinctBy(assembly => assembly.Path).ToArray(),
                StringComparer.Ordinal);

        return pairs;
    }

    private static CodeAssemblyBridgeInfo CreateBridge(
        SourceTypeInfo sourceType,
        AssemblyMetadataInfo assembly,
        string matchKind,
        string confidence)
    {
        return new CodeAssemblyBridgeInfo
        {
            SourceType = sourceType.FullName,
            SourceFile = sourceType.SourceFile,
            SourceAssemblyName = sourceType.AssemblyName,
            AssemblyPath = assembly.Path,
            AssemblyName = assembly.AssemblyName,
            AssemblyKind = assembly.Kind,
            MatchKind = matchKind,
            Confidence = confidence
        };
    }

    private static string NormalizeAssemblyName(string value)
    {
        var name = Path.GetFileNameWithoutExtension(value);
        if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            name = Path.GetFileNameWithoutExtension(name);
        }

        return name;
    }

    private static string ShortName(string value)
    {
        var index = value.LastIndexOf('.');
        return index >= 0 ? value[(index + 1)..] : value;
    }

    private static void AddDiagnostics(AnalysisContext context)
    {
        if (context.SourceTypes.Count > 0
            && context.Assemblies.Any(assembly => assembly.TypeNames.Count > 0)
            && context.CodeAssemblyBridges.Count == 0)
        {
            context.AddDiagnostic(new DiagnosticInfo
            {
                Severity = "info",
                Category = "code-assembly",
                Message = "Source roots and DLL roots were both indexed, but no source type matched compiled assembly metadata. Check whether the source directory belongs to the same build as the Unity project DLLs."
            });
        }

        var hotBridges = context.CodeAssemblyBridges.Count(bridge => bridge.AssemblyKind == "hot_update");
        if (hotBridges > 0)
        {
            context.AddDiagnostic(new DiagnosticInfo
            {
                Severity = "info",
                Category = "hybridclr",
                Message = $"Linked {hotBridges} source types to hot-update assemblies."
            });
        }
    }
}
