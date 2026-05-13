namespace OfflineUnityAnalyzer.Core.Models;

public sealed record SourceTypeInfo
{
    public string FullName { get; init; } = string.Empty;

    public string Namespace { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Kind { get; init; } = string.Empty;

    public string SourceFile { get; init; } = string.Empty;

    public string AssemblyName { get; init; } = string.Empty;

    public IReadOnlyList<string> BaseTypes { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Interfaces { get; init; } = Array.Empty<string>();

    public IReadOnlyList<SourceMemberInfo> Members { get; init; } = Array.Empty<SourceMemberInfo>();

    public bool IsMonoBehaviour { get; init; }

    public bool IsScriptableObject { get; init; }

    public bool IsEditorType { get; init; }
}
