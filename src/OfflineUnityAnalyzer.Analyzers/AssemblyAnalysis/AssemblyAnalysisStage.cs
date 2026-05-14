using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
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
                var typeIndex = TryReadTypeIndex(context, file);

                context.AddAssembly(new AssemblyMetadataInfo
                {
                    Path = file.FullPath,
                    FileName = Path.GetFileName(file.FullPath),
                    AssemblyName = assemblyName.Name ?? Path.GetFileNameWithoutExtension(file.FullPath),
                    Version = assemblyName.Version?.ToString(),
                    PublicKeyToken = FormatPublicKeyToken(assemblyName.GetPublicKeyToken()),
                    Kind = InferAssemblyKind(file),
                    Status = typeIndex.Error is null ? "ok" : "metadata-warning",
                    Error = typeIndex.Error,
                    TypeNames = typeIndex.TypeNames,
                    MonoBehaviourTypes = typeIndex.MonoBehaviourTypes,
                    ScriptableObjectTypes = typeIndex.ScriptableObjectTypes
                });

                if (typeIndex.Error is not null)
                {
                    context.AddWarning($"Read assembly name but failed to index types '{file.RelativePath}': {typeIndex.Error}");
                    warnings++;
                }
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

    private static AssemblyTypeIndex TryReadTypeIndex(AnalysisContext context, ProjectFile file)
    {
        try
        {
            using var stream = context.FileSystem.OpenRead(file.FullPath);
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata)
            {
                return new AssemblyTypeIndex(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), "Assembly has no managed metadata.");
            }

            var reader = peReader.GetMetadataReader();
            var typeNames = new List<string>();
            var monoBehaviours = new List<string>();
            var scriptableObjects = new List<string>();

            foreach (var handle in reader.TypeDefinitions)
            {
                var type = reader.GetTypeDefinition(handle);
                var name = reader.GetString(type.Name);
                if (name is "<Module>" or "")
                {
                    continue;
                }

                var namespaceName = reader.GetString(type.Namespace);
                var fullName = string.IsNullOrWhiteSpace(namespaceName)
                    ? name
                    : $"{namespaceName}.{name}";
                typeNames.Add(fullName);

                var baseName = ReadBaseTypeName(reader, type);
                if (baseName is null)
                {
                    continue;
                }

                if (baseName.EndsWith("MonoBehaviour", StringComparison.Ordinal)
                    || baseName.EndsWith("Behaviour", StringComparison.Ordinal))
                {
                    monoBehaviours.Add(fullName);
                }

                if (baseName.EndsWith("ScriptableObject", StringComparison.Ordinal))
                {
                    scriptableObjects.Add(fullName);
                }
            }

            return new AssemblyTypeIndex(
                typeNames.Distinct(StringComparer.Ordinal).OrderBy(value => value).ToArray(),
                monoBehaviours.Distinct(StringComparer.Ordinal).OrderBy(value => value).ToArray(),
                scriptableObjects.Distinct(StringComparer.Ordinal).OrderBy(value => value).ToArray(),
                null);
        }
        catch (Exception exception) when (exception is BadImageFormatException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new AssemblyTypeIndex(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), exception.Message);
        }
    }

    private static string? ReadBaseTypeName(MetadataReader reader, TypeDefinition type)
    {
        if (type.BaseType.IsNil)
        {
            return null;
        }

        return type.BaseType.Kind switch
        {
            HandleKind.TypeReference => ReadTypeReferenceName(reader, (TypeReferenceHandle)type.BaseType),
            HandleKind.TypeDefinition => ReadTypeDefinitionName(reader, (TypeDefinitionHandle)type.BaseType),
            HandleKind.TypeSpecification => "TypeSpecification",
            _ => null
        };
    }

    private static string ReadTypeReferenceName(MetadataReader reader, TypeReferenceHandle handle)
    {
        var reference = reader.GetTypeReference(handle);
        var namespaceName = reader.GetString(reference.Namespace);
        var name = reader.GetString(reference.Name);
        return string.IsNullOrWhiteSpace(namespaceName) ? name : $"{namespaceName}.{name}";
    }

    private static string ReadTypeDefinitionName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var definition = reader.GetTypeDefinition(handle);
        var namespaceName = reader.GetString(definition.Namespace);
        var name = reader.GetString(definition.Name);
        return string.IsNullOrWhiteSpace(namespaceName) ? name : $"{namespaceName}.{name}";
    }

    private static string? FormatPublicKeyToken(byte[]? token)
    {
        if (token is null || token.Length == 0)
        {
            return null;
        }

        return string.Concat(token.Select(value => value.ToString("x2")));
    }

    private sealed record AssemblyTypeIndex(
        IReadOnlyList<string> TypeNames,
        IReadOnlyList<string> MonoBehaviourTypes,
        IReadOnlyList<string> ScriptableObjectTypes,
        string? Error);
}
