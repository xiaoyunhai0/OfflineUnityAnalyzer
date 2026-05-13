# OfflineUnityAnalyzer

OfflineUnityAnalyzer is a local, offline, read-only Unity project understanding tool.

The v1 product target is documented in [docs/product-architecture-v1.md](docs/product-architecture-v1.md).

## Current State

This repository is at the v0.1 skeleton stage:

- .NET 8 solution and project layout
- GUI and CLI executable projects
- core configuration models
- readonly path guard
- safe output-only file system wrapper
- analyzer pipeline contracts
- safety preflight stage
- readonly file scan stage
- placeholder viewer server, export, indexing, and app shells

## Intended Commands

```bat
OfflineUnityAnalyzer.Cli.exe analyze ^
  --unity D:\UnityProject ^
  --code D:\GameCode ^
  --dll D:\UnityProject\Assets\Plugins ^
  --out D:\AnalyzerOutput ^
  --strict-readonly
```

During v0.1 the analyzer writes:

```text
AnalyzerOutput/
  summary.json
  logs/
    readonly-preflight.txt
```

## Readonly Rule

The tool must not create, modify, or delete files in the analyzed Unity project, source roots, DLL roots, local packages, or YooAsset manifest roots.

All analysis output must go to the user-selected output directory. The output directory must not be inside any input root.

## Build

Requires .NET 8 SDK:

```bash
dotnet build OfflineUnityAnalyzer.sln
```

The current development container used to create this skeleton did not include `dotnet`, so build verification is expected to run on a machine or CI image with .NET 8 installed.
