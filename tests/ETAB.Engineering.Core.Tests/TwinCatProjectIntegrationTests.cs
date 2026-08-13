using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using ETAB.Engineering.Core.Execution;
using ETAB.Engineering.Core.Generation;
using ETAB.Engineering.Core.Model;
using ETAB.Engineering.Core.Planning;
using ETAB.Engineering.Core.ProjectIntegration;
using ETAB.Engineering.Core.Validation;
using Xunit;

namespace ETAB.Engineering.Core.Tests;

public sealed class TwinCatProjectIntegrationTests
{
    private const string ProjectFileName = "AutomationBase_Beispiel.plcproj";
    private const string MsBuildNamespace = "http://schemas.microsoft.com/developer/msbuild/2003";

    private static readonly string FixtureDirectory =
        Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private static readonly string SchemaJson =
        File.ReadAllText(Path.Combine(FixtureDirectory, "etab-project.schema.json"));

    private static readonly string ValidProjectJson =
        File.ReadAllText(Path.Combine(FixtureDirectory, "BrushMachine.reference.etab.json"));

    private readonly ProjectValidator _validator = new();
    private readonly ArtifactPreviewGenerator _generator = new();
    private readonly GenerationPlanBuilder _artifactPlanner = new();
    private readonly TwinCatProjectIntegrationPlanBuilder _integrationPlanner = new();
    private readonly GenerationExecutor _executor = new();

    [Fact]
    public void InitialIntegration_ManagesOnlyAddedEntriesAndBecomesNoOp()
    {
        using var temporary = new TemporaryDirectory();
        WriteProjectFile(temporary.Path, MinimalProject());
        var project = Validate(ParseProject());
        var plan = BuildPlan(temporary.Path, project);

        Assert.False(plan.HasConflicts, FormatIssues(plan));
        Assert.Equal(GenerationChangeKind.Update, plan.ProjectFile!.ChangeKind);
        Assert.Equal(
            GenerationChangeKind.Create,
            plan.ProjectIntegrationManifest!.ChangeKind);

        var firstExecution = _executor.Execute(plan);

        Assert.True(firstExecution.Success, FormatIssues(firstExecution));
        var projectPath = Path.Combine(temporary.Path, ProjectFileName);
        var document = XDocument.Load(projectPath, LoadOptions.PreserveWhitespace);
        var ns = XNamespace.Get(MsBuildNamespace);
        var generatedCompiles = document.Descendants(ns + "Compile")
            .Select(element => (string?)element.Attribute("Include"))
            .Where(include => include?.StartsWith("Generated\\", StringComparison.Ordinal) == true)
            .ToArray();
        Assert.Equal(14, generatedCompiles.Length);
        Assert.Single(
            document.Descendants(ns + "PlaceholderReference"),
            element => (string?)element.Attribute("Include") == "ETAB");
        Assert.Single(
            document.Descendants(ns + "PlaceholderResolution"),
            element => (string?)element.Attribute("Include") == "ETAB");

        var integrationManifest = ReadIntegrationManifest(temporary.Path);
        Assert.Equal(14, integrationManifest.ManagedCompileIncludes.Count);
        Assert.Equal(6, integrationManifest.ManagedFolderIncludes.Count);
        Assert.NotNull(integrationManifest.ManagedPlaceholderReference);
        Assert.NotNull(integrationManifest.ManagedPlaceholderResolution);

        var before = SnapshotFiles(temporary.Path);
        var repeatedPlan = BuildPlan(temporary.Path, project);
        Assert.False(repeatedPlan.HasConflicts, FormatIssues(repeatedPlan));
        Assert.Equal(
            File.ReadAllText(projectPath, Encoding.UTF8),
            repeatedPlan.ProjectFile!.ProposedContent);
        Assert.Equal(GenerationChangeKind.Unchanged, repeatedPlan.ProjectFile!.ChangeKind);
        Assert.Equal(
            GenerationChangeKind.Unchanged,
            repeatedPlan.ProjectIntegrationManifest!.ChangeKind);
        Assert.Equal(GenerationChangeKind.Unchanged, repeatedPlan.Manifest.ChangeKind);
        Assert.All(
            repeatedPlan.Changes,
            change => Assert.Equal(GenerationChangeKind.Unchanged, change.ChangeKind));

        var secondExecution = _executor.Execute(repeatedPlan);

        Assert.True(secondExecution.Success, FormatIssues(secondExecution));
        Assert.Equal(before, SnapshotFiles(temporary.Path));
    }

