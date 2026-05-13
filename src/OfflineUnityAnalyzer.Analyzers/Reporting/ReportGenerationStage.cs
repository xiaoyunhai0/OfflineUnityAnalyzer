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
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public AnalysisStageKind Kind => AnalysisStageKind.ReportExport;

    public Task<AnalysisStageResult> RunAsync(AnalysisContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var summary = BuildSummary(context);

        WriteJson(context, "data/summary.json", summary);
        WriteJson(context, "data/files.json", context.Files);
        WriteJson(context, "data/types.json", context.SourceTypes);
        WriteJson(context, "data/assemblies.json", context.Assemblies);
        WriteJson(context, "data/unity-assets.json", context.UnityAssets);
        WriteJson(context, "data/unity-script-refs.json", context.UnityScriptReferences);
        WriteJson(context, "data/hybridclr.json", context.HybridClr);
        WriteJson(context, "data/yooasset.json", context.YooAsset);
        WriteJson(context, "data/diagnostics.json", context.Diagnostics);

        context.FileSystem.WriteAllTextToOutput("report/assets/style.css", BuildCss());
        context.FileSystem.WriteAllTextToOutput("report/assets/app.js", BuildAppJs(summary));
        context.FileSystem.WriteAllTextToOutput("report/report.html", BuildHtml(summary));

        return Task.FromResult(new AnalysisStageResult(
            Kind,
            AnalysisStageStatus.Completed,
            "Generated offline HTML report and JSON data."));
    }

    private static object BuildSummary(AnalysisContext context)
    {
        return new
        {
            generatedAtUtc = DateTime.UtcNow,
            fileCount = context.Files.Count,
            sourceTypeCount = context.SourceTypes.Count,
            monoBehaviourCount = context.SourceTypes.Count(type => type.IsMonoBehaviour),
            scriptableObjectCount = context.SourceTypes.Count(type => type.IsScriptableObject),
            assemblyCount = context.Assemblies.Count,
            unityAssetCount = context.UnityAssets.Count,
            unityScriptReferenceCount = context.UnityScriptReferences.Count,
            unresolvedUnityScriptReferenceCount = context.UnityScriptReferences.Count(reference => reference.ResolvedScriptPath is null),
            diagnosticCount = context.Diagnostics.Count,
            hybridClrDetected = context.HybridClr.Detected,
            yooAssetDetected = context.YooAsset.Detected,
            byFileKind = context.Files
                .GroupBy(file => file.Kind.ToString())
                .OrderBy(group => group.Key)
                .ToDictionary(group => group.Key, group => group.Count()),
            byAssemblyKind = context.Assemblies
                .GroupBy(assembly => assembly.Kind)
                .OrderBy(group => group.Key)
                .ToDictionary(group => group.Key, group => group.Count()),
            stages = context.Diagnostics
                .GroupBy(diagnostic => diagnostic.Severity)
                .OrderBy(group => group.Key)
                .ToDictionary(group => group.Key, group => group.Count())
        };
    }

    private static void WriteJson(AnalysisContext context, string relativePath, object value)
    {
        context.FileSystem.WriteAllTextToOutput(relativePath, JsonSerializer.Serialize(value, JsonOptions));
    }

    private static string BuildHtml(object summary)
    {
        var summaryJson = JsonSerializer.Serialize(summary, JsonOptions);
        return $$"""
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>OfflineUnityAnalyzer Report</title>
  <link rel="stylesheet" href="assets/style.css">
</head>
<body>
  <header>
    <h1>OfflineUnityAnalyzer</h1>
    <p>Offline, read-only Unity project map</p>
  </header>
  <main>
    <section class="grid" id="summary"></section>
    <section>
      <h2>Project Map</h2>
      <div class="panels">
        <article>
          <h3>Source Types</h3>
          <p>Open <code>../data/types.json</code> for the full type index.</p>
        </article>
        <article>
          <h3>Unity References</h3>
          <p>Open <code>../data/unity-script-refs.json</code> for script references.</p>
        </article>
        <article>
          <h3>Hot Update</h3>
          <p>HybridCLR and YooAsset evidence are exported as JSON data.</p>
        </article>
        <article>
          <h3>Diagnostics</h3>
          <p>Open <code>../data/diagnostics.json</code> for unresolved references and warnings.</p>
        </article>
      </div>
    </section>
  </main>
  <script>
    window.__OUA_SUMMARY__ = {{summaryJson}};
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
  font-family: "Segoe UI", Arial, sans-serif;
}

body {
  margin: 0;
  color: #1f2933;
  background: #f5f7fa;
}

header {
  padding: 28px 36px;
  color: #fff;
  background: #1f2933;
}

h1, h2, h3, p {
  margin-top: 0;
}

main {
  max-width: 1120px;
  margin: 0 auto;
  padding: 28px;
}

.grid,
.panels {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 14px;
}

.metric,
article {
  border: 1px solid #d9e2ec;
  border-radius: 6px;
  padding: 16px;
  background: #fff;
}

.metric strong {
  display: block;
  font-size: 26px;
  line-height: 1.2;
}

code {
  font-family: Consolas, "Liberation Mono", monospace;
  font-size: 0.92em;
}
""";
    }

    private static string BuildAppJs(object summary)
    {
        return """
(function () {
  const summary = window.__OUA_SUMMARY__ || {};
  const metrics = [
    ["Files", summary.fileCount],
    ["Source Types", summary.sourceTypeCount],
    ["MonoBehaviours", summary.monoBehaviourCount],
    ["ScriptableObjects", summary.scriptableObjectCount],
    ["Assemblies", summary.assemblyCount],
    ["Unity Assets", summary.unityAssetCount],
    ["Unity Script Refs", summary.unityScriptReferenceCount],
    ["Unresolved Refs", summary.unresolvedUnityScriptReferenceCount],
    ["Diagnostics", summary.diagnosticCount],
    ["HybridCLR", summary.hybridClrDetected ? "Detected" : "Not found"],
    ["YooAsset", summary.yooAssetDetected ? "Detected" : "Not found"]
  ];

  const root = document.getElementById("summary");
  for (const [label, value] of metrics) {
    const card = document.createElement("article");
    card.className = "metric";
    card.innerHTML = `<strong>${value ?? 0}</strong><span>${label}</span>`;
    root.appendChild(card);
  }
})();
""";
    }
}
