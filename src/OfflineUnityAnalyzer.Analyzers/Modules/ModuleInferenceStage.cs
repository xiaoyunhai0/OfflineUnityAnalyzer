using OfflineUnityAnalyzer.Core.Models;
using OfflineUnityAnalyzer.Core.Pipeline;

namespace OfflineUnityAnalyzer.Analyzers.Modules;

public sealed class ModuleInferenceStage : IAnalyzerStage
{
    public AnalysisStageKind Kind => AnalysisStageKind.ModuleInference;

    public Task<AnalysisStageResult> RunAsync(AnalysisContext context, CancellationToken cancellationToken)
    {
        var modules = new Dictionary<string, ModuleAccumulator>(StringComparer.OrdinalIgnoreCase);

        foreach (var asmdef in context.ProjectModel.AssemblyDefinitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Get(modules, CleanModuleName(asmdef.Name), "asmdef", "high")
                .AddEvidence(asmdef.Path);
        }

        foreach (var project in context.ProjectModel.CSharpProjects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Get(modules, CleanModuleName(project.AssemblyName), "csproj", "high")
                .AddEvidence(project.Path);
        }

        foreach (var assembly in context.Assemblies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Get(modules, CleanModuleName(assembly.AssemblyName), $"assembly:{assembly.Kind}", "medium")
                .AddEvidence(assembly.Path);
        }

        foreach (var type in context.SourceTypes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var moduleName = InferFromNamespace(type.Namespace)
                ?? CleanModuleName(type.AssemblyName);
            Get(modules, moduleName, "namespace", "medium")
                .AddType(type.FullName);
        }

        foreach (var asset in context.UnityAssets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var moduleName = InferFromPath(asset.Path);
            if (moduleName is not null)
            {
                Get(modules, moduleName, "asset-path", "low")
                    .AddAsset(asset.Path);
            }
        }

        foreach (var module in modules.Values
            .OrderByDescending(module => module.TypeCount)
            .ThenBy(module => module.Name, StringComparer.OrdinalIgnoreCase)
            .Select(module => module.ToInfo()))
        {
            context.AddModule(module);
        }

        AddArchitectureDiagnostics(context);

        return Task.FromResult(new AnalysisStageResult(
            Kind,
            AnalysisStageStatus.Completed,
            $"Inferred {context.Modules.Count} modules."));
    }

    private static ModuleAccumulator Get(
        Dictionary<string, ModuleAccumulator> modules,
        string name,
        string source,
        string confidence)
    {
        if (!modules.TryGetValue(name, out var module))
        {
            module = new ModuleAccumulator(name, source, confidence);
            modules[name] = module;
        }

        module.Merge(source, confidence);
        return module;
    }

    private static string CleanModuleName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Unknown";
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith("Assembly-CSharp", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        var parts = trimmed.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length <= 2
            ? trimmed
            : string.Join('.', parts.Take(2));
    }

    private static string? InferFromNamespace(string namespaceName)
    {
        if (string.IsNullOrWhiteSpace(namespaceName))
        {
            return null;
        }

        var parts = namespaceName.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        return parts.Length == 1 ? parts[0] : string.Join('.', parts.Take(2));
    }

    private static string? InferFromPath(string path)
    {
        var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var assetsIndex = Array.FindIndex(parts, part => part.Equals("Assets", StringComparison.OrdinalIgnoreCase));
        if (assetsIndex < 0 || assetsIndex + 1 >= parts.Length)
        {
            return null;
        }

        var candidate = parts[assetsIndex + 1];
        if (candidate.Equals("Plugins", StringComparison.OrdinalIgnoreCase)
            || candidate.Equals("Resources", StringComparison.OrdinalIgnoreCase)
            || candidate.Equals("StreamingAssets", StringComparison.OrdinalIgnoreCase))
        {
            return candidate;
        }

        return candidate;
    }

    private static void AddArchitectureDiagnostics(AnalysisContext context)
    {
        foreach (var module in context.Modules.Where(module => module.TypeCount > 500))
        {
            context.AddDiagnostic(new DiagnosticInfo
            {
                Severity = "info",
                Category = "architecture",
                Message = $"Large module candidate '{module.Name}' contains {module.TypeCount} indexed types."
            });
        }

        if (context.ProjectModel.AssemblyDefinitions.Count == 0 && context.SourceTypes.Count > 0)
        {
            context.AddDiagnostic(new DiagnosticInfo
            {
                Severity = "info",
                Category = "architecture",
                Message = "No asmdef files found. Runtime/editor assembly boundaries may be inferred from namespaces and paths only."
            });
        }
    }

    private sealed class ModuleAccumulator
    {
        private readonly HashSet<string> _sources = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _evidence = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _types = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _assets = new(StringComparer.OrdinalIgnoreCase);

        public ModuleAccumulator(string name, string source, string confidence)
        {
            Name = name;
            Confidence = confidence;
            _sources.Add(source);
        }

        public string Name { get; }

        public string Confidence { get; private set; }

        public int TypeCount => _types.Count;

        public int AssetCount => _assets.Count;

        public void Merge(string source, string confidence)
        {
            _sources.Add(source);
            Confidence = PickHigherConfidence(Confidence, confidence);
        }

        public void AddEvidence(string evidence)
        {
            _evidence.Add(evidence);
        }

        public void AddType(string type)
        {
            _types.Add(type);
        }

        public void AddAsset(string asset)
        {
            _assets.Add(asset);
        }

        public ModuleInfo ToInfo()
        {
            return new ModuleInfo
            {
                Name = Name,
                Source = string.Join(", ", _sources.OrderBy(source => source)),
                Confidence = Confidence,
                TypeCount = TypeCount,
                AssetCount = AssetCount,
                Evidence = _evidence.OrderBy(evidence => evidence).Take(20).ToArray()
            };
        }

        private static string PickHigherConfidence(string current, string candidate)
        {
            return Rank(candidate) > Rank(current) ? candidate : current;
        }

        private static int Rank(string confidence)
        {
            return confidence.ToLowerInvariant() switch
            {
                "high" => 3,
                "medium" => 2,
                "low" => 1,
                _ => 0
            };
        }
    }
}
