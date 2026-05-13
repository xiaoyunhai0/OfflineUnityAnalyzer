# OfflineUnityAnalyzer 开发文档

## 1. 项目目标

OfflineUnityAnalyzer 是一个面向大型 Unity 项目的本地离线只读分析工具。

工具目标是帮助开发者在不修改项目、不打开 Unity、不编译业务代码、不联网的前提下，快速理解 Unity 项目的代码结构、DLL 结构、资源引用关系和模块依赖关系。

本工具主要用于以下场景：

- 新人接手大型 Unity 项目时快速理解代码框架
- C# 业务代码不直接放在 Unity 工程中，而是独立 VS Project 编译成 DLL 后供 Unity 使用
- 离线开发环境无法访问 NuGet、GitHub、CDN 或外部服务
- 公司要求分析工具只能读取源码、DLL、Unity 资源，不能影响原项目
- 工具在云端服务器开发，并通过 GitHub 自动构建 release 包，离线环境下载 zip 后直接使用

---

## 2. 核心原则

### 2.1 完全离线

离线环境运行时不得依赖：

- Git
- Unity Editor
- Visual Studio
- dotnet SDK
- NuGet
- Node.js
- Python
- 网络
- CDN
- 数据库服务

运行包应当是便携式的：

```text
ProjectAnalyzer-win-x64.zip
  ├── ProjectAnalyzer.exe
  ├── templates/
  ├── assets/
  ├── config.sample.json
  ├── README.md
  └── licenses/
```

### 2.2 严格只读

工具不得：

- 修改源码目录
- 修改 Unity 工程目录
- 修改 `.cs` 文件
- 修改 `.meta` 文件
- 修改 `.prefab` 文件
- 修改 `.unity` 文件
- 修改 `.asset` 文件
- 修改 `.csproj` / `.sln`
- 运行 Unity Editor
- 编译业务项目
- 执行业务 DLL 中的代码
- 联网

工具只允许写入用户明确指定的输出目录。

### 2.3 分析与展示分离

工具应分为两层：

```text
AnalyzerCore / AnalyzerCli
  负责扫描、分析、建索引、生成报告

HTML Report / 后续 VSCode 插件
  负责搜索、展示、跳转、图谱查看
```

第一版优先实现独立 CLI 和本地 HTML 报告。VSCode 插件作为后续增强，不应阻塞核心分析工具开发。

---

## 3. 用户使用流程

### 3.1 云端开发流程

```text
开发者在云端服务器开发
  ↓
提交代码到 GitHub
  ↓
GitHub Actions 自动构建
  ↓
生成 ProjectAnalyzer-win-x64.zip
  ↓
发布到 GitHub Release 或 Actions Artifact
```

### 3.2 离线环境使用流程

```text
下载 ProjectAnalyzer-win-x64.zip
  ↓
拷贝到离线环境
  ↓
解压
  ↓
编辑配置文件或命令行传参
  ↓
运行 ProjectAnalyzer.exe
  ↓
打开输出目录中的 report.html
```

示例命令：

```bat
ProjectAnalyzer.exe ^
  --code D:\GameCode ^
  --sln D:\GameCode\GameLogic.sln ^
  --unity D:\UnityProject ^
  --dll D:\UnityProject\Assets\Plugins ^
  --out D:\AnalyzerOutput ^
  --strict-readonly
```

或者：

```bat
ProjectAnalyzer.exe --config D:\AnalyzerConfig\project-analyzer.json
```

---

## 4. 技术选型

### 4.1 开发语言

推荐：

```text
C# / .NET 8
```

原因：

- 项目目标是分析 C# / Unity / DLL
- Roslyn、Mono.Cecil、SQLite、YamlDotNet 均适合 .NET 生态
- 可发布 self-contained 独立 exe
- 便于后续接入 VSCode 插件或本地 Web UI

### 4.2 主要依赖

