using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace ETAB.Engineering.Service.Tests;

public sealed class EditorProjectServiceTests : IDisposable
{
    private readonly string testRoot;
    private readonly EditorProjectService service;

    public EditorProjectServiceTests()
    {
        testRoot = Path.Combine(
            Path.GetTempPath(),
            "etab-engineering-service-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);
        service = new EditorProjectService(
            testRoot,
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "etab-project.schema.json"));
    }

    [Fact]
    public void CreateNew_ReturnsValidMinimalMachineTemplateWithFreshStableIds()
    {
        var first = service.CreateNew();
        var second = service.CreateNew();

        Assert.True(first.Validation.IsValid);
        Assert.Equal("NewProject", first.Document["project"]!["name"]!.GetValue<string>());
        Assert.Equal("NEW", first.Document["project"]!["prefix"]!.GetValue<string>());
        Assert.Equal("0.1.0.3", first.Document["project"]!["etabLibrary"]!["version"]!.GetValue<string>());
        Assert.Single(first.Document["nodes"]!.AsArray());
        Assert.Equal("applicationUnit", first.Document["nodes"]![0]!["kind"]!.GetValue<string>());
        Assert.Equal(5, first.Document["nodes"]![0]!["commands"]!.AsArray().Count);
        var preview = service.Preview(first.Document, projectPath: null, testRoot);
        Assert.True(preview.Validation.IsValid);
        Assert.Equal(5, preview.Artifacts.Count);
        Assert.All(preview.Changes, change => Assert.Equal("create", change.ChangeKind));
        Assert.NotEqual(
            first.Document["project"]!["id"]!.GetValue<string>(),
            second.Document["project"]!["id"]!.GetValue<string>());
        Assert.NotEqual(
            first.Document["nodes"]![0]!["id"]!.GetValue<string>(),
            second.Document["nodes"]![0]!["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task OpenAsync_LoadsCompleteReferenceDocumentAndValidation()
    {
        var projectPath = CopyReferenceProject("BrushMachine.etab.json");

        var result = await service.OpenAsync(projectPath);

        Assert.True(result.Validation.IsValid);
        Assert.Equal(7, result.Document["nodes"]!.AsArray().Count);
        Assert.Equal(12, result.Document["relations"]!.AsArray().Count);
        Assert.Equal(Path.GetFullPath(projectPath), result.Path);
    }

    [Fact]
    public async Task SaveAsync_RoundTripsEveryJsonValueAndUsesUtf8WithoutBom()
    {
        var sourcePath = CopyReferenceProject("Source.etab.json");
        var opened = await service.OpenAsync(sourcePath);
        var targetPath = Path.Combine(testRoot, "RoundTrip.etab.json");

        var saved = await service.SaveAsync(targetPath, opened.Document);
        var reopened = await service.OpenAsync(targetPath);
        var bytes = await File.ReadAllBytesAsync(targetPath);

        Assert.True(saved.Validation.IsValid);
        Assert.True(reopened.Validation.IsValid);
        Assert.True(JsonNode.DeepEquals(opened.Document, reopened.Document));
        Assert.False(bytes.Length >= 3 &&
                     bytes[0] == 0xEF &&
                     bytes[1] == 0xBB &&
                     bytes[2] == 0xBF);
        Assert.DoesNotContain("\r\n", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public async Task SaveAsync_PreservesInvalidEditableDraftAndReturnsValidationIssues()
    {
        var sourcePath = CopyReferenceProject("Source.etab.json");
        var opened = await service.OpenAsync(sourcePath);
        opened.Document["project"]!.AsObject().Remove("name");
        var targetPath = Path.Combine(testRoot, "Draft.etab.json");

        var saved = await service.SaveAsync(targetPath, opened.Document);

        Assert.False(saved.Validation.IsValid);
        Assert.True(File.Exists(targetPath));
        Assert.Contains(saved.Validation.Issues, issue => issue.Path.StartsWith("/project", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Preview_UsesCoreGeneratorAndDoesNotWriteOutput()
    {
        var projectPath = CopyReferenceProject("BrushMachine.etab.json");
        var opened = await service.OpenAsync(projectPath);

        var preview = service.Preview(opened.Document, projectPath, testRoot);

        Assert.True(preview.Validation.IsValid);
        Assert.Equal(15, preview.Artifacts.Count);
        Assert.All(preview.Changes, change => Assert.Equal("create", change.ChangeKind));
        Assert.Equal("create", preview.Manifest!.ChangeKind);
        Assert.NotNull(preview.ConfirmationToken);
        Assert.False(Directory.Exists(Path.Combine(testRoot, "Generated")));
    }

    [Fact]
    public async Task Generate_AppliesExactlyConfirmedPreviewAndBecomesSynchronized()
    {
        var projectPath = CopyReferenceProject("BrushMachine.etab.json");
        var opened = await service.OpenAsync(projectPath);
        var preview = service.Preview(opened.Document, projectPath, testRoot);

        var generated = service.Generate(
            opened.Document,
            projectPath,
            testRoot,
            integrateProject: false,
            preview.ConfirmationToken!,
            confirmed: true);

        Assert.True(generated.Success);
        Assert.Equal(15, generated.Created);
        Assert.True(generated.ManifestChanged);
        Assert.False(generated.ProjectFileChanged);
        var repeated = service.Preview(opened.Document, projectPath, testRoot);
        Assert.All(repeated.Changes, change => Assert.Equal("unchanged", change.ChangeKind));
        Assert.Equal("unchanged", repeated.Manifest!.ChangeKind);
    }

    [Fact]
    public async Task Generate_RejectsUnconfirmedStaleOrUnsavedPlansWithoutWriting()
    {
        var projectPath = CopyReferenceProject("BrushMachine.etab.json");
        var opened = await service.OpenAsync(projectPath);
        var preview = service.Preview(opened.Document, projectPath, testRoot);

        var unconfirmed = Assert.Throws<EditorRequestException>(() => service.Generate(
            opened.Document,
            projectPath,
            testRoot,
            integrateProject: false,
            preview.ConfirmationToken!,
            confirmed: false));
        Assert.Equal("GENERATION_CONFIRMATION_REQUIRED", unconfirmed.Code);

        var otherRoot = Path.Combine(testRoot, "other-target");
        Directory.CreateDirectory(otherRoot);
        var stale = Assert.Throws<EditorRequestException>(() => service.Generate(
            opened.Document,
            projectPath,
            otherRoot,
            integrateProject: false,
            preview.ConfirmationToken!,
            confirmed: true));
        Assert.Equal("GENERATION_PREVIEW_STALE", stale.Code);

        opened.Document["project"]!["displayName"] = "Unsaved change";
        var unsaved = Assert.Throws<EditorRequestException>(() => service.Generate(
            opened.Document,
            projectPath,
            testRoot,
            integrateProject: false,
            preview.ConfirmationToken!,
            confirmed: true));
        Assert.Equal("GENERATION_MODEL_NOT_SAVED", unsaved.Code);
        Assert.False(Directory.Exists(Path.Combine(testRoot, "Generated")));
    }

    [Fact]
    public async Task Generate_WithProjectIntegrationWritesThePreviewedPlcProjectPlan()
    {
        var projectPath = CopyReferenceProject("BrushMachine.etab.json");
        var plcProjectPath = Path.Combine(testRoot, "AutomationBase_Beispiel.plcproj");
        await File.WriteAllTextAsync(
            plcProjectPath,
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
            "<Project DefaultTargets=\"Build\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">\n" +
            "  <PropertyGroup><Name>Test</Name></PropertyGroup>\n" +
            "  <ItemGroup />\n" +
            "</Project>\n",
            new UTF8Encoding(false));
        var opened = await service.OpenAsync(projectPath);
        var preview = service.Preview(
            opened.Document,
            projectPath,
            testRoot,
            integrateProject: true);

        Assert.NotNull(preview.ProjectFile);
        Assert.NotNull(preview.ProjectIntegrationManifest);
        var generated = service.Generate(
            opened.Document,
            projectPath,
            testRoot,
            integrateProject: true,
            preview.ConfirmationToken!,
            confirmed: true);

        Assert.True(generated.Success);
        Assert.True(generated.ProjectFileChanged);
        Assert.Contains(
            "Generated\\GVLs\\GVL_BM_Units.TcGVL",
            await File.ReadAllTextAsync(plcProjectPath),
            StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(
            testRoot,
            "Generated",
            "etab-project-integration-manifest.json")));
    }

    public void Dispose()
    {
        var expectedParent = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            "etab-engineering-service-tests"));
        var resolved = Path.GetFullPath(testRoot);
        if (!resolved.StartsWith(expectedParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing to remove a test directory outside the expected root.");
        }

        if (Directory.Exists(resolved))
        {
            Directory.Delete(resolved, recursive: true);
        }
    }

    private string CopyReferenceProject(string fileName)
    {
        var source = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "BrushMachine.reference.etab.json");
        var target = Path.Combine(testRoot, fileName);
        File.Copy(source, target);
        return target;
    }
}
