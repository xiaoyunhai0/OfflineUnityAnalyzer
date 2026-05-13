# OfflineUnityAnalyzer v0.1.0

This is the first local release package.

## Included

- GUI executable placeholder
- CLI executable with `analyze` and `serve`
- strict readonly path guard
- output-only safe file system
- safety preflight
- file scanning
- basic C# type/member index
- basic DLL and `.dll.bytes` assembly metadata scan
- Unity text YAML `m_Script` reference extraction
- `.meta` GUID mapping for script references
- HybridCLR evidence detection
- YooAsset evidence detection
- diagnostics for unresolved Unity script references
- JSON data export
- static offline HTML report
- local package scripts for Windows x64

## Not Complete Yet

- Avalonia GUI
- full Roslyn semantic analysis
- Mono.Cecil type-level DLL analysis
- full prefab merge engine
- full YooAsset manifest semantics
- full HybridCLR entry flow graph
- call graph
- diff
- SQLite index

The v1 target remains documented in `docs/product-architecture-v1.md`.
