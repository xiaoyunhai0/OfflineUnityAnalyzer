using System.Reflection;
using OfflineUnityAnalyzer.Core.Models;
using OfflineUnityAnalyzer.Core.Pipeline;

namespace OfflineUnityAnalyzer.Analyzers.AssemblyAnalysis;

public sealed class AssemblyAnalysisStage : IAnalyzerStage
{
    public AnalysisStageKind Kind => AnalysisStageKind.DllIndex;

    public Task<AnalysisStageResult> RunAsync(AnalysisContext context, CancellationToken cancellationToken)
    {
        var processed = 0;
        var warnings = 0;

        foreach (var file in context.Files.Where(file => file.Kind is ProjectFileKind.Dll or ProjectFileKind.DllBytes))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var assemblyName = file.Kind == ProjectFileKind.Dll
                    ? AssemblyName.GetAssemblyName(file.FullPath)
                    : TryReadDllBytesAssemblyName(file.FullPath);

                context.AddAssembly(new AssemblyMetadataInfo
                {
                    Path = file.FullPath,
                    FileName = Path.GetFileName(file.FullPath),
                    AssemblyName = assemblyName.Name ?? Path.GetFileNameWithoutExtension(file.FullPath),
                    Version = assemblyName.Version?.ToString(),
                    PublicKeyToken = FormatPublicKeyToken(assemblyName.GetPublicKeyToken()),
                    Kind = InferAssemblyKind(file),
                    Status = "ok"
                });
            }
            catch (Exception exception) when (exception is BadImageFormatException or FileLoadException or IOException or UnauthorizedAccessException)
            {
                context.AddAssembly(new AssemblyMetadataInfo
                {
                    Path = file.FullPath,
                    FileName = Path.GetFileName(file.FullPath),
                    AssemblyName = Path.GetFileNameWithoutExtension(file.FullPath),
                    Kind = InferAssemblyKind(file),
                    Status = "unreadable",
                    Error = exception.Message
                });
                context.AddWarning($"Failed to read assembly metadata '{file.RelativePath}': {exception.Message}");
                warnings++;
            }

            processed++;
        }

        var status = warnings == 0
            ? AnalysisStageStatus.Completed
            : AnalysisStageStatus.CompletedWithWarnings;

        return Task.FromResult(new AnalysisStageResult(
            Kind,
            status,
            $"Processed {processed} DLL files.",
            WarningCount: warnings));
    }

    private static AssemblyName TryReadDllBytesAssemblyName(string path)
    {
        return AssemblyName.GetAssemblyName(path);
    }

    private static string InferAssemblyKind(ProjectFile file)
    {
        var path = file.FullPath.Replace('\\', '/');

        if (file.Kind == ProjectFileKind.DllBytes
            || path.Contains("HotUpdate", StringComparison.OrdinalIgnoreCase)
            || path.Contains("HybridCLR", StringComparison.OrdinalIgnoreCase))
        {
            return path.Contains("AOT", StringComparison.OrdinalIgnoreCase)
                || path.Contains("AssembliesPostIl2CppStrip", StringComparison.OrdinalIgnoreCase)
                ? "aot_metadata"
                : "hot_update";
        }

        if (path.Contains("/Editor/", StringComparison.OrdinalIgnoreCase))
        {
            return "editor";
        }

        return "runtime";
    }

    private static string? FormatPublicKeyToken(byte[]? token)
    {
        if (token is null || token.Length == 0)
        {
            return null;
        }

        return string.Concat(token.Select(value => value.ToString("x2")));
    }
}
