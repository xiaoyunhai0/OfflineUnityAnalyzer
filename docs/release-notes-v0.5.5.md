本版修复真实项目中暴露出的两个稳定性和界面问题。

## 修复

- 修复重复源码类型名导致分析中断的问题，例如多个包或插件中同时存在 `TMPro.FastAction` 时，现在会降级为诊断信息而不是直接失败。
- 修复 GUI 复选框选中、悬停状态下文字颜色不可读的问题。

## 改进

- 重复类型会写入 `duplicate-source-type` 诊断，方便在报告中定位冲突来源。
- 增加重复类型回归测试，避免大型 Unity 项目中常见的插件重复源码再次打断分析。

## 验证

- `dotnet test OfflineUnityAnalyzer.sln`
- `dotnet build OfflineUnityAnalyzer.sln --no-restore`
