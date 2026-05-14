using OfflineUnityAnalyzer.Analyzers.Pipeline;
using OfflineUnityAnalyzer.Core.Configuration;
using OfflineUnityAnalyzer.Core.Models;
using Xunit;

namespace OfflineUnityAnalyzer.Tests;

public sealed class UnityYamlResilienceTests
{
    [Fact]
    public static async Task MalformedUnityYamlDoesNotStopReportGeneration()
    {
        var root = CreateTempDirectory("oua-yaml-root");
        var output = CreateTempDirectory("oua-yaml-output");

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Assets"));
            Directory.CreateDirectory(Path.Combine(root, "Packages"));
            Directory.CreateDirectory(Path.Combine(root, "ProjectSettings"));
            await File.WriteAllTextAsync(
                Path.Combine(root, "Packages", "manifest.json"),
                "{\"dependencies\":{}}");
            await File.WriteAllTextAsync(
                Path.Combine(root, "Assets", "Broken.prefab"),
                """
                %YAML 1.1
                --- !u!999999999999999999999 &100
                GameObject:
                  m_Name: InvalidClassId
                """);

            var analyzer = AnalyzerPipelineFactory.CreateDefault();
            var result = await analyzer.AnalyzeAsync(new AnalyzerConfig
            {
                UnityProject = root,
                Output = output,
                StrictReadonly = true
            });

            Assert.True(File.Exists(Path.Combine(output, "report", "report.html")), "Report should still be generated.");
            Assert.Contains(result.Stages, stage => stage.Stage == AnalysisStageKind.ReportExport);
            Assert.Contains(result.Stages, stage => stage.Stage == AnalysisStageKind.UnityYamlRawIndex
                && stage.Status == AnalysisStageStatus.CompletedWithWarnings);
            Assert.DoesNotContain(result.Stages, stage => stage.Stage == AnalysisStageKind.UnityYamlRawIndex
                && stage.Status == AnalysisStageStatus.Failed);
        }
        finally
        {
            DeleteDirectory(root);
            DeleteDirectory(output);
        }
    }

    private static string CreateTempDirectory(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
