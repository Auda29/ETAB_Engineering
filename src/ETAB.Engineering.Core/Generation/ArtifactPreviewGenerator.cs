using System.Security.Cryptography;
using System.Text;
using ETAB.Engineering.Core.Model;

namespace ETAB.Engineering.Core.Generation;

public sealed class ArtifactPreviewGenerator
{
    public static readonly Guid GeneratorNamespace =
        Guid.Parse("8d487292-cc21-4f2e-8c6e-3c4742e1d8a1");

    public GenerationPreview Generate(EtabProjectDocument project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var artifacts = new List<GeneratedArtifact>();
        var generatedRoot = NormalizeRoot(project.Project.Generation.GeneratedRoot);
        var orderedNodes = project.Nodes
            .OrderBy(node => node.Name, StringComparer.Ordinal)
            .ThenBy(node => node.Id, StringComparer.Ordinal)
            .ToArray();
        var relationWiringEnabled =
            project.Project.Generation.RelationWiring && project.Relations.Count > 0;

        foreach (var node in orderedNodes)
        {
            if (node.Generate.CommandEnum)
            {
                artifacts.Add(CreateCommandEnum(project, node, generatedRoot));
            }

            if (node.Generate.RequestType)
            {
                artifacts.Add(CreateRequestDut(project, node, generatedRoot));
            }

            if (node.Generate.StatusType)
            {
                artifacts.Add(CreateStatusDut(project, node, generatedRoot));
            }

            if (node.Generate.BaseFunctionBlock)
            {
                artifacts.Add(CreateBaseFunctionBlock(project, node, generatedRoot));
            }
        }

        var instances = orderedNodes
            .Where(node => node.Generate.Instance)
            .Select(node => new GeneratedInstance(
                $"fb{node.Name}",
                ResolveInstanceType(project, node)))
            .ToList();
        if (relationWiringEnabled)
        {
            artifacts.Add(CreateRelationWiring(project, generatedRoot));
            instances.Add(new GeneratedInstance(
                "fbEtabRelationWiring",
                $"FB_{project.Project.Prefix}_Relations"));
        }
        if (instances.Count > 0)
        {
            artifacts.Add(CreateInstanceGlobalVariableList(
                project,
                generatedRoot,
                instances));
        }
        if (instances.Count > 0 && project.Project.Generation.ProgramCallStructure)
        {
            var calledInstances = orderedNodes
                .Where(node => node.Generate.Instance && node.Generate.CallInProgram)
                .Select(node => new GeneratedInstance(
                    $"fb{node.Name}",
                    ResolveInstanceType(project, node)))
                .ToList();
            if (relationWiringEnabled)
            {
                calledInstances.Insert(0, new GeneratedInstance(
                    "fbEtabRelationWiring",
                    $"FB_{project.Project.Prefix}_Relations"));
            }
            artifacts.Add(CreateProgramCallStructure(
                project,
                generatedRoot,
                calledInstances));
        }

        return new GenerationPreview(project.Project.Id, project.Project.Name, artifacts.ToArray());
    }

    private static GeneratedArtifact CreateInstanceGlobalVariableList(
        EtabProjectDocument project,
        string generatedRoot,
        IReadOnlyList<GeneratedInstance> instances)
    {
        const GeneratedArtifactKind kind = GeneratedArtifactKind.InstanceGlobalVariableList;
        var name = $"GVL_{project.Project.Prefix}_Units";
        var guid = CreateTwinCatGuid(project.Project.Id, project.Project.Id, kind);
        var content = TwinCatGvlRenderer.RenderInstances(
            name,
            project.Project.Id,
            guid,
            project.Project.TwinCat.Version,
            instances);

        return CreateArtifact(
            project.Project.Id,
            kind,
            name,
            guid,
            InGeneratedRoot(generatedRoot, $"GVLs/{name}.TcGVL"),
            content);
    }

