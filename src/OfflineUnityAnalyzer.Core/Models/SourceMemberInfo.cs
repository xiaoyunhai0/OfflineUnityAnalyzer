namespace OfflineUnityAnalyzer.Core.Models;

public sealed record SourceMemberInfo
{
    public string Name { get; init; } = string.Empty;

    public string Kind { get; init; } = string.Empty;

    public string Signature { get; init; } = string.Empty;

    public string Visibility { get; init; } = string.Empty;

    public bool IsSerializedField { get; init; }
}
