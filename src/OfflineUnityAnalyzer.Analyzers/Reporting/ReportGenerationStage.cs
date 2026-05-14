using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using OfflineUnityAnalyzer.Core.Models;
using OfflineUnityAnalyzer.Core.Pipeline;

namespace OfflineUnityAnalyzer.Analyzers.Reporting;

public sealed class ReportGenerationStage : IAnalyzerStage
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public AnalysisStageKind Kind => AnalysisStageKind.ReportExport;

    public Task<AnalysisStageResult> RunAsync(AnalysisContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var summary = BuildSummary(context);
        WriteJson(context, "data/summary.json", summary);
        WriteJson(context, "data/files.json", context.Files);
        WriteJson(context, "data/project-model.json", context.ProjectModel);
        WriteJson(context, "data/modules.json", context.Modules);
        WriteJson(context, "data/types.json", context.SourceTypes);
        WriteJson(context, "data/source-relations.json", context.SourceTypeRelations);
        WriteJson(context, "data/code-assembly-bridges.json", context.CodeAssemblyBridges);
        WriteJson(context, "data/assemblies.json", context.Assemblies);
        WriteJson(context, "data/unity-assets.json", context.UnityAssets);
        WriteJson(context, "data/unity-script-refs.json", context.UnityScriptReferences);
        WriteJson(context, "data/unity-objects.json", context.UnityObjects);
        WriteJson(context, "data/unity-gameobjects.json", context.UnityGameObjects);
        WriteJson(context, "data/unity-components.json", context.UnityComponents);
        WriteJson(context, "data/unity-asset-refs.json", context.UnityAssetReferences);
        WriteJson(context, "data/hybridclr.json", context.HybridClr);
        WriteJson(context, "data/yooasset.json", context.YooAsset);
        WriteJson(context, "data/config-refs.json", context.ConfigReferences);
        WriteJson(context, "data/diagnostics.json", context.Diagnostics);

        var reportData = BuildReportData(context, summary);
        context.FileSystem.WriteAllTextToOutput("report/assets/style.css", BuildCss());
        context.FileSystem.WriteAllTextToOutput("report/assets/app.js", BuildAppJs());
        context.FileSystem.WriteAllTextToOutput("report/report.html", BuildHtml(reportData));

        return Task.FromResult(new AnalysisStageResult(
            Kind,
            AnalysisStageStatus.Completed,
            "Generated project-map HTML report and full JSON data."));
    }

    private static ReportSummary BuildSummary(AnalysisContext context)
    {
        return new ReportSummary
        {
            GeneratedAtUtc = DateTime.UtcNow,
            FileCount = context.Files.Count,
            SourceTypeCount = context.SourceTypes.Count,
            SourceRelationCount = context.SourceTypeRelations.Count,
            ResolvedSourceRelationCount = context.SourceTypeRelations.Count(relation => relation.IsResolved),
            CodeAssemblyBridgeCount = context.CodeAssemblyBridges.Count,
            HotUpdateBridgeCount = context.CodeAssemblyBridges.Count(bridge => bridge.AssemblyKind == "hot_update"),
            MonoBehaviourCount = context.SourceTypes.Count(type => type.IsMonoBehaviour),
            ScriptableObjectCount = context.SourceTypes.Count(type => type.IsScriptableObject),
            SerializedFieldCount = context.SourceTypes.Sum(type => type.Members.Count(member => member.IsSerializedField)),
            AssemblyCount = context.Assemblies.Count,
            PackageCount = context.ProjectModel.Packages.Count,
            AsmdefCount = context.ProjectModel.AssemblyDefinitions.Count,
            CsprojCount = context.ProjectModel.CSharpProjects.Count,
            ModuleCount = context.Modules.Count,
            UnityAssetCount = context.UnityAssets.Count,
            UnityObjectCount = context.UnityObjects.Count,
            UnityGameObjectCount = context.UnityGameObjects.Count,
            UnityComponentCount = context.UnityComponents.Count,
            UnityAssetReferenceCount = context.UnityAssetReferences.Count,
            UnityScriptReferenceCount = context.UnityScriptReferences.Count,
            UnresolvedUnityScriptReferenceCount = context.UnityScriptReferences.Count(reference => reference.ResolvedScriptPath is null),
            DiagnosticCount = context.Diagnostics.Count,
            WarningDiagnosticCount = context.Diagnostics.Count(diagnostic => diagnostic.Severity.Equals("warning", StringComparison.OrdinalIgnoreCase)),
            ErrorDiagnosticCount = context.Diagnostics.Count(diagnostic => diagnostic.Severity.Equals("error", StringComparison.OrdinalIgnoreCase)),
            ConfigReferenceCount = context.ConfigReferences.Count,
            YooAssetManifestAssetCount = context.YooAsset.Assets.Count,
            YooAssetCodeReferenceCount = context.YooAsset.StructuredCodeReferences.Count,
            HybridClrDetected = context.HybridClr.Detected,
            YooAssetDetected = context.YooAsset.Detected,
            ByFileKind = context.Files
                .GroupBy(file => file.Kind.ToString())
                .OrderBy(group => group.Key)
                .ToDictionary(group => group.Key, group => group.Count()),
            ByRelationKind = context.SourceTypeRelations
                .GroupBy(relation => relation.RelationKind)
                .OrderBy(group => group.Key)
                .ToDictionary(group => group.Key, group => group.Count()),
            ByAssemblyKind = context.Assemblies
                .GroupBy(assembly => assembly.Kind)
                .OrderBy(group => group.Key)
                .ToDictionary(group => group.Key, group => group.Count())
        };
    }

    private static object BuildReportData(AnalysisContext context, ReportSummary summary)
    {
        var projectBrief = BuildProjectBrief(context, summary);
        var moduleCards = BuildModuleCards(context).Take(80).ToArray();
        var moduleRelations = BuildModuleRelations(context).Take(120).ToArray();
        var typeRelationCards = BuildTypeRelationCards(context).Take(80).ToArray();
        var topTypes = BuildTopTypes(context).Take(120).ToArray();
        var unityBindings = BuildUnityBindings(context).Take(160).ToArray();
        var unresolvedRefs = context.UnityScriptReferences
            .Where(reference => reference.ResolvedScriptPath is null)
            .Take(120)
            .ToArray();
        var assetChains = BuildAssetChains(context).Take(160).ToArray();
        var projectModel = BuildProjectModel(context);
        var codeAssemblyBridges = BuildCodeAssemblyBridges(context).Take(160).ToArray();

        return new
        {
            summary,
            projectBrief,
            projectModel,
            codeAssemblyBridges,
            modules = moduleCards,
            moduleRelations,
            typeRelationCards,
            topTypes,
            unityBindings,
            unresolvedRefs,
            assetChains,
            diagnostics = context.Diagnostics.Take(160),
            configReferences = context.ConfigReferences.Take(160),
            hybridClr = context.HybridClr,
            yooAsset = context.YooAsset,
            filesByKind = summary.ByFileKind.Select(pair => new { kind = pair.Key, count = pair.Value }),
            fullDataFiles = new[]
            {
                "data/summary.json",
                "data/files.json",
                "data/types.json",
                "data/source-relations.json",
                "data/code-assembly-bridges.json",
                "data/unity-components.json",
                "data/unity-asset-refs.json",
                "data/yooasset.json",
                "data/diagnostics.json"
            }
        };
    }

    private static object BuildProjectBrief(AnalysisContext context, ReportSummary summary)
    {
        var sourceRoots = context.SourceTypes
            .Select(type => GuessRoot(type.SourceFile))
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(root => root, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();
        var unityRoot = context.Config.UnityProject is null
            ? "未提供 Unity 项目目录"
            : ShortPath(context.Config.UnityProject);
        var codeRootText = context.Config.CodeRoots.Count > 0
            ? string.Join("、", context.Config.CodeRoots.Select(ShortPath).Take(4))
            : sourceRoots.Length == 0
                ? "未识别到独立源码目录"
                : string.Join("、", sourceRoots.Select(ShortPath));
        var dllRootText = context.Config.DllRoots.Count > 0
            ? string.Join("、", context.Config.DllRoots.Select(ShortPath).Take(4))
            : "自动从 Unity 项目内扫描";
        var sourceUnityMode = context.CodeAssemblyBridges.Count > 0
            ? "已识别“外部源码 + Unity 编译 DLL”分离结构"
            : context.Config.CodeRoots.Count > 0
                ? "已提供独立源码目录，暂未找到源码/DLL 对应"
                : "以 Unity 项目内源码为主";

        var suggestions = new List<string>();
        if (context.SourceTypeRelations.Count == 0 && context.SourceTypes.Count > 0)
        {
            suggestions.Add("源码类型已解析，但关系边很少，建议确认是否开启 C# 关系分析。");
        }

        if (summary.UnresolvedUnityScriptReferenceCount > 0)
        {
            suggestions.Add($"有 {summary.UnresolvedUnityScriptReferenceCount} 个 Unity 脚本引用没有解析到脚本文件，可能存在 GUID 丢失或源码/DLL 分离。");
        }

        if (context.CodeAssemblyBridges.Count == 0 && context.Assemblies.Count > 0 && context.SourceTypes.Count > 0)
        {
            suggestions.Add("发现源码和 DLL，但暂未建立对应关系，报告会优先展示已确认的源码关系和 Unity 绑定。");
        }

        if (context.Diagnostics.Any(diagnostic => diagnostic.Severity.Equals("error", StringComparison.OrdinalIgnoreCase)))
        {
            suggestions.Add("存在错误级诊断，建议先查看“风险与诊断”里的解析失败原因。");
        }

        if (suggestions.Count == 0)
        {
            suggestions.Add("扫描结果完整度正常，可以从模块关系和重点类型关系开始阅读。");
        }

        return new
        {
            generatedAt = summary.GeneratedAtUtc,
            unityProject = unityRoot,
            codeRoots = codeRootText,
            dllRoots = dllRootText,
            sourceUnityMode,
            relationCoverage = summary.SourceRelationCount == 0
                ? "暂无源码关系"
                : $"{summary.ResolvedSourceRelationCount}/{summary.SourceRelationCount} 条源码关系已解析到具体类型",
            unityCoverage = summary.UnityScriptReferenceCount == 0
                ? "未发现 Unity 脚本引用"
                : $"{summary.UnityScriptReferenceCount - summary.UnresolvedUnityScriptReferenceCount}/{summary.UnityScriptReferenceCount} 个 Unity 脚本引用可回溯",
            hotUpdate = context.HybridClr.Detected
                ? $"检测到 HybridCLR，热更 DLL 对应 {summary.HotUpdateBridgeCount} 条"
                : "未检测到 HybridCLR 证据",
            assetSystem = context.YooAsset.Detected
                ? $"检测到 YooAsset，清单资源 {summary.YooAssetManifestAssetCount} 个，代码引用 {summary.YooAssetCodeReferenceCount} 条"
                : "未检测到 YooAsset 证据",
            suggestions = suggestions.Take(5).ToArray()
        };
    }

    private static IEnumerable<object> BuildModuleCards(AnalysisContext context)
    {
        var typesByModule = context.SourceTypes
            .GroupBy(type => InferModuleName(type, context.Modules))
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var assetsByModule = context.UnityAssets
            .GroupBy(asset => InferModuleFromPath(asset.Path) ?? "Unmapped Assets")
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var moduleNames = context.Modules.Select(module => module.Name)
            .Concat(typesByModule.Keys)
            .Concat(assetsByModule.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var name in moduleNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            typesByModule.TryGetValue(name, out var moduleTypes);
            assetsByModule.TryGetValue(name, out var moduleAssets);
            moduleTypes ??= Array.Empty<SourceTypeInfo>();
            moduleAssets ??= Array.Empty<UnityAssetInfo>();
            var typeNames = moduleTypes.Select(type => type.FullName).ToHashSet(StringComparer.Ordinal);
            var outbound = context.SourceTypeRelations
                .Where(relation => typeNames.Contains(relation.SourceType)
                    && !typeNames.Contains(relation.TargetType)
                    && relation.IsResolved)
                .GroupBy(relation => relation.TargetType)
                .OrderByDescending(group => group.Count())
                .Take(8)
                .Select(group => new
                {
                    target = ShortName(group.Key),
                    count = group.Count(),
                    kinds = group.Select(relation => relation.RelationKind).Distinct().OrderBy(value => value).ToArray()
                })
                .ToArray();

            yield return new
            {
                name,
                typeCount = moduleTypes.Length,
                monoBehaviourCount = moduleTypes.Count(type => type.IsMonoBehaviour),
                scriptableObjectCount = moduleTypes.Count(type => type.IsScriptableObject),
                assetCount = moduleAssets.Length,
                topTypes = moduleTypes
                    .OrderByDescending(type => RelationScore(context, type.FullName))
                    .Take(8)
                    .Select(type => new
                    {
                        type.FullName,
                        type.Name,
                        type.Kind,
                        type.IsMonoBehaviour,
                        type.IsScriptableObject,
                        memberCount = type.Members.Count,
                        relationCount = context.SourceTypeRelations.Count(relation => relation.SourceType == type.FullName)
                    })
                    .ToArray(),
                outbound
            };
        }
    }

    private static IEnumerable<object> BuildTopTypes(AnalysisContext context)
    {
        var inbound = context.SourceTypeRelations
            .Where(relation => relation.IsResolved)
            .GroupBy(relation => relation.TargetType)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var outbound = context.SourceTypeRelations
            .GroupBy(relation => relation.SourceType)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var unityRefs = context.UnityComponents
            .Where(component => !string.IsNullOrWhiteSpace(component.ResolvedType))
            .GroupBy(component => component.ResolvedType!)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        return context.SourceTypes
            .Select(type =>
            {
                var inCount = inbound.GetValueOrDefault(type.FullName);
                var outCount = outbound.GetValueOrDefault(type.FullName);
                var unityCount = unityRefs.GetValueOrDefault(type.Name);

                return new
                {
                    type.FullName,
                    type.Name,
                    type.Namespace,
                    type.Kind,
                    type.AssemblyName,
                    type.IsMonoBehaviour,
                    type.IsScriptableObject,
                    type.IsEditorType,
                    memberCount = type.Members.Count,
                    serializedFieldCount = type.Members.Count(member => member.IsSerializedField),
                    inboundCount = inCount,
                    outboundCount = outCount,
                    unityBindingCount = unityCount,
                    score = inCount * 2 + outCount + unityCount * 3 + (type.IsMonoBehaviour ? 5 : 0) + (type.IsScriptableObject ? 4 : 0),
                    bases = type.BaseTypes,
                    sourceFile = ShortPath(type.SourceFile)
                };
            })
            .OrderByDescending(type => type.score)
            .ThenBy(type => type.FullName, StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<object> BuildModuleRelations(AnalysisContext context)
    {
        var moduleByType = context.SourceTypes
            .GroupBy(type => type.FullName, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToDictionary(type => type.FullName, type => InferModuleName(type, context.Modules), StringComparer.Ordinal);

        return context.SourceTypeRelations
            .Where(relation => moduleByType.ContainsKey(relation.SourceType)
                && moduleByType.ContainsKey(relation.TargetType)
                && relation.IsResolved)
            .Select(relation => new
            {
                Relation = relation,
                SourceModule = moduleByType[relation.SourceType],
                TargetModule = moduleByType[relation.TargetType]
            })
            .Where(item => !item.SourceModule.Equals(item.TargetModule, StringComparison.OrdinalIgnoreCase))
            .GroupBy(item => new { item.SourceModule, item.TargetModule })
            .OrderByDescending(group => group.Count())
            .Select(group => new
            {
                sourceModule = group.Key.SourceModule,
                targetModule = group.Key.TargetModule,
                relationCount = group.Count(),
                relationKinds = group
                    .GroupBy(item => item.Relation.RelationKind)
                    .OrderByDescending(kind => kind.Count())
                    .ThenBy(kind => kind.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(kind => new { kind = kind.Key, count = kind.Count(), label = TranslateRelationKind(kind.Key) })
                    .ToArray(),
                sampleFlows = group
                    .GroupBy(item => new { item.Relation.SourceType, item.Relation.TargetType })
                    .OrderByDescending(flow => flow.Count())
                    .ThenBy(flow => flow.Key.SourceType, StringComparer.OrdinalIgnoreCase)
                    .Take(6)
                    .Select(flow => new
                    {
                        source = ShortName(flow.Key.SourceType),
                        sourceFullName = flow.Key.SourceType,
                        target = ShortName(flow.Key.TargetType),
                        targetFullName = flow.Key.TargetType,
                        count = flow.Count(),
                        relationKinds = flow
                            .Select(item => TranslateRelationKind(item.Relation.RelationKind))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                            .ToArray()
                    })
                    .ToArray()
            });
    }

    private static IEnumerable<object> BuildTypeRelationCards(AnalysisContext context)
    {
        var inbound = context.SourceTypeRelations
            .Where(relation => relation.IsResolved)
            .GroupBy(relation => relation.TargetType)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var outbound = context.SourceTypeRelations
            .GroupBy(relation => relation.SourceType)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var unityRefs = context.UnityComponents
            .Where(component => !string.IsNullOrWhiteSpace(component.ResolvedType))
            .GroupBy(component => component.ResolvedType!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        foreach (var type in context.SourceTypes
            .OrderByDescending(type => RelationScore(context, type.FullName)
                + (type.IsMonoBehaviour ? 10 : 0)
                + (type.IsScriptableObject ? 8 : 0)
                + (type.IsEditorType ? -2 : 0)
                + unityRefs.GetValueOrDefault(type.Name) * 4)
            .ThenBy(type => type.FullName, StringComparer.OrdinalIgnoreCase))
        {
            inbound.TryGetValue(type.FullName, out var inboundRelations);
            outbound.TryGetValue(type.FullName, out var outboundRelations);
            inboundRelations ??= Array.Empty<SourceTypeRelationInfo>();
            outboundRelations ??= Array.Empty<SourceTypeRelationInfo>();

            var tags = new List<string>();
            if (type.IsMonoBehaviour)
            {
                tags.Add("MonoBehaviour");
            }

            if (type.IsScriptableObject)
            {
                tags.Add("ScriptableObject");
            }

            if (type.IsEditorType)
            {
                tags.Add("Editor");
            }

            if (unityRefs.GetValueOrDefault(type.Name) > 0)
            {
                tags.Add("Unity 绑定");
            }

            if (tags.Count == 0)
            {
                tags.Add(type.Kind);
            }

            yield return new
            {
                type.FullName,
                type.Name,
                type.Namespace,
                module = InferModuleName(type, context.Modules),
                type.AssemblyName,
                tags,
                inboundCount = inboundRelations.Length,
                outboundCount = outboundRelations.Length,
                unityBindingCount = unityRefs.GetValueOrDefault(type.Name),
                baseTypes = type.BaseTypes.Select(ShortName).Take(5).ToArray(),
                sourceFile = ShortPath(type.SourceFile),
                outgoingGroups = BuildRelationGroups(outboundRelations, isOutgoing: true).Take(6).ToArray(),
                incomingGroups = BuildRelationGroups(inboundRelations, isOutgoing: false).Take(4).ToArray()
            };
        }
    }

    private static IEnumerable<object> BuildRelationGroups(IEnumerable<SourceTypeRelationInfo> relations, bool isOutgoing)
    {
        return relations
            .GroupBy(relation => relation.RelationKind)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                kind = group.Key,
                label = TranslateRelationKind(group.Key),
                count = group.Count(),
                samples = group
                    .GroupBy(relation => isOutgoing ? relation.TargetType : relation.SourceType)
                    .OrderByDescending(sample => sample.Count())
                    .ThenBy(sample => sample.Key, StringComparer.OrdinalIgnoreCase)
                    .Take(6)
                    .Select(sample => new
                    {
                        type = ShortName(sample.Key),
                        fullName = sample.Key,
                        count = sample.Count(),
                        members = sample
                            .Select(relation => relation.SourceMember)
                            .Where(member => !string.IsNullOrWhiteSpace(member))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Take(3)
                            .ToArray()
                    })
                    .ToArray()
            });
    }

    private static IEnumerable<object> BuildCodeAssemblyBridges(AnalysisContext context)
    {
        return context.CodeAssemblyBridges
            .OrderByDescending(bridge => bridge.Confidence == "high")
            .ThenBy(bridge => bridge.SourceType, StringComparer.Ordinal)
            .Select(bridge => new
            {
                sourceType = bridge.SourceType,
                source = ShortPath(bridge.SourceFile),
                assembly = bridge.AssemblyName,
                assemblyKind = bridge.AssemblyKind,
                assemblyPath = ShortPath(bridge.AssemblyPath),
                bridge.MatchKind,
                bridge.Confidence
            });
    }

    private static IEnumerable<object> BuildUnityBindings(AnalysisContext context)
    {
        return context.UnityComponents
            .Where(component => !string.IsNullOrWhiteSpace(component.ScriptGuid)
                || !string.IsNullOrWhiteSpace(component.GameObjectName))
            .GroupBy(component => new
            {
                component.AssetPath,
                component.GameObjectName,
                Script = component.ResolvedType
                    ?? (component.ResolvedScriptPath is null ? null : Path.GetFileNameWithoutExtension(component.ResolvedScriptPath))
                    ?? component.ScriptGuid,
                component.TypeName
            })
            .OrderBy(group => ShortPath(group.Key.AssetPath))
            .ThenBy(group => group.Key.GameObjectName)
            .Select(group => new
            {
                asset = ShortPath(group.Key.AssetPath),
                gameObject = group.Key.GameObjectName ?? "(asset component)",
                script = group.Key.Script ?? group.Key.TypeName,
                componentType = group.Key.TypeName,
                count = group.Count()
            });
    }

    private static IEnumerable<object> BuildAssetChains(AnalysisContext context)
    {
        return context.UnityAssetReferences
            .Where(reference => !string.IsNullOrWhiteSpace(reference.ResolvedPath))
            .GroupBy(reference => new
            {
                Source = reference.AssetPath,
                Target = reference.ResolvedPath!,
                reference.FieldName,
                reference.OwnerType
            })
            .OrderBy(group => ShortPath(group.Key.Source))
            .Select(group => new
            {
                source = ShortPath(group.Key.Source),
                ownerType = group.Key.OwnerType,
                field = group.Key.FieldName,
                target = ShortPath(group.Key.Target),
                count = group.Count()
            });
    }

    private static object BuildProjectModel(AnalysisContext context)
    {
        return new
        {
            solutions = context.ProjectModel.Solutions.Select(solution => new
            {
                path = ShortPath(solution.Path),
                projectCount = solution.ProjectPaths.Count
            }),
            asmdefs = context.ProjectModel.AssemblyDefinitions.Select(asmdef => new
            {
                asmdef.Name,
                path = ShortPath(asmdef.Path),
                referenceCount = asmdef.References.Count,
                asmdef.IsEditor,
                references = asmdef.References.Take(12)
            }),
            packages = context.ProjectModel.Packages.Take(120).Select(package => new
            {
                package.Name,
                package.VersionOrSource,
                package.Source
            }),
            assemblies = context.Assemblies.Take(160).Select(assembly => new
            {
                name = assembly.AssemblyName,
                assembly.Kind,
                path = ShortPath(assembly.Path),
                typeCount = assembly.TypeNames.Count,
                monoBehaviourCount = assembly.MonoBehaviourTypes.Count,
                scriptableObjectCount = assembly.ScriptableObjectTypes.Count,
                assembly.Status
            })
        };
    }

    private static int RelationScore(AnalysisContext context, string fullName)
    {
        return context.SourceTypeRelations.Count(relation => relation.SourceType == fullName)
            + context.SourceTypeRelations.Count(relation => relation.TargetType == fullName) * 2;
    }

    private static string InferModuleName(SourceTypeInfo type, IReadOnlyList<ModuleInfo> modules)
    {
        var explicitModule = modules
            .Where(module => module.Evidence.Any(evidence => ContainsPath(Path.GetDirectoryName(evidence) ?? evidence, type.SourceFile)))
            .OrderByDescending(module => module.Name.Length)
            .FirstOrDefault();
        if (explicitModule is not null)
        {
            return explicitModule.Name;
        }

        if (!string.IsNullOrWhiteSpace(type.Namespace))
        {
            var parts = type.Namespace.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length > 0)
            {
                return parts.Length == 1 ? parts[0] : string.Join('.', parts.Take(2));
            }
        }

        return InferModuleFromPath(type.SourceFile) ?? type.AssemblyName;
    }

    private static string? InferModuleFromPath(string path)
    {
        var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var assetsIndex = Array.FindIndex(parts, part => part.Equals("Assets", StringComparison.OrdinalIgnoreCase));
        if (assetsIndex >= 0 && assetsIndex + 1 < parts.Length)
        {
            return parts[assetsIndex + 1];
        }

        var packagesIndex = Array.FindIndex(parts, part => part.Equals("Packages", StringComparison.OrdinalIgnoreCase));
        if (packagesIndex >= 0 && packagesIndex + 1 < parts.Length)
        {
            return parts[packagesIndex + 1];
        }

        return null;
    }

    private static string? GuessRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        var parts = directory.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var assetsIndex = Array.FindIndex(parts, part => part.Equals("Assets", StringComparison.OrdinalIgnoreCase));
        if (assetsIndex >= 0)
        {
            return string.Join('/', parts.Take(assetsIndex + 1));
        }

        var packagesIndex = Array.FindIndex(parts, part => part.Equals("Packages", StringComparison.OrdinalIgnoreCase));
        if (packagesIndex >= 0 && packagesIndex + 1 < parts.Length)
        {
            return string.Join('/', parts.Take(packagesIndex + 2));
        }

        return string.Join('/', parts.Take(Math.Min(parts.Length, 4)));
    }

    private static string TranslateRelationKind(string kind)
    {
        return kind switch
        {
            "inherits" => "继承",
            "serialized-field" => "序列化字段",
            "field" => "字段引用",
            "property" => "属性引用",
            "returns" => "返回值",
            "parameter" => "参数",
            "creates" => "创建",
            "calls" => "疑似调用",
            _ => kind
        };
    }

    private static bool ContainsPath(string root, string path)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(path))
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

    private static string ShortName(string value)
    {
        var index = value.LastIndexOf('.');
        return index >= 0 ? value[(index + 1)..] : value;
    }

    private static string ShortPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join('/', parts.TakeLast(Math.Min(5, parts.Length)));
    }

    private static void WriteJson(AnalysisContext context, string relativePath, object value)
    {
        context.FileSystem.WriteAllTextToOutput(relativePath, JsonSerializer.Serialize(value, JsonOptions));
    }

    private static string BuildHtml(object reportData)
    {
        var reportJson = JsonSerializer.Serialize(reportData, JsonOptions);
        return $$"""
<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Unity 项目理解报告</title>
  <link rel="stylesheet" href="assets/style.css">
</head>
<body>
  <header class="app-header">
    <div>
      <p class="eyebrow">OfflineUnityAnalyzer</p>
      <h1>Unity 项目理解报告</h1>
      <p class="subtitle">默认从结构、模块、类型关系、Unity 绑定、源码/DLL 对应和诊断风险阅读项目，而不是只把文件清单堆出来。</p>
    </div>
    <div class="header-actions">
      <button data-scroll="overview">总览</button>
      <button data-scroll="modules">模块</button>
      <button data-scroll="relations">关系</button>
      <button data-scroll="compiled">源码/DLL</button>
      <button data-scroll="unity">Unity</button>
      <button data-scroll="diagnostics">诊断</button>
    </div>
  </header>

  <main>
    <section id="overview" class="section">
      <div class="section-title">
        <h2>总览</h2>
        <p>先看项目形态、扫描覆盖和需要注意的风险，再往下阅读关系。完整原始数据仍保存在 <code>../data/</code>。</p>
      </div>
      <div id="metrics" class="metrics"></div>
      <div class="split overview-split">
        <article class="panel">
          <h3>项目形态判断</h3>
          <div id="project-brief"></div>
        </article>
        <article class="panel">
          <h3>热更新与资源系统</h3>
          <div id="hotupdate"></div>
        </article>
      </div>
      <article class="panel detail-panel">
        <h3>扫描范围分布</h3>
        <div id="shape"></div>
      </article>
    </section>

    <section id="modules" class="section">
      <div class="section-title">
        <h2>模块理解</h2>
        <p>模块按 asmdef、命名空间、包和 Unity 资源路径自动推断。先看模块职责，再看模块之间的依赖方向。</p>
      </div>
      <div class="split wide-left">
        <article class="panel">
          <h3>模块关系概览</h3>
          <div id="module-relations"></div>
        </article>
        <article class="panel">
          <h3>模块职责卡片</h3>
          <div id="module-grid" class="module-grid"></div>
        </article>
      </div>
    </section>

    <section id="relations" class="section">
      <div class="section-title">
        <h2>代码关系</h2>
        <p>默认展示重点类型的上下游关系，按继承、序列化字段、字段、属性、创建和疑似调用分组，避免大图一团线。</p>
      </div>
      <div class="split wide-left">
        <article class="panel">
          <h3>重点类型关系</h3>
          <div class="toolbar"><input id="relation-filter" type="search" placeholder="筛选类型、模块、程序集、文件"></div>
          <div id="type-relation-cards" class="type-card-grid"></div>
        </article>
        <article class="panel">
          <h3>重点类型索引</h3>
          <div class="toolbar"><input id="type-filter" type="search" placeholder="筛选类型、程序集、命名空间"></div>
          <div id="top-types"></div>
        </article>
      </div>
    </section>

    <section id="compiled" class="section">
      <div class="section-title">
        <h2>源码/DLL 对应</h2>
        <p>用于你这种“外部 C# 源码 + Unity 项目内编译后 DLL”的结构，展示源码类型如何对应到 Unity 项目里的程序集。</p>
      </div>
      <div class="split">
        <article class="panel"><h3>源码到 DLL 的桥接</h3><div id="code-assembly-bridges"></div></article>
        <article class="panel"><h3>DLL 类型索引</h3><div id="dll-types"></div></article>
      </div>
    </section>

    <section id="unity" class="section">
      <div class="section-title">
        <h2>Unity 绑定路径</h2>
        <p>把场景、Prefab、GameObject、组件和脚本关联起来，帮助你从 Unity 资源入口追到代码。</p>
      </div>
      <div class="split">
        <article class="panel">
          <h3>GameObject 到脚本</h3>
          <div id="unity-bindings"></div>
        </article>
        <article class="panel">
          <h3>资源引用链</h3>
          <div id="asset-chains"></div>
        </article>
      </div>
    </section>

    <section id="project-model" class="section">
      <div class="section-title">
        <h2>项目模型明细</h2>
        <p>这里保留 asmdef、包、文件类别等明细，作为深入核对时使用的索引。</p>
      </div>
      <div class="split">
        <article class="panel"><h3>Asmdef</h3><div id="asmdefs"></div></article>
        <article class="panel"><h3>包与文件类别</h3><div id="packages"></div></article>
      </div>
    </section>

    <section id="diagnostics" class="section">
      <div class="section-title">
        <h2>风险与诊断</h2>
        <p>这里解释缺失关系、低置信度匹配、解析失败和重复类型等问题。报告看起来不完整时，先看这一块。</p>
      </div>
      <div class="split">
        <article class="panel"><h3>诊断信息</h3><div id="diagnostics-table"></div></article>
        <article class="panel"><h3>原始数据</h3><div id="data-files"></div></article>
      </div>
    </section>
  </main>

  <script>
    window.__OUA_REPORT__ = {{reportJson}};
  </script>
  <script src="assets/app.js"></script>
</body>
</html>
""";
    }

    private static string BuildCss()
    {
        return """
:root {
  color-scheme: light;
  --bg: #eef3f8;
  --surface: #ffffff;
  --surface-2: #f7fafc;
  --surface-3: #edf7f5;
  --ink: #182231;
  --muted: #667589;
  --line: #d6dee9;
  --line-strong: #aebdd0;
  --blue: #1f5fbf;
  --teal: #087b73;
  --green: #177245;
  --amber: #9a5a08;
  --red: #b3261e;
  --violet: #7047b8;
  font-family: "Segoe UI", "Microsoft YaHei UI", Arial, sans-serif;
}

* {
  box-sizing: border-box;
}

body {
  margin: 0;
  color: var(--ink);
  background: var(--bg);
}

.app-header {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  gap: 24px;
  align-items: end;
  padding: 28px 36px 24px;
  color: #ffffff;
  background: #142033;
  border-bottom: 4px solid #23b7a8;
}

.eyebrow {
  margin: 0 0 8px;
  color: #9ee8dc;
  font-size: 12px;
  font-weight: 700;
  letter-spacing: 0;
  text-transform: uppercase;
}

h1,
h2,
h3,
p {
  margin-top: 0;
}

h1 {
  margin-bottom: 8px;
  font-size: 34px;
  line-height: 1.08;
}

h2 {
  margin-bottom: 6px;
  font-size: 22px;
}

h3 {
  margin-bottom: 12px;
  font-size: 15px;
}

.subtitle {
  max-width: 860px;
  margin-bottom: 0;
  color: #dbeafe;
  line-height: 1.5;
}

.header-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  justify-content: flex-end;
}

button {
  border: 1px solid rgba(255, 255, 255, 0.24);
  border-radius: 6px;
  padding: 8px 11px;
  color: #ffffff;
  background: rgba(255, 255, 255, 0.08);
  font: inherit;
  cursor: pointer;
}

button:hover {
  background: rgba(255, 255, 255, 0.16);
}

main {
  max-width: 1500px;
  margin: 0 auto;
  padding: 26px;
}

.section {
  margin-bottom: 26px;
}

.section-title {
  margin-bottom: 14px;
}

.section-title p {
  max-width: 980px;
  margin-bottom: 0;
  color: var(--muted);
  line-height: 1.5;
}

.metrics {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(170px, 1fr));
  gap: 12px;
}

.metric,
.panel,
.module-card,
.relation-card,
.type-card {
  border: 1px solid var(--line);
  border-radius: 8px;
  background: var(--surface);
  box-shadow: 0 1px 2px rgba(17, 24, 39, 0.05);
}

.metric {
  min-height: 92px;
  padding: 15px;
}

.metric strong {
  display: block;
  margin-bottom: 4px;
  color: var(--ink);
  font-size: 28px;
  line-height: 1.05;
}

.metric span,
.muted {
  color: var(--muted);
}

.metric small {
  display: block;
  margin-top: 8px;
  color: var(--muted);
  line-height: 1.35;
}

.split {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 14px;
  margin-top: 14px;
}

.wide-left {
  grid-template-columns: minmax(520px, 1.08fr) minmax(430px, 0.92fr);
}

.overview-split {
  grid-template-columns: minmax(520px, 1.15fr) minmax(360px, 0.85fr);
}

.panel,
.module-card,
.relation-card,
.type-card {
  padding: 16px;
  overflow: hidden;
}

.detail-panel {
  margin-top: 14px;
}

.module-grid,
.type-card-grid,
.relation-list,
.brief-grid {
  display: grid;
  gap: 12px;
}

.module-grid {
  grid-template-columns: repeat(auto-fit, minmax(260px, 1fr));
}

.type-card-grid {
  grid-template-columns: repeat(auto-fit, minmax(320px, 1fr));
}

.brief-grid {
  grid-template-columns: repeat(auto-fit, minmax(230px, 1fr));
}

.brief-item {
  border: 1px solid #e2e8f0;
  border-radius: 8px;
  padding: 12px;
  background: var(--surface-2);
}

.brief-item span {
  display: block;
  margin-bottom: 6px;
  color: var(--muted);
  font-size: 12px;
}

.brief-item strong {
  overflow-wrap: anywhere;
}

.module-card h3,
.relation-card h3,
.type-card h3 {
  display: flex;
  justify-content: space-between;
  gap: 12px;
  align-items: flex-start;
  margin-bottom: 10px;
}

.type-name,
.module-name {
  min-width: 0;
  overflow-wrap: anywhere;
}

.type-card header {
  display: grid;
  gap: 8px;
  margin-bottom: 12px;
}

.stat-row,
.chips {
  display: flex;
  flex-wrap: wrap;
  gap: 7px;
  align-items: center;
}

.stat-row {
  margin-bottom: 12px;
}

.chip,
.pill {
  display: inline-flex;
  align-items: center;
  border-radius: 999px;
  padding: 3px 8px;
  color: #17415c;
  background: #e0f2fe;
  font-size: 12px;
  white-space: nowrap;
}

.chip.green {
  color: #14532d;
  background: #dcfce7;
}

.chip.amber {
  color: #78350f;
  background: #fef3c7;
}

.chip.violet {
  color: #4c1d95;
  background: #ede9fe;
}

.chip.gray {
  color: #475569;
  background: #e2e8f0;
}

.chip.red {
  color: #7f1d1d;
  background: #fee2e2;
}

.flow {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto minmax(0, 1fr);
  gap: 8px;
  align-items: center;
  border: 1px solid #e5edf6;
  border-radius: 8px;
  padding: 9px;
  background: var(--surface-2);
}

.flow strong,
.flow span {
  min-width: 0;
  overflow-wrap: anywhere;
}

.arrow {
  color: var(--teal);
  font-weight: 800;
}

.relation-groups {
  display: grid;
  gap: 9px;
  margin-top: 10px;
}

.relation-group {
  border-left: 3px solid var(--teal);
  padding: 8px 0 8px 10px;
  background: linear-gradient(90deg, rgba(8, 123, 115, 0.07), transparent);
}

.relation-group-title {
  display: flex;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 6px;
  color: var(--ink);
  font-weight: 700;
}

table {
  width: 100%;
  border-collapse: collapse;
  font-size: 13px;
}

th,
td {
  border-bottom: 1px solid #e8eef7;
  padding: 8px 7px;
  text-align: left;
  vertical-align: top;
}

th {
  color: #3d4d63;
  background: #f8fafc;
  font-weight: 700;
}

td {
  color: var(--ink);
  overflow-wrap: anywhere;
}

.toolbar {
  margin-bottom: 10px;
}

input {
  width: 100%;
  min-height: 38px;
  border: 1px solid var(--line-strong);
  border-radius: 6px;
  padding: 8px 10px;
  color: var(--ink);
  background: #ffffff;
  font: inherit;
}

input::placeholder {
  color: #6b778a;
}

.mini-list {
  display: grid;
  gap: 6px;
  margin: 10px 0 0;
  padding: 0;
  list-style: none;
}

.mini-list li {
  border: 1px solid #edf2f7;
  border-radius: 6px;
  padding: 8px;
  background: var(--surface-2);
  overflow-wrap: anywhere;
}

a {
  color: var(--blue);
}

code {
  font-family: Consolas, "Cascadia Mono", "Liberation Mono", monospace;
  font-size: 0.92em;
}

.empty {
  margin: 0;
  color: var(--muted);
}

@media (max-width: 980px) {
  .app-header,
  .split,
  .wide-left,
  .overview-split {
    grid-template-columns: 1fr;
  }

  .header-actions {
    justify-content: flex-start;
  }

  main {
    padding: 18px;
  }

  .flow {
    grid-template-columns: 1fr;
  }

  .arrow {
    display: none;
  }
}
""";
    }

    private static string BuildAppJs()
    {
        return """
(function () {
  const data = window.__OUA_REPORT__ || {};
  const summary = data.summary || {};

  renderMetrics();
  renderProjectBrief();
  renderShape();
  renderHotUpdate();
  renderModules();
  renderModuleRelations();
  renderTypeRelationCards();
  renderTopTypes();
  renderCompiled();
  renderUnity();
  renderProjectModel();
  renderDiagnostics();
  bindNavigation();

  function renderMetrics() {
    const metrics = [
      ["文件", summary.fileCount, "排除规则之后纳入索引的文件"],
      ["C# 类型", summary.sourceTypeCount, `${summary.monoBehaviourCount || 0} 个 MonoBehaviour，${summary.scriptableObjectCount || 0} 个 ScriptableObject`],
      ["代码关系", summary.sourceRelationCount, `${summary.resolvedSourceRelationCount || 0} 条已解析到项目内类型`],
      ["源码/DLL 桥接", summary.codeAssemblyBridgeCount, `${summary.hotUpdateBridgeCount || 0} 条热更程序集匹配`],
      ["序列化字段", summary.serializedFieldCount, "public 字段与 [SerializeField] 字段"],
      ["Unity 绑定", summary.unityComponentCount, `${summary.unityGameObjectCount || 0} 个 GameObject 已索引`],
      ["资源引用", summary.unityAssetReferenceCount, "场景、Prefab、材质、配置中的资源引用"],
      ["模块", summary.moduleCount, "自动推断的职责分组"],
      ["诊断", summary.diagnosticCount, `${summary.warningDiagnosticCount || 0} 个警告，${summary.errorDiagnosticCount || 0} 个错误`]
    ];
    document.getElementById("metrics").innerHTML = metrics.map(([label, value, note]) => `
      <article class="metric"><strong>${escapeHtml(value ?? 0)}</strong><span>${escapeHtml(label)}</span><small>${escapeHtml(note)}</small></article>
    `).join("");
  }

  function renderProjectBrief() {
    const brief = data.projectBrief || {};
    const rows = [
      ["项目结构", brief.sourceUnityMode],
      ["Unity 项目", brief.unityProject],
      ["源码目录", brief.codeRoots],
      ["DLL 来源", brief.dllRoots],
      ["代码关系覆盖", brief.relationCoverage],
      ["Unity 引用覆盖", brief.unityCoverage]
    ];
    const suggestions = brief.suggestions || [];
    document.getElementById("project-brief").innerHTML = `
      <div class="brief-grid">
        ${rows.map(([label, value]) => `<div class="brief-item"><span>${escapeHtml(label)}</span><strong>${escapeHtml(value || "无")}</strong></div>`).join("")}
      </div>
      <ul class="mini-list">
        ${suggestions.map(item => `<li>${escapeHtml(item)}</li>`).join("")}
      </ul>`;
  }

  function renderShape() {
    const rows = Object.entries(summary.byFileKind || {}).sort((a, b) => b[1] - a[1]).slice(0, 16)
      .map(([kind, count]) => ({ kind: translateFileKind(kind), count }));
    renderTable("shape", rows, [
      ["文件类别", "kind"],
      ["数量", "count"]
    ]);
  }

  function renderHotUpdate() {
    const hybrid = data.hybridClr || {};
    const yoo = data.yooAsset || {};
    const lines = [
      `<p><span class="chip ${hybrid.detected ? "green" : "gray"}">HybridCLR ${hybrid.detected ? "已检测到" : "未发现"}</span></p>`,
      `<p><span class="chip ${yoo.detected ? "green" : "gray"}">YooAsset ${yoo.detected ? "已检测到" : "未发现"}</span></p>`,
      `<div class="stat-row"><span class="chip">热更 DLL ${(hybrid.hotUpdateAssemblies || []).length}</span><span class="chip">AOT ${(hybrid.aotMetadataAssemblies || []).length}</span><span class="chip">Yoo 清单 ${(yoo.manifestFiles || []).length}</span><span class="chip">Yoo 代码引用 ${summary.yooAssetCodeReferenceCount || 0}</span></div>`
    ];
    if ((hybrid.evidence || []).length) {
      lines.push(`<ul class="mini-list">${hybrid.evidence.slice(0, 5).map(x => `<li>${escapeHtml(shortPath(x))}</li>`).join("")}</ul>`);
    }
    document.getElementById("hotupdate").innerHTML = lines.join("");
  }

  function renderModules() {
    const modules = data.modules || [];
    const root = document.getElementById("module-grid");
    if (!modules.length) {
      root.innerHTML = `<p class="empty">没有推断出模块。可以查看项目模型和诊断信息确认源码是否被扫描。</p>`;
      return;
    }
    root.innerHTML = modules.map(module => `
      <article class="module-card">
        <h3><span class="module-name">${escapeHtml(module.name)}</span><span class="chip gray">${module.typeCount || 0} 类型</span></h3>
        <div class="stat-row">
          <span class="chip green">${module.monoBehaviourCount || 0} MonoBehaviour</span>
          <span class="chip violet">${module.scriptableObjectCount || 0} ScriptableObject</span>
          <span class="chip">${module.assetCount || 0} 资源</span>
        </div>
        <strong class="muted">代表类型</strong>
        <ul class="mini-list">
          ${(module.topTypes || []).slice(0, 6).map(type => `<li>${escapeHtml(type.name)} <span class="muted">${escapeHtml(type.kind)} · ${type.relationCount || 0} 条关系</span></li>`).join("") || `<li class="muted">没有索引到类型。</li>`}
        </ul>
        ${(module.outbound || []).length ? `<strong class="muted">主要依赖</strong><ul class="mini-list">${module.outbound.slice(0, 5).map(edge => `<li>${escapeHtml(edge.target)} <span class="muted">${edge.count} 条 · ${escapeHtml((edge.kinds || []).map(translateRelationKind).join("、"))}</span></li>`).join("")}</ul>` : ""}
      </article>
    `).join("");
  }

  function renderModuleRelations() {
    const relations = data.moduleRelations || [];
    const root = document.getElementById("module-relations");
    if (!relations.length) {
      root.innerHTML = `<p class="empty">没有发现跨模块关系。可能项目本身模块较集中，或当前只扫描到部分源码。</p>`;
      return;
    }
    root.innerHTML = `<div class="relation-list">${relations.slice(0, 40).map(edge => `
      <article class="relation-card">
        <h3><span class="module-name">${escapeHtml(edge.sourceModule)}</span><span class="chip">${edge.relationCount || 0} 条</span></h3>
        <div class="flow"><strong>${escapeHtml(edge.sourceModule)}</strong><span class="arrow">-></span><strong>${escapeHtml(edge.targetModule)}</strong></div>
        <div class="chips" style="margin-top:10px">${(edge.relationKinds || []).slice(0, 5).map(kind => `<span class="chip ${relationTone(kind.kind)}">${escapeHtml(kind.label || translateRelationKind(kind.kind))} ${kind.count}</span>`).join("")}</div>
        <ul class="mini-list">
          ${(edge.sampleFlows || []).slice(0, 5).map(flow => `<li><strong>${escapeHtml(flow.source)}</strong> -> <strong>${escapeHtml(flow.target)}</strong> <span class="muted">${flow.count} 条 · ${escapeHtml((flow.relationKinds || []).join("、"))}</span></li>`).join("")}
        </ul>
      </article>
    `).join("")}</div>`;
  }

  function renderTypeRelationCards() {
    const cards = data.typeRelationCards || [];
    const root = document.getElementById("type-relation-cards");
    const render = items => {
      if (!items.length) {
        root.innerHTML = `<p class="empty">没有可展示的重点类型关系。</p>`;
        return;
      }
      root.innerHTML = items.slice(0, 60).map(type => `
        <article class="type-card">
          <header>
            <h3><span class="type-name">${escapeHtml(type.name)}</span><span class="chip gray">${escapeHtml(type.module || "")}</span></h3>
            <div class="chips">${(type.tags || []).map(tag => `<span class="chip ${tagTone(tag)}">${escapeHtml(tag)}</span>`).join("")}</div>
            <div class="stat-row">
              <span class="chip">入 ${type.inboundCount || 0}</span>
              <span class="chip">出 ${type.outboundCount || 0}</span>
              <span class="chip">Unity ${type.unityBindingCount || 0}</span>
              ${(type.baseTypes || []).length ? `<span class="chip violet">继承 ${escapeHtml(type.baseTypes.join("、"))}</span>` : ""}
            </div>
            <p class="muted">${escapeHtml(type.fullName)} · ${escapeHtml(type.sourceFile || "")}</p>
          </header>
          ${renderRelationGroups("主要使用了谁", type.outgoingGroups || [])}
          ${renderRelationGroups("谁在使用它", type.incomingGroups || [])}
        </article>
      `).join("");
    };
    render(cards);
    const filter = document.getElementById("relation-filter");
    filter.addEventListener("input", () => {
      const q = filter.value.trim().toLowerCase();
      if (!q) {
        render(cards);
        return;
      }
      render(cards.filter(card => JSON.stringify(card).toLowerCase().includes(q)));
    });
  }

  function renderTopTypes() {
    const rows = data.topTypes || [];
    const columns = [
      ["类型", row => `${row.name}${row.isMonoBehaviour ? " · MonoBehaviour" : ""}${row.isScriptableObject ? " · ScriptableObject" : ""}`],
      ["程序集", "assemblyName"],
      ["关系", row => `入 ${row.inboundCount || 0} / 出 ${row.outboundCount || 0}`],
      ["Unity", row => row.unityBindingCount || 0],
      ["文件", "sourceFile"]
    ];
    renderTable("top-types", rows, columns);
    const filter = document.getElementById("type-filter");
    filter.addEventListener("input", () => {
      const q = filter.value.toLowerCase();
      document.querySelectorAll("#top-types tbody tr").forEach(row => {
        row.hidden = q && !row.textContent.toLowerCase().includes(q);
      });
    });
  }

  function renderCompiled() {
    renderTable("code-assembly-bridges", data.codeAssemblyBridges || [], [
      ["源码类型", "sourceType"],
      ["源码文件", "source"],
      ["DLL", row => `${row.assembly} · ${row.assemblyKind}`],
      ["匹配方式", "matchKind"],
      ["置信度", "confidence"]
    ]);
    const assemblies = (data.projectModel && data.projectModel.assemblies) || [];
    renderTable("dll-types", assemblies, [
      ["程序集", "name"],
      ["类别", "kind"],
      ["类型数", "typeCount"],
      ["MonoBehaviour", "monoBehaviourCount"],
      ["路径", "path"]
    ]);
  }

  function renderUnity() {
    renderTable("unity-bindings", data.unityBindings || [], [
      ["资源", "asset"],
      ["GameObject", "gameObject"],
      ["脚本", "script"],
      ["组件", "componentType"],
      ["数量", "count"]
    ]);
    renderTable("asset-chains", data.assetChains || [], [
      ["来源资源", "source"],
      ["拥有者", "ownerType"],
      ["字段", "field"],
      ["目标资源", "target"],
      ["数量", "count"]
    ]);
  }

  function renderProjectModel() {
    const model = data.projectModel || {};
    renderTable("asmdefs", model.asmdefs || [], [
      ["名称", "name"],
      ["引用数", "referenceCount"],
      ["Editor", row => row.isEditor ? "是" : ""],
      ["路径", "path"]
    ]);
    const packageRows = [
      ...((model.packages || []).map(x => ({ kind: "包", name: x.name, detail: x.versionOrSource, source: x.source }))),
      ...((data.filesByKind || []).map(x => ({ kind: "文件类别", name: translateFileKind(x.kind), detail: x.count, source: "" })))
    ];
    renderTable("packages", packageRows, [
      ["类别", "kind"],
      ["名称", "name"],
      ["详情", "detail"],
      ["来源", "source"]
    ]);
  }

  function renderDiagnostics() {
    renderTable("diagnostics-table", data.diagnostics || [], [
      ["级别", "severity"],
      ["类别", "category"],
      ["信息", "message"],
      ["路径", row => shortPath(row.path)]
    ]);
    const files = data.fullDataFiles || [];
    document.getElementById("data-files").innerHTML = `<ul class="mini-list">${files.map(file => `<li><code>${escapeHtml(file)}</code></li>`).join("")}</ul>`;
  }

  function renderRelationGroups(title, groups) {
    if (!groups.length) return "";
    return `
      <div class="relation-groups">
        <strong class="muted">${escapeHtml(title)}</strong>
        ${groups.slice(0, 4).map(group => `
          <div class="relation-group">
            <div class="relation-group-title"><span>${escapeHtml(group.label || translateRelationKind(group.kind))}</span><span>${group.count || 0} 条</span></div>
            <div class="chips">${(group.samples || []).slice(0, 6).map(sample => `<span class="chip ${relationTone(group.kind)}">${escapeHtml(sample.type)}${sample.count > 1 ? ` ${sample.count}` : ""}</span>`).join("")}</div>
          </div>
        `).join("")}
      </div>`;
  }

  function renderTable(id, rows, columns) {
    const root = document.getElementById(id);
    if (!rows.length) {
      root.innerHTML = `<p class="empty">没有数据。</p>`;
      return;
    }
    const header = columns.map(([label]) => `<th>${escapeHtml(label)}</th>`).join("");
    const body = rows.map(row => `<tr>${columns.map(([, accessor]) => `<td>${escapeHtml(read(row, accessor))}</td>`).join("")}</tr>`).join("");
    root.innerHTML = `<table><thead><tr>${header}</tr></thead><tbody>${body}</tbody></table>`;
  }

  function bindNavigation() {
    document.querySelectorAll("[data-scroll]").forEach(button => {
      button.addEventListener("click", () => document.getElementById(button.dataset.scroll).scrollIntoView({ behavior: "smooth", block: "start" }));
    });
  }

  function read(row, accessor) {
    if (typeof accessor === "function") return accessor(row) ?? "";
    return row[accessor] ?? "";
  }

  function shortPath(path) {
    if (!path) return "";
    return String(path).replace(/\\/g, "/").split("/").slice(-5).join("/");
  }

  function translateRelationKind(kind) {
    const map = {
      "inherits": "继承",
      "serialized-field": "序列化字段",
      "field": "字段引用",
      "property": "属性引用",
      "returns": "返回值",
      "parameter": "参数",
      "creates": "创建",
      "calls": "疑似调用"
    };
    return map[kind] || kind || "";
  }

  function translateFileKind(kind) {
    const map = {
      "CSharp": "C# 源码",
      "AssemblyDefinition": "Asmdef",
      "CSharpProject": "C# 项目",
      "Solution": "解决方案",
      "UnityScene": "Unity 场景",
      "UnityPrefab": "Unity Prefab",
      "UnityAsset": "Unity 资源",
      "Dll": "DLL",
      "PackageManifest": "包清单",
      "ProjectSettings": "项目设置",
      "Json": "JSON",
      "Yaml": "YAML",
      "Other": "其他"
    };
    return map[kind] || kind || "";
  }

  function relationTone(kind) {
    if (kind === "inherits") return "violet";
    if (kind === "serialized-field") return "green";
    if (kind === "calls" || kind === "creates") return "amber";
    return "";
  }

  function tagTone(tag) {
    if (tag === "MonoBehaviour" || tag === "Unity 绑定") return "green";
    if (tag === "ScriptableObject") return "violet";
    if (tag === "Editor") return "amber";
    return "gray";
  }

  function escapeHtml(value) {
    return String(value ?? "")
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }
})();
""";
    }

    private sealed record ReportSummary
    {
        public DateTime GeneratedAtUtc { get; init; }
        public int FileCount { get; init; }
        public int SourceTypeCount { get; init; }
        public int SourceRelationCount { get; init; }
        public int ResolvedSourceRelationCount { get; init; }
        public int CodeAssemblyBridgeCount { get; init; }
        public int HotUpdateBridgeCount { get; init; }
        public int MonoBehaviourCount { get; init; }
        public int ScriptableObjectCount { get; init; }
        public int SerializedFieldCount { get; init; }
        public int AssemblyCount { get; init; }
        public int PackageCount { get; init; }
        public int AsmdefCount { get; init; }
        public int CsprojCount { get; init; }
        public int ModuleCount { get; init; }
        public int UnityAssetCount { get; init; }
        public int UnityObjectCount { get; init; }
        public int UnityGameObjectCount { get; init; }
        public int UnityComponentCount { get; init; }
        public int UnityAssetReferenceCount { get; init; }
        public int UnityScriptReferenceCount { get; init; }
        public int UnresolvedUnityScriptReferenceCount { get; init; }
        public int DiagnosticCount { get; init; }
        public int WarningDiagnosticCount { get; init; }
        public int ErrorDiagnosticCount { get; init; }
        public int ConfigReferenceCount { get; init; }
        public int YooAssetManifestAssetCount { get; init; }
        public int YooAssetCodeReferenceCount { get; init; }
        public bool HybridClrDetected { get; init; }
        public bool YooAssetDetected { get; init; }
        public IReadOnlyDictionary<string, int> ByFileKind { get; init; } = new Dictionary<string, int>();
        public IReadOnlyDictionary<string, int> ByRelationKind { get; init; } = new Dictionary<string, int>();
        public IReadOnlyDictionary<string, int> ByAssemblyKind { get; init; } = new Dictionary<string, int>();
    }
}
