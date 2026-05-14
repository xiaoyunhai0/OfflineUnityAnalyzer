using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace OfflineUnityAnalyzer.App;

public partial class MainWindow : Window
{
    private static readonly string[] StageOrder =
    {
        "SafetyPreflight",
        "FileScan",
        "ProjectModel",
        "SourceSyntaxIndex",
        "DllIndex",
        "UnityYamlRawIndex",
        "HybridClrIndex",
        "YooAssetIndex",
        "ConfigShallowIndex",
        "ModuleInference",
        "ReportExport"
    };

    private readonly ObservableCollection<StageViewModel> _stages;
    private readonly Dictionary<string, StageViewModel> _stageMap;
    private readonly HashSet<string> _autoFilledTargets = new(StringComparer.OrdinalIgnoreCase);
    private bool _isUpdatingInputs;
    private bool _isUpdatingStageOptions;
    private string? _lastOutputDirectory;
    private CancellationTokenSource? _analysisCancellation;
    private Process? _runningProcess;

    public MainWindow()
    {
        InitializeComponent();

        _stages = new ObservableCollection<StageViewModel>(
            StageOrder.Select(stage => new StageViewModel(DisplayStageName(stage))));
        _stageMap = StageOrder
            .Zip(_stages)
            .ToDictionary(pair => pair.First, pair => pair.Second, StringComparer.OrdinalIgnoreCase);

        StageList.ItemsSource = _stages;
        ProgressBar.IsVisible = false;
        UpdateStageProgress();
        UpdateInputSummary();
        UpdateGuardState();
        UpdateStageOptionSummary();
    }

    private async void BrowseUnityButton_Click(object? sender, RoutedEventArgs e)
    {
        await PickFolderIntoAsync(UnityPathBox, "选择 Unity 项目根目录");
        DiscoverFromUnityRoot(applySuggestions: true);
    }

    private async void BrowseCodeButton_Click(object? sender, RoutedEventArgs e)
    {
        await PickFolderIntoAsync(CodePathBox, "追加 C# 代码根目录", append: true);
    }

    private async void BrowseDllButton_Click(object? sender, RoutedEventArgs e)
    {
        await PickFolderIntoAsync(DllPathBox, "追加 DLL / HybridCLR 根目录", append: true);
    }

    private async void BrowseYooAssetButton_Click(object? sender, RoutedEventArgs e)
    {
        await PickFolderIntoAsync(YooAssetPathBox, "追加 YooAsset Manifest 根目录", append: true);
    }

    private async void BrowseOutputButton_Click(object? sender, RoutedEventArgs e)
    {
        await PickFolderIntoAsync(OutputPathBox, "选择分析输出目录");
    }

    private async void AnalyzeButton_Click(object? sender, RoutedEventArgs e)
    {
        await RunAnalysisAsync();
    }

    private void AutoDiscoverButton_Click(object? sender, RoutedEventArgs e)
    {
        DiscoverFromUnityRoot(applySuggestions: true);
    }

    private void EnableAllStagesButton_Click(object? sender, RoutedEventArgs e)
    {
        SetStageOptions(
            projectModel: true,
            source: true,
            dll: true,
            unityYaml: true,
            hybridClr: true,
            yooAsset: true,
            config: true,
            modules: true);
    }

    private void FastModeButton_Click(object? sender, RoutedEventArgs e)
    {
        SetStageOptions(
            projectModel: true,
            source: true,
            dll: false,
            unityYaml: false,
            hybridClr: true,
            yooAsset: true,
            config: false,
            modules: true);
    }

