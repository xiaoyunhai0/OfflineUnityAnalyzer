using OfflineUnityAnalyzer.Core.Configuration;
using OfflineUnityAnalyzer.Core.Models;
using OfflineUnityAnalyzer.Core.Safety;

namespace OfflineUnityAnalyzer.Core.Pipeline;

public sealed class AnalysisContext
{
    private readonly List<ProjectFile> _files = new();
    private readonly List<string> _warnings = new();
    private readonly List<string> _errors = new();

    public AnalysisContext(AnalyzerConfig config, PathGuard pathGuard, ISafeFileSystem fileSystem)
    {
        Config = config;
        PathGuard = pathGuard;
        FileSystem = fileSystem;
    }

    public AnalyzerConfig Config { get; }

    public PathGuard PathGuard { get; }

    public ISafeFileSystem FileSystem { get; }

    public IReadOnlyList<ProjectFile> Files => _files;

    public IReadOnlyList<string> Warnings => _warnings;

    public IReadOnlyList<string> Errors => _errors;

    public void AddFile(ProjectFile file)
    {
        _files.Add(file);
    }

    public void AddWarning(string warning)
    {
        _warnings.Add(warning);
    }

    public void AddError(string error)
    {
        _errors.Add(error);
    }
}