    [Fact]
    public void CompatibleExistingLibraryReference_RemainsUnmanaged()
    {
        using var temporary = new TemporaryDirectory();
        WriteProjectFile(temporary.Path, MinimalProject(includeCompatibleLibrary: true));

        var execution = _executor.Execute(BuildPlan(temporary.Path, Validate(ParseProject())));

        Assert.True(execution.Success, FormatIssues(execution));
        var manifest = ReadIntegrationManifest(temporary.Path);
        Assert.Null(manifest.ManagedPlaceholderReference);
        Assert.Null(manifest.ManagedPlaceholderResolution);
    }

    [Fact]
    public void ExistingUnmanagedCompileEntry_IsPreservedAndNotClaimed()
    {
        using var temporary = new TemporaryDirectory();
        const string existingGenerated =
            "Generated\\DUTs\\Status\\ST_BM_MachineStatus.TcDUT";
        WriteProjectFile(
            temporary.Path,
            MinimalProject(additionalCompileInclude: existingGenerated));

        var execution = _executor.Execute(BuildPlan(temporary.Path, Validate(ParseProject())));

        Assert.True(execution.Success, FormatIssues(execution));
        var manifest = ReadIntegrationManifest(temporary.Path);
        Assert.DoesNotContain(existingGenerated, manifest.ManagedCompileIncludes);
        var document = XDocument.Load(Path.Combine(temporary.Path, ProjectFileName));
        var ns = XNamespace.Get(MsBuildNamespace);
        Assert.Single(
            document.Descendants(ns + "Compile"),
            element => (string?)element.Attribute("Include") == existingGenerated);
    }

    [Fact]
    public void ManagedArtifactRename_UpdatesCompileEntriesAndFolders()
    {
        using var temporary = new TemporaryDirectory();
        WriteProjectFile(temporary.Path, MinimalProject());
        Assert.True(_executor.Execute(
            BuildPlan(temporary.Path, Validate(ParseProject()))).Success);
        var modifiedJson = ParseProject();
        modifiedJson["nodes"]![3]!["symbolStem"] = "ProcessRenamed";
        var modifiedProject = Validate(modifiedJson);

        var plan = BuildPlan(temporary.Path, modifiedProject);

        Assert.False(plan.HasConflicts, FormatIssues(plan));
        Assert.Equal(GenerationChangeKind.Update, plan.ProjectFile!.ChangeKind);
        var execution = _executor.Execute(plan);
        Assert.True(execution.Success, FormatIssues(execution));
        var document = XDocument.Load(Path.Combine(temporary.Path, ProjectFileName));
        var ns = XNamespace.Get(MsBuildNamespace);
        var includes = document.Descendants(ns + "Compile")
            .Select(element => (string?)element.Attribute("Include"))
            .Where(include => include is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "Generated\\DUTs\\Status\\ST_BM_ProcessStatus.TcDUT",
            includes);
        Assert.Contains(
            "Generated\\DUTs\\Status\\ST_BM_ProcessRenamedStatus.TcDUT",
            includes);
    }

