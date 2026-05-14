namespace OfflineUnityAnalyzer.Core.Models;

public sealed record AssemblyMetadataInfo
{
    public string Path { get; init; } = string.Empty;

    public string FileName { get; init; } = string.Empty;

    public string AssemblyName { get; init; } = string.Empty;

    public string Kind { get; init; } = "runtime";

    public string? Version { get; init; }

    public string? PublicKeyToken { get; init; }

    public string Status { get; init; } = "ok";

    public string? Error { get; init; }

    public IReadOnlyList<string> TypeNames { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> MonoBehaviourTypes { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> ScriptableObjectTypes { get; init; } = Array.Empty<string>();
}
