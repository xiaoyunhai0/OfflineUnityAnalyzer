using System.Text.Json;
using OfflineUnityAnalyzer.Analyzers.Pipeline;
using OfflineUnityAnalyzer.Core.Configuration;
using OfflineUnityAnalyzer.Core.Models;
using Xunit;

namespace OfflineUnityAnalyzer.Tests;

public sealed class SplitSourceUnityProjectTests
{
    [Fact]
    public static async Task UnityRootOnlyAutoDiscoversSiblingSourceAndLinksCompiledDll()
    {
        var workspace = CreateTempDirectory("oua-split-workspace");
        var output = CreateTempDirectory("oua-split-output");

        try
        {
            var sourceRoot = Path.Combine(workspace, "GameCode");
            var unityRoot = Path.Combine(workspace, "UnityProject");
            Directory.CreateDirectory(Path.Combine(sourceRoot, "Game.Client"));
            Directory.CreateDirectory(Path.Combine(unityRoot, "Assets", "Plugins", "HotUpdate"));
            Directory.CreateDirectory(Path.Combine(unityRoot, "Packages"));
            Directory.CreateDirectory(Path.Combine(unityRoot, "ProjectSettings"));

            await File.WriteAllTextAsync(Path.Combine(unityRoot, "Packages", "manifest.json"), "{\"dependencies\":{}}");
            await File.WriteAllTextAsync(
                Path.Combine(sourceRoot, "Game.Client", "Game.Client.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <AssemblyName>Game.Client</AssemblyName>
                  </PropertyGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(
                Path.Combine(sourceRoot, "Game.Client", "PlayerController.cs"),
                """
                namespace Game.Client
                {
                    public class PlayerController : UnityEngine.MonoBehaviour
                    {
                    }
                }
                """);
            await File.WriteAllTextAsync(
                Path.Combine(sourceRoot, "Game.Client", "UnityEngineStubs.cs"),
                """
                namespace UnityEngine
                {
                    public class MonoBehaviour { }
                }
                """);

            await BuildDllAsync(sourceRoot, Path.Combine(unityRoot, "Assets", "Plugins", "HotUpdate", "Game.Client.dll"));

            var analyzer = AnalyzerPipelineFactory.CreateDefault();
            var result = await analyzer.AnalyzeAsync(new AnalyzerConfig
            {
                UnityProject = unityRoot,
                Output = output,
                StrictReadonly = true
            });

            Assert.False(result.HasErrors);
            Assert.Contains(result.Stages, stage => stage.Stage == AnalysisStageKind.TypeMerge
                && stage.Status == AnalysisStageStatus.Completed);

            var typesJson = await File.ReadAllTextAsync(Path.Combine(output, "data", "types.json"));
            Assert.Contains("Game.Client.PlayerController", typesJson);

            using var summary = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output, "data", "summary.json")));
            Assert.True(summary.RootElement.GetProperty("codeAssemblyBridgeCount").GetInt32() >= 1);
            Assert.True(summary.RootElement.GetProperty("hotUpdateBridgeCount").GetInt32() >= 1);

            var bridgesJson = await File.ReadAllTextAsync(Path.Combine(output, "data", "code-assembly-bridges.json"));
            Assert.Contains("Game.Client.PlayerController", bridgesJson);
            Assert.Contains("Game.Client.dll", bridgesJson);
        }
        finally
        {
            DeleteDirectory(workspace);
            DeleteDirectory(output);
        }
    }

    private static async Task BuildDllAsync(string sourceRoot, string destination)
    {
        var project = Path.Combine(sourceRoot, "Game.Client", "Game.Client.csproj");
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "dotnet",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        process.StartInfo.ArgumentList.Add("build");
        process.StartInfo.ArgumentList.Add(project);
        process.StartInfo.ArgumentList.Add("-c");
        process.StartInfo.ArgumentList.Add("Release");
        process.StartInfo.ArgumentList.Add("-o");
        process.StartInfo.ArgumentList.Add(Path.GetDirectoryName(destination)!);
        process.Start();
        await process.WaitForExitAsync();
        Assert.Equal(0, process.ExitCode);
        Assert.True(File.Exists(destination), $"Expected compiled DLL at {destination}");
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
