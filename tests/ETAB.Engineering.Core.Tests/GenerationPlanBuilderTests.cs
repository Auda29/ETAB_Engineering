using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETAB.Engineering.Core.Generation;
using ETAB.Engineering.Core.Manifest;
using ETAB.Engineering.Core.Model;
using ETAB.Engineering.Core.Planning;
using ETAB.Engineering.Core.Validation;
using Xunit;

namespace ETAB.Engineering.Core.Tests;

public sealed class GenerationPlanBuilderTests
{
    private static readonly string FixtureDirectory =
        Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private static readonly string SchemaJson =
        File.ReadAllText(Path.Combine(FixtureDirectory, "etab-project.schema.json"));

    private static readonly string ValidProjectJson =
        File.ReadAllText(Path.Combine(FixtureDirectory, "BrushMachine.reference.etab.json"));

    private readonly ProjectValidator _validator = new();
    private readonly ArtifactPreviewGenerator _generator = new();
    private readonly GenerationPlanBuilder _planner = new();

    [Fact]
    public void EmptyRoot_PlansCreatesAndManifestCreate()
    {
        using var temporary = new TemporaryDirectory();

        var plan = BuildPlan(temporary.Path, ParseProject());

        Assert.False(plan.HasConflicts);
        Assert.Equal(GenerationChangeKind.Create, plan.Manifest.ChangeKind);
        Assert.Equal(16, plan.Changes.Count);
        Assert.All(plan.Changes, change => Assert.Equal(GenerationChangeKind.Create, change.ChangeKind));
        Assert.False(Directory.Exists(Path.Combine(temporary.Path, "Generated")));
    }

    [Fact]
    public void MatchingManagedState_IsUnchanged()
    {
        using var temporary = new TemporaryDirectory();
        var initialPlan = BuildPlan(temporary.Path, ParseProject());
        Materialize(temporary.Path, initialPlan);

        var repeatedPlan = BuildPlan(temporary.Path, ParseProject());

        Assert.False(repeatedPlan.HasConflicts);
        Assert.Equal(GenerationChangeKind.Unchanged, repeatedPlan.Manifest.ChangeKind);
        Assert.All(
            repeatedPlan.Changes,
            change => Assert.Equal(GenerationChangeKind.Unchanged, change.ChangeKind));
    }

    [Fact]
    public void ChangedPayload_PlansOnlyAffectedArtifactUpdate()
    {
        using var temporary = new TemporaryDirectory();
        Materialize(temporary.Path, BuildPlan(temporary.Path, ParseProject()));
        var modified = ParseProject();
        modified["nodes"]![3]!["statusPayload"]![0]!["name"] = "bExhaustReady";

        var plan = BuildPlan(temporary.Path, modified);

        Assert.False(plan.HasConflicts);
        Assert.Equal(GenerationChangeKind.Update, plan.Manifest.ChangeKind);
        var update = Assert.Single(
            plan.Changes,
            change => change.ChangeKind == GenerationChangeKind.Update);
        Assert.Equal("ST_BM_ProcessStatus", update.PlannedArtifact!.Name);
        Assert.Equal(15, plan.Changes.Count(
            change => change.ChangeKind == GenerationChangeKind.Unchanged));
    }

    [Fact]
    public void ChangedSymbolStem_PlansRenamesWithStableGuids()
    {
        using var temporary = new TemporaryDirectory();
        var originalPlan = BuildPlan(temporary.Path, ParseProject());
        var originalGuids = originalPlan.Changes
            .Where(change => change.SourceModelId == "40000000-0000-4000-8000-000000000001")
            .ToDictionary(change => change.ArtifactKind, change => change.PlannedArtifact!.TwinCatGuid);
        Materialize(temporary.Path, originalPlan);

        var modified = ParseProject();
        modified["nodes"]![3]!["symbolStem"] = "ProcessRenamed";
        var plan = BuildPlan(temporary.Path, modified);
        var renames = plan.Changes
            .Where(change => change.ChangeKind == GenerationChangeKind.Rename)
            .ToArray();

        Assert.False(plan.HasConflicts);
        Assert.Equal(4, renames.Length);
        Assert.All(renames, rename => Assert.NotNull(rename.PreviousRelativePath));
        Assert.Equal(
            originalGuids,
            renames.ToDictionary(
                change => change.ArtifactKind,
                change => change.PlannedArtifact!.TwinCatGuid));
    }

    [Fact]
    public void DisabledArtifact_PlansDeleteOnlyForUnchangedManagedFile()
    {
        using var temporary = new TemporaryDirectory();
        Materialize(temporary.Path, BuildPlan(temporary.Path, ParseProject()));
        var modified = ParseProject();
        modified["nodes"]![3]!["generate"]!["statusType"] = false;

        var plan = BuildPlan(temporary.Path, modified);

        Assert.False(plan.HasConflicts);
        var deletion = Assert.Single(
            plan.Changes,
            change => change.ChangeKind == GenerationChangeKind.Delete);
        Assert.Equal(
            "Generated/Application/Machine/Process Unit/ST_BM_ProcessStatus.TcDUT",
            deletion.RelativePath);
        Assert.Null(deletion.PlannedArtifact);
    }

