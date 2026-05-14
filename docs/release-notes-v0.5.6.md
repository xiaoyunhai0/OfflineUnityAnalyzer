本版重点修复报告可读性：默认报告改为中文“项目理解报告”，移除默认页面里难以阅读的 Relationship Graph，改用模块关系、重点类型关系和项目形态判断来帮助快速理解 Unity 项目。

## 更新内容

- 默认 `report.html` 改为中文界面，标题、导航、表格、空状态和说明文案统一中文化。
- 新增“项目形态判断”，自动说明 Unity 项目、源码目录、DLL 来源、源码/DLL 分离结构、关系覆盖和 Unity 引用覆盖。
- 新增“模块关系概览”，按模块之间的依赖方向、关系类型和代表类型流展示，不再用密集线图作为默认入口。
- 新增“重点类型关系”卡片，按继承、序列化字段、字段、属性、创建和疑似调用分组展示上下游。
- 保留完整 JSON 原始数据，但默认报告先展示可读结构，再把清单明细放到后面。
- 增加回归测试，确保报告默认中文且不再出现旧的 `Relationship Graph` 默认区块。

## 验证

- `dotnet test OfflineUnityAnalyzer.sln`
- `dotnet build OfflineUnityAnalyzer.sln --no-restore`
- `./build/package.sh`
