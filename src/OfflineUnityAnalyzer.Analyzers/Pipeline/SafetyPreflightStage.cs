using OfflineUnityAnalyzer.Core.Models;
using OfflineUnityAnalyzer.Core.Pipeline;

namespace OfflineUnityAnalyzer.Analyzers.Pipeline;

public sealed class SafetyPreflightStage : IAnalyzerStage
{
    public AnalysisStageKind Kind => AnalysisStageKind.SafetyPreflight;

    public Task<AnalysisStageResult> RunAsync(AnalysisContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var warnings = 0;

        if (context.PathGuard.InputRoots.Count == 0)
        {
            context.AddWarning("No existing input roots were registered. Analysis will only create an empty output summary.");
            warnings++;
        }

        foreach (var root in context.PathGuard.InputRoots)
        {
            if (!Directory.Exists(root.Path))
            {
                context.AddWarning($"Input root does not exist and will be skipped: {root.Path}");
                warnings++;
            }
        }

        context.FileSystem.EnsureOutputRootExists();
        context.FileSystem.WriteAllTextToOutput(
            "logs/readonly-preflight.txt",
            BuildPreflightReport(context));

        var status = warnings == 0
            ? AnalysisStageStatus.Completed
            : AnalysisStageStatus.CompletedWithWarnings;

        return Task.FromResult(new AnalysisStageResult(
            Kind,
            status,
            "Readonly preflight completed.",
            WarningCount: warnings));
    }

    private static string BuildPreflightReport(AnalysisContext context)
    {
        var lines = new List<string>
        {
            "OfflineUnityAnalyzer Readonly Preflight",
            $"Generated UTC: {DateTime.UtcNow:O}",
            $"Strict readonly: {context.Config.StrictReadonly}",
            string.Empty,
            "Input roots:"
        };

        foreach (var root in context.PathGuard.InputRoots)
        {
            lines.Add($"- {root.Label}: {root.Path}");
        }

        lines.Add(string.Empty);
        lines.Add($"Output root: {context.PathGuard.OutputRoot}");
        lines.Add(string.Empty);
        lines.Add("Policy:");
        lines.Add("- No files are created inside input roots.");
        lines.Add("- Output root must not overlap input roots.");
        lines.Add("- External project tools are not invoked by the analyzer core.");

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }
}
