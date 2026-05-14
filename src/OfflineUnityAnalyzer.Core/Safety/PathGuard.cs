using OfflineUnityAnalyzer.Core.Configuration;

namespace OfflineUnityAnalyzer.Core.Safety;

public sealed class PathGuard
{
    private readonly List<RegisteredRoot> _inputRoots;

    private PathGuard(IReadOnlyList<RegisteredRoot> inputRoots, string outputRoot)
    {
        _inputRoots = inputRoots.ToList();
        OutputRoot = NormalizeDirectory(outputRoot);
    }

    public IReadOnlyList<RegisteredRoot> InputRoots => _inputRoots;

    public string OutputRoot { get; }

    public static PathGuard Create(AnalyzerConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Output))
        {
            throw new ReadonlyViolationException("Output path is required.");
        }

        ValidateInputPaths(config);

        var roots = new List<RegisteredRoot>();

        AddExistingDirectoryRoots(roots, config.CodeRoots, "code");
        AddExistingDirectoryRoot(roots, config.UnityProject, "unity");
        AddExistingDirectoryRoots(roots, config.DllRoots, "dll");
        AddExistingDirectoryRoots(roots, config.YooAssetManifestRoots, "yooasset");
        AddAutoDiscoveredSiblingCodeRoots(roots, config);

        if (!string.IsNullOrWhiteSpace(config.SolutionPath))
        {
            var solutionFullPath = NormalizeFile(config.SolutionPath);
            var solutionDirectory = Path.GetDirectoryName(solutionFullPath);
            if (!string.IsNullOrWhiteSpace(solutionDirectory) && Directory.Exists(solutionDirectory))
            {
                roots.Add(new RegisteredRoot(NormalizeDirectory(solutionDirectory), PathRole.Input, "solution"));
            }
        }

        var outputRoot = NormalizeDirectory(config.Output);
        ValidateNoRootOverlap(roots, outputRoot);

        return new PathGuard(roots, outputRoot);
    }

    public void EnsureCanRead(string path)
    {
        var normalized = NormalizePath(path);
        if (IsUnderAnyInputRoot(normalized) || IsUnderOutputRoot(normalized))
        {
            return;
        }

        throw new ReadonlyViolationException($"Read path is outside registered roots: {path}");
    }

    public void EnsureCanWriteOutputRelative(string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            throw new ReadonlyViolationException($"Output writes must use relative paths: {relativePath}");
        }

        var combined = NormalizePath(Path.Combine(OutputRoot, relativePath));
        if (!IsUnderOutputRoot(combined))
        {
            throw new ReadonlyViolationException($"Output write escapes output root: {relativePath}");
        }

        if (IsUnderAnyInputRoot(combined))
        {
            throw new ReadonlyViolationException($"Output write targets an input root: {relativePath}");
        }
    }

    public string GetOutputPath(string relativePath)
    {
        EnsureCanWriteOutputRelative(relativePath);
        return NormalizePath(Path.Combine(OutputRoot, relativePath));
    }

    public bool IsUnderAnyInputRoot(string path)
    {
        var normalized = NormalizePath(path);
        return _inputRoots.Any(root => ContainsPath(root.Path, normalized));
    }

    public bool IsUnderOutputRoot(string path)
    {
        return ContainsPath(OutputRoot, NormalizePath(path));
    }

    public static string NormalizePath(string path)
    {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public static string NormalizeDirectory(string path)
    {
        return NormalizePath(path);
    }

    public static string NormalizeFile(string path)
    {
        return NormalizePath(path);
    }

    private static void AddExistingDirectoryRoots(List<RegisteredRoot> roots, IEnumerable<string> paths, string label)
    {
        foreach (var path in paths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            AddExistingDirectoryRoot(roots, path, label);
        }
    }

    private static void AddExistingDirectoryRoot(List<RegisteredRoot> roots, string? path, string label)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var normalized = NormalizeDirectory(path);
        if (Directory.Exists(normalized))
        {
            roots.Add(new RegisteredRoot(normalized, PathRole.Input, label));
        }
    }

    private static void AddAutoDiscoveredSiblingCodeRoots(List<RegisteredRoot> roots, AnalyzerConfig config)
    {
        if (!config.AutoDiscoverSiblingCodeRoots
            || string.IsNullOrWhiteSpace(config.UnityProject)
            || !Directory.Exists(config.UnityProject))
        {
            return;
        }

        var unityRoot = NormalizeDirectory(config.UnityProject);
        var parent = Directory.GetParent(unityRoot);
        if (parent is null)
        {
            return;
        }

        foreach (var directory in SafeEnumerateDirectories(parent.FullName).Take(80))
        {
            var normalized = NormalizeDirectory(directory);
            if (ContainsPath(normalized, unityRoot) || ContainsPath(unityRoot, normalized))
            {
                continue;
            }

            if (LooksLikeSourceRoot(normalized))
            {
                roots.Add(new RegisteredRoot(normalized, PathRole.Input, "auto-code"));
            }
        }
    }

    private static bool LooksLikeSourceRoot(string path)
    {
        var name = Path.GetFileName(path);
        var nameLooksRight = name.Contains("code", StringComparison.OrdinalIgnoreCase)
            || name.Contains("client", StringComparison.OrdinalIgnoreCase)
            || name.Contains("script", StringComparison.OrdinalIgnoreCase)
            || name.Contains("hot", StringComparison.OrdinalIgnoreCase)
            || name.Contains("logic", StringComparison.OrdinalIgnoreCase)
            || name.Contains("src", StringComparison.OrdinalIgnoreCase);

        return HasFile(path, ".sln", 2)
            || HasFile(path, ".csproj", 3)
            || HasFile(path, ".asmdef", 4)
            || (nameLooksRight && HasFile(path, ".cs", 4));
    }

    private static bool HasFile(string root, string extension, int maxDepth)
    {
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, 0));
        var visited = 0;

        while (pending.Count > 0 && visited < 12000)
        {
            var (current, depth) = pending.Pop();
            if (IsExcludedAutoDirectory(current))
            {
                continue;
            }

            foreach (var file in SafeEnumerateFiles(current))
            {
                visited++;
                if (file.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            if (depth >= maxDepth)
            {
                continue;
            }

            foreach (var directory in SafeEnumerateDirectories(current))
            {
                if (!IsExcludedAutoDirectory(directory))
                {
                    pending.Push((directory, depth + 1));
                }
            }
        }

        return false;
    }

    private static bool IsExcludedAutoDirectory(string path)
    {
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return DefaultExcludePatterns.All.Any(pattern => name.Equals(pattern, StringComparison.OrdinalIgnoreCase));
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

    private static void ValidateNoRootOverlap(IReadOnlyList<RegisteredRoot> inputRoots, string outputRoot)
    {
        foreach (var root in inputRoots)
        {
            if (ContainsPath(root.Path, outputRoot))
            {
                throw new ReadonlyViolationException($"Output root must not be inside input root '{root.Label}': {root.Path}");
            }

            if (ContainsPath(outputRoot, root.Path))
            {
                throw new ReadonlyViolationException($"Input root must not be inside output root '{root.Label}': {root.Path}");
            }
        }
    }

    private static bool ContainsPath(string root, string path)
    {
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

    private static void ValidateInputPaths(AnalyzerConfig config)
    {
        foreach (var path in config.CodeRoots.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            if (!Directory.Exists(path))
            {
                throw new InputPathException($"Code root does not exist: {path}");
            }
        }

        foreach (var path in config.DllRoots.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            if (!Directory.Exists(path))
            {
                throw new InputPathException($"DLL root does not exist: {path}");
            }
        }

        foreach (var path in config.YooAssetManifestRoots.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            if (!Directory.Exists(path))
            {
                throw new InputPathException($"YooAsset manifest root does not exist: {path}");
            }
        }

        if (!string.IsNullOrWhiteSpace(config.UnityProject) && !Directory.Exists(config.UnityProject))
        {
            throw new InputPathException($"Unity project root does not exist: {config.UnityProject}");
        }

        if (!string.IsNullOrWhiteSpace(config.SolutionPath) && !File.Exists(config.SolutionPath))
        {
            throw new InputPathException($"Solution path does not exist: {config.SolutionPath}");
        }
    }
}
