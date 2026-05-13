# OfflineUnityAnalyzer v1 Product Architecture

## 1. Product Goal

OfflineUnityAnalyzer v1 is a local, offline, read-only Unity project understanding tool.

Its purpose is not to replace Unity Editor, compile the project, or execute game code. Its purpose is to help a developer quickly and visually understand a large Unity project:

- What are the main scenes, prefabs, modules, assemblies, packages, and entry flows?
- Where is a type, method, prefab, scene, GUID, asset address, or hot update assembly used?
- How do C# source code, compiled DLLs, Unity YAML assets, HybridCLR hot update DLLs, and YooAsset manifests connect?
- Which analysis results are precise, which are inferred, and which failed or remain unresolved?

The product should be automatic by default. Users should not need to manually define module rules, priorities, project layout, or package structure before the tool becomes useful.

Manual configuration exists only to correct, override, or extend automatic discovery.

## 2. Non-Negotiable Constraints

### 2.1 Offline Only

Runtime analysis must not depend on:

- Network access
- Unity Editor
- Visual Studio
- dotnet SDK
- NuGet restore
- Git
- Node.js
- Python
- CDN resources
- External database services

All runtime dependencies, front-end assets, dictionaries, and help documents must be included in the release package.

### 2.2 Read-Only Project Boundary

The analyzed project must not be modified in any way.

Read-only roots include:

- Unity project root
- C# source roots
- DLL roots
- local package roots
- YooAsset manifest roots
- any user-confirmed additional input roots

The tool must not create, modify, delete, or rename any file inside those roots.

The output directory is the only analysis write location:

```text
AnalyzerOutput/
  index.db
  report/
  data/
  logs/
  exports/
  .cache/
```

The output directory must not be inside any input root, and no input root may be inside the output directory.

The desktop application may write its own application settings, such as recent output directories, to its own app data directory. This directory must not contain source content or Unity asset content.

### 2.3 No Real Write Tests In Input Roots

Safety checks must not verify read-only behavior by creating test files in the analyzed project.

Allowed safety checks:

- path containment checks
- write API restriction checks
- output/input overlap checks
- path traversal checks
- template and report asset checks for CDN references
- process allowlist checks
- unit tests using temporary test directories

Forbidden safety checks:

- creating temporary files in source roots
- creating temporary files in Unity roots
- creating temporary files in DLL roots
- touching `.meta`, `.prefab`, `.unity`, `.asset`, `.cs`, `.csproj`, or `.sln` files

## 3. Product Shape

v1 ships as a Windows x64 self-contained folder zip containing two executables:

```text
OfflineUnityAnalyzer-win-x64/
  OfflineUnityAnalyzer.exe
  OfflineUnityAnalyzer.Cli.exe
  assets/
  templates/
  docs/
  dictionaries/
  runtimes/
  config.sample.json
  README.md
  licenses/
```

### 3.1 GUI First

`OfflineUnityAnalyzer.exe` is the primary user entry point.

The GUI should support:

- open recent analysis
- open an existing output directory or `index.db`
- create a new analysis
- automatic path discovery
- live analysis progress
- partial result browsing while analysis continues
- project map
- global search
- diagnostics
- diff
- static export
- local help

### 3.2 CLI Complete

`OfflineUnityAnalyzer.Cli.exe` provides full automation support:

```bat
OfflineUnityAnalyzer.Cli.exe analyze --unity D:\UnityProject --out D:\AnalyzerOutput
OfflineUnityAnalyzer.Cli.exe serve --out D:\AnalyzerOutput --port 0
OfflineUnityAnalyzer.Cli.exe diff --base D:\OldOutput --target D:\NewOutput
OfflineUnityAnalyzer.Cli.exe export --out D:\AnalyzerOutput --export D:\Report.zip
```

The GUI starts the CLI as a child process for analysis work. This is allowed under strict readonly because the CLI is a signed/known component shipped with the tool.

Forbidden external processes still include:

- Unity
- MSBuild
- dotnet build
- git
- npm
- PowerShell scripts
- project scripts
- arbitrary unknown executables

## 4. Application Architecture

Recommended .NET project split:

