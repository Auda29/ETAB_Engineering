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
        Assert.Equal(14, preview.Artifacts.Count);
        Assert.All(preview.Changes, change => Assert.Equal("create", change.ChangeKind));
        Assert.Equal("create", preview.Manifest!.ChangeKind);
        Assert.False(Directory.Exists(Path.Combine(testRoot, "Generated")));
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
