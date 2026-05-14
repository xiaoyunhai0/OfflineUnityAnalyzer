using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using OfflineUnityAnalyzer.Core.Models;
using OfflineUnityAnalyzer.Core.Pipeline;

namespace OfflineUnityAnalyzer.Analyzers.Reporting;

public sealed class ReportGenerationStage : IAnalyzerStage
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public AnalysisStageKind Kind => AnalysisStageKind.ReportExport;

    public Task<AnalysisStageResult> RunAsync(AnalysisContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var summary = BuildSummary(context);
        WriteJson(context, "data/summary.json", summary);
        WriteJson(context, "data/files.json", context.Files);
        WriteJson(context, "data/project-model.json", context.ProjectModel);
        WriteJson(context, "data/modules.json", context.Modules);
        WriteJson(context, "data/types.json", context.SourceTypes);
        WriteJson(context, "data/source-relations.json", context.SourceTypeRelations);
        WriteJson(context, "data/code-assembly-bridges.json", context.CodeAssemblyBridges);
        WriteJson(context, "data/assemblies.json", context.Assemblies);
        WriteJson(context, "data/unity-assets.json", context.UnityAssets);
        WriteJson(context, "data/unity-script-refs.json", context.UnityScriptReferences);
        WriteJson(context, "data/unity-objects.json", context.UnityObjects);
        WriteJson(context, "data/unity-gameobjects.json", context.UnityGameObjects);
        WriteJson(context, "data/unity-components.json", context.UnityComponents);
        WriteJson(context, "data/unity-asset-refs.json", context.UnityAssetReferences);
        WriteJson(context, "data/hybridclr.json", context.HybridClr);
        WriteJson(context, "data/yooasset.json", context.YooAsset);
        WriteJson(context, "data/config-refs.json", context.ConfigReferences);
        WriteJson(context, "data/diagnostics.json", context.Diagnostics);

        var reportData = BuildReportData(context, summary);
        context.FileSystem.WriteAllTextToOutput("report/assets/style.css", BuildCss());
        context.FileSystem.WriteAllTextToOutput("report/assets/app.js", BuildAppJs());
        context.FileSystem.WriteAllTextToOutput("report/report.html", BuildHtml(reportData));

        return Task.FromResult(new AnalysisStageResult(
            Kind,
            AnalysisStageStatus.Completed,
            "Generated project-map HTML report and full JSON data."));
    }

    private static ReportSummary BuildSummary(AnalysisContext context)
    {
        return new ReportSummary
        {
            GeneratedAtUtc = DateTime.UtcNow,
            FileCount = context.Files.Count,
            SourceTypeCount = context.SourceTypes.Count,
            SourceRelationCount = context.SourceTypeRelations.Count,
            ResolvedSourceRelationCount = context.SourceTypeRelations.Count(relation => relation.IsResolved),
            CodeAssemblyBridgeCount = context.CodeAssemblyBridges.Count,
            HotUpdateBridgeCount = context.CodeAssemblyBridges.Count(bridge => bridge.AssemblyKind == "hot_update"),
            MonoBehaviourCount = context.SourceTypes.Count(type => type.IsMonoBehaviour),
            ScriptableObjectCount = context.SourceTypes.Count(type => type.IsScriptableObject),
            SerializedFieldCount = context.SourceTypes.Sum(type => type.Members.Count(member => member.IsSerializedField)),
            AssemblyCount = context.Assemblies.Count,
            PackageCount = context.ProjectModel.Packages.Count,
            AsmdefCount = context.ProjectModel.AssemblyDefinitions.Count,
            CsprojCount = context.ProjectModel.CSharpProjects.Count,
            ModuleCount = context.Modules.Count,
            UnityAssetCount = context.UnityAssets.Count,
            UnityObjectCount = context.UnityObjects.Count,
            UnityGameObjectCount = context.UnityGameObjects.Count,
            UnityComponentCount = context.UnityComponents.Count,
            UnityAssetReferenceCount = context.UnityAssetReferences.Count,
            UnityScriptReferenceCount = context.UnityScriptReferences.Count,
            UnresolvedUnityScriptReferenceCount = context.UnityScriptReferences.Count(reference => reference.ResolvedScriptPath is null),
            DiagnosticCount = context.Diagnostics.Count,
            WarningDiagnosticCount = context.Diagnostics.Count(diagnostic => diagnostic.Severity.Equals("warning", StringComparison.OrdinalIgnoreCase)),
            ErrorDiagnosticCount = context.Diagnostics.Count(diagnostic => diagnostic.Severity.Equals("error", StringComparison.OrdinalIgnoreCase)),
            ConfigReferenceCount = context.ConfigReferences.Count,
            YooAssetManifestAssetCount = context.YooAsset.Assets.Count,
            YooAssetCodeReferenceCount = context.YooAsset.StructuredCodeReferences.Count,
            HybridClrDetected = context.HybridClr.Detected,
            YooAssetDetected = context.YooAsset.Detected,
            ByFileKind = context.Files
                .GroupBy(file => file.Kind.ToString())
                .OrderBy(group => group.Key)
                .ToDictionary(group => group.Key, group => group.Count()),
            ByRelationKind = context.SourceTypeRelations
                .GroupBy(relation => relation.RelationKind)
                .OrderBy(group => group.Key)
                .ToDictionary(group => group.Key, group => group.Count()),
            ByAssemblyKind = context.Assemblies
                .GroupBy(assembly => assembly.Kind)
                .OrderBy(group => group.Key)
                .ToDictionary(group => group.Key, group => group.Count())
        };
    }

    private static object BuildReportData(AnalysisContext context, ReportSummary summary)
    {
        var moduleCards = BuildModuleCards(context).Take(80).ToArray();
        var topTypes = BuildTopTypes(context).Take(120).ToArray();
        var graph = BuildGraph(context, topTypes).ToArray();
        var unityBindings = BuildUnityBindings(context).Take(160).ToArray();
        var unresolvedRefs = context.UnityScriptReferences
            .Where(reference => reference.ResolvedScriptPath is null)
            .Take(120)
            .ToArray();
        var assetChains = BuildAssetChains(context).Take(160).ToArray();
        var projectModel = BuildProjectModel(context);
        var codeAssemblyBridges = BuildCodeAssemblyBridges(context).Take(160).ToArray();

        return new
        {
            summary,
            projectModel,
            codeAssemblyBridges,
            modules = moduleCards,
            topTypes,
            graph,
            unityBindings,
            unresolvedRefs,
            assetChains,
            diagnostics = context.Diagnostics.Take(160),
            configReferences = context.ConfigReferences.Take(160),
            hybridClr = context.HybridClr,
            yooAsset = context.YooAsset,
            filesByKind = summary.ByFileKind.Select(pair => new { kind = pair.Key, count = pair.Value }),
            fullDataFiles = new[]
            {
                "data/summary.json",
                "data/files.json",
                "data/types.json",
                "data/source-relations.json",
                "data/code-assembly-bridges.json",
                "data/unity-components.json",
                "data/unity-asset-refs.json",
                "data/yooasset.json",
                "data/diagnostics.json"
            }
        };
    }

    private static IEnumerable<object> BuildModuleCards(AnalysisContext context)
    {
        var typesByModule = context.SourceTypes
            .GroupBy(type => InferModuleName(type, context.Modules))
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var assetsByModule = context.UnityAssets
            .GroupBy(asset => InferModuleFromPath(asset.Path) ?? "Unmapped Assets")
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var moduleNames = context.Modules.Select(module => module.Name)
            .Concat(typesByModule.Keys)
            .Concat(assetsByModule.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var name in moduleNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            typesByModule.TryGetValue(name, out var moduleTypes);
            assetsByModule.TryGetValue(name, out var moduleAssets);
            moduleTypes ??= Array.Empty<SourceTypeInfo>();
            moduleAssets ??= Array.Empty<UnityAssetInfo>();
            var typeNames = moduleTypes.Select(type => type.FullName).ToHashSet(StringComparer.Ordinal);
            var outbound = context.SourceTypeRelations
                .Where(relation => typeNames.Contains(relation.SourceType)
                    && !typeNames.Contains(relation.TargetType)
                    && relation.IsResolved)
                .GroupBy(relation => relation.TargetType)
                .OrderByDescending(group => group.Count())
                .Take(8)
                .Select(group => new
                {
                    target = ShortName(group.Key),
                    count = group.Count(),
                    kinds = group.Select(relation => relation.RelationKind).Distinct().OrderBy(value => value).ToArray()
                })
                .ToArray();

            yield return new
            {
                name,
                typeCount = moduleTypes.Length,
                monoBehaviourCount = moduleTypes.Count(type => type.IsMonoBehaviour),
                scriptableObjectCount = moduleTypes.Count(type => type.IsScriptableObject),
                assetCount = moduleAssets.Length,
                topTypes = moduleTypes
                    .OrderByDescending(type => RelationScore(context, type.FullName))
                    .Take(8)
                    .Select(type => new
                    {
                        type.FullName,
                        type.Name,
                        type.Kind,
                        type.IsMonoBehaviour,
                        type.IsScriptableObject,
                        memberCount = type.Members.Count,
                        relationCount = context.SourceTypeRelations.Count(relation => relation.SourceType == type.FullName)
                    })
                    .ToArray(),
                outbound
            };
        }
    }

    private static IEnumerable<object> BuildTopTypes(AnalysisContext context)
    {
        var inbound = context.SourceTypeRelations
            .Where(relation => relation.IsResolved)
            .GroupBy(relation => relation.TargetType)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var outbound = context.SourceTypeRelations
            .GroupBy(relation => relation.SourceType)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var unityRefs = context.UnityComponents
            .Where(component => !string.IsNullOrWhiteSpace(component.ResolvedType))
            .GroupBy(component => component.ResolvedType!)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        return context.SourceTypes
            .Select(type =>
            {
                var inCount = inbound.GetValueOrDefault(type.FullName);
                var outCount = outbound.GetValueOrDefault(type.FullName);
                var unityCount = unityRefs.GetValueOrDefault(type.Name);

                return new
                {
                    type.FullName,
                    type.Name,
                    type.Namespace,
                    type.Kind,
                    type.AssemblyName,
                    type.IsMonoBehaviour,
                    type.IsScriptableObject,
                    type.IsEditorType,
                    memberCount = type.Members.Count,
                    serializedFieldCount = type.Members.Count(member => member.IsSerializedField),
                    inboundCount = inCount,
                    outboundCount = outCount,
                    unityBindingCount = unityCount,
                    score = inCount * 2 + outCount + unityCount * 3 + (type.IsMonoBehaviour ? 5 : 0) + (type.IsScriptableObject ? 4 : 0),
                    bases = type.BaseTypes,
                    sourceFile = ShortPath(type.SourceFile)
                };
            })
            .OrderByDescending(type => type.score)
            .ThenBy(type => type.FullName, StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<object> BuildGraph(AnalysisContext context, IReadOnlyList<object> topTypes)
    {
        var selected = context.SourceTypes
            .OrderByDescending(type => RelationScore(context, type.FullName)
                + (type.IsMonoBehaviour ? 5 : 0)
                + (type.IsScriptableObject ? 4 : 0))
            .Take(70)
            .Select(type => type.FullName)
            .ToHashSet(StringComparer.Ordinal);

        return context.SourceTypeRelations
            .Where(relation => selected.Contains(relation.SourceType)
                && selected.Contains(relation.TargetType)
                && relation.IsResolved)
            .GroupBy(relation => new { relation.SourceType, relation.TargetType, relation.RelationKind })
            .OrderByDescending(group => group.Count())
            .Take(220)
            .Select(group => new
            {
                source = ShortName(group.Key.SourceType),
                sourceFullName = group.Key.SourceType,
                target = ShortName(group.Key.TargetType),
                targetFullName = group.Key.TargetType,
                kind = group.Key.RelationKind,
                count = group.Count()
            });
    }

    private static IEnumerable<object> BuildCodeAssemblyBridges(AnalysisContext context)
    {
        return context.CodeAssemblyBridges
            .OrderByDescending(bridge => bridge.Confidence == "high")
            .ThenBy(bridge => bridge.SourceType, StringComparer.Ordinal)
            .Select(bridge => new
            {
                sourceType = bridge.SourceType,
                source = ShortPath(bridge.SourceFile),
                assembly = bridge.AssemblyName,
                assemblyKind = bridge.AssemblyKind,
                assemblyPath = ShortPath(bridge.AssemblyPath),
                bridge.MatchKind,
                bridge.Confidence
            });
    }

    private static IEnumerable<object> BuildUnityBindings(AnalysisContext context)
    {
        return context.UnityComponents
            .Where(component => !string.IsNullOrWhiteSpace(component.ScriptGuid)
                || !string.IsNullOrWhiteSpace(component.GameObjectName))
            .GroupBy(component => new
            {
                component.AssetPath,
                component.GameObjectName,
                Script = component.ResolvedType
                    ?? (component.ResolvedScriptPath is null ? null : Path.GetFileNameWithoutExtension(component.ResolvedScriptPath))
                    ?? component.ScriptGuid,
                component.TypeName
            })
            .OrderBy(group => ShortPath(group.Key.AssetPath))
            .ThenBy(group => group.Key.GameObjectName)
            .Select(group => new
            {
                asset = ShortPath(group.Key.AssetPath),
                gameObject = group.Key.GameObjectName ?? "(asset component)",
                script = group.Key.Script ?? group.Key.TypeName,
                componentType = group.Key.TypeName,
                count = group.Count()
            });
    }

    private static IEnumerable<object> BuildAssetChains(AnalysisContext context)
    {
        return context.UnityAssetReferences
            .Where(reference => !string.IsNullOrWhiteSpace(reference.ResolvedPath))
            .GroupBy(reference => new
            {
                Source = reference.AssetPath,
                Target = reference.ResolvedPath!,
                reference.FieldName,
                reference.OwnerType
            })
            .OrderBy(group => ShortPath(group.Key.Source))
            .Select(group => new
            {
                source = ShortPath(group.Key.Source),
                ownerType = group.Key.OwnerType,
                field = group.Key.FieldName,
                target = ShortPath(group.Key.Target),
                count = group.Count()
            });
    }

    private static object BuildProjectModel(AnalysisContext context)
    {
        return new
        {
            solutions = context.ProjectModel.Solutions.Select(solution => new
            {
                path = ShortPath(solution.Path),
                projectCount = solution.ProjectPaths.Count
            }),
            asmdefs = context.ProjectModel.AssemblyDefinitions.Select(asmdef => new
            {
                asmdef.Name,
                path = ShortPath(asmdef.Path),
                referenceCount = asmdef.References.Count,
                asmdef.IsEditor,
                references = asmdef.References.Take(12)
            }),
            packages = context.ProjectModel.Packages.Take(120).Select(package => new
            {
                package.Name,
                package.VersionOrSource,
                package.Source
            }),
            assemblies = context.Assemblies.Take(160).Select(assembly => new
            {
                name = assembly.AssemblyName,
                assembly.Kind,
                path = ShortPath(assembly.Path),
                typeCount = assembly.TypeNames.Count,
                monoBehaviourCount = assembly.MonoBehaviourTypes.Count,
                scriptableObjectCount = assembly.ScriptableObjectTypes.Count,
                assembly.Status
            })
        };
    }

    private static int RelationScore(AnalysisContext context, string fullName)
    {
        return context.SourceTypeRelations.Count(relation => relation.SourceType == fullName)
            + context.SourceTypeRelations.Count(relation => relation.TargetType == fullName) * 2;
    }

    private static string InferModuleName(SourceTypeInfo type, IReadOnlyList<ModuleInfo> modules)
    {
        var explicitModule = modules
            .Where(module => module.Evidence.Any(evidence => ContainsPath(Path.GetDirectoryName(evidence) ?? evidence, type.SourceFile)))
            .OrderByDescending(module => module.Name.Length)
            .FirstOrDefault();
        if (explicitModule is not null)
        {
            return explicitModule.Name;
        }

        if (!string.IsNullOrWhiteSpace(type.Namespace))
        {
            var parts = type.Namespace.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length > 0)
            {
                return parts.Length == 1 ? parts[0] : string.Join('.', parts.Take(2));
            }
        }

        return InferModuleFromPath(type.SourceFile) ?? type.AssemblyName;
    }

    private static string? InferModuleFromPath(string path)
    {
        var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var assetsIndex = Array.FindIndex(parts, part => part.Equals("Assets", StringComparison.OrdinalIgnoreCase));
        if (assetsIndex >= 0 && assetsIndex + 1 < parts.Length)
        {
            return parts[assetsIndex + 1];
        }

        var packagesIndex = Array.FindIndex(parts, part => part.Equals("Packages", StringComparison.OrdinalIgnoreCase));
        if (packagesIndex >= 0 && packagesIndex + 1 < parts.Length)
        {
            return parts[packagesIndex + 1];
        }

        return null;
    }

    private static bool ContainsPath(string root, string path)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (OperatingSystem.IsWindows())
        {
            normalizedRoot = normalizedRoot.ToUpperInvariant();
            normalizedPath = normalizedPath.ToUpperInvariant();
        }

        return normalizedPath.Equals(normalizedRoot, StringComparison.Ordinal)
            || normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || normalizedPath.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static string ShortName(string value)
    {
        var index = value.LastIndexOf('.');
        return index >= 0 ? value[(index + 1)..] : value;
    }

    private static string ShortPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join('/', parts.TakeLast(Math.Min(5, parts.Length)));
    }

    private static void WriteJson(AnalysisContext context, string relativePath, object value)
    {
        context.FileSystem.WriteAllTextToOutput(relativePath, JsonSerializer.Serialize(value, JsonOptions));
    }

    private static string BuildHtml(object reportData)
    {
        var reportJson = JsonSerializer.Serialize(reportData, JsonOptions);
        return $$"""
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>OfflineUnityAnalyzer Project Map</title>
  <link rel="stylesheet" href="assets/style.css">
</head>
<body>
  <header class="app-header">
    <div>
      <p class="eyebrow">Offline Unity project intelligence</p>
      <h1>Project Map</h1>
      <p class="subtitle">A relationship-first report for split source repositories, compiled Unity DLLs, scenes, prefabs, HybridCLR, YooAsset, and configuration evidence.</p>
    </div>
    <div class="header-actions">
      <button data-scroll="overview">Overview</button>
      <button data-scroll="modules">Modules</button>
      <button data-scroll="relations">Relations</button>
      <button data-scroll="compiled">Compiled</button>
      <button data-scroll="unity">Unity</button>
      <button data-scroll="diagnostics">Diagnostics</button>
    </div>
  </header>

  <main>
    <section id="overview" class="section">
      <div class="section-title">
        <h2>Overview</h2>
        <p>Counts are generated from the complete offline scan. Detailed full data is available in <code>../data/</code>.</p>
      </div>
      <div id="metrics" class="metrics"></div>
      <div class="split">
        <article class="panel">
          <h3>Analysis Shape</h3>
          <div id="shape"></div>
        </article>
        <article class="panel">
          <h3>Hot Update And Assets</h3>
          <div id="hotupdate"></div>
        </article>
      </div>
    </section>

    <section id="modules" class="section">
      <div class="section-title">
        <h2>Modules</h2>
        <p>Grouped by asmdef, namespace, package, and Unity asset paths so you can see what each area owns.</p>
      </div>
      <div id="module-grid" class="module-grid"></div>
    </section>

    <section id="relations" class="section">
      <div class="section-title">
        <h2>Code Relations</h2>
        <p>Inheritance, serialized fields, regular fields, properties, constructors, and likely calls between indexed C# types.</p>
      </div>
      <div class="relation-layout">
        <article class="panel">
          <h3>Relationship Graph</h3>
          <div id="graph" class="graph"></div>
        </article>
        <article class="panel">
          <h3>Important Types</h3>
          <div class="toolbar"><input id="type-filter" type="search" placeholder="Filter types, assemblies, namespaces"></div>
          <div id="top-types"></div>
        </article>
      </div>
    </section>

    <section id="compiled" class="section">
      <div class="section-title">
        <h2>Source To Compiled DLLs</h2>
        <p>Shows how external C# source roots line up with compiled assemblies found inside the Unity project.</p>
      </div>
      <div class="split">
        <article class="panel"><h3>Source/DLL Bridges</h3><div id="code-assembly-bridges"></div></article>
        <article class="panel"><h3>DLL Type Index</h3><div id="dll-types"></div></article>
      </div>
    </section>

    <section id="unity" class="section">
      <div class="section-title">
        <h2>Unity Bindings</h2>
        <p>Scene and prefab components resolved back to scripts, plus asset-to-asset reference chains.</p>
      </div>
      <div class="split">
        <article class="panel">
          <h3>GameObject To Script</h3>
          <div id="unity-bindings"></div>
        </article>
        <article class="panel">
          <h3>Asset Reference Chains</h3>
          <div id="asset-chains"></div>
        </article>
      </div>
    </section>

    <section id="project-model" class="section">
      <div class="section-title">
        <h2>Project Model</h2>
        <p>Assemblies, asmdefs, packages, and file categories found during the scan.</p>
      </div>
      <div class="split">
        <article class="panel"><h3>Asmdefs</h3><div id="asmdefs"></div></article>
        <article class="panel"><h3>Packages And Files</h3><div id="packages"></div></article>
      </div>
    </section>

    <section id="diagnostics" class="section">
      <div class="section-title">
        <h2>Diagnostics</h2>
        <p>Warnings and architecture hints collected while parsing. These explain missing or low-confidence edges.</p>
      </div>
      <div class="split">
        <article class="panel"><h3>Warnings And Hints</h3><div id="diagnostics-table"></div></article>
        <article class="panel"><h3>Raw Data</h3><div id="data-files"></div></article>
      </div>
    </section>
  </main>

  <script>
    window.__OUA_REPORT__ = {{reportJson}};
  </script>
  <script src="assets/app.js"></script>
</body>
</html>
""";
    }

    private static string BuildCss()
    {
        return """
:root {
  color-scheme: light;
  --bg: #f5f7fb;
  --surface: #ffffff;
  --surface-2: #f8fafc;
  --ink: #172033;
  --muted: #5f6f89;
  --line: #d8e0ec;
  --line-strong: #b8c4d6;
  --blue: #2458d3;
  --teal: #0f766e;
  --green: #17803a;
  --amber: #a15c05;
  --red: #b42318;
  --purple: #6d3fc7;
  font-family: "Segoe UI", "Microsoft YaHei UI", Arial, sans-serif;
}

* {
  box-sizing: border-box;
}

body {
  margin: 0;
  color: var(--ink);
  background: var(--bg);
}

.app-header {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  gap: 24px;
  align-items: end;
  padding: 28px 36px 24px;
  color: #ffffff;
  background: #142033;
  border-bottom: 4px solid #2dd4bf;
}

.eyebrow {
  margin: 0 0 8px;
  color: #a7f3d0;
  font-size: 12px;
  font-weight: 700;
  letter-spacing: 0;
  text-transform: uppercase;
}

h1,
h2,
h3,
p {
  margin-top: 0;
}

h1 {
  margin-bottom: 8px;
  font-size: 34px;
  line-height: 1.08;
}

h2 {
  margin-bottom: 6px;
  font-size: 22px;
}

h3 {
  margin-bottom: 12px;
  font-size: 15px;
}

.subtitle {
  max-width: 800px;
  margin-bottom: 0;
  color: #dbeafe;
  line-height: 1.5;
}

.header-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  justify-content: flex-end;
}

button {
  border: 1px solid rgba(255, 255, 255, 0.24);
  border-radius: 6px;
  padding: 8px 11px;
  color: #ffffff;
  background: rgba(255, 255, 255, 0.08);
  font: inherit;
  cursor: pointer;
}

button:hover {
  background: rgba(255, 255, 255, 0.16);
}

main {
  max-width: 1440px;
  margin: 0 auto;
  padding: 26px;
}

.section {
  margin-bottom: 26px;
}

.section-title {
  display: grid;
  grid-template-columns: minmax(0, 1fr);
  margin-bottom: 14px;
}

.section-title p {
  margin-bottom: 0;
  color: var(--muted);
  line-height: 1.5;
}

.metrics,
.module-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(170px, 1fr));
  gap: 12px;
}

.metric,
.panel,
.module-card {
  border: 1px solid var(--line);
  border-radius: 8px;
  background: var(--surface);
  box-shadow: 0 1px 2px rgba(17, 24, 39, 0.05);
}

.metric {
  min-height: 92px;
  padding: 15px;
}

.metric strong {
  display: block;
  margin-bottom: 4px;
  color: var(--ink);
  font-size: 28px;
  line-height: 1.05;
}

.metric span,
.muted {
  color: var(--muted);
}

.metric small {
  display: block;
  margin-top: 8px;
  color: var(--muted);
  line-height: 1.35;
}

.split,
.relation-layout {
  display: grid;
  gap: 14px;
}

.split {
  grid-template-columns: repeat(2, minmax(0, 1fr));
  margin-top: 14px;
}

.relation-layout {
  grid-template-columns: minmax(360px, 0.9fr) minmax(480px, 1.1fr);
}

.panel,
.module-card {
  padding: 16px;
  overflow: hidden;
}

.module-card h3 {
  display: flex;
  justify-content: space-between;
  gap: 12px;
  align-items: center;
  margin-bottom: 10px;
}

.stat-row,
.legend,
.chips {
  display: flex;
  flex-wrap: wrap;
  gap: 7px;
  align-items: center;
}

.stat-row {
  margin-bottom: 12px;
}

.chip,
.pill {
  display: inline-flex;
  align-items: center;
  border-radius: 999px;
  padding: 3px 8px;
  color: #17415c;
  background: #e0f2fe;
  font-size: 12px;
  white-space: nowrap;
}

.chip.green {
  color: #14532d;
  background: #dcfce7;
}

.chip.amber {
  color: #78350f;
  background: #fef3c7;
}

.chip.purple {
  color: #4c1d95;
  background: #ede9fe;
}

.chip.gray {
  color: #475569;
  background: #e2e8f0;
}

table {
  width: 100%;
  border-collapse: collapse;
  font-size: 13px;
}

th,
td {
  border-bottom: 1px solid #e8eef7;
  padding: 8px 7px;
  text-align: left;
  vertical-align: top;
}

th {
  color: #3d4d63;
  background: #f8fafc;
  font-weight: 700;
}

td {
  color: var(--ink);
  overflow-wrap: anywhere;
}

.toolbar {
  margin-bottom: 10px;
}

input {
  width: 100%;
  min-height: 36px;
  border: 1px solid var(--line-strong);
  border-radius: 6px;
  padding: 8px 10px;
  color: var(--ink);
  background: #ffffff;
  font: inherit;
}

.graph {
  min-height: 520px;
  border: 1px solid var(--line);
  border-radius: 8px;
  background: #fbfdff;
  overflow: auto;
}

.graph svg {
  display: block;
  min-width: 620px;
}

.node text {
  fill: #172033;
  font-size: 11px;
}

.node circle {
  fill: #ffffff;
  stroke: #2458d3;
  stroke-width: 2;
}

.node.is-unity circle {
  stroke: #0f766e;
}

.edge {
  stroke: #8091aa;
  stroke-width: 1.2;
  opacity: 0.72;
}

.edge.inherits {
  stroke: #6d3fc7;
}

.edge.serialized-field {
  stroke: #0f766e;
  stroke-width: 1.8;
}

.edge.calls {
  stroke: #a15c05;
  stroke-dasharray: 4 3;
}

.mini-list {
  display: grid;
  gap: 6px;
  margin: 10px 0 0;
  padding: 0;
  list-style: none;
}

.mini-list li {
  border: 1px solid #edf2f7;
  border-radius: 6px;
  padding: 8px;
  background: var(--surface-2);
  overflow-wrap: anywhere;
}

a {
  color: var(--blue);
}

code {
  font-family: Consolas, "Cascadia Mono", "Liberation Mono", monospace;
  font-size: 0.92em;
}

.empty {
  margin: 0;
  color: var(--muted);
}

@media (max-width: 980px) {
  .app-header,
  .split,
  .relation-layout {
    grid-template-columns: 1fr;
  }

  .header-actions {
    justify-content: flex-start;
  }

  main {
    padding: 18px;
  }
}
""";
    }

    private static string BuildAppJs()
    {
        return """
(function () {
  const data = window.__OUA_REPORT__ || {};
  const summary = data.summary || {};

  renderMetrics();
  renderShape();
  renderHotUpdate();
  renderModules();
  renderGraph();
  renderTopTypes();
  renderCompiled();
  renderUnity();
  renderProjectModel();
  renderDiagnostics();
  bindNavigation();

  function renderMetrics() {
    const metrics = [
      ["Files", summary.fileCount, "All indexed files after excludes"],
      ["C# Types", summary.sourceTypeCount, `${summary.monoBehaviourCount || 0} MonoBehaviour, ${summary.scriptableObjectCount || 0} ScriptableObject`],
      ["Relations", summary.sourceRelationCount, `${summary.resolvedSourceRelationCount || 0} resolved between indexed types`],
      ["Source/DLL Bridges", summary.codeAssemblyBridgeCount, `${summary.hotUpdateBridgeCount || 0} hot-update matches`],
      ["Serialized Fields", summary.serializedFieldCount, "Public fields and [SerializeField] members"],
      ["Unity Bindings", summary.unityComponentCount, `${summary.unityGameObjectCount || 0} GameObjects indexed`],
      ["Asset Refs", summary.unityAssetReferenceCount, "Scene/prefab/material/config references"],
      ["Modules", summary.moduleCount, "Inferred ownership groups"],
      ["Diagnostics", summary.diagnosticCount, `${summary.warningDiagnosticCount || 0} warnings`]
    ];
    document.getElementById("metrics").innerHTML = metrics.map(([label, value, note]) => `
      <article class="metric"><strong>${escapeHtml(value ?? 0)}</strong><span>${escapeHtml(label)}</span><small>${escapeHtml(note)}</small></article>
    `).join("");
  }

  function renderShape() {
    const rows = Object.entries(summary.byFileKind || {}).sort((a, b) => b[1] - a[1]).slice(0, 16)
      .map(([kind, count]) => ({ kind, count }));
    renderTable("shape", rows, [
      ["File Kind", "kind"],
      ["Count", "count"]
    ]);
  }

  function renderHotUpdate() {
    const hybrid = data.hybridClr || {};
    const yoo = data.yooAsset || {};
    const lines = [
      `<p><span class="chip ${hybrid.detected ? "green" : "gray"}">HybridCLR ${hybrid.detected ? "detected" : "not found"}</span></p>`,
      `<p><span class="chip ${yoo.detected ? "green" : "gray"}">YooAsset ${yoo.detected ? "detected" : "not found"}</span></p>`,
      `<div class="stat-row"><span class="chip">Hot DLLs ${(hybrid.hotUpdateAssemblies || []).length}</span><span class="chip">AOT ${(hybrid.aotMetadataAssemblies || []).length}</span><span class="chip">Yoo manifests ${(yoo.manifestFiles || []).length}</span><span class="chip">Yoo code refs ${summary.yooAssetCodeReferenceCount || 0}</span></div>`
    ];
    if ((hybrid.evidence || []).length) {
      lines.push(`<ul class="mini-list">${hybrid.evidence.slice(0, 5).map(x => `<li>${escapeHtml(shortPath(x))}</li>`).join("")}</ul>`);
    }
    document.getElementById("hotupdate").innerHTML = lines.join("");
  }

  function renderModules() {
    const modules = data.modules || [];
    const root = document.getElementById("module-grid");
    if (!modules.length) {
      root.innerHTML = `<p class="empty">No modules inferred. Check source/project model stages.</p>`;
      return;
    }
    root.innerHTML = modules.map(module => `
      <article class="module-card">
        <h3><span>${escapeHtml(module.name)}</span><span class="chip gray">${module.typeCount || 0} types</span></h3>
        <div class="stat-row">
          <span class="chip green">${module.monoBehaviourCount || 0} MonoBehaviour</span>
          <span class="chip purple">${module.scriptableObjectCount || 0} ScriptableObject</span>
          <span class="chip">${module.assetCount || 0} assets</span>
        </div>
        <strong class="muted">Important types</strong>
        <ul class="mini-list">
          ${(module.topTypes || []).slice(0, 6).map(type => `<li>${escapeHtml(type.name)} <span class="muted">${escapeHtml(type.kind)} · ${type.relationCount || 0} refs</span></li>`).join("") || `<li class="muted">No indexed types.</li>`}
        </ul>
        ${(module.outbound || []).length ? `<strong class="muted">Depends on</strong><ul class="mini-list">${module.outbound.slice(0, 5).map(edge => `<li>${escapeHtml(edge.target)} <span class="muted">${edge.count} ${escapeHtml((edge.kinds || []).join(", "))}</span></li>`).join("")}</ul>` : ""}
      </article>
    `).join("");
  }

  function renderGraph() {
    const edges = data.graph || [];
    const root = document.getElementById("graph");
    if (!edges.length) {
      root.innerHTML = `<p class="empty" style="padding:16px">No resolved code relations found. Full raw type data is still in ../data/types.json.</p>`;
      return;
    }

    const nodes = new Map();
    for (const edge of edges) {
      nodes.set(edge.sourceFullName, edge.source);
      nodes.set(edge.targetFullName, edge.target);
    }
    const nodeEntries = Array.from(nodes.entries()).slice(0, 72);
    const nodeIndex = new Map(nodeEntries.map(([full], index) => [full, index]));
    const width = 920;
    const height = Math.max(560, Math.ceil(nodeEntries.length / 8) * 86 + 80);
    const centerX = width / 2;
    const centerY = height / 2;
    const radiusX = Math.max(280, width / 2 - 120);
    const radiusY = Math.max(210, height / 2 - 90);
    const positions = nodeEntries.map(([full, name], index) => {
      const angle = (index / nodeEntries.length) * Math.PI * 2 - Math.PI / 2;
      return {
        full,
        name,
        x: centerX + Math.cos(angle) * radiusX,
        y: centerY + Math.sin(angle) * radiusY
      };
    });

    const edgeSvg = edges
      .filter(edge => nodeIndex.has(edge.sourceFullName) && nodeIndex.has(edge.targetFullName))
      .slice(0, 220)
      .map(edge => {
        const a = positions[nodeIndex.get(edge.sourceFullName)];
        const b = positions[nodeIndex.get(edge.targetFullName)];
        return `<line class="edge ${cssClass(edge.kind)}" x1="${a.x}" y1="${a.y}" x2="${b.x}" y2="${b.y}"><title>${escapeHtml(edge.source)} -> ${escapeHtml(edge.target)} (${escapeHtml(edge.kind)}, ${edge.count})</title></line>`;
      }).join("");
    const nodeSvg = positions.map(node => `
      <g class="node" transform="translate(${node.x},${node.y})">
        <circle r="18"><title>${escapeHtml(node.full)}</title></circle>
        <text x="24" y="4">${escapeHtml(node.name)}</text>
      </g>
    `).join("");

    root.innerHTML = `<svg viewBox="0 0 ${width} ${height}" width="${width}" height="${height}" role="img" aria-label="Code relationship graph">${edgeSvg}${nodeSvg}</svg>`;
  }

  function renderTopTypes() {
    const rows = data.topTypes || [];
    const columns = [
      ["Type", row => `${row.name}${row.isMonoBehaviour ? " · MonoBehaviour" : ""}${row.isScriptableObject ? " · ScriptableObject" : ""}`],
      ["Assembly", "assemblyName"],
      ["Relations", row => `in ${row.inboundCount || 0} / out ${row.outboundCount || 0}`],
      ["Unity", row => row.unityBindingCount || 0],
      ["File", "sourceFile"]
    ];
    renderTable("top-types", rows, columns);
    const filter = document.getElementById("type-filter");
    filter.addEventListener("input", () => {
      const q = filter.value.toLowerCase();
      document.querySelectorAll("#top-types tbody tr").forEach(row => {
        row.hidden = q && !row.textContent.toLowerCase().includes(q);
      });
    });
  }

  function renderCompiled() {
    renderTable("code-assembly-bridges", data.codeAssemblyBridges || [], [
      ["Source Type", "sourceType"],
      ["Source", "source"],
      ["DLL", row => `${row.assembly} · ${row.assemblyKind}`],
      ["Match", "matchKind"],
      ["Confidence", "confidence"]
    ]);
    const assemblies = (data.projectModel && data.projectModel.assemblies) || [];
    renderTable("dll-types", assemblies, [
      ["Assembly", "name"],
      ["Kind", "kind"],
      ["Types", "typeCount"],
      ["MonoBehaviour", "monoBehaviourCount"],
      ["Path", "path"]
    ]);
  }

  function renderUnity() {
    renderTable("unity-bindings", data.unityBindings || [], [
      ["Asset", "asset"],
      ["GameObject", "gameObject"],
      ["Script", "script"],
      ["Component", "componentType"],
      ["Count", "count"]
    ]);
    renderTable("asset-chains", data.assetChains || [], [
      ["Source", "source"],
      ["Owner", "ownerType"],
      ["Field", "field"],
      ["Target", "target"],
      ["Count", "count"]
    ]);
  }

  function renderProjectModel() {
    const model = data.projectModel || {};
    renderTable("asmdefs", model.asmdefs || [], [
      ["Name", "name"],
      ["Refs", "referenceCount"],
      ["Editor", row => row.isEditor ? "yes" : ""],
      ["Path", "path"]
    ]);
    const packageRows = [
      ...((model.packages || []).map(x => ({ kind: "package", name: x.name, detail: x.versionOrSource, source: x.source }))),
      ...((data.filesByKind || []).map(x => ({ kind: "file", name: x.kind, detail: x.count, source: "" })))
    ];
    renderTable("packages", packageRows, [
      ["Kind", "kind"],
      ["Name", "name"],
      ["Detail", "detail"],
      ["Source", "source"]
    ]);
  }

  function renderDiagnostics() {
    renderTable("diagnostics-table", data.diagnostics || [], [
      ["Severity", "severity"],
      ["Category", "category"],
      ["Message", "message"],
      ["Path", row => shortPath(row.path)]
    ]);
    const files = data.fullDataFiles || [];
    document.getElementById("data-files").innerHTML = `<ul class="mini-list">${files.map(file => `<li><code>${escapeHtml(file)}</code></li>`).join("")}</ul>`;
  }

  function renderTable(id, rows, columns) {
    const root = document.getElementById(id);
    if (!rows.length) {
      root.innerHTML = `<p class="empty">No data found.</p>`;
      return;
    }
    const header = columns.map(([label]) => `<th>${escapeHtml(label)}</th>`).join("");
    const body = rows.map(row => `<tr>${columns.map(([, accessor]) => `<td>${escapeHtml(read(row, accessor))}</td>`).join("")}</tr>`).join("");
    root.innerHTML = `<table><thead><tr>${header}</tr></thead><tbody>${body}</tbody></table>`;
  }

  function bindNavigation() {
    document.querySelectorAll("[data-scroll]").forEach(button => {
      button.addEventListener("click", () => document.getElementById(button.dataset.scroll).scrollIntoView({ behavior: "smooth", block: "start" }));
    });
  }

  function read(row, accessor) {
    if (typeof accessor === "function") return accessor(row) ?? "";
    return row[accessor] ?? "";
  }

  function shortPath(path) {
    if (!path) return "";
    return String(path).replace(/\\/g, "/").split("/").slice(-5).join("/");
  }

  function cssClass(value) {
    return String(value || "").replace(/[^a-z0-9_-]/gi, "-").toLowerCase();
  }

  function escapeHtml(value) {
    return String(value ?? "")
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }
})();
""";
    }

    private sealed record ReportSummary
    {
        public DateTime GeneratedAtUtc { get; init; }
        public int FileCount { get; init; }
        public int SourceTypeCount { get; init; }
        public int SourceRelationCount { get; init; }
        public int ResolvedSourceRelationCount { get; init; }
        public int CodeAssemblyBridgeCount { get; init; }
        public int HotUpdateBridgeCount { get; init; }
        public int MonoBehaviourCount { get; init; }
        public int ScriptableObjectCount { get; init; }
        public int SerializedFieldCount { get; init; }
        public int AssemblyCount { get; init; }
        public int PackageCount { get; init; }
        public int AsmdefCount { get; init; }
        public int CsprojCount { get; init; }
        public int ModuleCount { get; init; }
        public int UnityAssetCount { get; init; }
        public int UnityObjectCount { get; init; }
        public int UnityGameObjectCount { get; init; }
        public int UnityComponentCount { get; init; }
        public int UnityAssetReferenceCount { get; init; }
        public int UnityScriptReferenceCount { get; init; }
        public int UnresolvedUnityScriptReferenceCount { get; init; }
        public int DiagnosticCount { get; init; }
        public int WarningDiagnosticCount { get; init; }
        public int ErrorDiagnosticCount { get; init; }
        public int ConfigReferenceCount { get; init; }
        public int YooAssetManifestAssetCount { get; init; }
        public int YooAssetCodeReferenceCount { get; init; }
        public bool HybridClrDetected { get; init; }
        public bool YooAssetDetected { get; init; }
        public IReadOnlyDictionary<string, int> ByFileKind { get; init; } = new Dictionary<string, int>();
        public IReadOnlyDictionary<string, int> ByRelationKind { get; init; } = new Dictionary<string, int>();
        public IReadOnlyDictionary<string, int> ByAssemblyKind { get; init; } = new Dictionary<string, int>();
    }
}