```text
src/
  OfflineUnityAnalyzer.Core
  OfflineUnityAnalyzer.Analyzers
  OfflineUnityAnalyzer.Indexing
  OfflineUnityAnalyzer.ViewerServer
  OfflineUnityAnalyzer.App
  OfflineUnityAnalyzer.Cli
  OfflineUnityAnalyzer.ReportExport

tests/
  OfflineUnityAnalyzer.Tests
  OfflineUnityAnalyzer.Fixtures
```

Responsibilities:

- `Core`: shared models, configuration, pipeline contracts, safety contracts
- `Analyzers`: source, DLL, Unity YAML, prefab merge, HybridCLR, YooAsset, call graph, diff analyzers
- `Indexing`: SQLite schema, query APIs, write APIs, cache APIs
- `ViewerServer`: local read-only `127.0.0.1` API for GUI and CLI serve mode
- `App`: Avalonia desktop shell
- `Cli`: analyze, serve, diff, export commands
- `ReportExport`: static HTML and shareable zip export

## 5. GUI and Viewer Architecture

Use Avalonia for the desktop shell and a local Web Viewer for complex visualizations.

Avalonia owns:

- welcome screen
- recent analyses
- new analysis wizard
- path discovery confirmation
- progress and cancellation controls
- settings
- viewer hosting

The local Web Viewer owns:

- project map
- search
- module graph
- type detail pages
- scene and prefab hierarchy views
- merged prefab views
- entry flow views
- call graph views
- diagnostics
- diff views

The Web Viewer reads data through a local ASP.NET Core Minimal API server:

```text
GET /api/summary
GET /api/search?q=
GET /api/types/{id}
GET /api/assets/{id}
GET /api/prefabs/{id}/raw
GET /api/prefabs/{id}/merged
GET /api/scenes/{id}
GET /api/callgraph?methodId=&depth=3
GET /api/entryflows
GET /api/diagnostics
GET /api/diff
```

Server rules:

- bind only to `127.0.0.1`
- choose a random free port by default
- read only output data and indexes
- do not access the network
- shut down with the GUI
- reuse the same implementation for `Cli serve`

Static export still generates a file-based report for sharing. Static export must not rely on `fetch(file://...)`; it should use embedded or script-loaded local data files.

## 6. Automatic Discovery

The default user flow is:

```text
1. Select Unity project root
2. Tool auto-discovers likely inputs
3. User confirms or adjusts suggestions
4. Tool analyzes
5. GUI opens project map
```

Auto-discovery should detect:

- Unity project root
- `Assets`
- `Packages`
- `ProjectSettings`
- `Assets/Plugins`
- local and embedded packages
- `.asmdef`
- `.asmref`
- `.sln`
- `.csproj`
- external source roots near the Unity project
- DLL roots
- HybridCLR directories
- YooAsset settings and manifests
- ServerData or package manifest roots
- common config directories

Default discovery is conservative:

- scan the Unity project root
- scan known Unity subdirectories
- scan one sibling level for likely source directories
- scan one sibling level for `.sln`

Broader searches require explicit user confirmation.

## 7. Analysis Philosophy

The tool should produce a useful project understanding even when some data is incomplete.

Every major result should carry:

```text
confidence = high | medium | low
source = exact | semantic | syntax | inferred | heuristic | unresolved
explanation = why this result was produced
```

Partial failures must not destroy the whole report.

The report must show analysis coverage:

- source parsed percentage
- DLL parsed count
- Unity YAML parsed count
- prefab merge coverage
- call graph coverage
- unresolved reference count
- binary or unsupported asset count

Exit codes:

```text
0 success
1 argument/config error
2 input path error
3 readonly/safety violation
4 report generated with analysis gaps
5 failure with no useful output
```

## 8. Source Code Analysis

### 8.1 C# Parsing

Always perform syntax indexing:

- namespaces
- classes
- interfaces
- structs
- enums
- records
- partial declarations
- base types
- implemented interfaces
- attributes
- fields
- properties
- methods
- constructors
- events
- using directives
- file to type mapping

### 8.2 Project Model

Build a lightweight project model without invoking MSBuild:

- parse `.sln`
- parse `.csproj` XML
- parse `.asmdef`
- parse `.asmref`
- infer assembly names
- infer source inclusion
- infer project and assembly references
- infer Editor/Runtime classification

Do not run restore, build, Unity, or MSBuild.

### 8.3 Semantic Analysis

Use Roslyn semantic analysis where possible.

Build in-memory compilations using discovered source files and local DLL references. If references are missing or a compilation fails, record warnings and fall back to syntax results.

Support condition profiles:

- Unity Editor
- auto-detected primary player platform
- optional Windows/Android/iOS profiles

Infer symbols from:

- Unity version
- platform profile
- `.asmdef` constraints
- `.csproj` constants
- project settings
- package presence

## 9. DLL and Assembly Analysis

Use Mono.Cecil, not Reflection.

Analyze:

- assembly name
- MVID/hash
- references
- type definitions
- base types
- interfaces
- members
- attributes
- IL method calls
- Unity base classes
- Editor-only types

Do not load or execute business DLLs.

## 10. Logical Type Model

Source and DLL representations of the same type should be merged into one logical type with multiple origins.

Example:

```text
LogicalType:
  fullName: Game.Battle.PlayerController
  origins:
    - source: D:\GameCode\PlayerController.cs
      assembly: GameLogic
    - dll: D:\UnityProject\Assets\Plugins\GameLogic.dll
      assembly: GameLogic
      mvid: ...
  status: source_and_dll_match | source_only | dll_only | conflict
```

Source/DLL mismatches are first-class diagnostics.

High priority mismatches:

- Unity type identity differs
- base type differs
- serialized fields differ
- Unity resources reference a DLL type whose matching source type conflicts

## 11. Unity YAML Analysis

v1 focuses on Unity text serialization.

Binary serialized assets are detected, counted, and reported as unsupported for deep parsing. They must not fail the whole analysis.

Unity YAML must be parsed with Unity-specific handling:

- split YAML documents
- parse `!u!` class IDs
- parse local file IDs
- parse object type names
- parse `m_Script`
- parse `m_GameObject`
- parse `m_Component`
- parse `m_Name`
- parse external `{fileID, guid, type}` references
- parse local `{fileID}` references

Do not treat Unity YAML as ordinary YAML only.

## 12. GameObject, Component, and Serialized Field Views

v1 must show scripts in their actual Unity context:

```text
Scene: Battle.unity
  GameObject: Canvas/HUD/SkillPanel
    Component: SkillPanelController
    Script: Game.UI.SkillPanelController
```

Analyze serialized fields from C#:

- `[SerializeField]` fields
- public instance fields
- `[field: SerializeField]` auto properties
- `[HideInInspector]`
- `[NonSerialized]`
- `UnityEngine.Object` references
- arrays and lists
- serializable nested classes where practical

Analyze serialized values from YAML:

- external asset references
- local scene/prefab references
- array/list references
- ScriptableObject asset field references
- selected primitive values for context

## 13. Prefab Merge Engine

v1 must support both raw and merged prefab views.

Required:

- nested prefabs
- prefab variants
- base prefab chains
- added components
- removed components
- modified components
- field overrides
- source tracing for merged results

Each merged node or value should be able to explain whether it came from:

- base prefab
- nested prefab source
- variant override
- local addition
- removed element marker

The tool should not open Unity Editor and should not claim perfect runtime truth when Unity-specific merge behavior cannot be reproduced.

## 14. Unity Version Support

v1 should support a broad Unity version range:

- Unity 2018.4 LTS
- Unity 2019.4 LTS
- Unity 2020.3 LTS
- Unity 2021.3 LTS
- Unity 2022.3 LTS
- Unity 2023.x
- Unity 6 / 6000.x

Unknown or older versions should use compatibility mode and report parser confidence.

Required infrastructure:

- Unity version detector
- versioned YAML parser behavior
- versioned prefab format adapters
- versioned serialized property handling
- fixture corpus by Unity version

## 15. Fixture Corpus

v1 requires a multi-version Unity fixture corpus.

For each supported Unity version, include small text-serialized samples for:

- normal prefab
- nested prefab
- prefab variant
- scene
- ScriptableObject
- missing script
- serialized field references
- added component override
- removed component override
- modified component override
- AnimatorController
- YooAsset or HybridCLR minimal samples where practical

