namespace OfflineUnityAnalyzer.Core.Models;

public sealed record CodeAssemblyBridgeInfo
{
    public string SourceType { get; init; } = string.Empty;

    public string SourceFile { get; init; } = string.Empty;

    public string SourceAssemblyName { get; init; } = string.Empty;

    public string AssemblyPath { get; init; } = string.Empty;

    public string AssemblyName { get; init; } = string.Empty;

    public string AssemblyKind { get; init; } = string.Empty;

    public string MatchKind { get; init; } = string.Empty;

    public string Confidence { get; init; } = "medium";
}