```text
Microsoft.CodeAnalysis.CSharp
Microsoft.CodeAnalysis.CSharp.Workspaces
Mono.Cecil
YamlDotNet
Microsoft.Data.Sqlite
System.CommandLine
```

可选依赖：

```text
Serilog
Spectre.Console
Dapper
```

前端本地资源：

```text
Mermaid.js
Cytoscape.js
本地 CSS / JS
```

注意：HTML 报告中不得引用 CDN，所有 JS/CSS 必须随 zip 包一起发布。

---

## 5. 仓库结构

建议仓库结构如下：

```text
OfflineUnityAnalyzer/
├── .github/
│   └── workflows/
│       └── release.yml
│
├── src/
│   ├── AnalyzerCore/
│   │   ├── AnalyzerCore.csproj
│   │   ├── Configuration/
│   │   ├── Safety/
│   │   ├── FileScanning/
│   │   ├── SourceAnalysis/
│   │   ├── AssemblyAnalysis/
│   │   ├── UnityAnalysis/
│   │   ├── Indexing/
│   │   ├── Reporting/
│   │   └── Models/
│   │
│   └── AnalyzerCli/
│       ├── AnalyzerCli.csproj
│       └── Program.cs
│
├── templates/
│   ├── report.html
│   ├── types.html
│   ├── type_detail.html
│   ├── inheritance.html
│   ├── interfaces.html
│   ├── unity_refs.html
│   └── modules.html
│
├── assets/
│   ├── style.css
│   ├── app.js
│   ├── mermaid.min.js
│   └── cytoscape.min.js
│
├── docs/
│   ├── usage.md
│   ├── readonly-policy.md
│   ├── data-model.md
│   └── development.md
│
├── samples/
│   └── config.sample.json
│
├── build/
│   ├── package.ps1
│   └── package.sh
│
├── tests/
│   ├── AnalyzerCore.Tests/
│   └── TestProjects/
│
├── OfflineUnityAnalyzer.sln
├── README.md
└── LICENSE
```

---

## 6. 模块设计

## 6.1 AnalyzerCli

职责：

- 解析命令行参数
- 读取配置文件
- 初始化日志
- 创建输出目录
- 启动 AnalyzerCore
- 输出扫描摘要
- 返回正确的进程退出码

示例参数：

```text
--config <path>              配置文件路径
--code <path>                C# 源码根目录，可重复
--sln <path>                 Solution 文件路径，可选
--unity <path>               Unity 工程根目录
--dll <path>                 DLL 根目录，可重复
--out <path>                 输出目录
--strict-readonly            启用严格只读模式
--no-call-graph              跳过方法调用图
--include-generated          包含 Generated 目录
--verbose                    输出详细日志
```

退出码约定：

```text
0  成功
1  参数错误
2  输入路径不存在
3  只读策略违规
4  分析过程中发生非致命错误，但有输出
5  分析失败，无有效输出
```

---

## 6.2 AnalyzerCore

AnalyzerCore 是核心库，不直接处理命令行和 UI。

核心入口：

```csharp
public sealed class ProjectAnalyzer
{
    public Task<AnalyzeResult> AnalyzeAsync(AnalyzeConfig config, CancellationToken cancellationToken);
}
```

核心流程：

```text
1. 校验配置
2. 初始化只读安全策略
3. 扫描输入文件
4. 分析 C# 源码
5. 分析 DLL 元数据
6. 分析 Unity YAML 资源
7. 建立跨域映射
8. 写入 SQLite 索引
9. 生成 JSON / HTML 报告
10. 输出扫描摘要
```

---

## 6.3 Safety 模块

职责：保证工具只读。

### 6.3.1 PathGuard

要求：

- 所有输入目录登记为 readonly roots
- 输出目录登记为唯一 writable root
- 所有写文件操作必须通过 SafeFileSystem
- 禁止直接使用 `File.WriteAllText`、`File.Create`、`Directory.CreateDirectory` 写入输入目录