Fixtures must be small and safe to include in tests.

## 16. HybridCLR Support

The target project uses HybridCLR, so v1 must treat HybridCLR as a core feature.

Auto-detect HybridCLR from:

- `Packages/manifest.json`
- `Assets/HybridCLRData`
- `AOTGenericReferences.cs`
- `link.xml`
- `*.dll.bytes`
- `RuntimeApi.LoadMetadataForAOTAssembly`
- `Assembly.Load(byte[])`

Analyze:

- normal DLLs
- `.dll.bytes` files
- hot update assemblies
- AOT metadata assemblies
- cross-assembly calls between AOT and hot update code
- hot update entry points
- load paths

Group assemblies as:

- Runtime Assemblies
- Hot Update Assemblies
- AOT Metadata Assemblies
- Editor Assemblies
- Third Party Assemblies

## 17. YooAsset Support

The target project uses YooAsset, so v1 must treat YooAsset as a core feature.

Auto-detect:

- YooAsset package in `Packages/manifest.json`
- YooAsset settings assets
- `StreamingAssets`
- `ServerData`
- package manifests
- `PackageManifest*`
- YooAsset code calls

Index:

- package names
- manifests
- asset addresses
- bundle names
- asset paths
- hot update DLL bytes assets
- scene and prefab addresses
- loading code references

Recognize code patterns:

- `YooAssets.GetPackage`
- `LoadAssetAsync`
- `LoadSceneAsync`
- `LoadRawFileAsync`
- package-specific handle APIs

YooAsset manifest roots should be auto-discovered, with optional user-supplied roots when needed.

## 18. Resource Address and Config Table Indexing

v1 should include shallow indexing for resource addresses and config references.

Sources:

- JSON
- CSV
- XML
- readable `.bytes`
- ScriptableObject assets
- common config folders

Extract only structural clues:

- asset-like strings
- YooAsset addresses
- GUID-like strings
- type names
- method names
- prefab/scene/texture/audio path clues

Do not store full config tables by default.

Store:

```text
file
row/key path
field name
matched value
matched kind
confidence
```

Local GUI may read source/config snippets on demand. Static export must not include full config content by default.

## 19. Call Graph and Entry Flow

v1 should build full static call intelligence while clearly separating precise edges from inferred edges.

High confidence:

- direct method calls
- constructor calls
- base/this calls
- direct delegate calls
- event subscribe/unsubscribe
- lambda/local function relationships
- source-level async/await relationships

Medium confidence:

- virtual/interface possible targets
- Unity lifecycle entry points
- UnityEvent persistent calls
- coroutine string targets
- `Invoke` / `InvokeRepeating`
- `SendMessage` / `BroadcastMessage`
- UI Button YAML bindings
- Animator StateMachineBehaviour callbacks

Low confidence:

- reflection strings
- DI container registration/resolve
- resource address strings
- config-driven type or method names

The UI should not show one giant graph by default.

Required views:

- callers of method
- callees from method
- N-level expansion
- module-level call aggregation
- Unity entry flow
- scene/button/lifecycle to business code chain

Default entry flow depth is 3 levels, with manual expansion.

## 20. Global Search

Global search is a primary feature.

Search must cover:

- type names
- full names
- namespaces
- methods
- fields
- properties
- attributes
- prefab paths
- scene paths
- asset paths
- GameObject paths
- GUIDs
- file IDs
- local IDs
- assembly names
- asmdef names
- csproj names
- serialized field names
- UnityEvent target methods
- YooAsset addresses
- config references
- diagnostics

Search should support:

- case-insensitive search
- substring search
- token search
- path fragment search
- PascalCase acronym search
- GUID/fileID exact search
- Chinese substring search
- pinyin full spelling
- pinyin initials

Pinyin support must be fully offline and included in the package with license documentation.

## 21. Module Inference

Modules should be inferred automatically.

Evidence sources:

- assemblies
- `.asmdef`
- `.csproj`
- namespaces
- directory structure
- Unity resource clusters
- prefab/scene usage
- YooAsset packages
- HybridCLR assembly groups