    [Fact]
    public void ManuallyChangedManagedCompileEntry_BlocksAllWrites()
    {
        using var temporary = new TemporaryDirectory();
        WriteProjectFile(temporary.Path, MinimalProject());
        var project = Validate(ParseProject());
        Assert.True(_executor.Execute(BuildPlan(temporary.Path, project)).Success);
        var projectPath = Path.Combine(temporary.Path, ProjectFileName);
        var document = XDocument.Load(projectPath, LoadOptions.PreserveWhitespace);
        var ns = XNamespace.Get(MsBuildNamespace);
        var managed = document.Descendants(ns + "Compile").First(
            element => ((string?)element.Attribute("Include"))?.StartsWith(
                "Generated\\",
                StringComparison.Ordinal) == true);
        managed.Add(new XElement(ns + "DependentUpon", "foreign"));
        document.Save(projectPath, SaveOptions.DisableFormatting);
        var before = SnapshotFiles(temporary.Path);

        var plan = BuildPlan(temporary.Path, project);
        var execution = _executor.Execute(plan);

        Assert.True(plan.HasConflicts);
        Assert.Contains(
            plan.Issues,
            issue => issue.Code == "PLC_COMPILE_MANAGED_CHANGED");
        Assert.False(execution.Success);
        Assert.Equal(before, SnapshotFiles(temporary.Path));
    }

    [Fact]
    public void ProjectChangedAfterPreview_IsRejectedBeforeWritingArtifacts()
    {
        using var temporary = new TemporaryDirectory();
        WriteProjectFile(temporary.Path, MinimalProject());
        var plan = BuildPlan(temporary.Path, Validate(ParseProject()));
        var projectPath = Path.Combine(temporary.Path, ProjectFileName);
        File.AppendAllText(projectPath, " ", new UTF8Encoding(false));

        var execution = _executor.Execute(plan);

        Assert.False(execution.Success);
        Assert.Contains(
            execution.Issues,
            issue => issue.Code == "MANAGED_FILE_CHANGED");
        Assert.False(Directory.Exists(Path.Combine(temporary.Path, "Generated")));
    }