示例接口：

```csharp
public interface ISafeFileSystem
{
    string ReadAllText(string path);
    byte[] ReadAllBytes(string path);
    Stream OpenRead(string path);
    void WriteAllTextToOutput(string relativePath, string content);
    void WriteBytesToOutput(string relativePath, byte[] bytes);
}
```

路径规则：

```text
允许读取：codeRoots、unityProject、dllRoots
允许写入：outputRoot
禁止写入：codeRoots、unityProject、dllRoots
禁止写入：未声明的任意目录
```

### 6.3.2 外部进程禁用

Strict Readonly 模式下禁止调用：

- Unity
- MSBuild
- dotnet build
- git
- npm
- powershell 脚本
- 任意外部命令

工具内部不应依赖外部进程完成核心分析。

### 6.3.3 网络禁用原则

工具代码中不应出现：

- HttpClient 主动请求外网
- WebClient
- Socket 连接
- 在线 CDN 引用
- 自动更新检查

---

## 6.4 FileScanning 模块

职责：只读扫描文件。

扫描范围：

```text
C# 源码：
- **/*.cs
- **/*.csproj
- **/*.sln
- **/*.asmdef

DLL：
- **/*.dll

Unity 资源：
- Assets/**/*.prefab
- Assets/**/*.unity
- Assets/**/*.asset
- Assets/**/*.controller
- Assets/**/*.overrideController
- Assets/**/*.playable
- Assets/**/*.anim
- Assets/**/*.mat
- Assets/**/*.meta
- ProjectSettings/*.asset
- Packages/manifest.json
```

默认排除：

```text
.git
.vs
.idea
bin
obj
Library
Temp
Logs
Build
Builds
Generated
node_modules
```

扫描结果模型：

```csharp
public sealed record ProjectFile(
    string FullPath,
    string RelativePath,
    ProjectFileKind Kind,
    long SizeBytes,
    DateTime LastWriteTimeUtc
);
```

---

## 6.5 SourceAnalysis 模块

职责：分析 C# 源码。

第一版不要求完整语义分析，优先做稳定的语法分析。

### 第一阶段能力

- namespace 收集
- class / interface / struct / enum 收集
- partial class 合并标记
- base class 收集
- interface 实现收集
- attribute 收集
- method / property / field 收集
- using 收集
- 文件路径到类型映射

### 第二阶段能力

- 类型引用关系
- 方法调用关系
- 事件订阅关系
- 泛型约束分析
- async / await 方法标记
- Unity 生命周期方法标记

### 推荐实现方式

```text
Microsoft.CodeAnalysis.CSharp
CSharpSyntaxTree.ParseText
SyntaxWalker / SyntaxVisitor
```

不要在第一版强依赖 `MSBuildWorkspace`，因为它可能需要 MSBuild、SDK 和 restore 环境。可以后续作为可选增强。

---

## 6.6 AssemblyAnalysis 模块

职责：分析 DLL 元数据。

推荐使用 Mono.Cecil，不使用 Reflection。

原因：

- 不加载业务 DLL
- 不执行静态构造函数
- 不依赖运行时解析全部依赖
- 更适合离线只读分析

分析内容：

- Assembly 名称
- Assembly 引用
- TypeDef 列表
- BaseType
- Interfaces
- Methods
- Fields
- Properties
- Events
- Attributes
- IL 中的方法调用引用
- 是否继承 MonoBehaviour
- 是否继承 ScriptableObject
- 是否继承 EditorWindow
- 是否继承 StateMachineBehaviour

Unity 类型判断：

```text
BaseType 链中出现：
- UnityEngine.MonoBehaviour
- UnityEngine.ScriptableObject
- UnityEditor.EditorWindow
- UnityEngine.StateMachineBehaviour
```

注意：如果 UnityEngine.dll 不存在，也应尽量通过类型全名字符串判断。

---

