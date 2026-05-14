using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using OfflineUnityAnalyzer.Core.Models;
using OfflineUnityAnalyzer.Core.Pipeline;

namespace OfflineUnityAnalyzer.Analyzers.SourceAnalysis;

public sealed class SourceAnalysisStage : IAnalyzerStage
{
    private static readonly HashSet<string> IgnoredRelationTargets = new(StringComparer.Ordinal)
    {
        "void",
        "bool",
        "byte",
        "sbyte",
        "char",
        "decimal",
        "double",
        "float",
        "int",
        "uint",
        "nint",
        "nuint",
        "long",
        "ulong",
        "object",
        "short",
        "ushort",
        "string",
        "var",
        "Task",
        "ValueTask",
        "List",
        "Dictionary",
        "HashSet",
        "IReadOnlyList",
        "IEnumerable",
        "ICollection",
        "Action",
        "Func",
        "CancellationToken"
    };

    public AnalysisStageKind Kind => AnalysisStageKind.SourceSyntaxIndex;

    public Task<AnalysisStageResult> RunAsync(AnalysisContext context, CancellationToken cancellationToken)
    {
        var parsed = 0;
        var warnings = 0;
        var pendingRelations = new List<SourceTypeRelationInfo>();

        foreach (var file in context.Files.Where(file => file.Kind == ProjectFileKind.CSharpSource))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var text = context.FileSystem.ReadAllText(file.FullPath);
                var tree = CSharpSyntaxTree.ParseText(text, cancellationToken: cancellationToken);
                var root = tree.GetCompilationUnitRoot(cancellationToken);
                var assemblyName = InferAssemblyName(context, file);

                foreach (var declaration in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var type = BuildTypeInfo(file, declaration, assemblyName);
                    context.AddSourceType(type);
                    pendingRelations.AddRange(BuildRelations(file, type, declaration));
                }

                parsed++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                context.AddWarning($"Failed to parse C# source '{file.RelativePath}': {exception.Message}");
                warnings++;
            }
        }

        AddResolvedRelations(context, pendingRelations);
        AddSourceDiagnostics(context, parsed);

        var status = warnings == 0
            ? AnalysisStageStatus.Completed
            : AnalysisStageStatus.CompletedWithWarnings;

        return Task.FromResult(new AnalysisStageResult(
            Kind,
            status,
            $"Parsed {parsed} C# source files, found {context.SourceTypes.Count} types and {context.SourceTypeRelations.Count} type relations.",
            WarningCount: warnings));
    }

    private static SourceTypeInfo BuildTypeInfo(
        ProjectFile file,
        BaseTypeDeclarationSyntax declaration,
        string assemblyName)
    {
        var namespaceName = FindNamespace(declaration);
        var name = declaration.Identifier.ValueText;
        var containingTypes = declaration
            .Ancestors()
            .OfType<BaseTypeDeclarationSyntax>()
            .Reverse()
            .Select(type => type.Identifier.ValueText)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        var nestedName = containingTypes.Length == 0
            ? name
            : $"{string.Join(".", containingTypes)}.{name}";
        var fullName = string.IsNullOrWhiteSpace(namespaceName)
            ? nestedName
            : $"{namespaceName}.{nestedName}";
        var baseTypes = declaration.BaseList?.Types
            .Select(type => CleanTypeName(type.Type.ToString()))
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? Array.Empty<string>();
        var members = ParseMembers(declaration).ToArray();

        return new SourceTypeInfo
        {
            FullName = fullName,
            Namespace = namespaceName,
            Name = name,
            Kind = TypeKind(declaration),
            SourceFile = file.FullPath,
            AssemblyName = assemblyName,
            BaseTypes = baseTypes,
            Interfaces = baseTypes.Where(IsInterfaceName).ToArray(),
            Members = members,
            IsMonoBehaviour = baseTypes.Any(IsMonoBehaviourName),
            IsScriptableObject = baseTypes.Any(item => item.Contains("ScriptableObject", StringComparison.Ordinal)),
            IsEditorType = IsEditorFile(file) || declaration.SyntaxTree.GetText().ToString().Contains("UnityEditor", StringComparison.Ordinal)
        };
    }

    private static IEnumerable<SourceMemberInfo> ParseMembers(BaseTypeDeclarationSyntax declaration)
    {
        foreach (var member in GetMembers(declaration))
        {
            switch (member)
            {
                case FieldDeclarationSyntax field:
                    foreach (var variable in field.Declaration.Variables)
                    {
                        yield return new SourceMemberInfo
                        {
                            Name = variable.Identifier.ValueText,
                            Kind = "field",
                            Signature = BuildFieldSignature(field, variable),
                            Visibility = Visibility(field.Modifiers),
                            IsSerializedField = IsSerializedField(field)
                        };
                    }

                    break;
                case PropertyDeclarationSyntax property:
                    yield return new SourceMemberInfo
                    {
                        Name = property.Identifier.ValueText,
                        Kind = "property",
                        Signature = $"{Visibility(property.Modifiers)} {property.Type} {property.Identifier.ValueText}",
                        Visibility = Visibility(property.Modifiers),
                        IsSerializedField = IsSerializedField(property)
                    };
                    break;
                case MethodDeclarationSyntax method:
                    yield return new SourceMemberInfo
                    {
                        Name = method.Identifier.ValueText,
                        Kind = "method",
                        Signature = $"{Visibility(method.Modifiers)} {method.ReturnType} {method.Identifier.ValueText}({string.Join(", ", method.ParameterList.Parameters.Select(parameter => parameter.Type?.ToString() ?? "var"))})",
                        Visibility = Visibility(method.Modifiers),
                        IsSerializedField = false
                    };
                    break;
                case ConstructorDeclarationSyntax constructor:
                    yield return new SourceMemberInfo
                    {
                        Name = constructor.Identifier.ValueText,
                        Kind = "constructor",
                        Signature = $"{Visibility(constructor.Modifiers)} {constructor.Identifier.ValueText}({string.Join(", ", constructor.ParameterList.Parameters.Select(parameter => parameter.Type?.ToString() ?? "var"))})",
                        Visibility = Visibility(constructor.Modifiers),
                        IsSerializedField = false
                    };
                    break;
                case EventFieldDeclarationSyntax eventField:
                    foreach (var variable in eventField.Declaration.Variables)
                    {
                        yield return new SourceMemberInfo
                        {
                            Name = variable.Identifier.ValueText,
                            Kind = "event",
                            Signature = $"{Visibility(eventField.Modifiers)} event {eventField.Declaration.Type} {variable.Identifier.ValueText}",
                            Visibility = Visibility(eventField.Modifiers),
                            IsSerializedField = false
                        };
                    }

                    break;
            }
        }
    }

    private static IEnumerable<SourceTypeRelationInfo> BuildRelations(
        ProjectFile file,
        SourceTypeInfo sourceType,
        BaseTypeDeclarationSyntax declaration)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var target in sourceType.BaseTypes)
        {
            if (TryCreateRelation(file, sourceType, target, "inherits", null, seen, out var relation))
            {
                yield return relation;
            }
        }

        foreach (var field in GetMembers(declaration).OfType<FieldDeclarationSyntax>())
        {
            foreach (var target in ExtractTypeNames(field.Declaration.Type))
            {
                if (TryCreateRelation(
                    file,
                    sourceType,
                    target,
                    IsSerializedField(field) ? "serialized-field" : "field",
                    string.Join(", ", field.Declaration.Variables.Select(variable => variable.Identifier.ValueText)),
                    seen,
                    out var relation))
                {
                    yield return relation;
                }
            }
        }

        foreach (var property in GetMembers(declaration).OfType<PropertyDeclarationSyntax>())
        {
            foreach (var target in ExtractTypeNames(property.Type))
            {
                if (TryCreateRelation(file, sourceType, target, "property", property.Identifier.ValueText, seen, out var relation))
                {
                    yield return relation;
                }
            }
        }

        foreach (var method in GetMembers(declaration).OfType<MethodDeclarationSyntax>())
        {
            foreach (var target in ExtractTypeNames(method.ReturnType))
            {
                if (TryCreateRelation(file, sourceType, target, "returns", method.Identifier.ValueText, seen, out var relation))
                {
                    yield return relation;
                }
            }

            foreach (var parameter in method.ParameterList.Parameters)
            {
                if (parameter.Type is null)
                {
                    continue;
                }

                foreach (var target in ExtractTypeNames(parameter.Type))
                {
                    if (TryCreateRelation(file, sourceType, target, "parameter", method.Identifier.ValueText, seen, out var relation))
                    {
                        yield return relation;
                    }
                }
            }
        }

        foreach (var objectCreation in declaration.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
        {
            foreach (var target in ExtractTypeNames(objectCreation.Type))
            {
                if (TryCreateRelation(file, sourceType, target, "creates", FindMemberName(objectCreation), seen, out var relation))
                {
                    yield return relation;
                }
            }
        }

        foreach (var invocation in declaration.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var target = ExtractInvocationTarget(invocation);
            if (target is null)
            {
                continue;
            }

            if (TryCreateRelation(file, sourceType, target, "calls", FindMemberName(invocation), seen, out var relation))
            {
                yield return relation;
            }
        }
    }

    private static bool TryCreateRelation(
        ProjectFile file,
        SourceTypeInfo sourceType,
        string target,
        string kind,
        string? member,
        HashSet<string> seen,
        out SourceTypeRelationInfo relation)
    {
        var display = CleanTypeName(target);
        var key = $"{sourceType.FullName}|{display}|{kind}|{member}";
        if (!seen.Add(key))
        {
            relation = new SourceTypeRelationInfo();
            return false;
        }

        relation = new SourceTypeRelationInfo
        {
            SourceType = sourceType.FullName,
            TargetType = display,
            TargetDisplayName = display,
            RelationKind = kind,
            SourceFile = file.FullPath,
            SourceMember = member,
            Confidence = kind is "calls" ? "low" : "medium"
        };
        return true;
    }

    private static void AddResolvedRelations(AnalysisContext context, IEnumerable<SourceTypeRelationInfo> relations)
    {
        var byShortName = context.SourceTypes
            .GroupBy(type => type.Name, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.First().FullName, StringComparer.Ordinal);
        var byFullName = context.SourceTypes
            .ToDictionary(type => type.FullName, type => type.FullName, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var relation in relations)
        {
            if (string.IsNullOrWhiteSpace(relation.SourceType)
                || string.IsNullOrWhiteSpace(relation.TargetType)
                || relation.SourceType.Equals(relation.TargetType, StringComparison.Ordinal)
                || ShouldSkipTarget(relation.TargetType))
            {
                continue;
            }

            var resolved = byFullName.GetValueOrDefault(relation.TargetType)
                ?? byShortName.GetValueOrDefault(ShortName(relation.TargetType));
            var normalizedTarget = resolved ?? relation.TargetType;
            var key = $"{relation.SourceType}|{normalizedTarget}|{relation.RelationKind}|{relation.SourceMember}";
            if (!seen.Add(key))
            {
                continue;
            }

            context.AddSourceTypeRelation(relation with
            {
                TargetType = normalizedTarget,
                TargetDisplayName = resolved is null ? relation.TargetDisplayName : ShortName(resolved),
                IsResolved = resolved is not null,
                Confidence = resolved is not null ? "high" : relation.Confidence
            });
        }
    }

    private static IEnumerable<string> ExtractTypeNames(TypeSyntax type)
    {
        switch (type)
        {
            case PredefinedTypeSyntax predefined:
                yield return predefined.Keyword.ValueText;
                break;
            case IdentifierNameSyntax identifier:
                yield return CleanTypeName(identifier.Identifier.ValueText);
                break;
            case QualifiedNameSyntax qualified:
                yield return CleanTypeName(qualified.ToString());
                break;
            case AliasQualifiedNameSyntax alias:
                yield return CleanTypeName(alias.Name.Identifier.ValueText);
                break;
            case GenericNameSyntax generic:
                yield return CleanTypeName(generic.Identifier.ValueText);
                foreach (var argument in generic.TypeArgumentList.Arguments.SelectMany(ExtractTypeNames))
                {
                    yield return argument;
                }

                break;
            case NullableTypeSyntax nullable:
                foreach (var item in ExtractTypeNames(nullable.ElementType))
                {
                    yield return item;
                }

                break;
            case ArrayTypeSyntax array:
                foreach (var item in ExtractTypeNames(array.ElementType))
                {
                    yield return item;
                }

                break;
            case TupleTypeSyntax tuple:
                foreach (var item in tuple.Elements.Select(element => element.Type).SelectMany(ExtractTypeNames))
                {
                    yield return item;
                }

                break;
        }
    }

    private static IEnumerable<MemberDeclarationSyntax> GetMembers(BaseTypeDeclarationSyntax declaration)
    {
        return declaration switch
        {
            TypeDeclarationSyntax type => type.Members,
            _ => Array.Empty<MemberDeclarationSyntax>()
        };
    }

    private static string? ExtractInvocationTarget(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess
                when memberAccess.Expression is IdentifierNameSyntax identifier => CleanTypeName(identifier.Identifier.ValueText),
            MemberAccessExpressionSyntax memberAccess
                when memberAccess.Expression is GenericNameSyntax generic => CleanTypeName(generic.Identifier.ValueText),
            MemberAccessExpressionSyntax memberAccess
                when memberAccess.Expression is MemberAccessExpressionSyntax nested => CleanTypeName(nested.Name.Identifier.ValueText),
            IdentifierNameSyntax identifier => CleanTypeName(identifier.Identifier.ValueText),
            _ => null
        };
    }

    private static string? FindMemberName(SyntaxNode node)
    {
        return node.Ancestors().OfType<BaseMethodDeclarationSyntax>().FirstOrDefault()?.ToString().Split('(', 2)[0].Trim().Split(' ').LastOrDefault()
            ?? node.Ancestors().OfType<PropertyDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText;
    }

    private static string BuildFieldSignature(FieldDeclarationSyntax field, VariableDeclaratorSyntax variable)
    {
        var prefix = IsSerializedField(field)
            ? "[SerializeField] "
            : string.Empty;
        return $"{prefix}{Visibility(field.Modifiers)} {field.Declaration.Type} {variable.Identifier.ValueText}";
    }

    private static bool IsSerializedField(MemberDeclarationSyntax declaration)
    {
        if (declaration is FieldDeclarationSyntax field && field.Modifiers.Any(SyntaxKind.PublicKeyword))
        {
            return true;
        }

        return declaration.AttributeLists
            .SelectMany(list => list.Attributes)
            .Any(attribute =>
            {
                var name = attribute.Name.ToString();
                return name.Equals("SerializeField", StringComparison.Ordinal)
                    || name.EndsWith(".SerializeField", StringComparison.Ordinal)
                    || name.Equals("field:SerializeField", StringComparison.Ordinal);
            });
    }

    private static string FindNamespace(SyntaxNode node)
    {
        var namespaceNode = node.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();
        return namespaceNode?.Name.ToString() ?? string.Empty;
    }

    private static string TypeKind(BaseTypeDeclarationSyntax declaration)
    {
        return declaration switch
        {
            ClassDeclarationSyntax => "class",
            InterfaceDeclarationSyntax => "interface",
            StructDeclarationSyntax => "struct",
            EnumDeclarationSyntax => "enum",
            RecordDeclarationSyntax => "record",
            _ => "type"
        };
    }

    private static string Visibility(SyntaxTokenList modifiers)
    {
        if (modifiers.Any(SyntaxKind.PublicKeyword)) return "public";
        if (modifiers.Any(SyntaxKind.ProtectedKeyword) && modifiers.Any(SyntaxKind.InternalKeyword)) return "protected internal";
        if (modifiers.Any(SyntaxKind.ProtectedKeyword)) return "protected";
        if (modifiers.Any(SyntaxKind.InternalKeyword)) return "internal";
        return "private";
    }

    private static string CleanTypeName(string value)
    {
        var cleaned = value
            .Replace("global::", string.Empty, StringComparison.Ordinal)
            .Replace("?", string.Empty, StringComparison.Ordinal)
            .Trim();
        var genericIndex = cleaned.IndexOf('<', StringComparison.Ordinal);
        if (genericIndex >= 0)
        {
            cleaned = cleaned[..genericIndex];
        }

        var arrayIndex = cleaned.IndexOf('[', StringComparison.Ordinal);
        if (arrayIndex >= 0)
        {
            cleaned = cleaned[..arrayIndex];
        }

        return cleaned.Trim();
    }

    private static string ShortName(string value)
    {
        var cleaned = CleanTypeName(value);
        var index = cleaned.LastIndexOf('.');
        return index >= 0 ? cleaned[(index + 1)..] : cleaned;
    }

    private static bool ShouldSkipTarget(string target)
    {
        var shortName = ShortName(target);
        return string.IsNullOrWhiteSpace(shortName)
            || IgnoredRelationTargets.Contains(shortName)
            || char.IsLower(shortName[0])
            || shortName.Length <= 1;
    }

    private static bool IsInterfaceName(string name)
    {
        var shortName = ShortName(name);
        return shortName.StartsWith("I", StringComparison.Ordinal)
            && shortName.Length > 1
            && char.IsUpper(shortName[1]);
    }

    private static bool IsMonoBehaviourName(string name)
    {
        return name.Contains("MonoBehaviour", StringComparison.Ordinal)
            || name.EndsWith("Behaviour", StringComparison.Ordinal);
    }

    private static bool IsEditorFile(ProjectFile file)
    {
        return file.RelativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(part => part.Equals("Editor", StringComparison.OrdinalIgnoreCase));
    }

    private static string InferAssemblyName(AnalysisContext context, ProjectFile file)
    {
        var asmdef = context.ProjectModel.AssemblyDefinitions
            .Where(item => ContainsPath(Path.GetDirectoryName(item.Path) ?? string.Empty, file.FullPath))
            .OrderByDescending(item => item.Path.Length)
            .FirstOrDefault();
        if (asmdef is not null)
        {
            return asmdef.Name;
        }

        return IsEditorFile(file) ? "Assembly-CSharp-Editor" : "Assembly-CSharp";
    }

    private static bool ContainsPath(string root, string path)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (OperatingSystem.IsWindows())
        {
            normalizedRoot = normalizedRoot.ToUpperInvariant();
            normalizedPath = normalizedPath.ToUpperInvariant();
        }

        return normalizedPath.Equals(normalizedRoot, StringComparison.Ordinal)
            || normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || normalizedPath.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static void AddSourceDiagnostics(AnalysisContext context, int parsedFiles)
    {
        if (parsedFiles == 0)
        {
            context.AddDiagnostic(new DiagnosticInfo
            {
                Severity = "warning",
                Category = "source",
                Message = "No C# source files were indexed. Check Unity/code roots or disable source analysis for asset-only runs."
            });
        }

        if (context.SourceTypeRelations.Count == 0 && context.SourceTypes.Count > 0)
        {
            context.AddDiagnostic(new DiagnosticInfo
            {
                Severity = "info",
                Category = "source",
                Message = "C# types were indexed, but no cross-type relations were inferred. The project may use reflection, generated code, or weakly-typed event wiring."
            });
        }
    }
}
