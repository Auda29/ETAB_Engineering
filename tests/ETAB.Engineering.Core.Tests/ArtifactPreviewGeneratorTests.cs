using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using ETAB.Engineering.Core.Generation;
using ETAB.Engineering.Core.Model;
using ETAB.Engineering.Core.Validation;
using Xunit;

namespace ETAB.Engineering.Core.Tests;

public sealed class ArtifactPreviewGeneratorTests
{
    private static readonly string FixtureDirectory =
        Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private static readonly string SnapshotDirectory =
        Path.Combine(AppContext.BaseDirectory, "Snapshots");

    private static readonly string SchemaJson =
        File.ReadAllText(Path.Combine(FixtureDirectory, "etab-project.schema.json"));

    private static readonly string ValidProjectJson =
        File.ReadAllText(Path.Combine(FixtureDirectory, "BrushMachine.reference.etab.json"));

    private static readonly string IntegrationProjectJson =
        File.ReadAllText(Path.Combine(FixtureDirectory, "BrushMachine.integration.etab.json"));

    private readonly ProjectValidator _validator = new();
    private readonly ArtifactPreviewGenerator _generator = new();

    [Fact]
    public void ReferenceProject_GeneratesExpectedArtifactSet()
    {
        var preview = Generate(ParseProject());

        Assert.Equal(16, preview.Artifacts.Count);
        Assert.Equal(
            [
                "Generated/DUTs/Status/ST_BM_MachineStatus.TcDUT",
                "Generated/POUs/FB_BM_MachineUnitBase.TcPOU",
                "Generated/DUTs/Commands/E_BM_MotionCommand.TcDUT",
                "Generated/DUTs/Requests/ST_BM_MotionRequest.TcDUT",
                "Generated/DUTs/Status/ST_BM_MotionStatus.TcDUT",
                "Generated/POUs/FB_BM_MotionUnitBase.TcPOU",
                "Generated/DUTs/Commands/E_BM_ProcessCommand.TcDUT",
                "Generated/DUTs/Requests/ST_BM_ProcessRequest.TcDUT",
                "Generated/DUTs/Status/ST_BM_ProcessStatus.TcDUT",
                "Generated/POUs/FB_BM_ProcessUnitBase.TcPOU",
                "Generated/DUTs/Commands/E_BM_WorkpieceCommand.TcDUT",
                "Generated/DUTs/Requests/ST_BM_WorkpieceRequest.TcDUT",
                "Generated/DUTs/Status/ST_BM_WorkpieceStatus.TcDUT",
                "Generated/POUs/FB_BM_WorkpieceUnitBase.TcPOU",
                "Generated/POUs/FB_BM_Relations.TcPOU",
                "Generated/GVLs/GVL_BM_Units.TcGVL"
            ],
            preview.Artifacts.Select(artifact => artifact.RelativePath));
    }

    [Fact]
    public void ReferenceProject_GeneratesQualifiedInstanceGvlWithExplicitProjectTypes()
    {
        var artifact = Generate(ParseProject()).Artifacts.Single(
            item => item.Kind == GeneratedArtifactKind.InstanceGlobalVariableList);

        Assert.Equal("GVL_BM_Units", artifact.Name);
        Assert.Contains("{attribute 'qualified_only'}", artifact.Content);
        Assert.Contains("fbMachine : FB_BM_Machine;", artifact.Content);
        Assert.Contains("fbMotionUnit : FB_BM_MotionUnit;", artifact.Content);
        Assert.Contains("fbProcessCycle : FB_BM_ProcessCycle;", artifact.Content);
        Assert.Contains("fbRecipeManager : FB_BM_RecipeService;", artifact.Content);
        Assert.Contains("fbCellLink : FB_BM_CellInterface;", artifact.Content);
        Assert.Contains("fbEtabRelationWiring : FB_BM_Relations;", artifact.Content);
    }

