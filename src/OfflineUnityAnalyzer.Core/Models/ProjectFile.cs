namespace OfflineUnityAnalyzer.Core.Models;

public sealed record ProjectFile(
    string FullPath,
    string RootPath,
    string RelativePath,
    ProjectFileKind Kind,
    long SizeBytes,
    DateTime LastWriteTimeUtc);
