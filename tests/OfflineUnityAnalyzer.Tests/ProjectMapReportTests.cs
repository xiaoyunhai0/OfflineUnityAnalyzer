using System.Text.Json;
using OfflineUnityAnalyzer.Analyzers.Pipeline;
using OfflineUnityAnalyzer.Core.Configuration;
using OfflineUnityAnalyzer.Core.Models;
using Xunit;

namespace OfflineUnityAnalyzer.Tests;

public sealed class ProjectMapReportTests
{
    [Fact]
    public static async Task UnityRootOnlyScansProjectCodeAndExportsRelationships()
    {
        var root = CreateTempDirectory("oua-project-map-root");
        var output = CreateTempDirectory("oua-project-map-output");

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Assets", "Scripts"));
            Directory.CreateDirectory(Path.Combine(root, "Packages", "com.company.tool", "Runtime"));
            Directory.CreateDirectory(Path.Combine(root, "ProjectSettings"));
            await File.WriteAllTextAsync(
                Path.Combine(root, "Packages", "manifest.json"),
                "{\"dependencies\":{\"com.company.tool\":\"file:com.company.tool\"}}");
            await File.WriteAllTextAsync(
                Path.Combine(root, "Assets", "Scripts", "PlayerController.cs"),
                """
                using UnityEngine;

                namespace Game.Client
                {
                    public class PlayerController : MonoBehaviour
                    {
                        [SerializeField] private InventoryModel inventory;

                        public void Start()
                        {
                            inventory.Open();
                        }
                    }
                }
                """);
            await File.WriteAllTextAsync(
                Path.Combine(root, "Assets", "Scripts", "InventoryModel.cs"),
                """
                namespace Game.Client
                {
                    public sealed class InventoryModel
                    {
                        public void Open() { }
                    }
                }
                """);
            await File.WriteAllTextAsync(
                Path.Combine(root, "Packages", "com.company.tool", "Runtime", "PackageTool.cs"),
                """
                namespace Company.Tool
                {
                    public class PackageTool { }
                }
                """);
            await File.WriteAllTextAsync(
                Path.Combine(root, "ProjectSettings", "ProjectVersion.txt"),
                "m_EditorVersion: 2022.3.0f1");

            var analyzer = AnalyzerPipelineFactory.CreateDefault();
            var result = await analyzer.AnalyzeAsync(new AnalyzerConfig
            {
                UnityProject = root,
                Output = output,
                StrictReadonly = true
            });

            Assert.False(result.HasErrors);
            Assert.True(File.Exists(Path.Combine(output, "report", "report.html")));

            using var summary = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output, "data", "summary.json")));
            var rootElement = summary.RootElement;
            Assert.True(rootElement.GetProperty("sourceTypeCount").GetInt32() >= 3);
            Assert.True(rootElement.GetProperty("sourceRelationCount").GetInt32() >= 2);
            Assert.True(rootElement.GetProperty("resolvedSourceRelationCount").GetInt32() >= 1);

            var html = await File.ReadAllTextAsync(Path.Combine(output, "report", "report.html"));
            Assert.Contains("Project Map", html);
            Assert.Contains("Code Relations", html);
            Assert.Contains("Unity Bindings", html);

            var relationsJson = await File.ReadAllTextAsync(Path.Combine(output, "data", "source-relations.json"));
            Assert.Contains("serialized-field", relationsJson);
            Assert.Contains("Game.Client.InventoryModel", relationsJson);
            Assert.Contains("Company.Tool.PackageTool", await File.ReadAllTextAsync(Path.Combine(output, "data", "types.json")));
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