## 6.7 UnityAnalysis 模块

职责：只读解析 Unity 项目文件。

### 6.7.1 目标文件

```text
.prefab
.unity
.asset
.controller
.overrideController
.playable
.anim
.mat
.meta
ProjectSettings/EditorBuildSettings.asset
Packages/manifest.json
```

### 6.7.2 重点解析内容

- Asset GUID
- m_Script 引用
- m_EditorClassIdentifier
- MonoBehaviour 块
- Scene 中 GameObject / Component 关系
- Prefab 中 GameObject / Component 关系
- ScriptableObject asset 实例
- EditorBuildSettings 中启动场景列表

### 6.7.3 DLL 脚本映射

普通 `.cs` 脚本引用通常依赖 `.cs.meta` 中的 guid。

DLL 中的脚本类型可能通过以下方式出现：

```text
m_Script guid + fileID
m_EditorClassIdentifier
AssemblyName Namespace.TypeName
```

第一版策略：

```text
1. 收集所有 m_Script 引用
2. 收集所有 m_EditorClassIdentifier
3. 结合 DLL 中的 TypeFullName 和 AssemblyName 尝试匹配
4. 匹配失败时记录 unresolved_refs
```

不要为了 100% 准确而打开 Unity Editor。

---

## 6.8 Indexing 模块

职责：将分析结果写入 SQLite。

### 6.8.1 输出文件

```text
index.db
summary.json
graph.json
unresolved_refs.json
```

### 6.8.2 SQLite 表设计初版

```sql
CREATE TABLE files (
    id INTEGER PRIMARY KEY,
    path TEXT NOT NULL,
    relative_path TEXT,
    kind TEXT,
    size_bytes INTEGER,
    last_write_time_utc TEXT
);

CREATE TABLE assemblies (
    id INTEGER PRIMARY KEY,
    name TEXT NOT NULL,
    path TEXT
);

CREATE TABLE types (
    id INTEGER PRIMARY KEY,
    full_name TEXT NOT NULL,
    namespace TEXT,
    name TEXT NOT NULL,
    kind TEXT,
    assembly_name TEXT,
    source_file TEXT,
    is_partial INTEGER,
    is_mono_behaviour INTEGER,
    is_scriptable_object INTEGER,
    is_editor_type INTEGER
);

CREATE TABLE members (
    id INTEGER PRIMARY KEY,
    type_id INTEGER,
    name TEXT,
    kind TEXT,
    signature TEXT,
    visibility TEXT,
    is_static INTEGER,
    FOREIGN KEY(type_id) REFERENCES types(id)
);

CREATE TABLE inheritance_edges (
    id INTEGER PRIMARY KEY,
    derived_type TEXT NOT NULL,
    base_type TEXT NOT NULL
);

CREATE TABLE interface_edges (
    id INTEGER PRIMARY KEY,
    implementation_type TEXT NOT NULL,
    interface_type TEXT NOT NULL
);

CREATE TABLE type_references (
    id INTEGER PRIMARY KEY,
    from_type TEXT NOT NULL,
    to_type TEXT NOT NULL,
    reference_kind TEXT
);

CREATE TABLE method_calls (
    id INTEGER PRIMARY KEY,
    caller_type TEXT,
    caller_method TEXT,
    callee_type TEXT,
    callee_method TEXT,
    confidence TEXT
);

CREATE TABLE unity_assets (
    id INTEGER PRIMARY KEY,
    path TEXT NOT NULL,
    guid TEXT,
    asset_kind TEXT
);

CREATE TABLE unity_script_refs (
    id INTEGER PRIMARY KEY,
    asset_path TEXT NOT NULL,
    script_guid TEXT,
    file_id TEXT,
    editor_class_identifier TEXT,
    resolved_type TEXT,
    resolved_assembly TEXT,
    confidence TEXT
);

CREATE TABLE modules (
    id INTEGER PRIMARY KEY,
    name TEXT NOT NULL,
    rule TEXT
);

CREATE TABLE module_edges (
    id INTEGER PRIMARY KEY,
    from_module TEXT NOT NULL,
    to_module TEXT NOT NULL,
    weight INTEGER
);

CREATE TABLE unresolved_refs (
    id INTEGER PRIMARY KEY,
    source_path TEXT,
    ref_kind TEXT,
    raw_value TEXT,
    reason TEXT
);
```

