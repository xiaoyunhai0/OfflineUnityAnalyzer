namespace OfflineUnityAnalyzer.Core.Configuration;

public sealed record ReportConfig
{
    public bool GenerateHtml { get; init; } = true;

    public bool GenerateJson { get; init; } = true;

    public bool OpenAfterFinish { get; init; }
}
