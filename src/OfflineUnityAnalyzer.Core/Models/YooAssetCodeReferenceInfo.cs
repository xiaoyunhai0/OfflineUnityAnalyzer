namespace OfflineUnityAnalyzer.Core.Models;

public sealed record YooAssetCodeReferenceInfo
{
    public string SourceFile { get; init; } = string.Empty;

    public string ApiName { get; init; } = string.Empty;

    public string? AddressLiteral { get; init; }

    public string Confidence { get; init; } = "medium";
}
