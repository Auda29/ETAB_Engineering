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

    private static readonly string IntegrationProjectJson =
        File.ReadAllText(Path.Combine(FixtureDirectory, "BrushMachine.integration.etab.json"));

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
        Assert.Equal(16, generatedCompiles.Length);
        Assert.Single(
            document.Descendants(ns + "PlaceholderReference"),
            element => (string?)element.Attribute("Include") == "ETAB");
        Assert.Single(
            document.Descendants(ns + "PlaceholderResolution"),
            element => (string?)element.Attribute("Include") == "ETAB");

        var integrationManifest = ReadIntegrationManifest(temporary.Path);
        Assert.Equal(16, integrationManifest.ManagedCompileIncludes.Count);
        Assert.Equal(7, integrationManifest.ManagedFolderIncludes.Count);
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
    public void EnabledProgramCallStructure_IsManagedAsProjectCompileEntry()
    {
        using var temporary = new TemporaryDirectory();
        WriteProjectFile(temporary.Path, MinimalProject());
        var projectJson = ParseProject();
        projectJson["project"]!["generation"]!["programCallStructure"] = true;

        var execution = _executor.Execute(BuildPlan(temporary.Path, Validate(projectJson)));

        Assert.True(execution.Success, FormatIssues(execution));
        var expected = "Generated\\POUs\\PRG_BM_Generated.TcPOU";
        var document = XDocument.Load(Path.Combine(temporary.Path, ProjectFileName));
        var ns = XNamespace.Get(MsBuildNamespace);
        Assert.Single(
            document.Descendants(ns + "Compile"),
            element => (string?)element.Attribute("Include") == expected);
        Assert.Contains(expected, ReadIntegrationManifest(temporary.Path).ManagedCompileIncludes);
    }

    [Fact]
    public void RuntimeExecution_AssignsGeneratedProgramToDetectedTaskAndBecomesNoOp()
    {
        using var temporary = new TemporaryDirectory();
        WriteProjectFile(
            temporary.Path,
            MinimalProject(taskIncludes: ["PlcTask.TcTTO"]));
        WriteTaskFile(temporary.Path, "PlcTask.TcTTO", ["MAIN", "HandwrittenProgram"]);
        var project = Validate(EnableRuntimeExecution(ParseProject()));

        var plan = BuildPlan(temporary.Path, project);

        Assert.False(plan.HasConflicts, FormatIssues(plan));
        Assert.Equal(GenerationChangeKind.Update, plan.TaskFile!.ChangeKind);
        Assert.Equal("PlcTask.TcTTO", plan.TaskFile.RelativePath);
        Assert.Contains("add PRG_BM_Generated to task PlcTask", plan.TaskFile.Message);
        Assert.True(_executor.Execute(plan).Success);

        var task = XDocument.Load(Path.Combine(temporary.Path, "PlcTask.TcTTO"));
        Assert.Equal(
            ["MAIN", "HandwrittenProgram", "PRG_BM_Generated"],
            task.Descendants("PouCall").Select(call => call.Element("Name")!.Value).ToArray());
        var manifest = ReadIntegrationManifest(temporary.Path);
        Assert.Equal("PlcTask.TcTTO", manifest.ManagedTaskPouCall!.TaskFile);
        Assert.Equal("PRG_BM_Generated", manifest.ManagedTaskPouCall.ProgramName);

        var repeated = BuildPlan(temporary.Path, project);
        Assert.False(repeated.HasConflicts, FormatIssues(repeated));
        Assert.Equal(GenerationChangeKind.Unchanged, repeated.TaskFile!.ChangeKind);
        Assert.True(_executor.Execute(repeated).Success);
    }

    [Fact]
    public void DisablingRuntimeExecution_RemovesOnlyManagedTaskCall()
    {
        using var temporary = new TemporaryDirectory();
        WriteProjectFile(
            temporary.Path,
            MinimalProject(taskIncludes: ["PlcTask.TcTTO"]));
        WriteTaskFile(temporary.Path, "PlcTask.TcTTO", ["MAIN", "HandwrittenProgram"]);
        var enabledJson = EnableRuntimeExecution(ParseProject());
        Assert.True(_executor.Execute(BuildPlan(
            temporary.Path,
            Validate(enabledJson))).Success);

        enabledJson["project"]!["generation"]!["runtimeExecution"] = false;
        enabledJson["project"]!["generation"]!["programCallStructure"] = false;
        var disabledPlan = BuildPlan(temporary.Path, Validate(enabledJson));

        Assert.False(disabledPlan.HasConflicts, FormatIssues(disabledPlan));
        Assert.Equal(GenerationChangeKind.Update, disabledPlan.TaskFile!.ChangeKind);
        Assert.Contains("remove runtime call PRG_BM_Generated", disabledPlan.TaskFile.Message);
        Assert.True(_executor.Execute(disabledPlan).Success);
        var calls = XDocument.Load(Path.Combine(temporary.Path, "PlcTask.TcTTO"))
            .Descendants("PouCall")
            .Select(call => call.Element("Name")!.Value)
            .ToArray();
        Assert.Equal(["MAIN", "HandwrittenProgram"], calls);
        Assert.Null(ReadIntegrationManifest(temporary.Path).ManagedTaskPouCall);
    }

    [Fact]
    public void RuntimeExecution_SelectsUniqueTaskCallingMain()
    {
        using var temporary = new TemporaryDirectory();
        WriteProjectFile(
            temporary.Path,
            MinimalProject(taskIncludes: ["AuxTask.TcTTO", "PlcTask.TcTTO"]));
        WriteTaskFile(temporary.Path, "AuxTask.TcTTO", ["Background"]);
        WriteTaskFile(temporary.Path, "PlcTask.TcTTO", ["MAIN"]);

        var plan = BuildPlan(
            temporary.Path,
            Validate(EnableRuntimeExecution(ParseProject())));

        Assert.False(plan.HasConflicts, FormatIssues(plan));
        Assert.Equal("PlcTask.TcTTO", plan.TaskFile!.RelativePath);
        Assert.DoesNotContain("PRG_BM_Generated", File.ReadAllText(
            Path.Combine(temporary.Path, "AuxTask.TcTTO")));
    }

    [Fact]
    public void ExistingRuntimeCallWithDifferentCase_IsPreservedAndNotDuplicated()
    {
        using var temporary = new TemporaryDirectory();
        WriteProjectFile(
            temporary.Path,
            MinimalProject(taskIncludes: ["PlcTask.TcTTO"]));
        WriteTaskFile(
            temporary.Path,
            "PlcTask.TcTTO",
            ["MAIN", "prg_bm_generated"]);

        var plan = BuildPlan(
            temporary.Path,
            Validate(EnableRuntimeExecution(ParseProject())));

        Assert.False(plan.HasConflicts, FormatIssues(plan));
        Assert.Equal(GenerationChangeKind.Unchanged, plan.TaskFile!.ChangeKind);
        Assert.Single(
            XDocument.Parse(plan.TaskFile.ProposedContent).Descendants("PouCall"),
            call => string.Equals(
                call.Element("Name")?.Value,
                "PRG_BM_Generated",
                StringComparison.OrdinalIgnoreCase));
        Assert.Null(ReadProposedIntegrationManifest(plan).ManagedTaskPouCall);
    }

    [Fact]
    public void TaskChangedAfterPreview_IsRejectedBeforeWritingArtifacts()
    {
        using var temporary = new TemporaryDirectory();
        WriteProjectFile(
            temporary.Path,
            MinimalProject(taskIncludes: ["PlcTask.TcTTO"]));
        WriteTaskFile(temporary.Path, "PlcTask.TcTTO", ["MAIN"]);
        var plan = BuildPlan(
            temporary.Path,
            Validate(EnableRuntimeExecution(ParseProject())));
        File.AppendAllText(
            Path.Combine(temporary.Path, "PlcTask.TcTTO"),
            " ",
            new UTF8Encoding(false));

        var execution = _executor.Execute(plan);

        Assert.False(execution.Success);
        Assert.Contains(execution.Issues, issue => issue.Code == "MANAGED_FILE_CHANGED");
        Assert.False(Directory.Exists(Path.Combine(temporary.Path, "Generated")));
    }

    [Fact]
    public void FailureAfterTaskUpdate_RollsBackTaskProjectAndGeneratedFiles()
    {
        using var temporary = new TemporaryDirectory();
        var originalProject = MinimalProject(taskIncludes: ["PlcTask.TcTTO"]);
        const string originalTask = """
<?xml version="1.0" encoding="utf-8"?>
<TcPlcObject Version="1.1.0.1">
  <Task Name="PlcTask" Id="{10000000-0000-4000-8000-000000000001}">
    <PouCall>
      <Name>MAIN</Name>
    </PouCall>
    <TaskFBGuid>{10000000-0000-4000-8000-000000000002}</TaskFBGuid>
  </Task>
</TcPlcObject>
""";
        WriteProjectFile(temporary.Path, originalProject);
        File.WriteAllText(
            Path.Combine(temporary.Path, "PlcTask.TcTTO"),
            originalTask,
            new UTF8Encoding(false));
        var plan = BuildPlan(
            temporary.Path,
            Validate(EnableRuntimeExecution(ParseProject())));
        var taskOperation = plan.Changes.Count(
            change => change.ChangeKind != GenerationChangeKind.Unchanged) + 2;
        var failingExecutor = new GenerationExecutor(operation =>
        {
            if (operation == taskOperation)
            {
                throw new InvalidOperationException("Injected task transaction failure.");
            }
        });

        var execution = failingExecutor.Execute(plan);

        Assert.False(execution.Success);
        Assert.Equal(
            originalProject,
            File.ReadAllText(Path.Combine(temporary.Path, ProjectFileName), Encoding.UTF8));
        Assert.Equal(
            originalTask,
            File.ReadAllText(Path.Combine(temporary.Path, "PlcTask.TcTTO"), Encoding.UTF8));
        var generatedRoot = Path.Combine(temporary.Path, "Generated");
        Assert.Empty(Directory.GetFiles(generatedRoot, "*", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetDirectories(generatedRoot, ".etab-*"));
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
    public void ExistingCompiledIecObjectWithGeneratedName_BlocksAllWrites()
    {
        using var temporary = new TemporaryDirectory();
        const string existingInclude = "DUTs\\Commands\\LegacyMotionCommand.TcDUT";
        WriteProjectFile(
            temporary.Path,
            MinimalProject(additionalCompileInclude: existingInclude));
        var existingPath = Path.Combine(
            temporary.Path,
            existingInclude.Replace('\\', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(existingPath)!);
        File.WriteAllText(
            existingPath,
            """
<?xml version="1.0" encoding="utf-8"?>
<TcPlcObject Version="1.1.0.1">
  <DUT Name="e_bm_motioncommand" Id="{12345678-1234-4123-8123-123456789abc}">
    <Declaration><![CDATA[TYPE e_bm_motioncommand : (NONE := 0); END_TYPE]]></Declaration>
  </DUT>
</TcPlcObject>
""",
            new UTF8Encoding(false));
        var before = SnapshotFiles(temporary.Path);

        var plan = BuildPlan(temporary.Path, Validate(ParseProject()));
        var execution = _executor.Execute(plan);

        Assert.True(plan.HasConflicts);
        var issue = Assert.Single(
            plan.Issues,
            item => item.Code == "PLC_OBJECT_NAME_CONFLICT");
        Assert.Equal(existingInclude, issue.Path);
        Assert.Contains("E_BM_MotionCommand", issue.Message, StringComparison.Ordinal);
        Assert.False(execution.Success);
        Assert.Equal(before, SnapshotFiles(temporary.Path));
    }

    [Fact]
    public void IntegrationModel_PreservesExternalDutAndGeneratesOnlyOwnedArtifacts()
    {
        using var temporary = new TemporaryDirectory();
        const string existingInclude = "DUTs\\Commands\\E_BM_MotionCommand.TcDUT";
        WriteProjectFile(
            temporary.Path,
            MinimalProject(additionalCompileInclude: existingInclude));
        var existingPath = Path.Combine(
            temporary.Path,
            existingInclude.Replace('\\', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(existingPath)!);
        File.WriteAllText(
            existingPath,
            """
<?xml version="1.0" encoding="utf-8"?>
<TcPlcObject Version="1.1.0.1">
  <DUT Name="E_BM_MotionCommand" Id="{12345678-1234-4123-8123-123456789abc}">
    <Declaration><![CDATA[TYPE E_BM_MotionCommand : (NoAction := 0); END_TYPE]]></Declaration>
  </DUT>
</TcPlcObject>
""",
            new UTF8Encoding(false));
        var project = Validate(JsonNode.Parse(IntegrationProjectJson)!.AsObject());

        var plan = BuildPlan(temporary.Path, project);
        var execution = _executor.Execute(plan);

        Assert.False(plan.HasConflicts, FormatIssues(plan));
        Assert.Equal(9, plan.Changes.Count);
        Assert.True(execution.Success, FormatIssues(execution));
        Assert.Equal(9, execution.Created);
        Assert.Equal(
            "E_BM_MotionCommand",
            XDocument.Load(existingPath).Descendants("DUT").Single().Attribute("Name")?.Value);
        Assert.DoesNotContain(
            existingInclude,
            ReadIntegrationManifest(temporary.Path).ManagedCompileIncludes);
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

    private static JsonObject EnableRuntimeExecution(JsonObject project)
    {
        project["project"]!["generation"]!["runtimeExecution"] = true;
        foreach (var node in project["nodes"]!.AsArray())
        {
            if (node!["kind"]!.GetValue<string>() is "applicationUnit" or "commandUnit")
            {
                node["generate"]!["instance"] = true;
                node["generate"]!["callInProgram"] = true;
            }
        }
        return project;
    }

    private static ProjectIntegrationManifest ReadIntegrationManifest(string root) =>
        ProjectIntegrationManifestSerializer.Deserialize(File.ReadAllText(Path.Combine(
            root,
            "Generated",
            ProjectIntegrationManifestSerializer.FileName)));

    private static ProjectIntegrationManifest ReadProposedIntegrationManifest(
        GenerationPlan plan) =>
        ProjectIntegrationManifestSerializer.Deserialize(
            plan.ProjectIntegrationManifest!.ProposedContent);

    private static void WriteProjectFile(string root, string content)
    {
        File.WriteAllText(
            Path.Combine(root, ProjectFileName),
            content,
            new UTF8Encoding(false));
        var existingPath = Path.Combine(root, "Application", "Existing.TcPOU");
        Directory.CreateDirectory(Path.GetDirectoryName(existingPath)!);
        File.WriteAllText(
            existingPath,
            """
<?xml version="1.0" encoding="utf-8"?>
<TcPlcObject Version="1.1.0.1">
  <POU Name="Existing" Id="{87654321-4321-4321-8321-cba987654321}" SpecialFunc="None">
    <Declaration><![CDATA[PROGRAM Existing]]></Declaration>
    <Implementation><ST><![CDATA[]]></ST></Implementation>
  </POU>
</TcPlcObject>
""",
            new UTF8Encoding(false));
    }

    private static void WriteTaskFile(
        string root,
        string relativePath,
        IReadOnlyList<string> programs)
    {
        var calls = string.Join(
            "\r\n",
            programs.Select(program =>
                $"    <PouCall>\r\n      <Name>{program}</Name>\r\n    </PouCall>"));
        var content = $$"""
<?xml version="1.0" encoding="utf-8"?>
<TcPlcObject Version="1.1.0.1">
  <Task Name="{{Path.GetFileNameWithoutExtension(relativePath)}}" Id="{10000000-0000-4000-8000-000000000001}">
{{calls}}
    <TaskFBGuid>{10000000-0000-4000-8000-000000000002}</TaskFBGuid>
  </Task>
</TcPlcObject>
""";
        File.WriteAllText(
            Path.Combine(root, relativePath),
            content,
            new UTF8Encoding(false));
    }

    private static string MinimalProject(
        bool includeCompatibleLibrary = false,
        bool includeIncompatibleLibrary = false,
        string? additionalCompileInclude = null,
        IReadOnlyList<string>? taskIncludes = null)
    {
        var compileIncludes = (additionalCompileInclude is null
                ? []
                : new[] { additionalCompileInclude })
            .Concat(taskIncludes ?? [])
            .ToArray();
        var compile = string.Concat(compileIncludes.Select(include =>
            $"\r\n    <Compile Include=\"{include}\">\r\n      <SubType>Code</SubType>\r\n    </Compile>"));
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