Manual `moduleRules` are overrides, not prerequisites.

When automatic inference and user override conflict, the user override wins, but the automatic candidates remain visible as explanation data.

Each module assignment should expose:

```text
module
confidence
evidence
override status
```

## 22. Package Analysis

v1 should support:

- embedded packages
- local packages
- `file:` packages
- package lock files
- registry package references
- package cache discovery with user confirmation

Package data enters the architecture map but external packages are folded by default.

Package page should show:

- name
- version
- source
- asmdefs
- assets
- referenced by modules

## 23. Editor vs Runtime

Editor and runtime code must be separated.

Editor signals:

- path contains `Editor`
- asmdef platform is Editor
- references `UnityEditor`
- derives from editor-only Unity types

Report views:

- Runtime Map
- Editor Tools Map
- Shared/Ambiguous Code
- Runtime-to-Editor illegal references

## 24. Non-Code Unity Resources

v1 should analyze resource relationships, not art quality.

Support:

- Material to Shader
- Prefab/Scene to Material/Texture/AudioClip/AnimatorController
- AnimatorController to clips and StateMachineBehaviour
- ScriptableObject asset references
- YooAsset bundle/address to asset path
- Timeline/Playable generic references
- Cinemachine components as normal script/components

Do not do:

- texture compression analysis
- audio compression analysis
- shader variant optimization
- mesh content analysis
- Timeline runtime behavior simulation

## 25. AnimatorController

v1 should parse basic AnimatorController structure:

- layers
- states
- transitions
- parameters
- motion/animation clip references
- StateMachineBehaviour script references
- Entry/Exit/AnyState relationships

It does not need to fully simulate blend trees or runtime animation playback.

## 26. Diagnostics

Diagnostics are a primary page, not only logs.

Diagnostic groups:

- Understanding blockers
- Architecture risks
- Data/reference risks
- Hot update risks
- Package and platform risks

Examples:

- missing scripts
- unresolved GUIDs
- binary Unity assets
- prefab merge failures
- Source/DLL mismatches
- Runtime references Editor
- circular module dependencies
- HybridCLR hot update load path unresolved
- YooAsset address missing from manifest
- UnityEvent target missing
- huge prefabs/scenes

Diagnostics must show evidence and affected paths. Do not auto-fix.

## 27. Custom Rules

v1 supports simple declarative rules, generated or edited through the GUI.

Examples:

```json
{
  "rules": [
    {
      "kind": "forbidDependency",
      "from": "Game.UI",
      "to": "Game.Battle",
      "severity": "warning"
    },
    {
      "kind": "requireLayerDirection",
      "layers": ["UI", "Application", "Domain", "Infrastructure"]
    }
  ]
}
```

No scriptable rule plugins in v1.

## 28. Diff

v1 should support full comparison between two analysis outputs.

Diff must use stable identities, not absolute paths.

Identity priorities:

- Unity assets: GUID, then project-relative path fallback
- C# types: assembly identity plus full name
- members: containing type plus signature
- DLLs: assembly name plus MVID/hash
- GameObjects: asset identity plus local file ID plus hierarchy path fallback
- call edges: caller method plus callee method plus edge kind

Diff should cover:

- files
- types
- members
- inheritance
- interfaces
- DLL hashes/MVIDs
- Source/DLL mismatch changes
- Unity asset references
- serialized field references and selected value changes
- raw prefab view
- merged prefab view
- scene hierarchy
- UnityEvent persistent calls
- call graph edges and paths
- module dependencies
- diagnostics
- search index summary

Default diff must not include source method bodies or full YAML content.

## 29. Export and Redaction

Support shareable export:

```text
AnalysisReport.zip
  report.html
  assets/
  data/
  summary.json
  warnings.json
  diagnostics.json
```

Default export includes structure, signatures, paths, references, GUIDs, and diagnostics.

Default export excludes:

- source file content
- method bodies
- full Unity YAML
- full config tables
- DLL content

Redaction options:

- redact absolute paths
- redact usernames
- redact GUIDs
- module summary only
- exclude config references
- exclude low-confidence references

Local GUI can read source snippets on demand from original paths. Static export must not include snippets unless explicitly enabled.

## 30. Local Help