    [Fact]
    public void IntegrationProject_KeepsExistingDutsExternalAndGeneratesNineArtifacts()
    {
        var preview = Generate(ParseIntegrationProject());

        Assert.Equal(9, preview.Artifacts.Count);
        Assert.DoesNotContain(
            preview.Artifacts,
            artifact => artifact.Kind is GeneratedArtifactKind.CommandEnum or
                GeneratedArtifactKind.RequestDut || artifact.Name == "ST_BM_MachineStatus");
        Assert.Contains(
            "stOperation : ETAB.ST_ETAB_CommandStatus;",
            preview.Artifacts.Single(
                artifact => artifact.Name == "ST_BM_MotionStatus").Content);
        Assert.Contains(
            preview.Artifacts,
            artifact => artifact.Name == "GVL_BM_Units");
    }

    [Fact]
    public void RelationWiring_ProvidesExplicitAdaptersForEveryRelationKind()
    {
        var artifact = Generate(ParseProject()).Artifacts.Single(
            item => item.Kind == GeneratedArtifactKind.RelationWiring);

        Assert.Equal("FB_BM_Relations", artifact.Name);
        Assert.Contains(
            "GVL_BM_Units.fbMotionUnit.rUnit.ipMasterUnit := GVL_BM_Units.fbMachine.rUnit;",
            artifact.Content);
        Assert.Contains(
            "METHOD PUBLIC Command_ProcessCycle_To_MotionUnit : BOOL",
            artifact.Content);
        Assert.Contains("stRequest : ST_BM_MotionRequest;", artifact.Content);
        Assert.Contains(
            "GVL_BM_Units.stMotionUnitRequest := stRequest;",
            artifact.Content);
        Assert.Contains(
            "METHOD PUBLIC Observe_ProcessCycle_To_MotionUnit : ST_BM_MotionStatus",
            artifact.Content);
        Assert.Contains("GVL_BM_Units.stMotionUnitStatus;", artifact.Content);
        Assert.Contains(
            "METHOD PUBLIC Recipe_ProcessCycle_To_RecipeManager : ETAB.ST_ETAB_RecipeStatus",
            artifact.Content);
        Assert.Contains("GVL_BM_Units.fbRecipeManager.stManagerStatus;", artifact.Content);
        Assert.Contains(
            "METHOD PUBLIC Link_Machine_To_CellLink : ETAB.ST_ETAB_MachineLinkStatus",
            artifact.Content);
        Assert.Contains("GVL_BM_Units.fbCellLink.stStatus;", artifact.Content);
        Assert.Contains("contains: Machine -> ProcessCycle is structural only", artifact.Content);
    }

    [Fact]
    public void CommandRoutes_MapConfiguredSourceRequestsToTargetRequests()
    {
        var project = ParseProject();
        project["project"]!["generation"]!["runtimeExecution"] = true;
        var source = project["nodes"]![4]!;
        var target = project["nodes"]![1]!;
        source["generate"]!["commandEnum"] = true;
        source["generate"]!["requestType"] = true;
        var relation = project["relations"]!.AsArray().Single(item =>
            item!["kind"]!.GetValue<string>() == "commands" &&
            item["sourceNodeId"]!.GetValue<string>() == source["id"]!.GetValue<string>() &&
            item["targetNodeId"]!.GetValue<string>() == target["id"]!.GetValue<string>());
        relation!["commandRoutes"] = new JsonArray
        {
            new JsonObject
            {
                ["id"] = "99999999-0000-4000-8000-000000000001",
                ["sourceCommandId"] = source["commands"]![1]!["id"]!.GetValue<string>(),
                ["targetCommandId"] = target["commands"]![1]!["id"]!.GetValue<string>()
            }
        };

        var artifact = Generate(project).Artifacts.Single(
            item => item.Kind == GeneratedArtifactKind.RelationWiring);

        Assert.Contains("command routes: ProcessCycle -> MotionUnit", artifact.Content);
        Assert.Contains("CASE GVL_BM_Units.stProcessCycleRequest.eCommand OF", artifact.Content);
        Assert.Contains("E_BM_CycleCommand.Cycle:", artifact.Content);
        Assert.Contains(
            "GVL_BM_Units.stMotionUnitRequest.eCommand := E_BM_MotionCommand.HomeAll;",
            artifact.Content);
        Assert.Contains(
            "GVL_BM_Units.stMotionUnitRequest.nCommandID := GVL_BM_Units.stProcessCycleRequest.nCommandID;",
            artifact.Content);
    }