    private static GeneratedArtifact CreateRelationWiring(
        EtabProjectDocument project,
        string generatedRoot)
    {
        const GeneratedArtifactKind kind = GeneratedArtifactKind.RelationWiring;
        var name = $"FB_{project.Project.Prefix}_Relations";
        var gvlName = $"GVL_{project.Project.Prefix}_Units";
        var guid = CreateTwinCatGuid(project.Project.Id, project.Project.Id, kind);
        var nodesById = project.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var bindings = project.Relations
            .OrderBy(relation => relation.Kind, StringComparer.Ordinal)
            .ThenBy(relation => relation.SourceNodeId, StringComparer.Ordinal)
            .ThenBy(relation => relation.TargetNodeId, StringComparer.Ordinal)
            .ThenBy(relation => relation.Id, StringComparer.Ordinal)
            .Select(relation =>
            {
                var source = nodesById[relation.SourceNodeId];
                var target = nodesById[relation.TargetNodeId];
                var memberName = CreateRelationMemberName(relation, source, target);
                return new GeneratedRelationBinding(
                    relation.Id,
                    relation.Kind,
                    source.Name,
                    target.Name,
                    memberName,
                    $"fb{source.Name}",
                    $"fb{target.Name}",
                    target.Kind,
                    ResolveRelationStatusMember(target),
                    CreateNestedGuid(
                        project.Project.Id,
                        relation.Id,
                        kind,
                        $"member/{memberName}"));
            })
            .ToArray();
        var content = TwinCatRelationRenderer.Render(
            name,
            gvlName,
            project.Project.Id,
            guid,
            project.Project.TwinCat.Version,
            bindings);

        return CreateArtifact(
            project.Project.Id,
            kind,
            name,
            guid,
            InGeneratedRoot(generatedRoot, $"POUs/{name}.TcPOU"),
            content);
    }

    private static GeneratedArtifact CreateProgramCallStructure(
        EtabProjectDocument project,
        string generatedRoot,
        IReadOnlyList<GeneratedInstance> instances)
    {
        const GeneratedArtifactKind kind = GeneratedArtifactKind.ProgramCallStructure;
        var name = $"PRG_{project.Project.Prefix}_Generated";
        var gvlName = $"GVL_{project.Project.Prefix}_Units";
        var guid = CreateTwinCatGuid(project.Project.Id, project.Project.Id, kind);
        var content = TwinCatPouRenderer.RenderProgramCallStructure(
            name,
            gvlName,
            project.Project.Id,
            guid,
            project.Project.TwinCat.Version,
            instances);

        return CreateArtifact(
            project.Project.Id,
            kind,
            name,
            guid,
            InGeneratedRoot(generatedRoot, $"POUs/{name}.TcPOU"),
            content);
    }

    private static GeneratedArtifact CreateCommandEnum(
        EtabProjectDocument project,
        EtabNode node,
        string generatedRoot)
    {
        const GeneratedArtifactKind kind = GeneratedArtifactKind.CommandEnum;
        var name = $"E_{project.Project.Prefix}_{node.SymbolStem}Command";
        var guid = CreateTwinCatGuid(project.Project.Id, node.Id, kind);
        var content = TwinCatDutRenderer.RenderCommandEnum(
            name,
            node.Id,
            guid,
            project.Project.TwinCat.Version,
            node.Commands);

        return CreateArtifact(
            node.Id,
            kind,
            name,
            guid,
            InGeneratedRoot(generatedRoot, $"DUTs/Commands/{name}.TcDUT"),
            content);
    }

    private static GeneratedArtifact CreateRequestDut(
        EtabProjectDocument project,
        EtabNode node,
        string generatedRoot)
    {
        const GeneratedArtifactKind kind = GeneratedArtifactKind.RequestDut;
        var name = $"ST_{project.Project.Prefix}_{node.SymbolStem}Request";
        var commandEnumName = $"E_{project.Project.Prefix}_{node.SymbolStem}Command";
        var guid = CreateTwinCatGuid(project.Project.Id, node.Id, kind);
        var content = TwinCatDutRenderer.RenderRequestDut(
            name,
            commandEnumName,
            node.Id,
            guid,
            project.Project.TwinCat.Version,
            node.RequestPayload);

        return CreateArtifact(
            node.Id,
            kind,
            name,
            guid,
            InGeneratedRoot(generatedRoot, $"DUTs/Requests/{name}.TcDUT"),
            content);
    }

    private static GeneratedArtifact CreateStatusDut(
        EtabProjectDocument project,
        EtabNode node,
        string generatedRoot)
    {
        const GeneratedArtifactKind kind = GeneratedArtifactKind.StatusDut;
        var name = $"ST_{project.Project.Prefix}_{node.SymbolStem}Status";
        var guid = CreateTwinCatGuid(project.Project.Id, node.Id, kind);
        var content = TwinCatDutRenderer.RenderStatusDut(
            name,
            node,
            node.Id,
            guid,
            project.Project.TwinCat.Version);

        return CreateArtifact(
            node.Id,
            kind,
            name,
            guid,
            InGeneratedRoot(generatedRoot, $"DUTs/Status/{name}.TcDUT"),
            content);
    }

