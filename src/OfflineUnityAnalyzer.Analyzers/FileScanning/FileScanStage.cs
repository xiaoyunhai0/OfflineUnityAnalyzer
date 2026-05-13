using OfflineUnityAnalyzer.Core.Configuration;
using OfflineUnityAnalyzer.Core.Models;
using OfflineUnityAnalyzer.Core.Pipeline;

namespace OfflineUnityAnalyzer.Analyzers.FileScanning;

public sealed class FileScanStage : IAnalyzerStage
{
    public AnalysisStageKind Kind => AnalysisStageKind.FileScan;

    public async Task<AnalysisStageResult> RunAsync(AnalysisContext context, CancellationToken cancellationToken)
    {
        var scanned = 0;
        var skipped = 0;

        foreach (var root in context.PathGuard.InputRoots)
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
