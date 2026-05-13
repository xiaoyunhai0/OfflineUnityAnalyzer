namespace OfflineUnityAnalyzer.Core.Configuration;

public static class DefaultExcludePatterns
{
    public static readonly IReadOnlyList<string> All = new[]
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
        "Generated",
        "node_modules"
    };
}