    private static GeneratedArtifact CreateBaseFunctionBlock(
        EtabProjectDocument project,
        EtabNode node,
        string generatedRoot)
    {
        if (node.Kind != "applicationUnit")
        {
            throw new InvalidOperationException(
                $"Base function blocks are not supported for node kind '{node.Kind}'.");
        }

        const GeneratedArtifactKind kind = GeneratedArtifactKind.BaseFunctionBlock;
        var name = $"FB_{project.Project.Prefix}_{node.SymbolStem}UnitBase";
        var guid = CreateTwinCatGuid(project.Project.Id, node.Id, kind);
        var hookGuid = CreateNestedGuid(
            project.Project.Id,
            node.Id,
            kind,
            "method/OnExecuteOperation");
        var content = TwinCatPouRenderer.RenderApplicationUnitBase(
            name,
            node.Id,
            guid,
            hookGuid,
            project.Project.TwinCat.Version);

        return CreateArtifact(
            node.Id,
            kind,
            name,
            guid,
            InGeneratedRoot(generatedRoot, $"POUs/{name}.TcPOU"),
            content);
    }

    private static Guid CreateTwinCatGuid(
        string projectId,
        string modelId,
        GeneratedArtifactKind kind)
    {
        var name = $"{projectId}/{modelId}/{kind.ToContractName()}";
        return DeterministicGuid.CreateVersion5(GeneratorNamespace, name);
    }

    private static Guid CreateNestedGuid(
        string projectId,
        string modelId,
        GeneratedArtifactKind kind,
        string nestedObject)
    {
        var name = $"{projectId}/{modelId}/{kind.ToContractName()}/{nestedObject}";
        return DeterministicGuid.CreateVersion5(GeneratorNamespace, name);
    }

    private static string ResolveInstanceType(
        EtabProjectDocument project,
        EtabNode node)
    {
        if (!string.IsNullOrWhiteSpace(node.Generate.InstanceType))
        {
            return node.Generate.InstanceType.Trim();
        }

        return node.Kind switch
        {
            "applicationUnit" when node.Generate.BaseFunctionBlock =>
                $"FB_{project.Project.Prefix}_{node.SymbolStem}UnitBase",
            "applicationUnit" => "ETAB.FB_ETAB_ApplicationUnit",
            "commandUnit" => "ETAB.FB_ETAB_CommandUnit",
            "recipeManager" => "ETAB.FB_ETAB_RecipeManager",
            "machineLink" => "ETAB.FB_ETAB_MachineLink",
            _ => throw new InvalidOperationException(
                $"Instances are not supported for node kind '{node.Kind}'.")
        };
    }

    private static string CreateRelationMemberName(
        EtabRelation relation,
        EtabNode source,
        EtabNode target)
    {
        var prefix = relation.Kind switch
        {
            "contains" => "Contains",
            "commands" => "Command",
            "observes" => "Observe",
            "usesRecipe" => "Recipe",
            "usesLink" => "Link",
            _ => "Relation"
        };
        var candidate = $"{prefix}_{source.Name}_To_{target.Name}";
        const int maximumLength = 80;
        if (candidate.Length <= maximumLength)
        {
            return candidate;
        }

        var suffix = relation.Id.Replace("-", string.Empty, StringComparison.Ordinal)[..8];
        return $"{candidate[..(maximumLength - suffix.Length - 1)]}_{suffix}";
    }

    private static string ResolveRelationStatusMember(EtabNode target)
    {
        if (!string.IsNullOrWhiteSpace(target.Generate.RelationStatusMember))
        {
            return target.Generate.RelationStatusMember.Trim();
        }

        return target.Kind switch
        {
            "applicationUnit" => "refApplicationStatus",
            "commandUnit" => "refStatus",
            "recipeManager" or "machineLink" => "stStatus",
            _ => throw new InvalidOperationException(
                $"Relation status is not supported for node kind '{target.Kind}'.")
        };
    }

    private static GeneratedArtifact CreateArtifact(
        string sourceModelId,
        GeneratedArtifactKind kind,
        string name,
        Guid twinCatGuid,
        string relativePath,
        string content)
    {
        var contentHash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(content)))
            .ToLowerInvariant();

        return new GeneratedArtifact(
            sourceModelId,
            kind,
            name,
            twinCatGuid,
            relativePath,
            content,
            contentHash);
    }

    private static string NormalizeRoot(string generatedRoot)
    {
        var normalized = generatedRoot
            .Replace('\\', '/')
            .Trim('/');

        return normalized == "."
            ? string.Empty
            : string.IsNullOrEmpty(normalized)
                ? "Generated"
                : normalized;
    }

    private static string InGeneratedRoot(string generatedRoot, string relativePath) =>
        string.IsNullOrEmpty(generatedRoot)
            ? relativePath
            : $"{generatedRoot}/{relativePath}";
}