---

## 6.9 Reporting 模块

职责：生成离线 HTML 报告。

输出：

```text
report.html
assets/
  style.css
  app.js
  mermaid.min.js
  cytoscape.min.js
data/
  summary.json
  types.json
  inheritance.json
  interfaces.json
  unity_refs.json
  modules.json
  unresolved_refs.json
```

### 首页内容

- 项目摘要
- 扫描时间
- 文件数量
- 类型数量
- DLL 数量
- MonoBehaviour 数量
- ScriptableObject 数量
- Prefab / Scene 数量
- 未解析引用数量
- 模块依赖概览

### 类型详情页

每个类型展示：

- FullName
- Namespace
- Source File
- Assembly
- Base Type
- Interfaces
- Members
- Attributes
- Unity References
- Dependent Types
- Depended By

### Unity 引用页

展示：

- Prefab 使用了哪些脚本
- Scene 使用了哪些脚本
- ScriptableObject asset 对应哪些类型
- 未解析脚本引用

### 模块图页

展示：

- 模块列表
- 模块之间的依赖边
- 权重
- 高耦合模块提示

---

## 7. 配置文件设计

示例 `config.sample.json`：

```json
{
  "codeRoots": [
    "D:/GameCode"
  ],
  "solutionPath": "D:/GameCode/GameLogic.sln",
  "unityProject": "D:/UnityProject",
  "dllRoots": [
    "D:/UnityProject/Assets/Plugins"
  ],
  "output": "D:/AnalyzerOutput",
  "strictReadonly": true,
  "analyzeCallGraph": false,
  "excludePatterns": [
    ".git",
    ".vs",
    ".idea",
    "bin",
    "obj",
    "Library",
    "Temp",
    "Logs",
    "Build",
    "Builds",
    "Generated",
    "node_modules"
  ],
  "moduleRules": [
    {
      "name": "Battle",
      "namespacePrefix": "Game.Battle"
    },
    {
      "name": "UI",
      "namespacePrefix": "Game.UI"
    },
    {
      "name": "Network",
      "namespacePrefix": "Game.Network"
    },
    {
      "name": "Config",
      "namespacePrefix": "Game.Config"
    }
  ],
  "report": {
    "generateHtml": true,
    "generateJson": true,
    "openAfterFinish": false
  }
}
```

---

## 8. 自动打包设计

## 8.1 发布目标

第一版只要求 Windows x64：

```text
ProjectAnalyzer-win-x64.zip
```

后续可扩展：

```text
ProjectAnalyzer-linux-x64.tar.gz
ProjectAnalyzer-osx-x64.tar.gz
```

## 8.2 dotnet publish 命令

Windows x64 self-contained single-file：

```bash
dotnet publish src/AnalyzerCli/AnalyzerCli.csproj \
  -c Release \
  -r win-x64 \
  --self-contained true \
  /p:PublishSingleFile=true \
  /p:IncludeNativeLibrariesForSelfExtract=true \
  /p:EnableCompressionInSingleFile=true \
  -o publish/win-x64
```

发布目录需要额外复制：

```text
templates/
assets/
samples/config.sample.json
README.md
licenses/
```

最终 zip 结构：

```text
ProjectAnalyzer-win-x64/
├── ProjectAnalyzer.exe
├── templates/
├── assets/
├── config.sample.json
├── README.md
└── licenses/
```

---

## 8.3 GitHub Actions 示例

`.github/workflows/release.yml`：

