namespace OfflineUnityAnalyzer.Core.Models;

public sealed record ProjectModelInfo
{
    public IReadOnlyList<SolutionInfo> Solutions { get; init; } = Array.Empty<SolutionInfo>();

    public IReadOnlyList<CSharpProjectInfo> CSharpProjects { get; init; } = Array.Empty<CSharpProjectInfo>();

    public IReadOnlyList<AssemblyDefinitionInfo> AssemblyDefinitions { get; init; } = Array.Empty<AssemblyDefinitionInfo>();

    public IReadOnlyList<PackageInfo> Packages { get; init; } = Array.Empty<PackageInfo>();
}