v1 includes offline help documents:

- quick start
- path selection
- readonly policy
- confidence explanation
- analysis coverage explanation
- unresolved references
- Unity text serialization limits
- Source/DLL mismatch
- HybridCLR support
- YooAsset support
- search syntax
- diff
- export and redaction
- troubleshooting

Help must be local and packaged with the app.

## 31. Incremental Analysis and Resume

Analysis must be staged and resumable.

Stages:

```text
1. safety preflight
2. file scan
3. project model
4. source syntax index
5. DLL index
6. Unity YAML raw index
7. type/source/DLL merge
8. serialized field index
9. prefab merge
10. HybridCLR index
11. YooAsset index
12. config shallow index
13. semantic analysis
14. call graph
15. module inference
16. diagnostics
17. search index
18. report/export data
```

Each stage must store status in the output index/cache.

Cancellation:

- GUI cancel button
- CLI Ctrl+C
- stop at safe checkpoints
- preserve completed stage data
- mark run as cancelled

Resume:

- reopen same output
- detect completed stages
- detect stale file hashes
- rerun only needed work

## 32. Cache

Default cache location:

```text
AnalyzerOutput/.cache/
```

The cache must not be written to input roots.

Possible cache files:

- file hashes
- parsed source symbols
- parsed DLL metadata
- parsed Unity YAML
- prefab merge cache
- semantic analysis cache
- call graph cache
- search tokens

An explicit `--cache` option may be added later, but it must follow the same no-overlap rule as output.

## 33. Data Model Principles

SQLite is the primary working index.

Principles:

- stable IDs for entities
- logical type IDs separate from origins
- source/DLL origins preserved
- absolute and relative paths both stored in working index
- export redacts absolute paths by default
- all relationship edges include confidence and source
- all inferred results include explanation data where practical
- schema version stored in metadata

Core entities:

- roots
- files
- assemblies
- assembly references
- logical types
- type origins
- members
- inheritance edges
- interface edges
- method calls
- modules
- module evidence
- Unity assets
- Unity objects
- GameObjects
- Components
- serialized fields
- serialized references
- prefab raw nodes
- prefab merged nodes
- HybridCLR assemblies
- YooAsset packages/manifests/assets
- config references
- packages
- diagnostics
- search documents
- analysis runs
- analysis stages

## 34. Roadmap

Keep the complete v1 target, but deliver in betas.

```text
v0.1 Skeleton
- solution structure
- GUI shell
- CLI shell
- readonly path guard
- output index
- packaging

v0.2 Project Map
- file scan
- source syntax index
- DLL index
- asmdef/csproj/sln parsing
- package parsing
- basic search
- basic viewer

v0.3 Unity Deep
- Unity YAML parser
- GameObject/component hierarchy
- serialized references
- ScriptableObject references
- prefab raw view
- prefab merge engine
- AnimatorController basics

v0.4 Hot Update
- HybridCLR detection
- .dll.bytes analysis
- AOT metadata grouping
- YooAsset manifest parsing
- YooAsset address/load chain
- config shallow index

v0.5 Intelligence
- semantic analysis
- call graph
- entry flow
- module inference
- diagnostics
- custom declarative rules

v0.6 Diff and Export
- full output diff
- static export
- redaction
- local help
- resume and cancellation hardening

v1.0 Hardening
- Unity version fixture corpus
- performance tuning
- large-project stress tests
- UI polish
- installer/release packaging
```

## 35. v1 Success Criteria

v1 succeeds when a developer can:

1. Open the GUI.
2. Select a Unity project root.
3. Let the tool automatically discover code, DLLs, HybridCLR, YooAsset, packages, and output.
4. Run analysis without modifying the project.
5. Browse partial results while analysis continues.
6. Search for types, assets, GUIDs, YooAsset addresses, methods, fields, and GameObjects.
7. See scenes, prefabs, components, scripts, serialized fields, and references.
8. Understand hot update assemblies and YooAsset loading paths.
9. Trace entry flows from scenes/UI/lifecycle methods into business code.
10. Compare two analysis outputs.
11. Export a redacted shareable report.
12. See exactly what was resolved, inferred, unresolved, skipped, or failed.

