using OfflineUnityAnalyzer.Analyzers.Pipeline;
using OfflineUnityAnalyzer.Core.Configuration;
using OfflineUnityAnalyzer.Core.Models;
using OfflineUnityAnalyzer.Core.Pipeline;
using OfflineUnityAnalyzer.Core.Safety;
using OfflineUnityAnalyzer.ViewerServer;
using Microsoft.Extensions.Hosting;

namespace OfflineUnityAnalyzer.Cli
{
    public static class Program
    {
        public static Task<int> Main(string[] args)
        {
            return CliProgram.RunAsync(args);
        }
    }

    internal static class CliProgram
    {
        public static async Task<int> RunAsync(string[] args)
        {
            if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
            {
                PrintHelp();
                return 0;
            }

            var command = args[0].StartsWith("--", StringComparison.Ordinal)
                ? "analyze"
                : args[0].ToLowerInvariant();

            var commandArgs = command == "analyze" && args[0].StartsWith("--", StringComparison.Ordinal)
                ? args
                : args.Skip(1).ToArray();

            try
            {
                return command switch
                {
                    "analyze" => await AnalyzeAsync(commandArgs),
                    "serve" => await ServeAsync(commandArgs),
                    "diff" => PlannedCommand("diff"),
                    "export" => PlannedCommand("export"),
                    _ => UnknownCommand(command)
                };
            }
            catch (ReadonlyViolationException exception)
            {
                Console.Error.WriteLine($"Readonly policy violation: {exception.Message}");
                return 3;
            }
            catch (ArgumentException exception)
            {
                Console.Error.WriteLine($"Argument error: {exception.Message}");
                return 1;
            }
            catch (InputPathException exception)
            {
                Console.Error.WriteLine($"Input path error: {exception.Message}");
                return 2;
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine("Analysis cancelled.");
                return 4;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"Fatal error: {exception.Message}");
                return 5;
            }
        }

        private static async Task<int> AnalyzeAsync(string[] args)
        {
            var options = CommandLineOptions.Parse(args);
            var config = await BuildConfigAsync(options, CancellationToken.None);

            if (string.IsNullOrWhiteSpace(config.Output))
            {
                throw new ArgumentException("Output path is required. Use --out <path> or config.output.");
            }

            IProgressReporter reporter = options.ProgressJson
                ? new JsonProgressReporter()
                : new ConsoleProgressReporter();

            var analyzer = AnalyzerPipelineFactory.CreateDefault();
            var result = await analyzer.AnalyzeAsync(config, reporter, CancellationToken.None);

            PrintSummary(result);

            if (result.HasErrors)
            {
                return 5;
            }

            return result.HasWarnings ? 4 : 0;
        }

        private static async Task<int> ServeAsync(string[] args)
        {
            var options = CommandLineOptions.Parse(args);
            if (!options.Values.TryGetValue("out", out var outputRoot) || string.IsNullOrWhiteSpace(outputRoot))
            {
                throw new ArgumentException("serve requires --out <path>.");
            }

            var port = 0;
            if (options.Values.TryGetValue("port", out var portText)
                && !int.TryParse(portText, out port))
            {
                throw new ArgumentException("--port must be a number.");
            }

            var app = ViewerServerHost.Build(new ViewerServerOptions
            {
                OutputRoot = outputRoot,
                Port = port
            });

            await app.StartAsync();
            var address = app.Urls.FirstOrDefault() ?? "http://127.0.0.1";
            Console.WriteLine($"Viewer server listening: {address}");
            Console.WriteLine("Press Ctrl+C to stop.");
            await app.WaitForShutdownAsync();
            return 0;
        }

        private static int PlannedCommand(string command)
        {
            Console.WriteLine($"{command} is planned but not implemented in v0.1.");
            return 0;
        }

        private static int UnknownCommand(string command)
        {
            Console.Error.WriteLine($"Unknown command: {command}");
            PrintHelp();
            return 1;
        }

