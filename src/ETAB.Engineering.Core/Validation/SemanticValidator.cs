using System.Text.Json;
using ETAB.Engineering.Core.Model;

namespace ETAB.Engineering.Core.Validation;

internal sealed class SemanticValidator
{
    private static readonly StringComparer IecNameComparer = StringComparer.OrdinalIgnoreCase;

    public IReadOnlyList<ValidationIssue> Validate(
        EtabProjectDocument project,
        JsonElement rawProject)
    {
        var issues = new List<ValidationIssue>();

        ValidateStableIds(rawProject, issues);
        ValidateNodes(project, issues);
        ValidateProgramCallStructure(project, issues);
        ValidateRelations(project, issues);
        ValidateLayout(project, issues);
        ValidateGeneratedArtifactNames(project, issues);

        return issues;
    }

    private static void ValidateStableIds(JsonElement rawProject, ICollection<ValidationIssue> issues)
    {
        var pathsById = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (id, path) in EnumerateStableIds(rawProject, string.Empty))
        {
            if (pathsById.TryGetValue(id, out var firstPath))
            {
                issues.Add(new ValidationIssue(
                    "ID_DUPLICATE",
                    path,
                    $"Stable ID '{id}' is already used at '{firstPath}'."));
            }
            else
            {
                pathsById.Add(id, path);
            }
        }
    }

    private static IEnumerable<(string Id, string Path)> EnumerateStableIds(
        JsonElement element,
        string path)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var propertyPath = $"{path}/{EscapePointerToken(property.Name)}";

                    if (property.NameEquals("id") && property.Value.ValueKind == JsonValueKind.String)
                    {
                        yield return (property.Value.GetString()!, propertyPath);
                    }

                    foreach (var nested in EnumerateStableIds(property.Value, propertyPath))
                    {
                        yield return nested;
                    }
                }

                break;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var nested in EnumerateStableIds(item, $"{path}/{index}"))
                    {
                        yield return nested;
                    }

                    index++;
                }

                break;
        }
    }

    private static void ValidateNodes(EtabProjectDocument project, ICollection<ValidationIssue> issues)
    {
        AddDuplicateIssues(
            project.Nodes,
            node => node.Name,
            node => $"/nodes/{project.Nodes.IndexOf(node)}/name",
            "NODE_NAME_DUPLICATE",
            "Node name",
            issues,
            IecNameComparer);

        for (var nodeIndex = 0; nodeIndex < project.Nodes.Count; nodeIndex++)
        {
            var node = project.Nodes[nodeIndex];
            var nodePath = $"/nodes/{nodeIndex}";

            ValidateCommands(node, nodePath, issues);
            ValidatePayload(node.RequestPayload, $"{nodePath}/requestPayload", issues);
            ValidatePayload(node.StatusPayload, $"{nodePath}/statusPayload", issues);
            ValidateRequestContract(node, nodePath, issues);
            ValidateStatusContract(node, nodePath, issues);
            ValidateBaseFunctionBlockContract(node, nodePath, issues);
            ValidateInstanceContract(node, nodePath, issues);
            ValidateMtp(node, nodePath, issues);
        }
    }

    private static void ValidateCommands(
        EtabNode node,
        string nodePath,
        ICollection<ValidationIssue> issues)
    {
        AddDuplicateIssues(
            node.Commands,
            command => command.Name,
            command => $"{nodePath}/commands/{node.Commands.IndexOf(command)}/name",
            "COMMAND_NAME_DUPLICATE",
            "Command name",
            issues,
            IecNameComparer);

        AddDuplicateIssues(
            node.Commands,
            command => command.EnumValue,
            command => $"{nodePath}/commands/{node.Commands.IndexOf(command)}/enumValue",
            "ENUM_VALUE_DUPLICATE",
            "enumValue",
            issues);

        if (!node.Generate.CommandEnum)
        {
            return;
        }

        var noActions = node.Commands
            .Select((command, index) => (command, index))
            .Where(item => IecNameComparer.Equals(item.command.Name, "NoAction"))
            .ToArray();

        if (noActions.Length != 1)
        {
            issues.Add(new ValidationIssue(
                "NO_ACTION_REQUIRED",
                $"{nodePath}/commands",
                "A generated command enum requires exactly one 'NoAction' command."));
        }
        else if (noActions[0].command.EnumValue != 0)
        {
            issues.Add(new ValidationIssue(
                "NO_ACTION_VALUE",
                $"{nodePath}/commands/{noActions[0].index}/enumValue",
                "The 'NoAction' command must use enumValue 0."));
        }
    }

    private static void ValidatePayload(
        IReadOnlyList<EtabField> fields,
        string payloadPath,
        ICollection<ValidationIssue> issues)
    {
        AddDuplicateIssues(
            fields,
            field => field.Name,
            field => $"{payloadPath}/{fields.IndexOf(field)}/name",
            "FIELD_NAME_DUPLICATE",
            "Payload field name",
            issues,
            IecNameComparer);

        for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
        {
            var dimensions = fields[fieldIndex].ArrayDimensions;
            if (dimensions is null)
            {
                continue;
            }

            for (var dimensionIndex = 0; dimensionIndex < dimensions.Count; dimensionIndex++)
            {
                var dimension = dimensions[dimensionIndex];
                if (dimension.Lower > dimension.Upper)
                {
                    issues.Add(new ValidationIssue(
                        "ARRAY_BOUNDS",
                        $"{payloadPath}/{fieldIndex}/arrayDimensions/{dimensionIndex}",
                        $"Array lower bound {dimension.Lower} exceeds upper bound {dimension.Upper}."));
                }
            }
        }
    }

    private static void ValidateRequestContract(
        EtabNode node,
        string nodePath,
        ICollection<ValidationIssue> issues)
    {
        if (node.Generate.RequestType && !node.Generate.CommandEnum)
        {
            issues.Add(new ValidationIssue(
                "REQUEST_WITHOUT_COMMAND_ENUM",
                $"{nodePath}/generate/requestType",
                "A generated request type requires a generated command enum."));
        }

        var reservedNames = new HashSet<string>(
            ["bExecute", "eCommand", "nCommandID"],
            IecNameComparer);

        for (var fieldIndex = 0; fieldIndex < node.RequestPayload.Count; fieldIndex++)
        {
            var field = node.RequestPayload[fieldIndex];
            if (reservedNames.Contains(field.Name))
            {
                issues.Add(new ValidationIssue(
                    "REQUEST_RESERVED_FIELD",
                    $"{nodePath}/requestPayload/{fieldIndex}/name",
                    $"Request field '{field.Name}' is generated implicitly and cannot be declared as payload."));
            }
        }
    }

    private static void ValidateStatusContract(
        EtabNode node,
        string nodePath,
        ICollection<ValidationIssue> issues)
    {
        if (!node.Generate.StatusType)
        {
            return;
        }

        var reservedNames = node.Kind switch
        {
            "applicationUnit" => node.Generate.CommandEnum && node.Generate.RequestType
                ? new HashSet<string>(["stUnit", "stOperation"], IecNameComparer)
                : new HashSet<string>(["stUnit"], IecNameComparer),
            "commandUnit" => new HashSet<string>(["stCommand"], IecNameComparer),
            "recipeManager" => new HashSet<string>(["stRecipe"], IecNameComparer),
            "machineLink" => new HashSet<string>(["stLink"], IecNameComparer),
            _ => []
        };

        for (var fieldIndex = 0; fieldIndex < node.StatusPayload.Count; fieldIndex++)
        {
            var field = node.StatusPayload[fieldIndex];
            if (reservedNames.Contains(field.Name))
            {
                issues.Add(new ValidationIssue(
                    "STATUS_RESERVED_FIELD",
                    $"{nodePath}/statusPayload/{fieldIndex}/name",
                    $"Status field '{field.Name}' is reserved for the embedded library status."));
            }
        }
    }

    private static void ValidateBaseFunctionBlockContract(
        EtabNode node,
        string nodePath,
        ICollection<ValidationIssue> issues)
    {
        if (node.Generate.BaseFunctionBlock && node.Kind != "applicationUnit")
        {
            issues.Add(new ValidationIssue(
                "BASE_FB_NODE_KIND",
                $"{nodePath}/generate/baseFunctionBlock",
                "Generated base function blocks are supported only for applicationUnit nodes in model v0.1."));
        }
    }

    private static void ValidateInstanceContract(
        EtabNode node,
        string nodePath,
        ICollection<ValidationIssue> issues)
    {
        if (!node.Generate.Instance && !string.IsNullOrWhiteSpace(node.Generate.InstanceType))
        {
            issues.Add(new ValidationIssue(
                "INSTANCE_TYPE_WITHOUT_INSTANCE",
                $"{nodePath}/generate/instanceType",
                "An explicit instanceType is valid only when instance generation is enabled."));
        }
        if (!node.Generate.Instance && !string.IsNullOrWhiteSpace(node.Generate.RelationStatusMember))
        {
            issues.Add(new ValidationIssue(
                "RELATION_STATUS_WITHOUT_INSTANCE",
                $"{nodePath}/generate/relationStatusMember",
                "A relation status output is valid only when instance generation is enabled."));
        }
        if (!string.IsNullOrWhiteSpace(node.Generate.RelationStatusMember) &&
            node.Kind is not ("recipeManager" or "machineLink"))
        {
            issues.Add(new ValidationIssue(
                "RELATION_STATUS_NODE_KIND",
                $"{nodePath}/generate/relationStatusMember",
                "A custom relation status output is supported only for RecipeManager and MachineLink nodes."));
        }
        if (!node.Generate.Instance && node.Generate.CallInProgram)
        {
            issues.Add(new ValidationIssue(
                "PROGRAM_CALL_WITHOUT_INSTANCE",
                $"{nodePath}/generate/callInProgram",
                "A node can be called by the generated PRG only when instance generation is enabled."));
        }
    }

    private static void ValidateProgramCallStructure(
        EtabProjectDocument project,
        ICollection<ValidationIssue> issues)
    {
        if (project.Project.Generation.ProgramCallStructure &&
            !project.Nodes.Any(node => node.Generate.Instance && node.Generate.CallInProgram))
        {
            issues.Add(new ValidationIssue(
                "PROGRAM_WITHOUT_INSTANCES",
                "/project/generation/programCallStructure",
                "The generated PRG call structure requires at least one instance selected with callInProgram."));
        }
    }

    private static void ValidateMtp(
        EtabNode node,
        string nodePath,
        ICollection<ValidationIssue> issues)
    {
        if (node.Mtp is null)
        {
            return;
        }

        var commandIds = node.Commands
            .Select(command => command.Id)
            .ToHashSet(StringComparer.Ordinal);

        AddDuplicateIssues(
            node.Mtp.Procedures,
            procedure => procedure.ProcedureId,
            procedure => $"{nodePath}/mtp/procedures/{node.Mtp.Procedures.IndexOf(procedure)}/procedureId",
            "MTP_PROCEDURE_ID_DUPLICATE",
            "MTP procedureId",
            issues);

        for (var index = 0; index < node.Mtp.Procedures.Count; index++)
        {
            var procedure = node.Mtp.Procedures[index];
            if (!commandIds.Contains(procedure.CommandId))
            {
                issues.Add(new ValidationIssue(
                    "MTP_COMMAND_MISSING",
                    $"{nodePath}/mtp/procedures/{index}/commandId",
                    $"MTP procedure references unknown command ID '{procedure.CommandId}' in this node."));
            }
        }
    }

    private static void ValidateRelations(EtabProjectDocument project, ICollection<ValidationIssue> issues)
    {
        var nodesById = project.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var parentByChild = new Dictionary<string, string>(StringComparer.Ordinal);
        var childrenByParent = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var relationKeys = new HashSet<(string Kind, string SourceId, string TargetId)>();

        if (project.Project.Generation.RelationWiring && project.Relations.Count > 0)
        {
            var reservedInstance = project.Nodes
                .Select((node, index) => (node, index))
                .FirstOrDefault(item =>
                    item.node.Generate.Instance &&
                    IecNameComparer.Equals(item.node.Name, "EtabRelationWiring"));
            if (reservedInstance.node is not null)
            {
                issues.Add(new ValidationIssue(
                    "RELATION_WIRING_INSTANCE_COLLISION",
                    $"/nodes/{reservedInstance.index}/name",
                    "Generated relation wiring reserves the PLC instance name 'fbEtabRelationWiring'."));
            }
        }

        for (var relationIndex = 0; relationIndex < project.Relations.Count; relationIndex++)
        {
            var relation = project.Relations[relationIndex];
            var relationPath = $"/relations/{relationIndex}";
            var sourceExists = nodesById.ContainsKey(relation.SourceNodeId);
            var targetExists = nodesById.ContainsKey(relation.TargetNodeId);

            if (!relationKeys.Add((relation.Kind, relation.SourceNodeId, relation.TargetNodeId)))
            {
                issues.Add(new ValidationIssue(
                    "RELATION_DUPLICATE",
                    relationPath,
                    $"Relation '{relation.Kind}' already exists between these nodes."));
            }

            if (!sourceExists)
            {
                issues.Add(new ValidationIssue(
                    "RELATION_ENDPOINT",
                    $"{relationPath}/sourceNodeId",
                    $"Source node '{relation.SourceNodeId}' does not exist."));
            }

            if (!targetExists)
            {
                issues.Add(new ValidationIssue(
                    "RELATION_ENDPOINT",
                    $"{relationPath}/targetNodeId",
                    $"Target node '{relation.TargetNodeId}' does not exist."));
            }

            if (relation.SourceNodeId == relation.TargetNodeId)
            {
                issues.Add(new ValidationIssue(
                    "RELATION_SELF",
                    relationPath,
                    "A relation cannot reference the same source and target node."));
            }

            if (!sourceExists || !targetExists)
            {
                continue;
            }

            ValidateRelationEndpointKinds(
                relation,
                nodesById[relation.SourceNodeId],
                nodesById[relation.TargetNodeId],
                relationPath,
                issues);

            if (project.Project.Generation.RelationWiring)
            {
                if (!nodesById[relation.SourceNodeId].Generate.Instance)
                {
                    issues.Add(new ValidationIssue(
                        "RELATION_SOURCE_INSTANCE_REQUIRED",
                        $"{relationPath}/sourceNodeId",
                        "Generated relation wiring requires the source node to generate a PLC instance."));
                }

                if (!nodesById[relation.TargetNodeId].Generate.Instance)
                {
                    issues.Add(new ValidationIssue(
                        "RELATION_TARGET_INSTANCE_REQUIRED",
                        $"{relationPath}/targetNodeId",
                        "Generated relation wiring requires the target node to generate a PLC instance."));
                }
            }

            if (relation.Kind != "contains")
            {
                continue;
            }

            if (parentByChild.TryGetValue(relation.TargetNodeId, out var existingParent))
            {
                issues.Add(new ValidationIssue(
                    "CONTAINS_MULTIPLE_PARENT",
                    $"{relationPath}/targetNodeId",
                    $"Node already has contains-parent '{existingParent}'."));
            }
            else
            {
                parentByChild.Add(relation.TargetNodeId, relation.SourceNodeId);
            }

            if (!childrenByParent.TryGetValue(relation.SourceNodeId, out var children))
            {
                children = [];
                childrenByParent.Add(relation.SourceNodeId, children);
            }

            children.Add(relation.TargetNodeId);
        }

        ValidateContainsCycles(project.Nodes, childrenByParent, issues);
    }

    private static void ValidateRelationEndpointKinds(
        EtabRelation relation,
        EtabNode source,
        EtabNode target,
        string relationPath,
        ICollection<ValidationIssue> issues)
    {
        var sourceValid = relation.Kind switch
        {
            "contains" => source.Kind == "applicationUnit",
            "commands" or "observes" or "usesRecipe" or "usesLink" =>
                source.Kind is "applicationUnit" or "commandUnit",
            _ => false
        };

        if (!sourceValid)
        {
            issues.Add(new ValidationIssue(
                "RELATION_SOURCE_KIND",
                $"{relationPath}/sourceNodeId",
                $"Relation '{relation.Kind}' cannot use node kind '{source.Kind}' as its source."));
        }

        var targetValid = relation.Kind switch
        {
            "contains" or "commands" or "observes" =>
                target.Kind is "applicationUnit" or "commandUnit",
            "usesRecipe" => target.Kind == "recipeManager",
            "usesLink" => target.Kind == "machineLink",
            _ => false
        };

        if (!targetValid)
        {
            issues.Add(new ValidationIssue(
                "RELATION_TARGET_KIND",
                $"{relationPath}/targetNodeId",
                $"Relation '{relation.Kind}' cannot target node kind '{target.Kind}'."));
        }
    }

    private static void ValidateContainsCycles(
        IEnumerable<EtabNode> nodes,
        IReadOnlyDictionary<string, List<string>> childrenByParent,
        ICollection<ValidationIssue> issues)
    {
        var state = new Dictionary<string, int>(StringComparer.Ordinal);

        bool Visit(string nodeId)
        {
            if (state.TryGetValue(nodeId, out var currentState))
            {
                return currentState == 1;
            }

            state[nodeId] = 1;

            if (childrenByParent.TryGetValue(nodeId, out var children))
            {
                foreach (var child in children)
                {
                    if (Visit(child))
                    {
                        return true;
                    }
                }
            }

            state[nodeId] = 2;
            return false;
        }

        foreach (var node in nodes)
        {
            if (Visit(node.Id))
            {
                issues.Add(new ValidationIssue(
                    "CONTAINS_CYCLE",
                    "/relations",
                    "The contains hierarchy contains a cycle."));
                return;
            }
        }
    }

    private static void ValidateLayout(EtabProjectDocument project, ICollection<ValidationIssue> issues)
    {
        var nodeIds = project.Nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var seenLayoutNodes = new HashSet<string>(StringComparer.Ordinal);
        var declaredGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (project.Layout.Groups is not null)
        {
            for (var index = 0; index < project.Layout.Groups.Count; index++)
            {
                var group = project.Layout.Groups[index];
                if (!declaredGroups.Add(group.Name))
                {
                    issues.Add(new ValidationIssue(
                        "LAYOUT_GROUP_DUPLICATE",
                        $"/layout/groups/{index}/name",
                        $"Layout area name '{group.Name}' is already declared."));
                }
            }
        }

        for (var index = 0; index < project.Layout.Nodes.Count; index++)
        {
            var layout = project.Layout.Nodes[index];

            if (!nodeIds.Contains(layout.NodeId))
            {
                issues.Add(new ValidationIssue(
                    "LAYOUT_NODE_MISSING",
                    $"/layout/nodes/{index}/nodeId",
                    $"Layout references unknown node '{layout.NodeId}'."));
            }

            if (!seenLayoutNodes.Add(layout.NodeId))
            {
                issues.Add(new ValidationIssue(
                    "LAYOUT_NODE_DUPLICATE",
                    $"/layout/nodes/{index}/nodeId",
                    $"Node '{layout.NodeId}' has more than one layout entry."));
            }

            if (declaredGroups.Count > 0 &&
                !string.IsNullOrWhiteSpace(layout.Group) &&
                !declaredGroups.Contains(layout.Group))
            {
                issues.Add(new ValidationIssue(
                    "LAYOUT_GROUP_MISSING",
                    $"/layout/nodes/{index}/group",
                    $"Layout references undeclared area '{layout.Group}'."));
            }
        }
    }

    private static void ValidateGeneratedArtifactNames(
        EtabProjectDocument project,
        ICollection<ValidationIssue> issues)
    {
        var sourcesByArtifact = new Dictionary<string, string>(IecNameComparer);

        for (var nodeIndex = 0; nodeIndex < project.Nodes.Count; nodeIndex++)
        {
            var node = project.Nodes[nodeIndex];
            var names = new List<(string Name, string Kind)>();

            if (node.Generate.CommandEnum)
            {
                names.Add(($"E_{project.Project.Prefix}_{node.SymbolStem}Command", "command enum"));
            }

            if (node.Generate.RequestType)
            {
                names.Add(($"ST_{project.Project.Prefix}_{node.SymbolStem}Request", "request DUT"));
            }

            if (node.Generate.StatusType)
            {
                names.Add(($"ST_{project.Project.Prefix}_{node.SymbolStem}Status", "status DUT"));
            }

            if (node.Generate.BaseFunctionBlock)
            {
                names.Add(($"FB_{project.Project.Prefix}_{node.SymbolStem}UnitBase", "base function block"));
            }

            foreach (var (name, kind) in names)
            {
                if (sourcesByArtifact.TryGetValue(name, out var existingSource))
                {
                    issues.Add(new ValidationIssue(
                        "ARTIFACT_NAME_COLLISION",
                        $"/nodes/{nodeIndex}/symbolStem",
                        $"Generated {kind} '{name}' collides with {existingSource}."));
                }
                else
                {
                    sourcesByArtifact.Add(name, $"node '{node.Name}'");
                }
            }
        }
    }

    private static void AddDuplicateIssues<TItem, TKey>(
        IReadOnlyList<TItem> items,
        Func<TItem, TKey> keySelector,
        Func<TItem, string> pathSelector,
        string code,
        string label,
        ICollection<ValidationIssue> issues,
        IEqualityComparer<TKey>? comparer = null)
        where TKey : notnull
    {
        var firstPathByKey = new Dictionary<TKey, string>(comparer);

        foreach (var item in items)
        {
            var key = keySelector(item);
            var path = pathSelector(item);

            if (firstPathByKey.TryGetValue(key, out var firstPath))
            {
                issues.Add(new ValidationIssue(
                    code,
                    path,
                    $"{label} '{key}' is already used at '{firstPath}'."));
            }
            else
            {
                firstPathByKey.Add(key, path);
            }
        }
    }

    private static string EscapePointerToken(string value) =>
        value.Replace("~", "~0", StringComparison.Ordinal)
            .Replace("/", "~1", StringComparison.Ordinal);
}

internal static class ReadOnlyListExtensions
{
    public static int IndexOf<T>(this IReadOnlyList<T> items, T value)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (EqualityComparer<T>.Default.Equals(items[index], value))
            {
                return index;
            }
        }

        return -1;
    }
}
