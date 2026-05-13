using OfflineUnityAnalyzer.Core.Configuration;
using OfflineUnityAnalyzer.Core.Models;
using OfflineUnityAnalyzer.Core.Safety;

namespace OfflineUnityAnalyzer.Core.Pipeline;

public sealed class ProjectAnalyzer
{
    private readonly IReadOnlyList<IAnalyzerStage> _stages;

    public ProjectAnalyzer(IEnumerable<IAnalyzerStage> stages)
    {
        _stages = stages.ToArray();
    }

    public async Task<AnalyzeResult> AnalyzeAsync(
        AnalyzerConfig config,
        IProgressReporter? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        var startedAt = DateTime.UtcNow;
        var reporter = progressReporter ?? NullProgressReporter.Instance;
        var stageResults = new List<AnalysisStageResult>();
        var pathGuard = PathGuard.Create(config);
        var fileSystem = new SafeFileSystem(pathGuard);
        var context = new AnalysisContext(config, pathGuard, fileSystem);

        fileSystem.EnsureOutputRootExists();

        foreach (var stage in _stages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            reporter.StageStarted(stage.Kind, $"Starting {stage.Kind}");

            try
            {
                var result = await stage.RunAsync(context, cancellationToken);
                stageResults.Add(result);
                reporter.StageFinished(result);

                if (result.Status == AnalysisStageStatus.Failed)
                {
                    context.AddError(result.Message);
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                var result = new AnalysisStageResult(stage.Kind, AnalysisStageStatus.Cancelled, "Analysis cancelled.");
                stageResults.Add(result);
                reporter.StageFinished(result);
                throw;
            }
            catch (Exception exception)
            {
                var result = new AnalysisStageResult(
                    stage.Kind,
                    AnalysisStageStatus.Failed,
                    exception.Message,
                    ErrorCount: 1);

                stageResults.Add(result);
                context.AddError(exception.Message);
                reporter.StageFinished(result);
                break;
            }
        }

        return new AnalyzeResult
        {
            StartedAtUtc = startedAt,
            FinishedAtUtc = DateTime.UtcNow,
            OutputRoot = pathGuard.OutputRoot,
            FileCount = context.Files.Count,
            Stages = stageResults,
            Warnings = context.Warnings,
            Errors = context.Errors
        };
    }
}
