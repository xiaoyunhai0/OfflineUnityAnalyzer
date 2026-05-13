namespace OfflineUnityAnalyzer.Core.Models;

public sealed record AssemblyDefinitionInfo
{
    public string Path { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public IReadOnlyList<string> References { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> IncludePlatforms { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> ExcludePlatforms { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> DefineConstraints { get; init; } = Array.Empty<string>();

    public bool IsEditor => IncludePlatforms.Any(platform => platform.Equals("Editor", StringComparison.OrdinalIgnoreCase))
        || Path.Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)
            .Any(part => part.Equals("Editor", StringComparison.OrdinalIgnoreCase));
}