    [Fact]
    public void RelationWiring_IsOptInForExistingModels()
    {
        var project = ParseProject();
        project["project"]!["generation"]!.AsObject().Remove("relationWiring");

        var preview = Generate(project);

        Assert.DoesNotContain(
            preview.Artifacts,
            item => item.Kind == GeneratedArtifactKind.RelationWiring);
        Assert.DoesNotContain(
            "fbEtabRelationWiring",
            preview.Artifacts.Single(
                item => item.Kind == GeneratedArtifactKind.InstanceGlobalVariableList).Content);
    }

    [Fact]
    public void InstanceTypeDefaultsToGeneratedBaseOrEtabLibraryType()
    {
        var project = ParseProject();
        project["nodes"]![1]!["generate"]!.AsObject().Remove("instanceType");
        project["nodes"]![4]!["generate"]!.AsObject().Remove("instanceType");
        project["nodes"]![5]!["generate"]!.AsObject().Remove("instanceType");
        project["nodes"]![5]!["generate"]!.AsObject().Remove("relationStatusMember");
        project["nodes"]![6]!["generate"]!.AsObject().Remove("instanceType");

        var content = Generate(project).Artifacts.Single(
            item => item.Kind == GeneratedArtifactKind.InstanceGlobalVariableList).Content;

        Assert.Contains("fbMotionUnit : FB_BM_MotionUnitBase;", content);
        Assert.Contains("fbProcessCycle : ETAB.FB_ETAB_CommandUnit;", content);
        Assert.Contains("fbRecipeManager : ETAB.FB_ETAB_RecipeManager;", content);
        Assert.Contains("fbCellLink : ETAB.FB_ETAB_MachineLink;", content);
    }

    [Fact]
    public void ProgramCallStructure_IsOptInAndCallsInstancesInDeterministicOrder()
    {
        var project = ParseProject();
        Assert.DoesNotContain(
            Generate(project).Artifacts,
            item => item.Kind == GeneratedArtifactKind.ProgramCallStructure);

        project["project"]!["generation"]!["programCallStructure"] = true;
        var artifact = Generate(project).Artifacts.Single(
            item => item.Kind == GeneratedArtifactKind.ProgramCallStructure);

        Assert.Equal("PRG_BM_Generated", artifact.Name);
        Assert.Contains("PROGRAM PRG_BM_Generated", artifact.Content);
        Assert.Contains("GVL_BM_Units.fbEtabRelationWiring();", artifact.Content);
        Assert.Contains("GVL_BM_Units.fbMachine();", artifact.Content);
        Assert.True(
            artifact.Content.IndexOf("fbEtabRelationWiring()", StringComparison.Ordinal) <
            artifact.Content.IndexOf("fbMachine()", StringComparison.Ordinal));
        Assert.True(
            artifact.Content.IndexOf("fbMachine()", StringComparison.Ordinal) <
            artifact.Content.IndexOf("fbMotionUnit()", StringComparison.Ordinal));
    }