    [Fact]
    public void ManuallyChangedManagedFile_IsConflict()
    {
        using var temporary = new TemporaryDirectory();
        var initialPlan = BuildPlan(temporary.Path, ParseProject());
        Materialize(temporary.Path, initialPlan);
        var target = Resolve(
            temporary.Path,
            "Generated/Application/Machine/Process Unit/ST_BM_ProcessStatus.TcDUT");
        File.AppendAllText(target, "manual change", new UTF8Encoding(false));

        var plan = BuildPlan(temporary.Path, ParseProject());

        Assert.True(plan.HasConflicts);
        Assert.Equal(GenerationChangeKind.Conflict, plan.Manifest.ChangeKind);
        var conflict = Assert.Single(
            plan.Changes,
            change => change.ChangeKind == GenerationChangeKind.Conflict);
        Assert.Contains("changed outside", conflict.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnmanagedOccupiedTarget_IsConflict()
    {
        using var temporary = new TemporaryDirectory();
        var target = Resolve(
            temporary.Path,
            "Generated/Application/Machine/Process Unit/ST_BM_ProcessStatus.TcDUT");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, "foreign file", new UTF8Encoding(false));

        var plan = BuildPlan(temporary.Path, ParseProject());

        Assert.True(plan.HasConflicts);
        var conflict = Assert.Single(
            plan.Changes,
            change => change.ChangeKind == GenerationChangeKind.Conflict);
        Assert.Equal(
            "Generated/Application/Machine/Process Unit/ST_BM_ProcessStatus.TcDUT",
            conflict.RelativePath);
        Assert.Contains("not managed", conflict.Message, StringComparison.Ordinal);

        File.Delete(target);
        Directory.CreateDirectory(target);

        var directoryPlan = BuildPlan(temporary.Path, ParseProject());
        Assert.True(directoryPlan.HasConflicts);
        var directoryConflict = Assert.Single(
            directoryPlan.Changes,
            change => change.ChangeKind == GenerationChangeKind.Conflict);
        Assert.Equal(
            "Generated/Application/Machine/Process Unit/ST_BM_ProcessStatus.TcDUT",
            directoryConflict.RelativePath);
    }

    [Fact]
    public void InvalidManifest_BlocksSafeComparison()
    {
        using var temporary = new TemporaryDirectory();
        var manifestPath = Resolve(
            temporary.Path,
            "Generated/etab-generation-manifest.json");
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        File.WriteAllText(manifestPath, "{ invalid", new UTF8Encoding(false));

        var plan = BuildPlan(temporary.Path, ParseProject());

        Assert.True(plan.HasConflicts);
        Assert.Contains(plan.Issues, issue => issue.Code == "MANIFEST_INVALID");
        Assert.All(plan.Changes, change => Assert.Equal(GenerationChangeKind.Conflict, change.ChangeKind));
    }

    [Fact]
    public void EscapingGeneratedRoot_IsRejectedBeforeComparison()
    {
        using var temporary = new TemporaryDirectory();
        var project = ParseProject();
        project["project"]!["generation"]!["generatedRoot"] = "../Outside";

        var plan = BuildPlan(temporary.Path, project);

        Assert.True(plan.HasConflicts);
        Assert.Contains(plan.Issues, issue => issue.Code == "OUTPUT_ROOT_INVALID");
        Assert.False(Directory.Exists(Path.Combine(temporary.Path, "Generated")));
    }

    [Fact]
    public void SemanticModelHash_IgnoresLayoutAndCanonicalInputOrder()
    {
        var original = ParseProject();
        var reordered = original.DeepClone().AsObject();
        reordered["layout"]!["nodes"]![0]!["x"] = 4321.0;
        Reverse(reordered["nodes"]!.AsArray());
        Reverse(reordered["relations"]!.AsArray());
        foreach (var node in reordered["nodes"]!.AsArray())
        {
            Reverse(node!["commands"]!.AsArray());
        }

        var originalHash = SemanticModelHasher.Compute(Validate(original));
        var reorderedHash = SemanticModelHasher.Compute(Validate(reordered));

        Assert.Equal(originalHash, reorderedHash);
    }

    private GenerationPlan BuildPlan(string root, JsonObject projectJson)
    {
        var project = Validate(projectJson);
        return _planner.Build(root, project, _generator.Generate(project));
    }

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

    private static void Materialize(string root, GenerationPlan plan)
    {
        Assert.False(plan.HasConflicts);

        foreach (var change in plan.Changes.Where(
                     change => change.PlannedArtifact is not null))
        {
            var target = Resolve(root, change.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(
                target,
                change.PlannedArtifact!.Content,
                new UTF8Encoding(false));
        }

        var manifestPath = Resolve(root, plan.Manifest.RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        File.WriteAllText(
            manifestPath,
            plan.Manifest.ProposedContent,
            new UTF8Encoding(false));
    }

    private static string Resolve(string root, string relativePath) =>
        Path.GetFullPath(
            relativePath.Replace('/', Path.DirectorySeparatorChar),
            root);

    private static JsonObject ParseProject() =>
        JsonNode.Parse(ValidProjectJson)!.AsObject();

    private static void Reverse(JsonArray array)
    {
        var items = array.Select(item => item?.DeepClone()).Reverse().ToArray();
        array.Clear();

        foreach (var item in items)
        {
            array.Add(item);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private static readonly string TestRoot = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "etab-engineering-tests");

        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(TestRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (!Directory.Exists(Path))
            {
                return;
            }

            var resolvedPath = System.IO.Path.GetFullPath(Path);
            var resolvedRoot = System.IO.Path.GetFullPath(TestRoot) +
                               System.IO.Path.DirectorySeparatorChar;
            if (!resolvedPath.StartsWith(resolvedRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Refusing to remove a directory outside the test root.");
            }

            Directory.Delete(resolvedPath, recursive: true);
        }
    }
}
