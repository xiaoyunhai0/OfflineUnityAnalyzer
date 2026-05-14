using OfflineUnityAnalyzer.Core.Configuration;
using OfflineUnityAnalyzer.Core.Models;
using OfflineUnityAnalyzer.Core.Pipeline;
using OfflineUnityAnalyzer.Core.Safety;

namespace OfflineUnityAnalyzer.Analyzers.FileScanning;

public sealed class FileScanStage : IAnalyzerStage
{
    public AnalysisStageKind Kind => AnalysisStageKind.FileScan;

    public async Task<AnalysisStageResult> RunAsync(AnalysisContext context, CancellationToken cancellationToken)
    {
        var scanned = 0;
        var skipped = 0;
        var scannedPaths = new HashSet<string>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);

        foreach (var root in ExpandScanRoots(context))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!Directory.Exists(root.Path))
            {
                skipped++;
                continue;
            }

            foreach (var file in EnumerateFilesSafe(root.Path, context.Config.ExcludePatterns, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var normalizedFile = Path.GetFullPath(file);
                if (!scannedPaths.Add(normalizedFile))
                {
                    continue;
                }

                var kind = FileKindDetector.Detect(file);

                if (kind == ProjectFileKind.Unknown)
                {
                    continue;
                }

                var info = new FileInfo(file);
                context.AddFile(new ProjectFile(
                    FullPath: info.FullName,
                    RootPath: root.Path,
                    RelativePath: Path.GetRelativePath(root.Path, info.FullName),
                    Kind: kind,
                    SizeBytes: info.Length,
                    LastWriteTimeUtc: info.LastWriteTimeUtc));

                scanned++;
            }
        }

        await WriteSummaryAsync(context, cancellationToken);

        var message = skipped == 0
            ? $"Scanned {scanned} files."
            : $"Scanned {scanned} files. Skipped {skipped} missing roots.";

        return new AnalysisStageResult(Kind, AnalysisStageStatus.Completed, message);
    }

    private static IReadOnlyList<RegisteredRoot> ExpandScanRoots(AnalysisContext context)
    {
        var roots = new List<RegisteredRoot>();
        foreach (var root in context.PathGuard.InputRoots)
        {
            roots.Add(root);
        }

        if (!string.IsNullOrWhiteSpace(context.Config.UnityProject))
        {
            var unityRoot = Path.GetFullPath(context.Config.UnityProject);
            AddIfDirectory(roots, Path.Combine(unityRoot, "Assets"), "unity-assets");
            AddIfDirectory(roots, Path.Combine(unityRoot, "Packages"), "unity-packages");
            AddIfDirectory(roots, Path.Combine(unityRoot, "ProjectSettings"), "unity-settings");
        }

        return CompactRoots(roots);
    }

    private static void AddIfDirectory(List<RegisteredRoot> roots, string path, string label)
    {
        if (Directory.Exists(path))
        {
            roots.Add(new RegisteredRoot(Path.GetFullPath(path), PathRole.Input, label));
        }
    }

    private static IReadOnlyList<RegisteredRoot> CompactRoots(IEnumerable<RegisteredRoot> roots)
    {
        var ordered = roots
            .Where(root => Directory.Exists(root.Path))
            .Select(root => root with { Path = Path.GetFullPath(root.Path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) })
            .DistinctBy(root => root.Path, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .OrderBy(root => root.Path.Length)
            .ToList();
        var result = new List<RegisteredRoot>();

        foreach (var root in ordered)
        {
            if (!result.Any(existing => ContainsPath(existing.Path, root.Path)))
            {
                result.Add(root);
            }
        }

        return result;
    }

    private static bool ContainsPath(string root, string path)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (OperatingSystem.IsWindows())
        {
            normalizedRoot = normalizedRoot.ToUpperInvariant();
            normalizedPath = normalizedPath.ToUpperInvariant();
        }

        return normalizedPath.Equals(normalizedRoot, StringComparison.Ordinal)
            || normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || normalizedPath.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static IEnumerable<string> EnumerateFilesSafe(
        string root,
        IReadOnlyList<string> excludePatterns,
        CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();

            if (IsExcluded(current, excludePatterns))
            {
                continue;
            }

            string[] directories;
            string[] files;

            try
            {
                directories = Directory.EnumerateDirectories(current).ToArray();
                files = Directory.EnumerateFiles(current).ToArray();
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var directory in directories)
            {
                if (!IsExcluded(directory, excludePatterns))
                {
                    pending.Push(directory);
                }
            }

            foreach (var file in files)
            {
                if (!IsExcluded(file, excludePatterns))
                {
                    yield return file;
                }
            }
        }
    }

    private static bool IsExcluded(string path, IReadOnlyList<string> excludePatterns)
    {
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return excludePatterns.Any(pattern => name.Equals(pattern, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task WriteSummaryAsync(AnalysisContext context, CancellationToken cancellationToken)
    {
        var lines = new List<string>
        {
            "{",
            $"  \"generatedAtUtc\": \"{DateTime.UtcNow:O}\",",
            $"  \"fileCount\": {context.Files.Count},",
            "  \"byKind\": {"
        };

        var byKind = context.Files
            .GroupBy(file => file.Kind)
            .OrderBy(group => group.Key.ToString())
            .ToArray();

        for (var index = 0; index < byKind.Length; index++)
        {
            var group = byKind[index];
            var comma = index == byKind.Length - 1 ? string.Empty : ",";
            lines.Add($"    \"{group.Key}\": {group.Count()}{comma}");
        }

        lines.Add("  }");
        lines.Add("}");

        context.FileSystem.WriteAllTextToOutput("summary.json", string.Join(Environment.NewLine, lines) + Environment.NewLine);
        await Task.CompletedTask.WaitAsync(cancellationToken);
    }
}
