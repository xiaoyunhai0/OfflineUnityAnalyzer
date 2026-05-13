using OfflineUnityAnalyzer.Core.Configuration;
using OfflineUnityAnalyzer.Core.Models;
using OfflineUnityAnalyzer.Core.Safety;

namespace OfflineUnityAnalyzer.Core.Pipeline;

public interface IAnalyzerStage
{
    AnalysisStageKind Kind { get; }

    Task<AnalysisStageResult> RunAsync(AnalysisContext context, CancellationToken cancellationToken);
}