```yaml
name: Build Release

on:
  push:
    tags:
      - "v*.*.*"
  workflow_dispatch:

jobs:
  build-win-x64:
    runs-on: windows-latest

    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "8.0.x"

      - name: Restore
        run: dotnet restore OfflineUnityAnalyzer.sln

      - name: Test
        run: dotnet test OfflineUnityAnalyzer.sln -c Release --no-restore

      - name: Publish win-x64
        run: >
          dotnet publish src/AnalyzerCli/AnalyzerCli.csproj
          -c Release
          -r win-x64
          --self-contained true
          /p:PublishSingleFile=true
          /p:IncludeNativeLibrariesForSelfExtract=true
          /p:EnableCompressionInSingleFile=true
          -o publish/ProjectAnalyzer-win-x64

      - name: Prepare package files
        shell: pwsh
        run: |
          Copy-Item -Recurse templates publish/ProjectAnalyzer-win-x64/templates
          Copy-Item -Recurse assets publish/ProjectAnalyzer-win-x64/assets
          Copy-Item samples/config.sample.json publish/ProjectAnalyzer-win-x64/config.sample.json
          Copy-Item README.md publish/ProjectAnalyzer-win-x64/README.md
          if (Test-Path licenses) {
            Copy-Item -Recurse licenses publish/ProjectAnalyzer-win-x64/licenses
          }
          Rename-Item publish/ProjectAnalyzer-win-x64/AnalyzerCli.exe ProjectAnalyzer.exe

      - name: Zip package
        shell: pwsh
        run: |
          Compress-Archive -Path publish/ProjectAnalyzer-win-x64 -DestinationPath publish/ProjectAnalyzer-win-x64.zip -Force

      - name: Upload artifact
        uses: actions/upload-artifact@v4
        with:
          name: ProjectAnalyzer-win-x64
          path: publish/ProjectAnalyzer-win-x64.zip

      - name: Create GitHub Release
        if: startsWith(github.ref, 'refs/tags/')
        uses: softprops/action-gh-release@v2
        with:
          files: publish/ProjectAnalyzer-win-x64.zip
```

---

## 9. 开发里程碑

## Milestone 0：项目骨架

目标：搭建基础仓库和自动打包。

任务：

- 创建 solution
- 创建 AnalyzerCore 项目
- 创建 AnalyzerCli 项目
- 创建测试项目
- 创建 config.sample.json
- 创建 GitHub Actions
- 验证能生成 ProjectAnalyzer-win-x64.zip

验收标准：

- GitHub Actions 可以手动触发
- 能生成 zip
- zip 解压后可以运行 `ProjectAnalyzer.exe --help`

---

## Milestone 1：只读文件扫描

目标：完成只读扫描和安全策略。

任务：

- 实现配置解析
- 实现 PathGuard
- 实现 SafeFileSystem
- 实现文件枚举
- 实现排除规则
- 输出 scan_summary.json

验收标准：

- 能扫描 code / unity / dll 目录
- 不会写入输入目录
- 只会写入 output 目录
- 输入目录写入测试必须失败

---

## Milestone 2：C# 源码类型索引

目标：建立源码类型索引。

任务：

- 解析 `.cs` 文件
- 收集 namespace
- 收集 class / interface / struct / enum
- 收集 base type
- 收集 interfaces
- 收集 attributes
- 收集 members
- 输出 types.json

验收标准：

- 能根据类名搜索源码位置
- 能看到继承关系
- 能看到接口实现关系

---

## Milestone 3：DLL 元数据分析

目标：读取业务 DLL 元数据。

任务：

- 使用 Mono.Cecil 读取 DLL
- 收集程序集名称
- 收集类型
- 收集继承关系
- 判断 MonoBehaviour / ScriptableObject
- 输出 assemblies.json / dll_types.json

验收标准：