    [Fact]
    public void RuntimeExecution_GeneratesProgramAndRunsRelationWiringFirst()
    {
        var project = ParseProject();
        project["project"]!["generation"]!["runtimeExecution"] = true;

        var artifact = Generate(project).Artifacts.Single(
            item => item.Kind == GeneratedArtifactKind.ProgramCallStructure);

        Assert.Contains("GVL_BM_Units.fbEtabRelationWiring();", artifact.Content);
        Assert.True(
            artifact.Content.IndexOf("fbEtabRelationWiring()", StringComparison.Ordinal) <
            artifact.Content.IndexOf("fbCellLink(", StringComparison.Ordinal));
        Assert.True(
            artifact.Content.IndexOf("fbCellLink(", StringComparison.Ordinal) <
            artifact.Content.IndexOf("fbMachine(", StringComparison.Ordinal));
        Assert.True(
            artifact.Content.IndexOf("fbMachine(", StringComparison.Ordinal) <
            artifact.Content.IndexOf("fbMotionUnit(", StringComparison.Ordinal));
        Assert.True(
            artifact.Content.IndexOf("fbRecipeManager(", StringComparison.Ordinal) <
            artifact.Content.IndexOf("fbProcessCycle(", StringComparison.Ordinal));
    }

    [Fact]
    public void RuntimeExecution_BindsRequestsOptionsAndStatuses()
    {
        var project = ParseProject();
        project["project"]!["generation"]!["runtimeExecution"] = true;
        project["nodes"]![5]!["generate"]!["requestType"] = true;
        project["nodes"]![5]!["generate"]!["statusType"] = true;
        project["nodes"]![6]!["generate"]!["requestType"] = true;
        project["nodes"]![6]!["generate"]!["statusType"] = true;

        var preview = Generate(project);
        var program = preview.Artifacts.Single(
            item => item.Kind == GeneratedArtifactKind.ProgramCallStructure).Content;
        var gvl = preview.Artifacts.Single(
            item => item.Kind == GeneratedArtifactKind.InstanceGlobalVariableList).Content;

        Assert.Contains("CASE GVL_BM_Units.stMotionUnitRequest.eCommand OF", program);
        Assert.Contains(
            "eMotionUnitEtabCommand := ETAB.E_ETAB_UnitCommand.User;",
            program);
        Assert.Contains("nStartState := 1", program);
        Assert.Contains(
            "GVL_BM_Units.stMotionUnitStatus.stUnit := GVL_BM_Units.fbMotionUnit.stStatus;",
            program);
        Assert.Contains("pData := ADR(GVL_BM_Units.stRecipeManagerData)", program);
        Assert.Contains("sFileName := 'BrushMachineRecipe.xml'", program);
        Assert.Contains(
            "eBridgeType := ETAB.E_ETAB_MachineLinkBridgeType.ExternalBridge",
            program);
        Assert.Contains("stMotionUnitRequest : ST_BM_MotionRequest;", gvl);
        Assert.Contains("stMotionUnitStatus : ST_BM_MotionStatus;", gvl);
        Assert.Contains("stRecipeManagerData : ST_BM_ProductRecipe;", gvl);
        Assert.Contains("stCellLinkTx : ETAB.ST_ETAB_MachineLinkData;", gvl);
    }

    [Fact]
    public void CreateUserStubs_UsesPreservedDerivedFunctionBlocks()
    {
        var project = ParseProject();
        project["project"]!["generation"]!["createUserStubs"] = true;
        project["nodes"]![1]!["generate"]!.AsObject().Remove("instanceType");

        var preview = Generate(project);
        var stub = preview.Artifacts.Single(
            item => item.Kind == GeneratedArtifactKind.UserFunctionBlock &&
                    item.SourceModelId == "20000000-0000-4000-8000-000000000001");
        var gvl = preview.Artifacts.Single(
            item => item.Kind == GeneratedArtifactKind.InstanceGlobalVariableList).Content;

        Assert.True(stub.PreserveUserEdits);
        Assert.Equal(
            "Generated/Application/FB_BM_MotionUnit.TcPOU",
            stub.RelativePath);
        Assert.Contains("This file is user-owned and is never overwritten", stub.Content);
        Assert.Contains("fbMotionUnit : FB_BM_MotionUnit;", gvl);
    }

