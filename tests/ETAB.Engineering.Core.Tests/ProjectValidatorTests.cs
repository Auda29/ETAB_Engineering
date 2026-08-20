using System.Text.Json;
using System.Text.Json.Nodes;
using ETAB.Engineering.Core.Validation;
using Xunit;

namespace ETAB.Engineering.Core.Tests;

public sealed class ProjectValidatorTests
{
    private static readonly string FixtureDirectory =
        Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private static readonly string SchemaJson =
        File.ReadAllText(Path.Combine(FixtureDirectory, "etab-project.schema.json"));

    private static readonly string ValidProjectJson =
        File.ReadAllText(Path.Combine(FixtureDirectory, "BrushMachine.reference.etab.json"));

    private readonly ProjectValidator _validator = new();

    [Fact]
    public void ReferenceProject_IsValid()
    {
        var result = _validator.Validate(ValidProjectJson, SchemaJson);

        Assert.True(result.IsValid, FormatIssues(result));
        Assert.NotNull(result.Project);
        Assert.Equal("BrushMachine", result.Project.Project.Name);
        Assert.Equal(7, result.Project.Nodes.Count);
    }

    [Fact]
    public void LegacyValueProperty_IsRejectedBySchema()
    {
        var project = ParseProject();
        var command = project["nodes"]![0]!["commands"]![0]!.AsObject();
        command["value"] = command["enumValue"]!.GetValue<uint>();
        command.Remove("enumValue");

        var result = Validate(project);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "SCHEMA");
    }

    [Fact]
    public void DuplicateStableId_IsRejectedSemantically()
    {
        var project = ParseProject();
        var commands = project["nodes"]![1]!["commands"]!.AsArray();
        commands[1]!["id"] = commands[0]!["id"]!.GetValue<string>();

        var result = Validate(project);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "ID_DUPLICATE");
    }

    [Fact]
    public void DuplicateEnumValue_IsRejectedSemantically()
    {
        var project = ParseProject();
        var commands = project["nodes"]![1]!["commands"]!.AsArray();
        commands[2]!["enumValue"] = commands[1]!["enumValue"]!.GetValue<uint>();

        var result = Validate(project);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "ENUM_VALUE_DUPLICATE");
    }

    [Theory]
    [InlineData("Reference")]
    [InlineData("reference")]
    public void TwinCatReservedCommandName_IsRejectedSemantically(string commandName)
    {
        var project = ParseProject();
        project["nodes"]![1]!["commands"]![1]!["name"] = commandName;

        var result = Validate(project);

        Assert.False(result.IsValid);
        var issue = Assert.Single(result.Issues, issue => issue.Code == "IEC_IDENTIFIER_RESERVED");
        Assert.Equal("/nodes/1/commands/1/name", issue.Path);
        Assert.Contains(commandName, issue.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TwinCatReservedGeneratedPayloadField_IsRejectedSemantically()
    {
        var project = ParseProject();
        project["nodes"]![1]!["requestPayload"]![0]!["name"] = "VAR";

        var result = Validate(project);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue =>
            issue.Code == "IEC_IDENTIFIER_RESERVED" &&
            issue.Path == "/nodes/1/requestPayload/0/name");
    }

    [Fact]
    public void LibraryOwnedStatusField_IsRejectedSemantically()
    {
        var project = ParseProject();
        var statusPayload = project["nodes"]![0]!["statusPayload"]!.AsArray();
        statusPayload.Add(new JsonObject
        {
            ["id"] = "11100000-0000-4000-8000-00000000ffff",
            ["name"] = "stUnit",
            ["dataType"] = "ETAB.ST_ApplicationUnitStatus"
        });

        var result = Validate(project);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "STATUS_RESERVED_FIELD");
    }

    [Fact]
    public void UnknownRelationEndpoint_IsRejectedSemantically()
    {
        var project = ParseProject();
        project["relations"]![0]!["targetNodeId"] =
            "ffffffff-ffff-4fff-8fff-ffffffffffff";

        var result = Validate(project);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "RELATION_ENDPOINT");
    }

    [Fact]
    public void RecipeManagerCannotBeRelationSource()
    {
        var project = ParseProject();
        var recipeManagerId = project["nodes"]![5]!["id"]!.GetValue<string>();
        project["relations"]![11]!["sourceNodeId"] = recipeManagerId;

        var result = Validate(project);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "RELATION_SOURCE_KIND");
    }

    [Fact]
    public void ContainsCannotTargetMachineLink()
    {
        var project = ParseProject();
        var machineLinkId = project["nodes"]![6]!["id"]!.GetValue<string>();
        project["relations"]![0]!["targetNodeId"] = machineLinkId;

        var result = Validate(project);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "RELATION_TARGET_KIND");
    }

    [Fact]
    public void DuplicateRelationIsRejected()
    {
        var project = ParseProject();
        var duplicate = project["relations"]![11]!.DeepClone().AsObject();
        duplicate["id"] = "99999999-9999-4999-8999-999999999999";
        project["relations"]!.AsArray().Add(duplicate);

        var result = Validate(project);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "RELATION_DUPLICATE");
    }

    [Fact]
    public void DuplicateLayoutAreaNameIsRejected()
    {
        var project = ParseProject();
        project["layout"]!["groups"]!.AsArray().Add(new JsonObject
        {
            ["name"] = "Machine",
            ["displayName"] = "Duplicate machine"
        });

        var result = Validate(project);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "LAYOUT_GROUP_DUPLICATE");
    }

    [Fact]
    public void InvalidTwinCatAreaFolderNameIsRejected()
    {
        var project = ParseProject();
        project["layout"]!["groups"]![0]!["displayName"] = "Machine/Overview";

        var result = Validate(project);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "LAYOUT_GROUP_FOLDER_NAME");
    }

    [Fact]
    public void DuplicateTwinCatAreaFolderNameIsRejected()
    {
        var project = ParseProject();
        project["layout"]!["groups"]!.AsArray().Add(new JsonObject
        {
            ["name"] = "duplicateFolder",
            ["displayName"] = "machine"
        });

        var result = Validate(project);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "LAYOUT_GROUP_FOLDER_DUPLICATE");
    }

    [Fact]
    public void ReservedUnassignedAreaFolderNameIsRejected()
    {
        var project = ParseProject();
        project["layout"]!["groups"]![0]!["displayName"] = "Unassigned";

        var result = Validate(project);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "LAYOUT_GROUP_FOLDER_DUPLICATE");
    }

    [Fact]
    public void DuplicateNodeFolderNameInsideAreaIsRejected()
    {
        var project = ParseProject();
        project["nodes"]![2]!["displayName"] =
            project["nodes"]![1]!["displayName"]!.GetValue<string>();

        var result = Validate(project);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "LAYOUT_NODE_FOLDER_DUPLICATE");
    }

    [Fact]
    public void UndeclaredLayoutAreaReferenceIsRejected()
    {
        var project = ParseProject();
        project["layout"]!["nodes"]![0]!["group"] = "missingArea";

        var result = Validate(project);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "LAYOUT_GROUP_MISSING");
    }

    [Fact]
    public void InvertedArrayBounds_AreRejectedSemantically()
    {
        var project = ParseProject();
        var field = FindFirstFieldWithArrayDimensions(project);
        var dimension = field["arrayDimensions"]![0]!;
        dimension["lower"] = 5;
        dimension["upper"] = 2;

        var result = Validate(project);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "ARRAY_BOUNDS");
    }

    [Fact]
    public void BaseFunctionBlockForUnsupportedNodeKind_IsRejectedSemantically()
    {
        var project = ParseProject();
        project["nodes"]![4]!["generate"]!["baseFunctionBlock"] = true;

        var result = Validate(project);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "BASE_FB_NODE_KIND");
    }

    [Fact]
    public void InstanceTypeWithoutEnabledInstance_IsRejectedSemantically()
    {
        var project = ParseProject();
        project["nodes"]![0]!["generate"]!["instance"] = false;

        var result = Validate(project);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "INSTANCE_TYPE_WITHOUT_INSTANCE");
    }

    [Fact]
    public void ProgramCallStructureWithoutInstances_IsRejectedSemantically()
    {
        var project = ParseProject();
        project["project"]!["generation"]!["programCallStructure"] = true;
        foreach (var node in project["nodes"]!.AsArray())
        {
            node!["generate"]!["instance"] = false;
            node["generate"]!.AsObject().Remove("instanceType");
        }

        var result = Validate(project);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "PROGRAM_WITHOUT_INSTANCES");
    }

    [Fact]
    public void RuntimeExecutionWithoutCyclicInstances_IsRejectedSemantically()
    {
        var project = ParseProject();
        project["project"]!["generation"]!["runtimeExecution"] = true;
        foreach (var node in project["nodes"]!.AsArray())
        {
            node!["generate"]!["callInProgram"] = false;
        }

        var result = Validate(project);

        Assert.False(result.IsValid);
        var issue = Assert.Single(
            result.Issues,
            item => item.Code == "PROGRAM_WITHOUT_INSTANCES");
        Assert.Equal("/project/generation/runtimeExecution", issue.Path);
    }

    [Fact]
    public void ProgramCallSelectionWithoutInstance_IsRejectedSemantically()
    {
        var project = ParseProject();
        project["nodes"]![0]!["generate"]!["instance"] = false;
        project["nodes"]![0]!["generate"]!["callInProgram"] = true;
        project["nodes"]![0]!["generate"]!.AsObject().Remove("instanceType");

        var result = Validate(project);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "PROGRAM_CALL_WITHOUT_INSTANCE");
    }

    [Fact]
    public void RelationWiringRequiresSourceAndTargetInstances()
    {
        var project = ParseProject();
        var sourceGenerate = project["nodes"]![4]!["generate"]!.AsObject();
        sourceGenerate["instance"] = false;
        sourceGenerate["callInProgram"] = false;
        sourceGenerate.Remove("instanceType");
        var targetGenerate = project["nodes"]![1]!["generate"]!.AsObject();
        targetGenerate["instance"] = false;
        targetGenerate["callInProgram"] = false;
        targetGenerate.Remove("instanceType");

        var result = Validate(project);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Issues,
            issue => issue.Code == "RELATION_SOURCE_INSTANCE_REQUIRED");
        Assert.Contains(
            result.Issues,
            issue => issue.Code == "RELATION_TARGET_INSTANCE_REQUIRED");
    }

    [Fact]
    public void LogicalRelationsMayRemainWithoutInstancesWhenWiringIsDisabled()
    {
        var project = ParseProject();
        project["project"]!["generation"]!["relationWiring"] = false;
        var generate = project["nodes"]![4]!["generate"]!.AsObject();
        generate["instance"] = false;
        generate["callInProgram"] = false;
        generate.Remove("instanceType");

        var result = Validate(project);

        Assert.True(
            result.IsValid,
            string.Join(Environment.NewLine, result.Issues.Select(issue => issue.Message)));
    }

    [Fact]
    public void CustomRelationStatusMemberIsLimitedToWrapperNodeKinds()
    {
        var project = ParseProject();
        project["nodes"]![0]!["generate"]!["relationStatusMember"] = "stCustomStatus";

        var result = Validate(project);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Issues,
            issue => issue.Code == "RELATION_STATUS_NODE_KIND");
    }

    [Fact]
    public void CommandRoutesRequireExistingCommandsAndGeneratedContracts()
    {
        var project = ParseProject();
        var relation = project["relations"]!.AsArray().First(item =>
            item!["kind"]!.GetValue<string>() == "commands");
        relation!["commandRoutes"] = new JsonArray
        {
            new JsonObject
            {
                ["id"] = "99999999-0000-4000-8000-000000000010",
                ["sourceCommandId"] = "ffffffff-ffff-4fff-8fff-ffffffffffff",
                ["targetCommandId"] = project["nodes"]![1]!["commands"]![0]!["id"]!.GetValue<string>()
            }
        };

        var result = Validate(project);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "COMMAND_ROUTES_CONTRACT_REQUIRED");
        Assert.Contains(result.Issues, issue => issue.Code == "COMMAND_ROUTE_SOURCE_MISSING");
    }

    [Fact]
    public void AutomaticCommandRoutesRequireRelationWiring()
    {
        var project = ParseProject();
        var source = project["nodes"]![4]!;
        var target = project["nodes"]![1]!;
        source["generate"]!["commandEnum"] = true;
        source["generate"]!["requestType"] = true;
        project["project"]!["generation"]!["relationWiring"] = false;
        project["project"]!["generation"]!["runtimeExecution"] = true;
        var relation = project["relations"]!.AsArray().First(item =>
            item!["kind"]!.GetValue<string>() == "commands");
        relation!["commandRoutes"] = new JsonArray
        {
            new JsonObject
            {
                ["id"] = "99999999-0000-4000-8000-000000000011",
                ["sourceCommandId"] = source["commands"]![0]!["id"]!.GetValue<string>(),
                ["targetCommandId"] = target["commands"]![0]!["id"]!.GetValue<string>()
            }
        };

        var result = Validate(project);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "COMMAND_ROUTES_WIRING_REQUIRED");
    }

    private static JsonObject ParseProject() =>
        JsonNode.Parse(ValidProjectJson)!.AsObject();

    private ProjectValidationResult Validate(JsonObject project) =>
        _validator.Validate(
            project.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            SchemaJson);

    private static JsonObject FindFirstFieldWithArrayDimensions(JsonObject project)
    {
        foreach (var node in project["nodes"]!.AsArray())
        {
            foreach (var payloadName in new[] { "requestPayload", "statusPayload" })
            {
                foreach (var field in node![payloadName]!.AsArray())
                {
                    if (field!["arrayDimensions"] is JsonArray { Count: > 0 })
                    {
                        return field.AsObject();
                    }
                }
            }
        }

        throw new InvalidOperationException("The reference fixture has no array field.");
    }

    private static string FormatIssues(ProjectValidationResult result) =>
        string.Join(Environment.NewLine, result.Issues.Select(
            issue => $"[{issue.Code}] {issue.Path}: {issue.Message}"));
}