- 不使用 Reflection 加载业务 DLL
- 能列出 DLL 中的 Unity 类型
- 能把 DLL 类型和源码类型按 FullName 关联

---

## Milestone 4：Unity YAML 分析

目标：只读分析 Unity 资源引用。

任务：

- 扫描 prefab / scene / asset / meta
- 解析 guid
- 解析 m_Script
- 解析 m_EditorClassIdentifier
- 尝试映射到 DLL 类型或源码类型
- 输出 unity_refs.json / unresolved_refs.json

验收标准：

- 能列出 Prefab / Scene 使用了哪些脚本
- 能列出某个 MonoBehaviour 被哪些资源引用
- 解析失败的引用不会丢失，会进入 unresolved_refs

---

## Milestone 5：SQLite 索引

目标：将所有结果写入 index.db。

任务：

- 创建 SQLite schema
- 写入 files / types / assemblies
- 写入 inheritance_edges / interface_edges
- 写入 unity_assets / unity_script_refs
- 写入 unresolved_refs
- 提供基本查询接口

验收标准：

- index.db 可被 sqlite 工具打开
- CLI 能从 index.db 查询类型详情
- HTML 报告能读取 JSON 或 SQLite 导出的数据

---

## Milestone 6：HTML 报告

目标：生成可离线浏览的报告。

任务：

- 生成 report.html
- 生成类型列表页
- 生成类型详情页
- 生成继承关系页
- 生成接口实现页
- 生成 Unity 引用页
- 生成模块依赖页
- 所有 JS/CSS 本地化

验收标准：

- 离线浏览器打开 report.html 正常显示
- 不访问网络
- 可以搜索类型
- 可以查看 Unity 引用
- 可以查看模块图

---

## Milestone 7：增量分析与性能优化

目标：支持大项目。

任务：

- 文件 hash 缓存
- 增量扫描
- 大文件跳过或降级解析
- 并行扫描
- 分阶段进度显示
- 日志压缩

验收标准：

- 大型项目首次扫描可接受
- 第二次扫描明显更快
- 内存占用稳定

---

## Milestone 8：VSCode 插件，可选

目标：将本地工具集成到 VSCode。

任务：

- VSCode 插件调用 ProjectAnalyzer.exe
- 读取 index.db 或 JSON
- 搜索类型
- 当前文件详情
- 跳转源码
- 显示 Unity 引用

验收标准：

- 插件不做重分析
- 插件只是 AnalyzerCli 的前端
- 插件可选安装，不影响 CLI 使用

---

## 10. 测试策略

### 10.1 单元测试

测试范围：

- 配置解析
- PathGuard
- 文件扫描
- C# 类型解析
- DLL 元数据解析
- Unity YAML 解析
- SQLite 写入

### 10.2 集成测试

构造一个小型 Unity-like 测试项目：

```text
TestProject/
├── Code/
│   ├── GameLogic.sln
│   └── PlayerController.cs
├── UnityProject/
│   ├── Assets/
│   │   ├── Prefabs/Player.prefab
│   │   └── Scenes/Battle.unity
│   └── ProjectSettings/EditorBuildSettings.asset
└── Dlls/
    └── GameLogic.dll
```

验证：

- 能解析类型
- 能解析继承
- 能解析 DLL
- 能解析 Prefab 引用
- 能生成报告

### 10.3 只读安全测试

必须包含测试：

- 输出目录在输入目录内时拒绝运行
- 尝试写入源码目录时报错
- 尝试写入 Unity 目录时报错
- StrictReadonly 下禁止外部进程
- 报告资源不会写入 Unity 工程

---

## 11. 性能要求

第一版建议目标：

```text
10 万行 C#：可正常分析
100 万行 C#：可完成基础类型索引
数千个 prefab / scene / asset：可完成 YAML 引用扫描
数百 MB DLL：分批读取，失败不阻断全局
```

性能策略：

