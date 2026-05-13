# OfflineUnityAnalyzer v0.2.0

This release improves the project map and offline report.

## Added

- project model parsing for `.sln`, `.csproj`, `.asmdef`, and `Packages/manifest.json`
- package dependency export
- automatic module inference from asmdefs, csproj files, assemblies, namespaces, and Unity asset paths
- module diagnostics for large modules and projects without asmdefs
- `project-model.json`
- `modules.json`
- richer offline HTML report with visible tables for:
  - modules
  - packages
  - assemblies
  - hot update evidence
  - source types
  - Unity script references
  - diagnostics
- simple report table filter
- UTF-8 output without BOM
- camelCase JSON output for report data

## Still Planned

- Avalonia GUI
- SQLite index
- full Roslyn semantic analysis
- Mono.Cecil type/member DLL analysis
- Prefab merge engine
- YooAsset manifest semantic model
- HybridCLR entry flow graph
- call graph
- diff
