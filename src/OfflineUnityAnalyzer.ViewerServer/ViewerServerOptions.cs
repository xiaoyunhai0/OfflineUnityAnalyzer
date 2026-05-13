namespace OfflineUnityAnalyzer.ViewerServer;

public sealed record ViewerServerOptions
{
    public string OutputRoot { get; init; } = string.Empty;

    public int Port { get; init; }
}