        private static async Task<AnalyzerConfig> BuildConfigAsync(CommandLineOptions options, CancellationToken cancellationToken)
        {
            AnalyzerConfig config = AnalyzerConfig.Empty;

            if (options.Values.TryGetValue("config", out var configPath) && !string.IsNullOrWhiteSpace(configPath))
            {
                config = await ConfigLoader.LoadAsync(configPath, cancellationToken);
            }

            var codeRoots = options.ListValues.TryGetValue("code", out var code)
                ? code
                : config.CodeRoots;

            var dllRoots = options.ListValues.TryGetValue("dll", out var dll)
                ? dll
                : config.DllRoots;

            var yooAssetRoots = options.ListValues.TryGetValue("yooasset-manifest", out var yooAsset)
                ? yooAsset
                : config.YooAssetManifestRoots;

            return config with
            {
                CodeRoots = codeRoots.ToArray(),
                DllRoots = dllRoots.ToArray(),
                YooAssetManifestRoots = yooAssetRoots.ToArray(),
                SolutionPath = options.Values.GetValueOrDefault("sln") ?? config.SolutionPath,
                UnityProject = options.Values.GetValueOrDefault("unity") ?? config.UnityProject,
                Output = options.Values.GetValueOrDefault("out") ?? config.Output,
                StrictReadonly = options.Flags.Contains("strict-readonly") || config.StrictReadonly,
                AnalyzeCallGraph = !options.Flags.Contains("no-call-graph") && config.AnalyzeCallGraph,
                Stages = ApplyStageFlags(config.Stages, options.Flags)
            };
        }

        private static StageSelection ApplyStageFlags(StageSelection stages, IReadOnlySet<string> flags)
        {
            return stages with
            {
                ProjectModel = !flags.Contains("skip-project-model") && stages.ProjectModel,
                SourceSyntaxIndex = !flags.Contains("skip-source") && stages.SourceSyntaxIndex,
                DllIndex = !flags.Contains("skip-dll") && stages.DllIndex,
                UnityYamlRawIndex = !flags.Contains("skip-unity-yaml") && stages.UnityYamlRawIndex,
                HybridClrIndex = !flags.Contains("skip-hybridclr") && stages.HybridClrIndex,
                YooAssetIndex = !flags.Contains("skip-yooasset") && stages.YooAssetIndex,
                ConfigShallowIndex = !flags.Contains("skip-config") && stages.ConfigShallowIndex,
                ModuleInference = !flags.Contains("skip-modules") && stages.ModuleInference,
                ReportExport = !flags.Contains("skip-report") && stages.ReportExport
            };
        }

        private static void PrintSummary(AnalyzeResult result)
        {
            Console.WriteLine();
            Console.WriteLine("Analysis summary");
            Console.WriteLine($"  Output: {result.OutputRoot}");
            Console.WriteLine($"  Files:  {result.FileCount}");
            Console.WriteLine($"  Report: {Path.Combine(result.OutputRoot, "report", "report.html")}");
            Console.WriteLine($"  Time:   {(result.FinishedAtUtc - result.StartedAtUtc).TotalSeconds:F1}s");

            foreach (var stage in result.Stages)
            {
                Console.WriteLine($"  {stage.Stage}: {stage.Status} - {stage.Message}");
            }
        }

