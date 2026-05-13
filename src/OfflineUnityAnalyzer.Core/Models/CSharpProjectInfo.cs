namespace OfflineUnityAnalyzer.Core.Models;

public sealed record CSharpProjectInfo
{
    public string Path { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string AssemblyName { get; init; } = string.Empty;

    public IReadOnlyList<string> ProjectReferences { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> PackageReferences { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> DefineConstants { get; init; } = Array.Empty<string>();
}
