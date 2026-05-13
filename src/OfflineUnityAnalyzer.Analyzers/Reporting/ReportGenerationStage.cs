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

        context.FileSystem.WriteAllTextToOutput("report/assets/style.css", BuildCss());
        var reportData = BuildReportData(context, summary);
        context.FileSystem.WriteAllTextToOutput("report/assets/app.js", BuildAppJs());
        context.FileSystem.WriteAllTextToOutput("report/report.html", BuildHtml(reportData));

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
            packageCount = context.ProjectModel.Packages.Count,
            asmdefCount = context.ProjectModel.AssemblyDefinitions.Count,
            csprojCount = context.ProjectModel.CSharpProjects.Count,
            moduleCount = context.Modules.Count,
            unityAssetCount = context.UnityAssets.Count,
            unityObjectCount = context.UnityObjects.Count,
            unityGameObjectCount = context.UnityGameObjects.Count,
            unityComponentCount = context.UnityComponents.Count,
            unityAssetReferenceCount = context.UnityAssetReferences.Count,
            unityScriptReferenceCount = context.UnityScriptReferences.Count,
            unresolvedUnityScriptReferenceCount = context.UnityScriptReferences.Count(reference => reference.ResolvedScriptPath is null),
            diagnosticCount = context.Diagnostics.Count,
            configReferenceCount = context.ConfigReferences.Count,
            yooAssetManifestAssetCount = context.YooAsset.Assets.Count,
            yooAssetCodeReferenceCount = context.YooAsset.StructuredCodeReferences.Count,
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

    private static object BuildReportData(AnalysisContext context, object summary)
    {
        return new
        {
            summary,
            modules = context.Modules.Take(24),
            packages = context.ProjectModel.Packages.Take(24),
            assemblies = context.Assemblies.Take(24),
            diagnostics = context.Diagnostics.Take(50),
            sourceTypes = context.SourceTypes
                .OrderByDescending(type => type.IsMonoBehaviour)
                .ThenBy(type => type.FullName)
                .Take(50),
            unityGameObjects = context.UnityGameObjects.Take(80),
            unityComponents = context.UnityComponents.Take(80),
            unityReferences = context.UnityScriptReferences.Take(50),
            unityAssetReferences = context.UnityAssetReferences.Take(80),
            yooAssetManifestAssets = context.YooAsset.Assets.Take(80),
            yooAssetCodeReferences = context.YooAsset.StructuredCodeReferences.Take(80),
            configReferences = context.ConfigReferences.Take(80),
            hybridClr = context.HybridClr,
            yooAsset = context.YooAsset
        };
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
    <section class="toolbar">
      <input id="filter" type="search" placeholder="Filter visible tables">
    </section>
    <section>
      <h2>Project Map</h2>
      <div class="panels">
        <article><h3>Modules</h3><div id="modules"></div></article>
        <article><h3>Packages</h3><div id="packages"></div></article>
        <article><h3>Assemblies</h3><div id="assemblies"></div></article>
        <article><h3>Hot Update</h3><div id="hotupdate"></div></article>
      </div>
    </section>
    <section class="wide">
      <h2>Source Types</h2>
      <div id="types"></div>
    </section>
    <section class="wide">
      <h2>Unity GameObjects</h2>
      <div id="gameobjects"></div>
    </section>
    <section class="wide">
      <h2>Unity Components</h2>
      <div id="components"></div>
    </section>
    <section class="wide">
      <h2>Unity Script References</h2>
      <div id="unityrefs"></div>
    </section>
    <section class="wide">
      <h2>Unity Asset References</h2>
      <div id="assetrefs"></div>
    </section>
    <section class="wide">
      <h2>YooAsset Manifest Assets</h2>
      <div id="yooassets"></div>
    </section>
    <section class="wide">
      <h2>YooAsset Code References</h2>
      <div id="yoocode"></div>
    </section>
    <section class="wide">
      <h2>Config References</h2>
      <div id="configrefs"></div>
    </section>
    <section class="wide">
      <h2>Diagnostics</h2>
      <div id="diagnostics"></div>
    </section>
    <section class="wide">
      <h2>Data Files</h2>
      <p>Full data is available next to this report in <code>../data/</code>.</p>
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

.toolbar {
  margin-bottom: 16px;
}

.toolbar input {
  box-sizing: border-box;
  width: 100%;
  border: 1px solid #bcccdc;
  border-radius: 6px;
  padding: 10px 12px;
  font: inherit;
  background: #fff;
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

.wide {
  margin-top: 22px;
  border: 1px solid #d9e2ec;
  border-radius: 6px;
  padding: 16px;
  background: #fff;
}

table {
  width: 100%;
  border-collapse: collapse;
  font-size: 13px;
}

th,
td {
  border-bottom: 1px solid #edf2f7;
  padding: 8px;
  text-align: left;
  vertical-align: top;
}

th {
  color: #52606d;
  font-weight: 650;
}

.pill {
  display: inline-block;
  border-radius: 999px;
  padding: 2px 8px;
  font-size: 12px;
  background: #e0f2fe;
  color: #075985;
}

.muted {
  color: #697386;
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

    private static string BuildAppJs()
    {
        return """
(function () {
  const data = window.__OUA_REPORT__ || {};
  const summary = data.summary || {};
  const metrics = [
    ["Files", summary.fileCount],
    ["Source Types", summary.sourceTypeCount],
    ["MonoBehaviours", summary.monoBehaviourCount],
    ["ScriptableObjects", summary.scriptableObjectCount],
    ["Assemblies", summary.assemblyCount],
    ["Packages", summary.packageCount],
    ["Asmdefs", summary.asmdefCount],
    ["Modules", summary.moduleCount],
    ["Unity Assets", summary.unityAssetCount],
    ["Unity Objects", summary.unityObjectCount],
    ["GameObjects", summary.unityGameObjectCount],
    ["Components", summary.unityComponentCount],
    ["Asset Refs", summary.unityAssetReferenceCount],
    ["Unity Script Refs", summary.unityScriptReferenceCount],
    ["Yoo Assets", summary.yooAssetManifestAssetCount],
    ["Yoo Code Refs", summary.yooAssetCodeReferenceCount],
    ["Config Refs", summary.configReferenceCount],
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

  renderTable("modules", data.modules || [], [
    ["Name", "name"],
    ["Source", "source"],
    ["Confidence", "confidence"],
    ["Types", "typeCount"],
    ["Assets", "assetCount"]
  ]);

  renderTable("packages", data.packages || [], [
    ["Name", "name"],
    ["Version/Source", "versionOrSource"],
    ["Source", "source"]
  ]);

  renderTable("assemblies", data.assemblies || [], [
    ["Name", "assemblyName"],
    ["Kind", "kind"],
    ["Status", "status"],
    ["File", "fileName"]
  ]);

  renderHotUpdate();

  renderTable("types", data.sourceTypes || [], [
    ["Full Name", "fullName"],
    ["Kind", "kind"],
    ["Assembly", "assemblyName"],
    ["Unity", value => [value.isMonoBehaviour ? "MonoBehaviour" : "", value.isScriptableObject ? "ScriptableObject" : ""].filter(Boolean).join(", ")]
  ]);

  renderTable("gameobjects", data.unityGameObjects || [], [
    ["Asset", value => shortPath(value.assetPath)],
    ["Name", "name"],
    ["Local ID", "localId"],
    ["Components", value => (value.componentLocalIds || []).length]
  ]);

  renderTable("components", data.unityComponents || [], [
    ["Asset", value => shortPath(value.assetPath)],
    ["Component", "typeName"],
    ["GameObject", "gameObjectName"],
    ["Script", value => value.resolvedType || shortPath(value.resolvedScriptPath) || value.scriptGuid || ""]
  ]);

  renderTable("unityrefs", data.unityReferences || [], [
    ["Asset", value => shortPath(value.assetPath)],
    ["GUID", "scriptGuid"],
    ["Resolved", value => value.resolvedType || shortPath(value.resolvedScriptPath) || ""],
    ["Confidence", "confidence"]
  ]);

  renderTable("assetrefs", data.unityAssetReferences || [], [
    ["Asset", value => shortPath(value.assetPath)],
    ["Owner", "ownerType"],
    ["Field", "fieldName"],
    ["Target", value => shortPath(value.resolvedPath) || value.guid || value.fileId || ""],
    ["Kind", "referenceKind"]
  ]);

  renderTable("yooassets", data.yooAssetManifestAssets || [], [
    ["Package", "packageName"],
    ["Address", "address"],
    ["Asset Path", "assetPath"],
    ["Bundle", "bundleName"]
  ]);

  renderTable("yoocode", data.yooAssetCodeReferences || [], [
    ["Source", value => shortPath(value.sourceFile)],
    ["API", "apiName"],
    ["Address", "addressLiteral"],
    ["Confidence", "confidence"]
  ]);

  renderTable("configrefs", data.configReferences || [], [
    ["Source", value => shortPath(value.sourcePath)],
    ["Location", "location"],
    ["Field", "fieldName"],
    ["Value", "value"],
    ["Kind", "matchKind"]
  ]);

  renderTable("diagnostics", data.diagnostics || [], [
    ["Severity", "severity"],
    ["Category", "category"],
    ["Message", "message"],
    ["Path", value => shortPath(value.path)]
  ]);

  document.getElementById("filter").addEventListener("input", event => {
    const query = event.target.value.toLowerCase();
    for (const row of document.querySelectorAll("tbody tr")) {
      row.hidden = query && !row.textContent.toLowerCase().includes(query);
    }
  });

  function renderHotUpdate() {
    const hybrid = data.hybridClr || {};
    const yoo = data.yooAsset || {};
    const root = document.getElementById("hotupdate");
    root.innerHTML = `
      <p><span class="pill">HybridCLR</span> ${hybrid.detected ? "Detected" : "Not found"}</p>
      <p><span class="pill">YooAsset</span> ${yoo.detected ? "Detected" : "Not found"}</p>
      <p class="muted">Hybrid evidence: ${(hybrid.evidence || []).length}, YooAsset manifests: ${(yoo.manifestFiles || []).length}, code refs: ${(yoo.codeReferences || []).length}</p>
    `;
  }

  function renderTable(id, rows, columns) {
    const root = document.getElementById(id);
    if (!rows.length) {
      root.innerHTML = '<p class="muted">No data found.</p>';
      return;
    }

    const header = columns.map(([label]) => `<th>${escapeHtml(label)}</th>`).join("");
    const body = rows.map(row => `<tr>${columns.map(([, accessor]) => `<td>${escapeHtml(read(row, accessor))}</td>`).join("")}</tr>`).join("");
    root.innerHTML = `<table><thead><tr>${header}</tr></thead><tbody>${body}</tbody></table>`;
  }

  function read(row, accessor) {
    if (typeof accessor === "function") return accessor(row) ?? "";
    return row[accessor] ?? "";
  }

  function shortPath(path) {
    if (!path) return "";
    return String(path).replace(/\\/g, "/").split("/").slice(-4).join("/");
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
}
