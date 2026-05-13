namespace OfflineUnityAnalyzer.Indexing;

public sealed record IndexMetadata
{
    public int SchemaVersion { get; init; } = 1;

    public string AnalyzerVersion { get; init; } = "0.1.0";

    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
}