    private void StageOption_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isUpdatingStageOptions)
        {
            return;
        }

        UpdateStageOptionSummary();
        ResetStages();
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        AppendLog("正在请求停止分析...");
        _analysisCancellation?.Cancel();

        try
        {
            if (_runningProcess is { HasExited: false })
            {
                _runningProcess.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception)
        {
            AppendLog($"停止失败: {exception.Message}");
        }
    }

    private void ResetButton_Click(object? sender, RoutedEventArgs e)
    {
        SetText(UnityPathBox, string.Empty);
        SetText(CodePathBox, string.Empty);
        SetText(DllPathBox, string.Empty);
        SetText(YooAssetPathBox, string.Empty);
        SetText(OutputPathBox, string.Empty);
        SetStageOptions(
            projectModel: true,
            source: true,
            dll: true,
            unityYaml: true,
            hybridClr: true,
            yooAsset: true,
            config: true,
            modules: true);
        _autoFilledTargets.Clear();
        _lastOutputDirectory = null;
        LastResultText.Text = "未运行";
        DiscoveryText.Text = "等待选择项目。";
        StatusText.Text = "选择 Unity 项目根目录后，点击自动发现路径。";
        ResetStages();
        UpdateInputSummary();
        UpdateGuardState();
    }

    private void OpenOutputButton_Click(object? sender, RoutedEventArgs e)
    {
        OpenPath(_lastOutputDirectory ?? OutputPathBox.Text);
    }

    private void OpenReportButton_Click(object? sender, RoutedEventArgs e)
    {
        var output = _lastOutputDirectory ?? OutputPathBox.Text;
        if (string.IsNullOrWhiteSpace(output))
        {
            AppendLog("还没有输出目录。");
            return;
        }

        OpenPath(Path.Combine(output, "report", "report.html"));
    }

    private void ClearLogButton_Click(object? sender, RoutedEventArgs e)
    {
        LogText.Text = string.Empty;
    }

    private void OpenReleaseButton_Click(object? sender, RoutedEventArgs e)
    {
        var releaseNotes = FindRepoFile("docs", "release-notes-v0.5.0.md")
            ?? Path.Combine(AppContext.BaseDirectory, "docs", "release-notes-v0.5.0.md");
        OpenPath(releaseNotes);
    }

    private async void CopyCommandButton_Click(object? sender, RoutedEventArgs e)
    {
        var output = OutputPathBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(output))
        {
            AppendLog("输出目录为空，暂时无法生成命令。");
            return;
        }

        var command = ResolveCliCommand();
        var arguments = BuildCliArgumentList(output, includeProgressJson: false);
        var preview = string.Join(" ", new[] { command.Executable }
            .Concat(command.PrefixArguments)
            .Concat(arguments)
            .Select(Quote));

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            AppendLog(preview);
            return;
        }

        await clipboard.SetTextAsync(preview);
        AppendLog("已复制 CLI 命令。");
    }

    private void InputPath_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdatingInputs)
        {
            return;
        }

        if (ReferenceEquals(sender, UnityPathBox))
        {
            SuggestOutputPath();
        }

        if (sender is TextBox textBox)
        {
            _autoFilledTargets.Remove(textBox.Name ?? string.Empty);
        }

        UpdateInputSummary();
        UpdateGuardState();
        UpdateDiscoveryPreview();
    }

    private async Task PickFolderIntoAsync(TextBox target, string title, bool append = false)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null)
        {
            AppendLog("当前窗口无法打开目录选择器。");
            return;
        }

        var selected = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });
        var path = selected.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var nextValue = append && !string.IsNullOrWhiteSpace(target.Text)
            ? MergePathList(target.Text, new[] { path })
            : path;
        SetText(target, nextValue);
    }

    private async Task RunAnalysisAsync()
    {
        DiscoverFromUnityRoot(applySuggestions: false);

        var validation = ValidateInputs();
        if (!validation.IsValid)
        {
            StatusText.Text = "请先修正路径。";
            AppendLog(validation.Message);
            UpdateGuardState(validation.Message);
            return;
        }

        var output = OutputPathBox.Text!.Trim();
        _lastOutputDirectory = output;
        ResetStages();
        AnalyzeButton.IsEnabled = false;
        AutoDiscoverButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        ProgressBar.IsVisible = true;
        ProgressBar.IsIndeterminate = false;
        ProgressBar.Value = 0;
        StatusText.Text = "分析运行中...";
        LastResultText.Text = "运行中";
        AppendLog("开始分析。");

        _analysisCancellation = new CancellationTokenSource();

        try
        {
            var command = ResolveCliCommand();
            if (!ExecutableExists(command))
            {
                AppendLog($"未找到 CLI: {command.Executable}");
                StatusText.Text = "未找到 CLI。";
                LastResultText.Text = "失败";
                return;
            }

            var arguments = BuildCliArgumentList(output, includeProgressJson: true);
            var allArguments = command.PrefixArguments.Concat(arguments).ToArray();
            AppendLog($"{command.Executable} {string.Join(" ", allArguments.Select(Quote))}");

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = command.Executable,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                },
                EnableRaisingEvents = true
            };

            foreach (var argument in allArguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            _runningProcess = process;
            process.OutputDataReceived += (_, eventArgs) =>
            {
                if (eventArgs.Data is not null)
                {
                    Dispatcher.UIThread.Post(() => HandleProcessLine(eventArgs.Data));
                }
            };
            process.ErrorDataReceived += (_, eventArgs) =>
            {
                if (eventArgs.Data is not null)
                {
                    Dispatcher.UIThread.Post(() => AppendLog(eventArgs.Data));
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(_analysisCancellation.Token);

            var success = process.ExitCode == 0;
            var warnings = process.ExitCode == 4;
            StatusText.Text = success
                ? "分析完成。"
                : warnings
                    ? "分析完成，但存在警告。"
                    : $"分析结束，退出码 {process.ExitCode}。";
            LastResultText.Text = success ? "完成" : warnings ? "有警告" : "失败";
            AppendLog(StatusText.Text);

            if (success || warnings)
            {
                OpenReportButton.IsEnabled = true;
            }
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "分析已停止。";
            LastResultText.Text = "已停止";
            MarkRunningStagesCancelled();
            AppendLog("分析已停止。");
        }
        catch (Exception exception)
        {
            StatusText.Text = "分析失败。";
            LastResultText.Text = "失败";
            AppendLog(exception.Message);
        }
        finally
        {
            _runningProcess = null;
            _analysisCancellation?.Dispose();
            _analysisCancellation = null;
            AnalyzeButton.IsEnabled = true;
            AutoDiscoverButton.IsEnabled = true;
            CancelButton.IsEnabled = false;
            ProgressBar.IsIndeterminate = false;
            ProgressBar.IsVisible = false;
            UpdateStageProgress();
            UpdateGuardState();
        }
    }

    private void DiscoverFromUnityRoot(bool applySuggestions)
    {
        var unityRoot = UnityPathBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(unityRoot))
        {
            DiscoveryText.Text = "先选择 Unity 项目根目录。";
            UpdateInputSummary();
            UpdateGuardState();
            return;
        }

        var discovery = ProjectDiscovery.Discover(unityRoot);
        if (applySuggestions)
        {
            ApplyDiscovery(discovery);
        }

        DiscoveryText.Text = BuildDiscoverySummary(discovery);
        StatusText.Text = discovery.IsUnityProject
            ? "已发现项目线索，检查路径后即可开始分析。"
            : "目录不像标准 Unity 项目，仍可手动补全路径后分析。";
        UpdateInputSummary(discovery);
        UpdateGuardState();
    }

    private void ApplyDiscovery(DiscoveryResult discovery)
    {
        _isUpdatingInputs = true;
        try
        {
            SetAutoText(CodePathBox, MergePathList(CodePathBox.Text, discovery.CodeRoots));
            SetAutoText(DllPathBox, MergePathList(DllPathBox.Text, discovery.DllRoots));
            SetAutoText(YooAssetPathBox, MergePathList(YooAssetPathBox.Text, discovery.YooAssetManifestRoots));

            if (string.IsNullOrWhiteSpace(OutputPathBox.Text) || _autoFilledTargets.Contains(OutputPathBox.Name ?? string.Empty))
            {
                var output = SuggestOutputPath(discovery.ProjectName);
                SetAutoText(OutputPathBox, output);
            }
        }
        finally
        {
            _isUpdatingInputs = false;
        }

        UpdateInputSummary(discovery);
    }

    private void UpdateDiscoveryPreview()
    {
        var unityRoot = UnityPathBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(unityRoot) || !Directory.Exists(unityRoot))
        {
            return;
        }

        DiscoveryText.Text = BuildDiscoverySummary(ProjectDiscovery.Discover(unityRoot));
    }

    private ValidationResult ValidateInputs()
    {
        var unity = UnityPathBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(unity)
            && !SplitPathList(CodePathBox.Text).Any()
            && !SplitPathList(DllPathBox.Text).Any()
            && !SplitPathList(YooAssetPathBox.Text).Any())
        {
            return ValidationResult.Invalid("至少需要一个 Unity、代码、DLL 或 YooAsset 输入根目录。");
        }

        foreach (var path in EnumerateInputPaths(includeUnity: true))
        {
            if (!Directory.Exists(path))
            {
                return ValidationResult.Invalid($"输入目录不存在: {path}");
            }
        }

        var output = OutputPathBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(output))
        {
            return ValidationResult.Invalid("输出目录不能为空。");
        }

        foreach (var input in EnumerateInputPaths(includeUnity: true))
        {
            if (ContainsPath(input, output))
            {
                return ValidationResult.Invalid($"输出目录不能位于输入目录内: {input}");
            }

            if (ContainsPath(output, input))
            {
                return ValidationResult.Invalid($"输入目录不能位于输出目录内: {input}");
            }
        }

        return ValidationResult.Valid();
    }

    private void UpdateGuardState(string? forcedWarning = null)
    {
        var validation = forcedWarning is null ? ValidateInputsSoft() : ValidationResult.Invalid(forcedWarning);
        if (validation.IsValid)
        {
            GuardPanel.Background = Brush.Parse("#E7F5EF");
            GuardPanel.BorderBrush = Brush.Parse("#A7D8C9");
            GuardIconText.Text = "OK";
            GuardIconText.Foreground = Brush.Parse("#0F766E");
            GuardTitleText.Text = "只读保护已启用";
            GuardTitleText.Foreground = Brush.Parse("#0F766E");
            GuardDetailText.Text = "分析只读取输入根目录，所有结果只写入输出目录。";
            GuardDetailText.Foreground = Brush.Parse("#315B52");
            return;
        }

        GuardPanel.Background = Brush.Parse("#FFF7ED");
        GuardPanel.BorderBrush = Brush.Parse("#FDBA74");
        GuardIconText.Text = "!";
        GuardIconText.Foreground = Brush.Parse("#B45309");
        GuardTitleText.Text = "需要检查路径";
        GuardTitleText.Foreground = Brush.Parse("#B45309");
        GuardDetailText.Text = validation.Message;
        GuardDetailText.Foreground = Brush.Parse("#7C2D12");
    }

    private ValidationResult ValidateInputsSoft()
    {
        var output = OutputPathBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(output))
        {
            return ValidationResult.Invalid("请选择输出目录；建议使用文档目录下的 OfflineUnityAnalyzer 子目录。");
        }

        foreach (var input in EnumerateInputPaths(includeUnity: true))
        {
            if (ContainsPath(input, output))
            {
                return ValidationResult.Invalid($"输出目录不能位于输入目录内: {input}");
            }

            if (ContainsPath(output, input))
            {
                return ValidationResult.Invalid($"输入目录不能位于输出目录内: {input}");
            }
        }

        return ValidationResult.Valid();
    }

    private void HandleProcessLine(string line)
    {
        if (TryHandleProgressJson(line))
        {
            return;
        }

        AppendLog(line);
    }

    private bool TryHandleProgressJson(string line)
    {
        if (!line.StartsWith('{'))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var type = ReadJsonString(root, "type");
            var stage = ReadJsonString(root, "stage");
            var message = ReadJsonString(root, "message") ?? ReadJsonString(root, "Message") ?? string.Empty;

            if (string.IsNullOrWhiteSpace(type))
            {
                return false;
            }

            if (type.Equals("stageStarted", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(stage))
            {
                UpdateStage(stage, "运行中", message, "#DBEAFE", "#1D4ED8");
                StatusText.Text = $"{DisplayStageName(stage)} 运行中...";
                AppendLog($"{DisplayStageName(stage)}: {message}");
                UpdateStageProgress();
                return true;
            }

            if (type.Equals("stageFinished", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(stage))
            {
                var status = ReadJsonString(root, "status") ?? "Completed";
                var warnings = ReadJsonInt(root, "WarningCount") ?? 0;
                var errors = ReadJsonInt(root, "ErrorCount") ?? 0;
                var (badgeBackground, badgeForeground, displayStatus) = StyleForStatus(status);
                var warningSummary = errors > 0
                    ? $"{errors} 错误"
                    : warnings > 0
                        ? $"{warnings} 警告"
                        : string.Empty;

                UpdateStage(stage, displayStatus, message, badgeBackground, badgeForeground, warningSummary);
                AppendLog($"{DisplayStageName(stage)}: {status} - {message}");
                UpdateStageProgress();
                return true;
            }

            if (type.Equals("message", StringComparison.OrdinalIgnoreCase))
            {
                AppendLog(message);
                return true;
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }

    private void UpdateStage(string stage, string status, string message, string badgeBackground, string badgeForeground, string warningSummary = "")
    {
        if (!_stageMap.TryGetValue(stage, out var stageViewModel))
        {
            stageViewModel = new StageViewModel(DisplayStageName(stage));
            _stageMap[stage] = stageViewModel;
            _stages.Add(stageViewModel);
        }

        stageViewModel.Status = status;
        stageViewModel.Message = string.IsNullOrWhiteSpace(message) ? stageViewModel.Message : message;
        stageViewModel.BadgeBackground = Brush.Parse(badgeBackground);
        stageViewModel.BadgeForeground = Brush.Parse(badgeForeground);
        stageViewModel.WarningSummary = warningSummary;
    }

    private void ResetStages()
    {
        foreach (var stage in _stages)
        {
            stage.Status = "等待";
            stage.Message = "尚未运行";
            stage.BadgeBackground = Brush.Parse("#EEF2F6");
            stage.BadgeForeground = Brush.Parse("#52606D");
            stage.WarningSummary = string.Empty;
        }

        UpdateStageProgress();
    }

    private void MarkRunningStagesCancelled()
    {
        foreach (var stage in _stages.Where(stage => stage.Status == "运行中"))
        {
            stage.Status = "已停止";
            stage.Message = "用户停止分析";
            stage.BadgeBackground = Brush.Parse("#FEE2E2");
            stage.BadgeForeground = Brush.Parse("#B91C1C");
        }

        UpdateStageProgress();
    }

    private void UpdateStageProgress()
    {
        var finished = _stages.Count(stage => stage.Status is "完成" or "警告" or "跳过");
        StageCountText.Text = $"{finished} / {_stages.Count}";
        ProgressBar.Value = _stages.Count == 0 ? 0 : finished * 100.0 / _stages.Count;
    }

    private void UpdateInputSummary(DiscoveryResult? discovery = null)
    {
        var inputCount = EnumerateInputPaths(includeUnity: true).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        InputCountText.Text = inputCount.ToString();

        var clueCount = discovery?.ClueCount ?? EstimateClueCount();
        ClueCountText.Text = clueCount.ToString();
    }

    private int EstimateClueCount()
    {
        var count = 0;
        var unity = UnityPathBox.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(unity))
        {
            if (Directory.Exists(Path.Combine(unity, "Assets"))) count++;
            if (Directory.Exists(Path.Combine(unity, "Packages"))) count++;
            if (Directory.Exists(Path.Combine(unity, "ProjectSettings"))) count++;
        }

        count += SplitPathList(CodePathBox.Text).Count();
        count += SplitPathList(DllPathBox.Text).Count();
        count += SplitPathList(YooAssetPathBox.Text).Count();
        return count;
    }

    private IReadOnlyList<string> BuildCliArgumentList(string output, bool includeProgressJson)
    {
        var arguments = new List<string>
        {
            "analyze",
            "--out",
            output,
            "--strict-readonly"
        };
        var coveredRoots = new List<string>();

        if (includeProgressJson)
        {
            arguments.Add("--progress-json");
        }

        AddSingle(arguments, "--unity", UnityPathBox.Text, coveredRoots);
        AddMany(arguments, "--code", CodePathBox.Text, coveredRoots);
        AddMany(arguments, "--dll", DllPathBox.Text, coveredRoots);
        AddMany(arguments, "--yooasset-manifest", YooAssetPathBox.Text, coveredRoots);
        AddStageSkipFlags(arguments);

        return arguments;
    }

    private void AddStageSkipFlags(List<string> arguments)
    {
        AddSkipFlag(arguments, ProjectModelCheckBox, "--skip-project-model");
        AddSkipFlag(arguments, SourceCheckBox, "--skip-source");
        AddSkipFlag(arguments, DllCheckBox, "--skip-dll");
        AddSkipFlag(arguments, UnityYamlCheckBox, "--skip-unity-yaml");
        AddSkipFlag(arguments, HybridClrCheckBox, "--skip-hybridclr");
        AddSkipFlag(arguments, YooAssetCheckBox, "--skip-yooasset");
        AddSkipFlag(arguments, ConfigCheckBox, "--skip-config");
        AddSkipFlag(arguments, ModulesCheckBox, "--skip-modules");
    }

    private static void AddSkipFlag(List<string> arguments, CheckBox checkBox, string flag)
    {
        if (checkBox.IsChecked != true)
        {
            arguments.Add(flag);
        }
    }

    private static void AddSingle(List<string> arguments, string option, string? value, List<string> coveredRoots)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var trimmed = value.Trim();
        arguments.Add(option);
        arguments.Add(trimmed);
        coveredRoots.Add(trimmed);
    }

    private static void AddMany(List<string> arguments, string option, string? rawValue, List<string> coveredRoots)
    {
        foreach (var value in SplitPathList(rawValue))
        {
            if (coveredRoots.Any(root => ContainsPath(root, value)))
            {
                continue;
            }

            arguments.Add(option);
            arguments.Add(value);
            coveredRoots.Add(value);
        }
    }

    private static CliCommand ResolveCliCommand()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var exeName = OperatingSystem.IsWindows()
            ? "OfflineUnityAnalyzer.Cli.exe"
            : "OfflineUnityAnalyzer.Cli";
        var sameDirectory = Path.Combine(baseDirectory, exeName);
        if (File.Exists(sameDirectory))
        {
            return new CliCommand(sameDirectory, Array.Empty<string>());
        }

        var dllPath = Path.Combine(baseDirectory, "OfflineUnityAnalyzer.Cli.dll");
        if (File.Exists(dllPath))
        {
            return new CliCommand("dotnet", new[] { dllPath });
        }

        var repoCliDll = FindRepoFile("src", "OfflineUnityAnalyzer.Cli", "bin", "Debug", "net8.0", "OfflineUnityAnalyzer.Cli.dll")
            ?? FindRepoFile("src", "OfflineUnityAnalyzer.Cli", "bin", "Release", "net8.0", "OfflineUnityAnalyzer.Cli.dll");
        if (repoCliDll is not null)
        {
            return new CliCommand("dotnet", new[] { repoCliDll });
        }

        return new CliCommand(sameDirectory, Array.Empty<string>());
    }

    private static bool ExecutableExists(CliCommand command)
    {
        return command.Executable.Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            || File.Exists(command.Executable);
    }

    private static string? FindRepoFile(params string[] parts)
    {
        var current = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var candidate = Path.Combine(new[] { current }.Concat(parts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            var parent = Directory.GetParent(current);
            if (parent is null)
            {
                return null;
            }

            current = parent.FullName;
        }

        return null;
    }

    private static string BuildDiscoverySummary(DiscoveryResult discovery)
    {
        var builder = new StringBuilder();
        builder.AppendLine(discovery.IsUnityProject
            ? $"识别为 Unity 项目: {discovery.ProjectName}"
            : "未完整识别 Unity 标准目录，可继续手动补充。");

        AppendGroup(builder, "代码根目录", discovery.CodeRoots);
        AppendGroup(builder, "DLL / HybridCLR", discovery.DllRoots);
        AppendGroup(builder, "YooAsset Manifest", discovery.YooAssetManifestRoots);
        AppendGroup(builder, "项目线索", discovery.Clues);

        return builder.ToString().TrimEnd();
    }

    private static void AppendGroup(StringBuilder builder, string title, IReadOnlyList<string> values)
    {
        builder.AppendLine();
        builder.AppendLine($"{title}: {(values.Count == 0 ? "未发现" : values.Count)}");
        foreach (var value in values.Take(12))
        {
            builder.AppendLine($"  {value}");
        }

        if (values.Count > 12)
        {
            builder.AppendLine($"  以及 {values.Count - 12} 项...");
        }
    }

    private IEnumerable<string> EnumerateInputPaths(bool includeUnity)
    {
        if (includeUnity && !string.IsNullOrWhiteSpace(UnityPathBox.Text))
        {
            yield return UnityPathBox.Text.Trim();
        }

        foreach (var path in SplitPathList(CodePathBox.Text)
                     .Concat(SplitPathList(DllPathBox.Text))
                     .Concat(SplitPathList(YooAssetPathBox.Text)))
        {
            yield return path;
        }
    }

    private static IReadOnlyList<string> SplitPathList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        return value
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string MergePathList(string? existing, IEnumerable<string> additions)
    {
        return string.Join(";", SplitPathList(existing)
            .Concat(additions.Where(path => !string.IsNullOrWhiteSpace(path)))
            .Select(path => path.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private void SetStageOptions(
        bool projectModel,
        bool source,
        bool dll,
        bool unityYaml,
        bool hybridClr,
        bool yooAsset,
        bool config,
        bool modules)
    {
        _isUpdatingStageOptions = true;
        try
        {
            ProjectModelCheckBox.IsChecked = projectModel;
            SourceCheckBox.IsChecked = source;
            DllCheckBox.IsChecked = dll;
            UnityYamlCheckBox.IsChecked = unityYaml;
            HybridClrCheckBox.IsChecked = hybridClr;
            YooAssetCheckBox.IsChecked = yooAsset;
            ConfigCheckBox.IsChecked = config;
            ModulesCheckBox.IsChecked = modules;
        }
        finally
        {
            _isUpdatingStageOptions = false;
        }

        UpdateStageOptionSummary();
        ResetStages();
    }

    private void UpdateStageOptionSummary()
    {
        var skipped = GetSkippedStageLabels().ToArray();
        StageOptionSummaryText.Text = skipped.Length == 0
            ? "当前为完整分析。"
            : $"将跳过: {string.Join("、", skipped)}。";
    }

    private IEnumerable<string> GetSkippedStageLabels()
    {
        if (ProjectModelCheckBox.IsChecked != true) yield return "项目模型";
        if (SourceCheckBox.IsChecked != true) yield return "C# 源码";
        if (DllCheckBox.IsChecked != true) yield return "DLL 元数据";
        if (UnityYamlCheckBox.IsChecked != true) yield return "Unity YAML";
        if (HybridClrCheckBox.IsChecked != true) yield return "HybridCLR";
        if (YooAssetCheckBox.IsChecked != true) yield return "YooAsset";
        if (ConfigCheckBox.IsChecked != true) yield return "配置引用";
        if (ModulesCheckBox.IsChecked != true) yield return "模块推断";
    }

    private void SetAutoText(TextBox target, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        SetText(target, value);
        _autoFilledTargets.Add(target.Name ?? string.Empty);
    }

    private void SetText(TextBox target, string value)
    {
        _isUpdatingInputs = true;
        try
        {
            target.Text = value;
        }
        finally
        {
            _isUpdatingInputs = false;
        }

        UpdateInputSummary();
        UpdateGuardState();
    }

    private void SuggestOutputPath()
    {
        if (!string.IsNullOrWhiteSpace(OutputPathBox.Text)
            && !_autoFilledTargets.Contains(OutputPathBox.Name ?? string.Empty))
        {
            return;
        }

        SetAutoText(OutputPathBox, SuggestOutputPath(ProjectDiscovery.ProjectNameFromPath(UnityPathBox.Text)));
    }

    private static string SuggestOutputPath(string? projectName)
    {
        var safeName = string.IsNullOrWhiteSpace(projectName) ? "AnalysisOutput" : SanitizeFileName(projectName);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(documents))
        {
            documents = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        return Path.Combine(documents, "OfflineUnityAnalyzer", safeName);
    }

    private static string SanitizeFileName(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return value;
    }

    private void AppendLog(string message)
    {
        if (string.IsNullOrEmpty(LogText.Text))
        {
            LogText.Text = message;
        }
        else
        {
            LogText.Text += Environment.NewLine + message;
        }

        LogScrollViewer.ScrollToEnd();
    }

    private void OpenPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            AppendLog("没有可打开的路径。");
            return;
        }

        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                AppendLog($"路径不存在: {path}");
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            AppendLog(exception.Message);
        }
    }

    private static string DisplayStageName(string stage)
    {
        return stage switch
        {
            "SafetyPreflight" => "只读预检",
            "FileScan" => "文件扫描",
            "ProjectModel" => "项目模型",
            "SourceSyntaxIndex" => "C# 类型索引",
            "DllIndex" => "DLL 索引",
            "UnityYamlRawIndex" => "Unity YAML",
            "HybridClrIndex" => "HybridCLR",
            "YooAssetIndex" => "YooAsset",
            "ConfigShallowIndex" => "配置线索",
            "ModuleInference" => "模块推断",
            "ReportExport" => "报告导出",
            _ => stage
        };
    }

    private static (string BadgeBackground, string BadgeForeground, string DisplayStatus) StyleForStatus(string status)
    {
        return status switch
        {
            "Completed" => ("#DCFCE7", "#15803D", "完成"),
            "CompletedWithWarnings" => ("#FEF3C7", "#B45309", "警告"),
            "Skipped" => ("#EEF2F6", "#52606D", "跳过"),
            "Failed" => ("#FEE2E2", "#B91C1C", "失败"),
            "Cancelled" => ("#FEE2E2", "#B91C1C", "已停止"),
            _ => ("#DBEAFE", "#1D4ED8", status)
        };
    }

    private static string? ReadJsonString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static int? ReadJsonInt(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;
    }

    private static bool ContainsPath(string root, string path)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalizedRoot = NormalizePath(root);
        var normalizedPath = NormalizePath(path);

        if (OperatingSystem.IsWindows())
        {
            normalizedRoot = normalizedRoot.ToUpperInvariant();
            normalizedPath = normalizedPath.ToUpperInvariant();
        }

        return normalizedPath.Equals(normalizedRoot, StringComparison.Ordinal)
            || normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || normalizedPath.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string Quote(string value)
    {
        return value.Contains(' ') || value.Contains(';') || value.Contains('"')
            ? $"\"{value.Replace("\"", "\\\"")}\""
            : value;
    }

    private sealed record CliCommand(string Executable, IReadOnlyList<string> PrefixArguments);

    private sealed record ValidationResult(bool IsValid, string Message)
    {
        public static ValidationResult Valid()
        {
            return new ValidationResult(true, string.Empty);
        }

        public static ValidationResult Invalid(string message)
        {
            return new ValidationResult(false, message);
        }
    }

    private sealed class StageViewModel : ObservableObject
    {
        private string _status = "等待";
        private string _message = "尚未运行";
        private IBrush _badgeBackground = Brush.Parse("#EEF2F6");
        private IBrush _badgeForeground = Brush.Parse("#52606D");
        private string _warningSummary = string.Empty;

        public StageViewModel(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        public string Message
        {
            get => _message;
            set => SetProperty(ref _message, value);
        }

        public IBrush BadgeBackground
        {
            get => _badgeBackground;
            set => SetProperty(ref _badgeBackground, value);
        }

        public IBrush BadgeForeground
        {
            get => _badgeForeground;
            set => SetProperty(ref _badgeForeground, value);
        }

        public string WarningSummary
        {
            get => _warningSummary;
            set => SetProperty(ref _warningSummary, value);
        }
    }

    private abstract class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    private sealed class DiscoveryResult
    {
        public string Root { get; init; } = string.Empty;

        public string ProjectName { get; init; } = string.Empty;

        public bool IsUnityProject { get; init; }

        public IReadOnlyList<string> CodeRoots { get; init; } = Array.Empty<string>();

        public IReadOnlyList<string> DllRoots { get; init; } = Array.Empty<string>();

        public IReadOnlyList<string> YooAssetManifestRoots { get; init; } = Array.Empty<string>();

        public IReadOnlyList<string> Clues { get; init; } = Array.Empty<string>();

        public int ClueCount => Clues.Count + CodeRoots.Count + DllRoots.Count + YooAssetManifestRoots.Count;
    }

    private static class ProjectDiscovery
    {
        private static readonly string[] ExcludedDirectories =
        {
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
            "node_modules"
        };

        public static DiscoveryResult Discover(string unityRoot)
        {
            var normalizedRoot = NormalizePath(unityRoot);
            var assets = Path.Combine(normalizedRoot, "Assets");
            var packages = Path.Combine(normalizedRoot, "Packages");
            var projectSettings = Path.Combine(normalizedRoot, "ProjectSettings");
            var clues = new List<string>();
            var codeRoots = new List<string>();
            var dllRoots = new List<string>();
            var yooAssetRoots = new List<string>();

            AddClueIfDirectory(clues, assets, "Assets");
            AddClueIfDirectory(clues, packages, "Packages");
            AddClueIfDirectory(clues, projectSettings, "ProjectSettings");

            if (Directory.Exists(assets))
            {
                codeRoots.Add(assets);
            }

            if (Directory.Exists(packages))
            {
                codeRoots.Add(packages);
            }

            AddIfDirectory(codeRoots, Path.Combine(normalizedRoot, "Assets", "Scripts"));
            AddIfDirectory(codeRoots, Path.Combine(normalizedRoot, "Assets", "Game"));
            AddIfDirectory(codeRoots, Path.Combine(normalizedRoot, "Assets", "HotUpdate"));
            AddSiblingCodeRoots(normalizedRoot, codeRoots, clues);

            AddIfDirectory(dllRoots, Path.Combine(normalizedRoot, "Assets", "Plugins"));
            AddIfDirectory(dllRoots, Path.Combine(normalizedRoot, "Assets", "HybridCLR"));
            AddIfDirectory(dllRoots, Path.Combine(normalizedRoot, "HybridCLRData"));
            AddIfDirectory(dllRoots, Path.Combine(normalizedRoot, "Assets", "HotUpdate"));

            AddIfDirectory(yooAssetRoots, Path.Combine(normalizedRoot, "ServerData"));
            AddIfDirectory(yooAssetRoots, Path.Combine(normalizedRoot, "Bundles"));
            AddIfDirectory(yooAssetRoots, Path.Combine(normalizedRoot, "Assets", "StreamingAssets"));
            AddIfDirectory(yooAssetRoots, Path.Combine(normalizedRoot, "Assets", "YooAsset"));

            DiscoverFiles(normalizedRoot, codeRoots, dllRoots, yooAssetRoots, clues);

            return new DiscoveryResult
            {
                Root = normalizedRoot,
                ProjectName = ProjectNameFromPath(normalizedRoot),
                IsUnityProject = Directory.Exists(assets) && Directory.Exists(projectSettings),
                CodeRoots = CompactRoots(codeRoots),
                DllRoots = CompactRoots(dllRoots),
                YooAssetManifestRoots = CompactRoots(yooAssetRoots),
                Clues = clues.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value).ToArray()
            };
        }

        public static string ProjectNameFromPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "AnalysisOutput";
            }

            return Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        private static void DiscoverFiles(
            string root,
            List<string> codeRoots,
            List<string> dllRoots,
            List<string> yooAssetRoots,
            List<string> clues)
        {
            if (!Directory.Exists(root))
            {
                return;
            }

            var visitedFiles = 0;
            foreach (var file in EnumerateFiles(root, maxDepth: 6))
            {
                visitedFiles++;
                var fileName = Path.GetFileName(file);
                var directory = Path.GetDirectoryName(file);
                if (directory is null)
                {
                    continue;
                }

                if (fileName.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase)
                    || fileName.EndsWith(".asmref", StringComparison.OrdinalIgnoreCase)
                    || fileName.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                    || fileName.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
                {
                    codeRoots.Add(directory);
                    clues.Add($"{KindLabel(fileName)}: {file}");
                }

                if (fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                    || fileName.EndsWith(".dll.bytes", StringComparison.OrdinalIgnoreCase))
                {
                    dllRoots.Add(directory);
                    clues.Add($"DLL: {file}");
                }

                if (fileName.StartsWith("PackageManifest", StringComparison.OrdinalIgnoreCase)
                    || fileName.Contains("YooAsset", StringComparison.OrdinalIgnoreCase))
                {
                    yooAssetRoots.Add(directory);
                    clues.Add($"YooAsset: {file}");
                }

                if (visitedFiles >= 12000)
                {
                    clues.Add("自动发现达到 12000 个文件上限，深层目录会在正式分析时继续扫描。");
                    break;
                }
            }
        }

        private static void AddSiblingCodeRoots(string root, List<string> codeRoots, List<string> clues)
        {
            var parent = Directory.GetParent(root);
            if (parent is null)
            {
                return;
            }

            foreach (var directory in SafeEnumerateDirectories(parent.FullName).Take(32))
            {
                if (PathsEqual(directory, root))
                {
                    continue;
                }

                var name = Path.GetFileName(directory);
                if (name.Contains("client", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("code", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("script", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("hot", StringComparison.OrdinalIgnoreCase)
                    || SafeEnumerateFiles(directory).Any(file => file.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)))
                {
                    if (HasFileWithExtension(directory, ".cs", maxDepth: 3)
                        || SafeEnumerateFiles(directory).Any(file => file.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)))
                    {
                        codeRoots.Add(directory);
                        clues.Add($"相邻代码目录: {directory}");
                    }
                }
            }
        }

        private static IEnumerable<string> EnumerateFiles(string root, int maxDepth)
        {
            var pending = new Stack<(string Path, int Depth)>();
            pending.Push((root, 0));

            while (pending.Count > 0)
            {
                var (current, depth) = pending.Pop();
                if (IsExcluded(current))
                {
                    continue;
                }

                foreach (var file in SafeEnumerateFiles(current))
                {
                    yield return file;
                }

                if (depth >= maxDepth)
                {
                    continue;
                }

                foreach (var directory in SafeEnumerateDirectories(current))
                {
                    if (!IsExcluded(directory))
                    {
                        pending.Push((directory, depth + 1));
                    }
                }
            }
        }

        private static IEnumerable<string> SafeEnumerateDirectories(string path)
        {
            try
            {
                return Directory.EnumerateDirectories(path).ToArray();
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
            {
                return Array.Empty<string>();
            }
        }

        private static IEnumerable<string> SafeEnumerateFiles(string path)
        {
            try
            {
                return Directory.EnumerateFiles(path).ToArray();
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
            {
                return Array.Empty<string>();
            }
        }

        private static void AddIfDirectory(List<string> values, string path, string? label = null)
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            values.Add(path);
            if (label is not null)
            {
                values.Add($"{label}: {path}");
            }
        }

        private static void AddClueIfDirectory(List<string> clues, string path, string label)
        {
            if (Directory.Exists(path))
            {
                clues.Add($"{label}: {path}");
            }
        }

        private static bool HasFileWithExtension(string root, string extension, int maxDepth)
        {
            var visited = 0;
            foreach (var file in EnumerateFiles(root, maxDepth))
            {
                visited++;
                if (file.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (visited >= 4000)
                {
                    return false;
                }
            }

            return false;
        }

        private static IReadOnlyList<string> CompactRoots(IEnumerable<string> roots)
        {
            var ordered = roots
                .Where(Directory.Exists)
                .Select(NormalizePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path.Length)
                .ToList();

            var result = new List<string>();
            foreach (var root in ordered)
            {
                if (!result.Any(existing => ContainsPath(existing, root)))
                {
                    result.Add(root);
                }
            }

            return result;
        }

        private static bool IsExcluded(string path)
        {
            var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return ExcludedDirectories.Any(excluded => name.Equals(excluded, StringComparison.OrdinalIgnoreCase));
        }

        private static bool PathsEqual(string left, string right)
        {
            return string.Equals(NormalizePath(left), NormalizePath(right), OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
        }

        private static string KindLabel(string fileName)
        {
            var extension = Path.GetExtension(fileName);
            return extension switch
            {
                ".asmdef" => "asmdef",
                ".asmref" => "asmref",
                ".csproj" => "csproj",
                ".sln" => "solution",
                _ => "code"
            };
        }
    }
}