    [Fact]
    public void ProjectAndNodeOrderChanges_DoNotAffectProjectLevelArtifacts()
    {
        var original = ParseProject();
        original["project"]!["generation"]!["programCallStructure"] = true;
        var modified = original.DeepClone().AsObject();
        Reverse(modified["nodes"]!.AsArray());

        var originalProjectArtifacts = Generate(original).Artifacts
            .Where(item => item.Kind is GeneratedArtifactKind.InstanceGlobalVariableList or
                GeneratedArtifactKind.RelationWiring or
                GeneratedArtifactKind.ProgramCallStructure)
            .Select(item => $"{item.Kind}|{item.TwinCatGuid:D}|{item.Sha256}|{item.Content}")
            .ToArray();
        var modifiedProjectArtifacts = Generate(modified).Artifacts
            .Where(item => item.Kind is GeneratedArtifactKind.InstanceGlobalVariableList or
                GeneratedArtifactKind.RelationWiring or
                GeneratedArtifactKind.ProgramCallStructure)
            .Select(item => $"{item.Kind}|{item.TwinCatGuid:D}|{item.Sha256}|{item.Content}")
            .ToArray();

        Assert.Equal(originalProjectArtifacts, modifiedProjectArtifacts);
    }

    [Fact]
    public void RelationOrderChanges_DoNotAffectRelationWiring()
    {
        var original = ParseProject();
        var modified = original.DeepClone().AsObject();
        Reverse(modified["relations"]!.AsArray());

        var originalArtifact = Generate(original).Artifacts.Single(
            item => item.Kind == GeneratedArtifactKind.RelationWiring);
        var modifiedArtifact = Generate(modified).Artifacts.Single(
            item => item.Kind == GeneratedArtifactKind.RelationWiring);

        Assert.Equal(originalArtifact.TwinCatGuid, modifiedArtifact.TwinCatGuid);
        Assert.Equal(originalArtifact.Sha256, modifiedArtifact.Sha256);
        Assert.Equal(originalArtifact.Content, modifiedArtifact.Content);
    }

    [Theory]
    [InlineData("E_BM_ProcessCommand", "E_BM_ProcessCommand.TcDUT.snap")]
    [InlineData("ST_BM_ProcessRequest", "ST_BM_ProcessRequest.TcDUT.snap")]
    [InlineData("ST_BM_ProcessStatus", "ST_BM_ProcessStatus.TcDUT.snap")]
    [InlineData("FB_BM_ProcessUnitBase", "FB_BM_ProcessUnitBase.TcPOU.snap")]
    public void ProcessArtifacts_MatchGoldenSnapshots(string artifactName, string snapshotName)
    {
        var artifact = Generate(ParseProject()).Artifacts.Single(item => item.Name == artifactName);
        var expected = NormalizeLineEndings(
            File.ReadAllText(Path.Combine(SnapshotDirectory, snapshotName)));

        Assert.Equal(expected, artifact.Content);
    }