    [Fact]
    public void FailureAfterProjectUpdate_RollsBackProjectAndGeneratedFiles()
    {
        using var temporary = new TemporaryDirectory();
        var originalProjectFile = MinimalProject();
        WriteProjectFile(temporary.Path, originalProjectFile);
        var plan = BuildPlan(temporary.Path, Validate(ParseProject()));
        var projectOperation = plan.Changes.Count(
            change => change.ChangeKind != GenerationChangeKind.Unchanged) + 1;
        var failingExecutor = new GenerationExecutor(operation =>
        {
            if (operation == projectOperation)
            {
                throw new InvalidOperationException("Injected project transaction failure.");
            }
        });

        var execution = failingExecutor.Execute(plan);

        Assert.False(execution.Success);
        Assert.Contains(
            execution.Issues,
            issue => issue.Code == "GENERATION_WRITE_FAILED");
        Assert.Equal(
            originalProjectFile,
            File.ReadAllText(Path.Combine(temporary.Path, ProjectFileName), Encoding.UTF8));
        var generatedRoot = Path.Combine(temporary.Path, "Generated");
        Assert.Empty(Directory.GetFiles(generatedRoot, "*", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetDirectories(generatedRoot, ".etab-*"));
    }

    [Fact]
    public void IncompatibleExistingLibraryReference_IsConflict()
    {
        using var temporary = new TemporaryDirectory();
        WriteProjectFile(temporary.Path, MinimalProject(includeIncompatibleLibrary: true));

        var plan = BuildPlan(temporary.Path, Validate(ParseProject()));

        Assert.True(plan.HasConflicts);
        Assert.Contains(
            plan.Issues,
            issue => issue.Code == "PLC_LIBRARY_REFERENCE_CONFLICT");
    }

    private GenerationPlan BuildPlan(string root, EtabProjectDocument project)
    {
        var preview = _generator.Generate(project);
        var artifactPlan = _artifactPlanner.Build(root, project, preview);
        return _integrationPlanner.Build(artifactPlan, project, preview);
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

    private static JsonObject ParseProject() =>
        JsonNode.Parse(ValidProjectJson)!.AsObject();

    private static ProjectIntegrationManifest ReadIntegrationManifest(string root) =>
        ProjectIntegrationManifestSerializer.Deserialize(File.ReadAllText(Path.Combine(
            root,
            "Generated",
            ProjectIntegrationManifestSerializer.FileName)));

    private static void WriteProjectFile(string root, string content) =>
        File.WriteAllText(
            Path.Combine(root, ProjectFileName),
            content,
            new UTF8Encoding(false));

    private static string MinimalProject(
        bool includeCompatibleLibrary = false,
        bool includeIncompatibleLibrary = false,
        string? additionalCompileInclude = null)
    {
        var compile = additionalCompileInclude is null
            ? string.Empty
            : $"\r\n    <Compile Include=\"{additionalCompileInclude}\">\r\n      <SubType>Code</SubType>\r\n    </Compile>";
        var library = includeCompatibleLibrary
            ? """
  <ItemGroup>
    <PlaceholderReference Include="ETAB">
      <DefaultResolution>EngineeringToolboxAutomationBase, * (NiklasW)</DefaultResolution>
      <Namespace>ETAB</Namespace>
    </PlaceholderReference>
  </ItemGroup>
  <ItemGroup>
    <PlaceholderResolution Include="ETAB">
      <Resolution>EngineeringToolboxAutomationBase, 0.1.0.3 (NiklasW)</Resolution>
    </PlaceholderResolution>
  </ItemGroup>
"""
            : includeIncompatibleLibrary
                ? """
  <ItemGroup>
    <PlaceholderReference Include="ETAB">
      <DefaultResolution>WrongLibrary, * (SomeoneElse)</DefaultResolution>
      <Namespace>ETAB</Namespace>
    </PlaceholderReference>
  </ItemGroup>
"""
                : string.Empty;

        return ("""
<?xml version="1.0" encoding="utf-8"?>
<Project DefaultTargets="Build" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <PropertyGroup>
    <SchemaVersion>2.0</SchemaVersion>
    <Name>AutomationBase_Beispiel</Name>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="Application\Existing.TcPOU">
      <SubType>Code</SubType>
    </Compile>{COMPILE}
  </ItemGroup>
  <ItemGroup>
    <Folder Include="Application" />
  </ItemGroup>
{LIBRARY}</Project>
""")
            .Replace("{COMPILE}", compile, StringComparison.Ordinal)
            .Replace("{LIBRARY}", library, StringComparison.Ordinal)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\n", "\r\n", StringComparison.Ordinal);
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

    private static string FormatIssues(GenerationPlan plan) =>
        string.Join(
            Environment.NewLine,
            plan.Issues.Select(issue => $"[{issue.Code}] {issue.Path}: {issue.Message}"));

    private static string FormatIssues(GenerationExecutionResult result) =>
        string.Join(
            Environment.NewLine,
            result.Issues.Select(issue => $"[{issue.Code}] {issue.Message}"));

    private sealed class TemporaryDirectory : IDisposable
    {
        private static readonly string TestRoot = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "etab-engineering-project-integration-tests");

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

            var resolved = System.IO.Path.GetFullPath(Path);
            var boundary = System.IO.Path.GetFullPath(TestRoot) +
                           System.IO.Path.DirectorySeparatorChar;
            if (!resolved.StartsWith(boundary, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Refusing to remove a directory outside the integration test root.");
            }

            var entries = new[] { new DirectoryInfo(resolved) }
                .Cast<FileSystemInfo>()
                .Concat(new DirectoryInfo(resolved)
                    .EnumerateFileSystemInfos("*", SearchOption.AllDirectories));
            if (entries.Any(entry => entry.Attributes.HasFlag(FileAttributes.ReparsePoint)))
            {
                throw new InvalidOperationException(
                    "Refusing to remove integration test data containing reparse points.");
            }

            Directory.Delete(resolved, recursive: true);
        }
    }
}