        private static void PrintHelp()
        {
            Console.WriteLine("OfflineUnityAnalyzer.Cli");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  OfflineUnityAnalyzer.Cli.exe analyze --unity <path> --out <path> [options]");
            Console.WriteLine("  OfflineUnityAnalyzer.Cli.exe serve --out <path>");
            Console.WriteLine("  OfflineUnityAnalyzer.Cli.exe diff --base <oldOut> --target <newOut>");
            Console.WriteLine("  OfflineUnityAnalyzer.Cli.exe export --out <path> --export <zip>");
            Console.WriteLine();
            Console.WriteLine("Analyze options:");
            Console.WriteLine("  --config <path>             Config file path");
            Console.WriteLine("  --code <path>               C# source root; repeatable");
            Console.WriteLine("  --sln <path>                Solution path");
            Console.WriteLine("  --unity <path>              Unity project root");
            Console.WriteLine("  --dll <path>                DLL root; repeatable");
            Console.WriteLine("  --yooasset-manifest <path>  YooAsset manifest root; repeatable");
            Console.WriteLine("  --out <path>                Output directory");
            Console.WriteLine("  --strict-readonly           Enforce strict readonly policy");
            Console.WriteLine("  --skip-project-model        Skip .sln/.csproj/.asmdef/package model parsing");
            Console.WriteLine("  --skip-source               Skip C# source type/member indexing");
            Console.WriteLine("  --skip-dll                  Skip DLL and .dll.bytes metadata indexing");
            Console.WriteLine("  --skip-unity-yaml           Skip scene/prefab/asset YAML parsing");
            Console.WriteLine("  --skip-hybridclr            Skip HybridCLR evidence detection");
            Console.WriteLine("  --skip-yooasset             Skip YooAsset evidence, manifest, and code reference scan");
            Console.WriteLine("  --skip-config               Skip JSON/CSV/XML/.bytes shallow reference scan");
            Console.WriteLine("  --skip-modules              Skip module inference");
            Console.WriteLine("  --skip-report               Skip offline report export");
            Console.WriteLine("  --no-call-graph             Skip call graph stages when implemented");
            Console.WriteLine("  --progress-json             Emit machine-readable progress events");
            Console.WriteLine();
            Console.WriteLine("Serve options:");
            Console.WriteLine("  --out <path>                Analysis output directory");
            Console.WriteLine("  --port <number>             Port; 0 means random free port");
        }
    }

    internal sealed class CommandLineOptions
    {
        private static readonly HashSet<string> RepeatableOptions = new(StringComparer.OrdinalIgnoreCase)
        {
            "code",
            "dll",
            "yooasset-manifest"
        };

        private static readonly HashSet<string> KnownFlags = new(StringComparer.OrdinalIgnoreCase)
        {
            "strict-readonly",
            "no-call-graph",
            "progress-json",
            "skip-project-model",
            "skip-source",
            "skip-dll",
            "skip-unity-yaml",
            "skip-hybridclr",
            "skip-yooasset",
            "skip-config",
            "skip-modules",
            "skip-report"
        };

        public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, List<string>> ListValues { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> Flags { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool ProgressJson => Flags.Contains("progress-json");

        public static CommandLineOptions Parse(string[] args)
        {
            var options = new CommandLineOptions();

            for (var index = 0; index < args.Length; index++)
            {
                var token = args[index];
                if (!token.StartsWith("--", StringComparison.Ordinal))
                {
                    throw new ArgumentException($"Unexpected argument: {token}");
                }

                var name = token[2..];
                if (KnownFlags.Contains(name))
                {
                    options.Flags.Add(name);
                    continue;
                }

                if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new ArgumentException($"Option requires a value: --{name}");
                }

                var value = args[++index];
                if (RepeatableOptions.Contains(name))
                {
                    if (!options.ListValues.TryGetValue(name, out var values))
                    {
                        values = new List<string>();
                        options.ListValues[name] = values;
                    }

                    values.Add(value);
                }
                else
                {
                    options.Values[name] = value;
                }
            }

            return options;
        }
    }

    internal sealed class ConsoleProgressReporter : IProgressReporter
    {
        public void StageStarted(AnalysisStageKind stage, string message)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
        }

        public void StageFinished(AnalysisStageResult result)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {result.Stage}: {result.Status} - {result.Message}");
        }

        public void Message(string message)
        {
            Console.WriteLine(message);
        }
    }

    internal sealed class JsonProgressReporter : IProgressReporter
    {
        public void StageStarted(AnalysisStageKind stage, string message)
        {
            Write(new
            {
                type = "stageStarted",
                stage = stage.ToString(),
                message
            });
        }

        public void StageFinished(AnalysisStageResult result)
        {
            Write(new
            {
                type = "stageFinished",
                stage = result.Stage.ToString(),
                status = result.Status.ToString(),
                result.Message,
                result.WarningCount,
                result.ErrorCount
            });
        }

        public void Message(string message)
        {
            Write(new
            {
                type = "message",
                message
            });
        }

        private static void Write(object value)
        {
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(value));
        }
    }
}