    [Fact]
    public void EveryArtifact_IsWellFormedXmlWithStableHashAndLfLineEndings()
    {
        var preview = Generate(ParseProject());

        foreach (var artifact in preview.Artifacts)
        {
            _ = XDocument.Parse(artifact.Content, LoadOptions.PreserveWhitespace);
            Assert.DoesNotContain('\r', artifact.Content);

            var expectedHash = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(artifact.Content)))
                .ToLowerInvariant();
            Assert.Equal(expectedHash, artifact.Sha256);
        }
    }

    [Fact]
    public void LayoutOnlyChanges_DoNotAffectArtifacts()
    {
        var original = ParseProject();
        var modified = original.DeepClone().AsObject();
        modified["layout"]!["nodes"]![0]!["x"] = 9876.5;
        modified["layout"]!["nodes"]![0]!["y"] = -1234.25;
        modified["layout"]!["nodes"]![0]!["group"] = "MovedOnCanvas";
        modified["layout"]!["groups"]!.AsArray().Add(new JsonObject
        {
            ["name"] = "MovedOnCanvas",
            ["displayName"] = "Moved on canvas"
        });

        Assert.Equal(Signatures(Generate(original)), Signatures(Generate(modified)));
    }

    [Fact]
    public void NodeAndCommandInputOrder_DoNotAffectArtifacts()
    {
        var original = ParseProject();
        var modified = original.DeepClone().AsObject();
        Reverse(modified["nodes"]!.AsArray());

        foreach (var node in modified["nodes"]!.AsArray())
        {
            Reverse(node!["commands"]!.AsArray());
        }

        Assert.Equal(Signatures(Generate(original)), Signatures(Generate(modified)));
    }

    [Fact]
    public void NodeRename_KeepsTwinCatGuidsStable()
    {
        var original = ParseProject();
        var modified = original.DeepClone().AsObject();
        modified["nodes"]![1]!["name"] = "RenamedMotionUnit";

        var originalArtifacts = Generate(original).Artifacts
            .Where(artifact => artifact.SourceModelId == "20000000-0000-4000-8000-000000000001")
            .ToDictionary(artifact => artifact.Kind, artifact => artifact.TwinCatGuid);
        var modifiedArtifacts = Generate(modified).Artifacts
            .Where(artifact => artifact.SourceModelId == "20000000-0000-4000-8000-000000000001")
            .ToDictionary(artifact => artifact.Kind, artifact => artifact.TwinCatGuid);

        Assert.Equal(originalArtifacts, modifiedArtifacts);
    }

    [Fact]
    public void StatusDuts_EmbedTheLibraryStatusForEveryNodeKind()
    {
        var project = ParseProject();
        project["nodes"]![4]!["generate"]!["statusType"] = true;
        project["nodes"]![5]!["generate"]!["statusType"] = true;
        project["nodes"]![6]!["generate"]!["statusType"] = true;

        var artifacts = Generate(project).Artifacts;

        Assert.Contains(
            "stCommand : ETAB.ST_ETAB_CommandStatus;",
            artifacts.Single(artifact => artifact.Name == "ST_BM_CycleStatus").Content);
        Assert.Contains(
            "stRecipe : ETAB.ST_ETAB_RecipeStatus;",
            artifacts.Single(artifact => artifact.Name == "ST_BM_RecipeStatus").Content);
        Assert.Contains(
            "stLink : ETAB.ST_ETAB_MachineLinkStatus;",
            artifacts.Single(artifact => artifact.Name == "ST_BM_CellLinkStatus").Content);
    }

    [Fact]
    public void Version5Guid_MatchesRfcKnownVector()
    {
        var dnsNamespace = Guid.Parse("6ba7b810-9dad-11d1-80b4-00c04fd430c8");

        var actual = DeterministicGuid.CreateVersion5(dnsNamespace, "www.widgets.com");

        Assert.Equal(Guid.Parse("21f7f8de-8051-5b89-8680-0195ef798b6a"), actual);
    }

    private GenerationPreview Generate(JsonObject project) =>
        _generator.Generate(Validate(project));

    private EtabProjectDocument Validate(JsonObject project)
    {
        var result = _validator.Validate(
            project.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            SchemaJson);

        Assert.True(
            result.IsValid,
            string.Join(Environment.NewLine, result.Issues.Select(
                issue => $"[{issue.Code}] {issue.Path}: {issue.Message}")));
        return result.Project!;
    }

    private static JsonObject ParseProject() =>
        JsonNode.Parse(ValidProjectJson)!.AsObject();

    private static JsonObject ParseIntegrationProject() =>
        JsonNode.Parse(IntegrationProjectJson)!.AsObject();

    private static string[] Signatures(GenerationPreview preview) =>
        preview.Artifacts
            .Select(artifact =>
                $"{artifact.Kind}|{artifact.SourceModelId}|{artifact.TwinCatGuid:D}|{artifact.RelativePath}|{artifact.Sha256}|{artifact.Content}")
            .ToArray();

    private static void Reverse(JsonArray array)
    {
        var items = array.Select(item => item?.DeepClone()).Reverse().ToArray();
        array.Clear();

        foreach (var item in items)
        {
            array.Add(item);
        }
    }

    private static string NormalizeLineEndings(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
}
