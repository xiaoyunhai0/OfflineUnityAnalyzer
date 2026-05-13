namespace OfflineUnityAnalyzer.Core.Models;

public sealed record DiagnosticInfo
{
    public string Severity { get; init; } = "info";

    public string Category { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public string? Path { get; init; }
}