- 文件扫描使用流式处理
- 大文件设置阈值
- 解析失败单文件降级，不中断全局
- DLL 逐个处理
- HTML 报告数据分页或分文件输出
- method call graph 默认关闭

---

## 12. 日志设计

输出日志：

```text
scan_log.txt
errors.json
warnings.json
summary.json
```

日志内容：

- 工具版本
- 运行时间
- 输入目录
- 输出目录
- strict readonly 状态
- 扫描文件数
- 解析成功数
- 解析失败数
- 未解析 Unity 引用数
- 错误详情

日志中不得记录敏感源码内容，只记录路径、类型名和错误摘要。

---

## 13. CLI 输出示例

```text
OfflineUnityAnalyzer v0.1.0

Readonly mode: enabled
External process execution: disabled
Network access: disabled

Input roots:
  Code:  D:\GameCode
  Unity: D:\UnityProject
  DLL:   D:\UnityProject\Assets\Plugins

Output root:
  D:\AnalyzerOutput

Scanning files...
  C# files:       12843
  Unity assets:   52319
  DLL files:      37

Analyzing source...
  Types:          18422
  Interfaces:     1130
  Inheritance:    9211

Analyzing assemblies...
  Assemblies:     37
  DLL types:      14580
  MonoBehaviours: 812
  ScriptableObjects: 216

Analyzing Unity assets...
  Script refs:    6423
  Resolved:       5901
  Unresolved:     522

Generating report...
  D:\AnalyzerOutput\report.html

Done.
```

---

## 14. 第一版不做的事情

第一版明确不做：

- 不打开 Unity Editor
- 不调用 Unity batchmode
- 不编译业务代码
- 不生成或修改 `.csproj`
- 不做完整调用图
- 不做运行时行为分析
- 不解析 Lua / 热更脚本，除非后续单独设计
- 不做 AI 语义理解
- 不上传任何文件
- 不做在线更新
- 不做自动修复代码

---

## 15. 后续扩展方向

可选增强：

- VSCode 插件
- 增量索引
- 方法调用图
- 事件总线订阅关系分析
- 反射字符串类型引用分析
- 配表驱动类型引用分析
- Lua / 热更代码索引
- 本地 Web Server 模式
- 模块耦合评分
- 废弃代码候选分析
- 启动链路推断
- Unity Editor 副本分析模式

注意：任何增强都不能破坏默认 Strict Readonly 模式。

---

## 16. 开发优先级总结

建议按以下顺序开发：

```text
1. 项目骨架 + GitHub Actions 自动打包
2. CLI 参数和配置系统
3. Strict Readonly 安全层
4. 文件扫描器
5. C# 类型索引
6. DLL 元数据分析
7. Unity YAML 脚本引用分析
8. SQLite 索引
9. HTML 离线报告
10. 性能优化和增量分析
11. VSCode 插件
```

第一阶段验收目标：

```text
下载 ProjectAnalyzer-win-x64.zip
解压后无需安装任何依赖
运行 ProjectAnalyzer.exe --help 成功
运行 ProjectAnalyzer.exe --config config.json 成功
只写 output 目录
生成 report.html
离线浏览 report.html 正常
```

---

## 17. 最小可行版本 MVP

MVP 只做以下功能：

- CLI 可运行
- 自动打包 zip
- 只读路径保护
- 扫描 `.cs`
- 扫描 `.dll`
- 扫描 `.prefab` / `.unity` / `.asset`
- 生成类型列表
- 生成继承关系
- 生成接口实现关系
- 生成 Unity 脚本引用表
- 生成未解析引用表
- 生成离线 HTML 报告

MVP 成功标准：

```text
用户可以在离线环境中直接运行 exe，
然后通过 report.html 查到：

1. 某个类在哪里
2. 它继承谁
3. 谁实现了某个接口
4. 哪些 MonoBehaviour 存在于 DLL 中
5. 哪些 prefab / scene 引用了某个脚本
6. 哪些 Unity 引用未解析
```

