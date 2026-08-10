using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using ETAB.Engineering.Core.Execution;
using ETAB.Engineering.Core.Generation;
using ETAB.Engineering.Core.Manifest;
using ETAB.Engineering.Core.Model;
using ETAB.Engineering.Core.Planning;
using ETAB.Engineering.Core.Validation;
using Xunit;

namespace ETAB.Engineering.Core.Tests;

public sealed class GenerationExecutorTests
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
    private readonly GenerationExecutor _executor = new();

    [Fact]
    public void InitialGenerate_WritesArtifactsAndManifestThenBecomesNoOp()
    {
        using var temporary = new TemporaryDirectory();
        var initialPlan = BuildPlan(temporary.Path, ParseProject());

        var firstExecution = _executor.Execute(initialPlan);

        Assert.True(firstExecution.Success, FormatIssues(firstExecution));
        Assert.Equal(14, firstExecution.Created);
        Assert.Equal(0, firstExecution.Updated);
        Assert.Equal(0, firstExecution.Renamed);
        Assert.Equal(0, firstExecution.Deleted);

        var generatedRoot = Path.Combine(temporary.Path, "Generated");
        Assert.Equal(
            15,
            Directory.GetFiles(generatedRoot, "*", SearchOption.AllDirectories).Length);
        Assert.Empty(Directory.GetDirectories(generatedRoot, ".etab-*"));

        var before = SnapshotFiles(generatedRoot);
        var synchronizedPlan = BuildPlan(temporary.Path, ParseProject());
        Assert.Equal(GenerationChangeKind.Unchanged, synchronizedPlan.Manifest.ChangeKind);
        Assert.All(
            synchronizedPlan.Changes,
            change => Assert.Equal(GenerationChangeKind.Unchanged, change.ChangeKind));

        var secondExecution = _executor.Execute(synchronizedPlan);
        var after = SnapshotFiles(generatedRoot);

        Assert.True(secondExecution.Success, FormatIssues(secondExecution));
        Assert.Equal(0, secondExecution.Created);
        Assert.Equal(0, secondExecution.Updated);
        Assert.Equal(0, secondExecution.Renamed);
        Assert.Equal(0, secondExecution.Deleted);
        Assert.Equal(before, after);

        var manifestJson = File.ReadAllText(
            Path.Combine(generatedRoot, GenerationManifestSerializer.FileName));
        var manifest = GenerationManifestSerializer.Deserialize(manifestJson);
        Assert.Equal(14, manifest.Artifacts.Count);

        var twinCatIds = new List<string>();
        foreach (var artifact in manifest.Artifacts)
        {
            var artifactPath = Resolve(temporary.Path, artifact.RelativePath);
            var bytes = File.ReadAllBytes(artifactPath);
            Assert.False(bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }));
            Assert.DoesNotContain("\r\n", Encoding.UTF8.GetString(bytes));
            Assert.Equal(artifact.ContentHash, ComputeFileHash(artifactPath));

            var document = XDocument.Load(artifactPath);
            twinCatIds.AddRange(
                document.Root!.DescendantsAndSelf()
                    .Attributes("Id")
                    .Select(attribute => attribute.Value));
        }

        Assert.Equal(
            twinCatIds.Count,
            twinCatIds.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void UpdateRenameAndDelete_AreAppliedAndBecomeSynchronized()
    {
        using var temporary = new TemporaryDirectory();
        var original = ParseProject();
        Assert.True(_executor.Execute(BuildPlan(temporary.Path, original)).Success);

        var modified = ParseProject();
        modified["nodes"]![3]!["statusPayload"]![0]!["name"] = "bExhaustReady";
        modified["nodes"]![1]!["symbolStem"] = "MotionRenamed";
        modified["nodes"]![2]!["generate"]!["statusType"] = false;
        var changedPlan = BuildPlan(temporary.Path, modified);

        Assert.Equal(
            1,
            changedPlan.Changes.Count(change => change.ChangeKind == GenerationChangeKind.Update));
        Assert.Equal(
            4,
            changedPlan.Changes.Count(change => change.ChangeKind == GenerationChangeKind.Rename));
        Assert.Equal(
            1,
            changedPlan.Changes.Count(change => change.ChangeKind == GenerationChangeKind.Delete));

        var execution = _executor.Execute(changedPlan);

        Assert.True(execution.Success, FormatIssues(execution));
        Assert.Equal(1, execution.Updated);
        Assert.Equal(4, execution.Renamed);
        Assert.Equal(1, execution.Deleted);
        Assert.False(File.Exists(Resolve(
            temporary.Path,
            "Generated/DUTs/Commands/E_BM_MotionCommand.TcDUT")));
        Assert.True(File.Exists(Resolve(
            temporary.Path,
            "Generated/DUTs/Commands/E_BM_MotionRenamedCommand.TcDUT")));
        Assert.False(File.Exists(Resolve(
            temporary.Path,
            "Generated/DUTs/Status/ST_BM_WorkpieceStatus.TcDUT")));

        var synchronizedPlan = BuildPlan(temporary.Path, modified);
        Assert.False(synchronizedPlan.HasConflicts);
        Assert.Equal(GenerationChangeKind.Unchanged, synchronizedPlan.Manifest.ChangeKind);
        Assert.All(
            synchronizedPlan.Changes,
            change => Assert.Equal(GenerationChangeKind.Unchanged, change.ChangeKind));
    }

    [Fact]
    public void Conflict_BlocksEveryPlannedWrite()
    {
        using var temporary = new TemporaryDirectory();
        Assert.True(_executor.Execute(BuildPlan(temporary.Path, ParseProject())).Success);
        var changedFile = Resolve(
            temporary.Path,
            "Generated/DUTs/Status/ST_BM_ProcessStatus.TcDUT");
        File.AppendAllText(changedFile, "manual change", new UTF8Encoding(false));

        var modified = ParseProject();
        modified["nodes"]![1]!["statusPayload"]![0]!["name"] = "bAxesHomed";
        var plan = BuildPlan(temporary.Path, modified);
        var before = SnapshotFiles(Path.Combine(temporary.Path, "Generated"));

        var execution = _executor.Execute(plan);
        var after = SnapshotFiles(Path.Combine(temporary.Path, "Generated"));

        Assert.True(plan.HasConflicts);
        Assert.False(execution.Success);
        Assert.Contains(execution.Issues, issue => issue.Code == "GENERATION_CONFLICT");
        Assert.Equal(before, after);
    }

    [Fact]
    public void TargetOccupiedAfterPreview_IsCaughtByExecutionPreflight()
    {
        using var temporary = new TemporaryDirectory();
        var plan = BuildPlan(temporary.Path, ParseProject());
        var occupiedTarget = Resolve(
            temporary.Path,
            "Generated/DUTs/Status/ST_BM_MachineStatus.TcDUT");
        Directory.CreateDirectory(Path.GetDirectoryName(occupiedTarget)!);
        File.WriteAllText(occupiedTarget, "foreign", new UTF8Encoding(false));

        var execution = _executor.Execute(plan);

        Assert.False(execution.Success);
        Assert.Contains(
            execution.Issues,
            issue => issue.Code == "TARGET_BECAME_OCCUPIED");
        Assert.Equal("foreign", File.ReadAllText(occupiedTarget));
        Assert.False(File.Exists(Resolve(
            temporary.Path,
            "Generated/etab-generation-manifest.json")));
        Assert.Single(
            Directory.GetFiles(
                Path.Combine(temporary.Path, "Generated"),
                "*",
                SearchOption.AllDirectories));
    }

    [Fact]
    public void FilesOutsideGeneratedRoot_AreNeverChanged()
    {
        using var temporary = new TemporaryDirectory();
        var userFile = Path.Combine(temporary.Path, "Application", "FB_User.TcPOU");
        Directory.CreateDirectory(Path.GetDirectoryName(userFile)!);
        File.WriteAllText(userFile, "user-owned", new UTF8Encoding(false));
        var beforeHash = ComputeFileHash(userFile);

        var execution = _executor.Execute(BuildPlan(temporary.Path, ParseProject()));

        Assert.True(execution.Success, FormatIssues(execution));
        Assert.Equal(beforeHash, ComputeFileHash(userFile));
        Assert.Equal("user-owned", File.ReadAllText(userFile));
    }

    [Fact]
    public void WriteFailureAfterPartialUpdate_RestoresEveryManagedFile()
    {
        using var temporary = new TemporaryDirectory();
        var original = ParseProject();
        Assert.True(_executor.Execute(BuildPlan(temporary.Path, original)).Success);

        var generatedRoot = Path.Combine(temporary.Path, "Generated");
        var before = SnapshotFiles(generatedRoot);
        var modified = ParseProject();
        modified["nodes"]![3]!["statusPayload"]![0]!["name"] = "bExhaustReady";
        modified["nodes"]![1]!["symbolStem"] = "MotionRenamed";
        var plan = BuildPlan(temporary.Path, modified);
        var failingExecutor = new GenerationExecutor(operationNumber =>
        {
            if (operationNumber == 2)
            {
                throw new InvalidOperationException("Injected transaction failure.");
            }
        });

        var execution = failingExecutor.Execute(plan);
        var after = SnapshotFiles(generatedRoot);

        Assert.False(execution.Success);
        Assert.Contains(
            execution.Issues,
            issue => issue.Code == "GENERATION_WRITE_FAILED" &&
                     issue.Message.Contains("Injected transaction failure.", StringComparison.Ordinal));
        Assert.DoesNotContain(
            execution.Issues,
            issue => issue.Code == "GENERATION_ROLLBACK_FAILED");
        Assert.Equal(before, after);
        Assert.Empty(Directory.GetDirectories(generatedRoot, ".etab-*"));
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

    private static SortedDictionary<string, string> SnapshotFiles(string root) =>
        Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(root, path).Replace('\\', '/'),
                ComputeFileHash,
                StringComparer.Ordinal)
            .ToSortedDictionary(StringComparer.Ordinal);

    private static string ComputeFileHash(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static string Resolve(string root, string relativePath) =>
        Path.GetFullPath(relativePath.Replace('/', Path.DirectorySeparatorChar), root);

    private static JsonObject ParseProject() =>
        JsonNode.Parse(ValidProjectJson)!.AsObject();

    private static string FormatIssues(GenerationExecutionResult result) =>
        string.Join(
            Environment.NewLine,
            result.Issues.Select(issue => $"[{issue.Code}] {issue.Message}"));

    private sealed class TemporaryDirectory : IDisposable
    {
        private static readonly string TestRoot = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "etab-engineering-executor-tests");

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
                throw new InvalidOperationException(
                    "Refusing to remove a directory outside the executor test root.");
            }

            EnsureNoReparsePoints(resolvedPath);
            Directory.Delete(resolvedPath, recursive: true);
        }

        private static void EnsureNoReparsePoints(string root)
        {
            var pending = new Stack<DirectoryInfo>();
            pending.Push(new DirectoryInfo(root));

            while (pending.Count > 0)
            {
                var directory = pending.Pop();
                if (directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    throw new InvalidOperationException(
                        $"Refusing to remove test directory containing reparse point '{directory.FullName}'.");
                }

                foreach (var entry in directory.EnumerateFileSystemInfos())
                {
                    if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        throw new InvalidOperationException(
                            $"Refusing to remove test directory containing reparse point '{entry.FullName}'.");
                    }

                    if (entry is DirectoryInfo childDirectory)
                    {
                        pending.Push(childDirectory);
                    }
                }
            }
        }
    }
}

internal static class DictionaryTestExtensions
{
    public static SortedDictionary<TKey, TValue> ToSortedDictionary<TKey, TValue>(
        this IDictionary<TKey, TValue> source,
        IComparer<TKey> comparer)
        where TKey : notnull
    {
        var result = new SortedDictionary<TKey, TValue>(comparer);
        foreach (var item in source)
        {
            result.Add(item.Key, item.Value);
        }

        return result;
    }
}
