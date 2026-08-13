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

    private readonly ProjectValidator _validator = new();
    private readonly ArtifactPreviewGenerator _generator = new();

    [Fact]
    public void ReferenceProject_GeneratesExpectedArtifactSet()
    {
        var preview = Generate(ParseProject());

        Assert.Equal(15, preview.Artifacts.Count);
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
    }

    [Fact]
    public void InstanceTypeDefaultsToGeneratedBaseOrEtabLibraryType()
    {
        var project = ParseProject();
        project["nodes"]![1]!["generate"]!.AsObject().Remove("instanceType");
        project["nodes"]![4]!["generate"]!.AsObject().Remove("instanceType");
        project["nodes"]![5]!["generate"]!.AsObject().Remove("instanceType");
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
        Assert.Contains("GVL_BM_Units.fbMachine();", artifact.Content);
        Assert.True(
            artifact.Content.IndexOf("fbMachine()", StringComparison.Ordinal) <
            artifact.Content.IndexOf("fbMotionUnit()", StringComparison.Ordinal));
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
                GeneratedArtifactKind.ProgramCallStructure)
            .Select(item => $"{item.Kind}|{item.TwinCatGuid:D}|{item.Sha256}|{item.Content}")
            .ToArray();
        var modifiedProjectArtifacts = Generate(modified).Artifacts
            .Where(item => item.Kind is GeneratedArtifactKind.InstanceGlobalVariableList or
                GeneratedArtifactKind.ProgramCallStructure)
            .Select(item => $"{item.Kind}|{item.TwinCatGuid:D}|{item.Sha256}|{item.Content}")
            .ToArray();

        Assert.Equal(originalProjectArtifacts, modifiedProjectArtifacts);
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
