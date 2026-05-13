using OfflineUnityAnalyzer.Core.Models;
using OfflineUnityAnalyzer.Core.Pipeline;

namespace OfflineUnityAnalyzer.Analyzers.HotUpdate;

public sealed class HybridClrAnalysisStage : IAnalyzerStage
{
    public AnalysisStageKind Kind => AnalysisStageKind.HybridClrIndex;

    public Task<AnalysisStageResult> RunAsync(AnalysisContext context, CancellationToken cancellationToken)
    {
        var evidence = new List<string>();

        foreach (var file in context.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = file.FullPath.Replace('\\', '/');

            if (path.Contains("HybridCLR", StringComparison.OrdinalIgnoreCase)
                || path.Contains("AOTGenericReferences.cs", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".dll.bytes", StringComparison.OrdinalIgnoreCase))
            {
                evidence.Add(file.FullPath);
            }
        }

        foreach (var type in context.SourceTypes)
        {
            if (type.Members.Any(member => member.Signature.Contains("LoadMetadataForAOTAssembly", StringComparison.OrdinalIgnoreCase)))
            {
                evidence.Add(type.SourceFile);
            }
        }

        var hotUpdate = context.Assemblies
            .Where(assembly => assembly.Kind == "hot_update")
            .Select(assembly => assembly.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var aot = context.Assemblies
            .Where(assembly => assembly.Kind == "aot_metadata")
            .Select(assembly => assembly.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var detected = evidence.Any(path =>
                path.Contains("Assets/HybridCLRData", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".dll.bytes", StringComparison.OrdinalIgnoreCase)
                || path.Contains("AOTGenericReferences.cs", StringComparison.OrdinalIgnoreCase)
                || path.Contains("link.xml", StringComparison.OrdinalIgnoreCase))
            || hotUpdate.Length > 0
            || aot.Length > 0;

        context.SetHybridClr(new HybridClrInfo
        {
            Detected = detected,
            Evidence = evidence.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path).ToArray(),
            HotUpdateAssemblies = hotUpdate,
            AotMetadataAssemblies = aot
        });

        return Task.FromResult(new AnalysisStageResult(
            Kind,
            AnalysisStageStatus.Completed,
            context.HybridClr.Detected
                ? $"HybridCLR evidence found: {context.HybridClr.Evidence.Count} items."
                : "HybridCLR evidence not found."));
    }
}
