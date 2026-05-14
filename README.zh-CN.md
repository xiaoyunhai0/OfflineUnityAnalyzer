# OfflineUnityAnalyzer

[English](README.md) | 简体中文

OfflineUnityAnalyzer 是一个离线、只读的 Unity 项目理解工具。它不会打开 Unity，不会构建目标项目，不会 restore 目标项目依赖，也不会改动源码和资源；分析结果只写入你选择的输出目录。

| 状态 | 内容 |
| --- | --- |
| 主要入口 | `OfflineUnityAnalyzer.exe` 桌面 GUI |
| 自动化入口 | `OfflineUnityAnalyzer.Cli.exe analyze` |
| 运行时 | .NET 8 |
| 当前包 | Windows x64 自包含 zip |
| 安全模型 | 读取输入，只写入用户选择的输出目录 |
| 目标 Unity 模式 | C# 工程、Unity YAML、HybridCLR、YooAsset、DLL 元数据、配置引用 |

## 目录

- [为什么需要](#为什么需要)
- [功能亮点](#功能亮点)
- [快速开始](#快速开始)
- [选择性分析](#选择性分析)
- [架构](#架构)
- [当前索引内容](#当前索引内容)
- [分析输出](#分析输出)
- [只读安全模型](#只读安全模型)
- [构建](#构建)
- [发布](#发布)
- [路线图](#路线图)

## 为什么需要

大型 Unity 项目通常会混合源码、生成的工程文件、序列化 YAML 资源、托管 DLL、热更新程序集、类似 Addressables 的资源系统和配置文件。OfflineUnityAnalyzer 的目标是在不依赖 Unity Editor、不构建目标项目的前提下，给你一张本地项目地图。

它适合回答这些问题：

- 脚本、Prefab、Scene、GUID、YooAsset 地址、热更新程序集在哪里被使用？
- 项目里有哪些模块？这些模块如何从代码、程序集、包和资源路径里推断出来？
- 在离线只读前提下，能从 C# 源码、Unity YAML、DLL 元数据、HybridCLR 线索、YooAsset Manifest、配置文件里理解到什么？

## 功能亮点

- **默认离线**：不启动 Unity Editor，不构建目标项目，不 restore，不访问网络。
- **只读保护**：输入根目录注册为只读，输出目录必须位于输入目录之外。
- **GUI 优先**：选择 Unity 根目录后自动发现路径，运行分析，打开报告。
- **CLI 自动化**：提供可重复执行的 `analyze` 和 `serve` 入口，适合本地脚本化流程。
- **Unity 语义索引**：支持 C# 类型、代码关系、项目模型文件、DLL 元数据、Unity YAML 对象、GameObject、Component、资源引用、HybridCLR 线索、YooAsset 资源、配置引用。
- **项目地图报告**：离线 HTML 报告会优先展示模块、重点类型、代码关系、Unity 绑定、资源链路和诊断信息。

## 快速开始

下载或构建 Windows 包后运行：

```text
OfflineUnityAnalyzer.exe
```

推荐 GUI 流程：

1. 选择 Unity 项目根目录。
2. 点击 `自动发现路径`。
3. 检查自动发现的 C# 代码根目录、DLL/HybridCLR 根目录、YooAsset Manifest 根目录。
4. 使用默认输出目录，或选择一个不在项目里的输出目录。
5. 点击 `开始分析`。
6. 通过右上角 `打开报告` 查看结果。

等价 CLI 示例：

```bat
OfflineUnityAnalyzer.Cli.exe analyze ^
  --unity D:\UnityProject ^
  --code D:\UnityProject\Assets ^
  --dll D:\UnityProject\Assets\Plugins ^
  --yooasset-manifest D:\UnityProject\ServerData ^
  --out D:\AnalyzerOutput ^
  --strict-readonly
```

## 选择性分析

大型项目不一定每次都需要全量分析。GUI 里新增了 `分析范围` 面板，每个分析器都可以独立开关。`快速模式` 会保留项目模型、C# 源码、HybridCLR、YooAsset 和模块推断，同时跳过更耗时的 DLL、Unity YAML 和配置扫描。

CLI 也支持同样能力：

```bat
OfflineUnityAnalyzer.Cli.exe analyze ^
  --unity D:\UnityProject ^
  --out D:\AnalyzerOutput ^
  --strict-readonly ^
  --skip-unity-yaml ^
  --skip-config
```

可用跳过参数：

| 参数 | 效果 |
| --- | --- |
| `--skip-project-model` | 跳过 `.sln`、`.csproj`、`.asmdef`、包模型解析 |
| `--skip-source` | 跳过 C# 类型/成员索引 |
| `--skip-dll` | 跳过 `.dll` 和 `.dll.bytes` 元数据索引 |
| `--skip-unity-yaml` | 跳过 Scene、Prefab 和 Unity Asset YAML 解析 |
| `--skip-hybridclr` | 跳过 HybridCLR 线索检测 |
| `--skip-yooasset` | 跳过 YooAsset 线索、Manifest 和代码引用扫描 |
| `--skip-config` | 跳过 JSON、CSV、XML、可读 `.bytes` 浅层引用扫描 |
| `--skip-modules` | 跳过模块推断 |
| `--skip-report` | 跳过离线报告导出 |

## 架构

```mermaid
flowchart TB
    subgraph Entry["入口层"]
        App["OfflineUnityAnalyzer.App<br/>Avalonia GUI"]
        Cli["OfflineUnityAnalyzer.Cli<br/>analyze / serve"]
    end

    subgraph Safety["安全边界"]
        Guard["只读路径保护"]
        SafeFs["安全输出文件系统"]
    end

    subgraph Pipeline["分析流水线"]
        Preflight["只读预检"]
        FileScan["文件扫描"]
        ProjectModel["项目模型"]
        Source["C# 源码索引"]
        Dll["DLL 元数据索引"]
        UnityYaml["Unity YAML 索引"]
        HotUpdate["HybridCLR / YooAsset"]
        Config["配置引用索引"]
        Modules["模块推断"]
        Export["报告导出"]
    end

    subgraph Outputs["生成结果"]
        Json["JSON 数据文件"]
        Html["离线 HTML 报告"]
        Logs["只读预检日志"]
        Viewer["本地 Viewer Server<br/>127.0.0.1"]
    end

    App -->|启动随包 CLI Worker| Cli
    Cli --> Guard
    Guard --> Preflight
    SafeFs --> Export
    Preflight --> FileScan --> ProjectModel --> Source --> Dll --> UnityYaml --> HotUpdate --> Config --> Modules --> Export
    Export --> Json
    Export --> Html
    Export --> Logs
    Cli --> Viewer
```

| 层级 | 项目 | 职责 |
| --- | --- | --- |
| 桌面端 | `OfflineUnityAnalyzer.App` | Avalonia 桌面壳、路径发现、进度展示、打开报告 |
| 命令行 | `OfflineUnityAnalyzer.Cli` | `analyze`、`serve`，以及计划中的 `diff`、`export` 命令 |
| 核心 | `OfflineUnityAnalyzer.Core` | 共享模型、配置、流水线契约、只读安全 |
| 分析 | `OfflineUnityAnalyzer.Analyzers` | 文件扫描、C# 源码、DLL、Unity YAML、HybridCLR、YooAsset、配置、模块、报告阶段 |
| 导出 | `OfflineUnityAnalyzer.ReportExport` | 离线静态报告生成 |
| 查看器 | `OfflineUnityAnalyzer.ViewerServer` | 本地只读 `127.0.0.1` Viewer API 壳 |
| 索引 | `OfflineUnityAnalyzer.Indexing` | 后续索引元数据和查询存储 |

## 当前索引内容

| 领域 | 当前线索 |
| --- | --- |
| C# 源码 | 类型名、命名空间、成员、序列化字段、MonoBehaviour/ScriptableObject 线索和推断出的类型关系 |
| 项目模型 | `.sln`、`.csproj`、`.asmdef`、`.asmref`、`Packages/manifest.json`、package lock |
| 托管程序集 | `.dll`、`.dll.bytes`、程序集名、版本、公钥 Token、热更新线索 |
| Unity 资源 | Scene、Prefab、Asset、Controller、Material、Animation、`.meta` GUID 数据 |
| 序列化对象 | Unity YAML 对象 ID、GameObject、Component、脚本引用、资源引用 |
| HybridCLR | 热更新和 AOT metadata 目录/文件线索 |
| YooAsset | Manifest 资源、包名、地址、资源路径、Bundle、Tag、C# 加载 API 字符串 |
| 配置文件 | JSON、CSV、XML、可读 `.bytes` 文件中的浅层引用 |

## 分析输出

分析过程只写入用户选择的输出目录：

```text
AnalyzerOutput/
  summary.json
  data/
    types.json
    source-relations.json
    project-model.json
    modules.json
    assemblies.json
    unity-script-refs.json
    unity-objects.json
    unity-gameobjects.json
    unity-components.json
    unity-asset-refs.json
    hybridclr.json
    yooasset.json
    config-refs.json
    diagnostics.json
  report/
    report.html
  logs/
    readonly-preflight.txt
```

## 只读安全模型

OfflineUnityAnalyzer 禁止在以下目录中创建、修改或删除文件：

- 被分析的 Unity 项目
- 源码根目录
- DLL 根目录
- 本地包或嵌入包
- YooAsset Manifest 根目录

所有生成文件都写入用户选择的输出目录。输出目录不能位于任何输入根目录内，输入根目录也不能位于输出目录内。

GUI 可以在系统正常应用数据目录保存自己的设置，但不会把分析数据写进目标项目。

## 构建

需要 .NET 8 SDK。

```bash
dotnet build OfflineUnityAnalyzer.sln
dotnet test OfflineUnityAnalyzer.sln --no-build
```

创建本地 Windows 包：

```bash
./build/package.sh
```

生成位置：

```text
publish/OfflineUnityAnalyzer-win-x64.zip
```

## 发布

GitHub Release 包由 `.github/workflows/release.yml` 构建。

```bash
git tag -a v0.5.0 -m "发布 v0.5.0"
git push origin main --tags
```

发布说明位于 `docs/release-notes-v*.md`。

## 路线图

v1 产品架构记录在 [docs/product-architecture-v1.md](docs/product-architecture-v1.md)。后续计划包括更强搜索、Viewer Server API、图导航、Prefab 合并视图、诊断、Diff 和静态导出增强。
