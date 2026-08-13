using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETAB.Engineering.Core.Execution;
using ETAB.Engineering.Core.Generation;
using ETAB.Engineering.Core.Model;
using ETAB.Engineering.Core.Planning;
using ETAB.Engineering.Core.ProjectIntegration;
using ETAB.Engineering.Core.Validation;

namespace ETAB.Engineering.Service;

public sealed class EditorProjectService
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true
    };

    private readonly string schemaJson;

    public EditorProjectService(string workspaceRoot, string schemaPath)
    {
        WorkspaceRoot = Path.GetFullPath(workspaceRoot);
        schemaJson = File.ReadAllText(Path.GetFullPath(schemaPath), Encoding.UTF8);
    }

    public string WorkspaceRoot { get; }

    public string ExampleProjectPath =>
        Path.Combine(WorkspaceRoot, "examples", "BrushMachine.reference.etab.json");

    public async Task<OpenProjectResponse> OpenAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var fullPath = ResolveProjectPath(path);
        if (!File.Exists(fullPath))
        {
            throw new EditorRequestException(
                "PROJECT_NOT_FOUND",
                $"The project file does not exist: {fullPath}");
        }

        string json;
        try
        {
            json = await File.ReadAllTextAsync(fullPath, Encoding.UTF8, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new EditorRequestException("PROJECT_READ_ERROR", exception.Message);
        }

        var document = ParseDocument(json);
        return new OpenProjectResponse(
            fullPath,
            Path.GetDirectoryName(fullPath)!,
            document,
            ValidateJson(json));
    }

    public async Task<SaveProjectResponse> SaveAsync(
        string path,
        JsonNode document,
        CancellationToken cancellationToken = default)
    {
        var fullPath = ResolveProjectPath(path);
        var directory = Path.GetDirectoryName(fullPath)!;
        if (!Directory.Exists(directory))
        {
            throw new EditorRequestException(
                "PROJECT_DIRECTORY_NOT_FOUND",
                $"The target directory does not exist: {directory}");
        }

        RejectReparsePoint(fullPath);

        var json = NormalizeJson(document);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                json,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            throw new EditorRequestException("PROJECT_WRITE_ERROR", exception.Message);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        return new SaveProjectResponse(
            fullPath,
            directory,
            ComputeSha256(json),
            ValidateJson(json));
    }

    public ValidationResponse Validate(JsonNode document) =>
        ValidateJson(document.ToJsonString());

    public PreviewResponse Preview(
        JsonNode document,
        string? projectPath,
        string? projectRoot,
        bool integrateProject = false)
    {
        var validationResult = new ProjectValidator().Validate(
            document.ToJsonString(),
            schemaJson);
        var validation = ToResponse(validationResult);
        if (!validationResult.IsValid || validationResult.Project is null)
        {
            return new PreviewResponse(
                validation,
                null,
                null,
                null,
                null,
                false,
                [],
                [],
                null,
                null,
                null,
                null,
                integrateProject,
                []);
        }

        var resolvedRoot = ResolveProjectRoot(projectRoot, projectPath);
        var preview = new ArtifactPreviewGenerator().Generate(validationResult.Project);
        var plan = BuildPlan(
            resolvedRoot,
            validationResult.Project,
            preview,
            integrateProject);

        return new PreviewResponse(
            validation,
            preview.ProjectId,
            preview.ProjectName,
            plan.ProjectRoot,
            plan.GeneratedRoot,
            plan.HasConflicts,
            preview.Artifacts.Select(artifact => new ArtifactPreviewResponse(
                artifact.SourceModelId,
                artifact.Kind.ToContractName(),
                artifact.Name,
                artifact.TwinCatGuid.ToString("D"),
                artifact.RelativePath,
                artifact.Sha256,
                artifact.Content)).ToArray(),
            plan.Changes.Select(change => new PlannedChangeResponse(
                change.ChangeKind.ToContractName(),
                change.ArtifactKind.ToContractName(),
                change.SourceModelId,
                change.RelativePath,
                change.PreviousRelativePath,
                change.Message)).ToArray(),
            new ManifestPreviewResponse(
                plan.Manifest.ChangeKind.ToContractName(),
                plan.Manifest.RelativePath,
                plan.Manifest.Message,
                plan.Manifest.ProposedContent),
            plan.ProjectFile is null
                ? null
                : new ManifestPreviewResponse(
                    plan.ProjectFile.ChangeKind.ToContractName(),
                    plan.ProjectFile.RelativePath,
                    plan.ProjectFile.Message,
                    plan.ProjectFile.ProposedContent),
            plan.ProjectIntegrationManifest is null
                ? null
                : new ManifestPreviewResponse(
                    plan.ProjectIntegrationManifest.ChangeKind.ToContractName(),
                    plan.ProjectIntegrationManifest.RelativePath,
                    plan.ProjectIntegrationManifest.Message,
                    plan.ProjectIntegrationManifest.ProposedContent),
            plan.HasConflicts ? null : ComputeConfirmationToken(plan),
            integrateProject,
            plan.Issues.Select(issue => new GenerationPlanIssueResponse(
                issue.Code,
                issue.Path,
                issue.Message)).ToArray());
    }

    public GenerateProjectResponse Generate(
        JsonNode document,
        string projectPath,
        string projectRoot,
        bool integrateProject,
        string confirmationToken,
        bool confirmed)
    {
        if (!confirmed)
        {
            throw new EditorRequestException(
                "GENERATION_CONFIRMATION_REQUIRED",
                "Generation requires explicit confirmation.");
        }
        if (string.IsNullOrWhiteSpace(confirmationToken))
        {
            throw new EditorRequestException(
                "GENERATION_PREVIEW_REQUIRED",
                "Refresh the generation preview before writing.");
        }

        EnsureSavedDocumentMatches(projectPath, document);
        var validationResult = new ProjectValidator().Validate(
            document.ToJsonString(),
            schemaJson);
        if (!validationResult.IsValid || validationResult.Project is null)
        {
            throw new EditorRequestException(
                "GENERATION_MODEL_INVALID",
                "The project model must pass schema and semantic validation before generation.");
        }

        var resolvedRoot = ResolveProjectRoot(projectRoot, projectPath);
        var preview = new ArtifactPreviewGenerator().Generate(validationResult.Project);
        var plan = BuildPlan(
            resolvedRoot,
            validationResult.Project,
            preview,
            integrateProject);
        if (plan.HasConflicts)
        {
            var details = string.Join(
                Environment.NewLine,
                plan.Issues.Select(issue => $"[{issue.Code}] {issue.Path}: {issue.Message}"));
            throw new EditorRequestException(
                "GENERATION_CONFLICT",
                string.IsNullOrWhiteSpace(details)
                    ? "The current generation plan contains conflicts."
                    : details);
        }

        var currentToken = ComputeConfirmationToken(plan);
        if (!string.Equals(
                currentToken,
                confirmationToken,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new EditorRequestException(
                "GENERATION_PREVIEW_STALE",
                "The target or generation plan changed after preview. Refresh and confirm the new plan.");
        }

        var projectFileChanged = plan.ProjectFile is not null &&
                                 plan.ProjectFile.ChangeKind != GenerationChangeKind.Unchanged;
        var manifestChanged = plan.Manifest.ChangeKind != GenerationChangeKind.Unchanged ||
                              (plan.ProjectIntegrationManifest is not null &&
                               plan.ProjectIntegrationManifest.ChangeKind != GenerationChangeKind.Unchanged);
        var result = new GenerationExecutor().Execute(plan);
        return new GenerateProjectResponse(
            result.Success,
            plan.ProjectRoot,
            result.Created,
            result.Updated,
            result.Renamed,
            result.Deleted,
            projectFileChanged,
            manifestChanged,
            result.Issues.Select(issue => new GenerationExecutionIssueResponse(
                issue.Code,
                issue.Message)).ToArray());
    }

    private static GenerationPlan BuildPlan(
        string projectRoot,
        EtabProjectDocument project,
        GenerationPreview preview,
        bool integrateProject)
    {
        var plan = new GenerationPlanBuilder().Build(projectRoot, project, preview);
        return integrateProject
            ? new TwinCatProjectIntegrationPlanBuilder().Build(plan, project, preview)
            : plan;
    }

    private void EnsureSavedDocumentMatches(string projectPath, JsonNode document)
    {
        var fullPath = ResolveProjectPath(projectPath);
        if (!File.Exists(fullPath))
        {
            throw new EditorRequestException(
                "PROJECT_NOT_FOUND",
                $"The project file does not exist: {fullPath}");
        }
        RejectReparsePoint(fullPath);

        JsonNode savedDocument;
        try
        {
            savedDocument = ParseDocument(File.ReadAllText(fullPath, Encoding.UTF8));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            throw new EditorRequestException("PROJECT_READ_ERROR", exception.Message);
        }

        if (!JsonNode.DeepEquals(savedDocument, document))
        {
            throw new EditorRequestException(
                "GENERATION_MODEL_NOT_SAVED",
                "Save the current ETAB model before generating PLC files.");
        }
    }

    private static string ComputeConfirmationToken(GenerationPlan plan)
    {
        var signature = new StringBuilder();
        AppendTokenValue(signature, plan.ProjectRoot);
        AppendTokenValue(signature, plan.GeneratedRoot);
        foreach (var change in plan.Changes)
        {
            AppendTokenValue(signature, change.ChangeKind.ToContractName());
            AppendTokenValue(signature, change.ArtifactKind.ToContractName());
            AppendTokenValue(signature, change.SourceModelId);
            AppendTokenValue(signature, change.RelativePath);
            AppendTokenValue(signature, change.PreviousRelativePath);
            AppendTokenValue(signature, change.ExpectedExistingHash);
            AppendTokenValue(signature, change.PlannedArtifact?.Sha256);
        }

        AppendTokenValue(signature, plan.Manifest.ChangeKind.ToContractName());
        AppendTokenValue(signature, plan.Manifest.RelativePath);
        AppendTokenValue(signature, plan.Manifest.ExpectedExistingHash);
        AppendTokenValue(signature, ComputeSha256(plan.Manifest.ProposedContent));

        if (plan.ProjectFile is not null)
        {
            AppendTokenValue(signature, plan.ProjectFile.ChangeKind.ToContractName());
            AppendTokenValue(signature, plan.ProjectFile.RelativePath);
            AppendTokenValue(signature, plan.ProjectFile.AbsolutePath);
            AppendTokenValue(signature, plan.ProjectFile.ExpectedExistingHash);
            AppendTokenValue(signature, plan.ProjectFile.ProposedHash);
        }
        if (plan.ProjectIntegrationManifest is not null)
        {
            AppendTokenValue(
                signature,
                plan.ProjectIntegrationManifest.ChangeKind.ToContractName());
            AppendTokenValue(signature, plan.ProjectIntegrationManifest.RelativePath);
            AppendTokenValue(
                signature,
                plan.ProjectIntegrationManifest.ExpectedExistingHash);
            AppendTokenValue(
                signature,
                ComputeSha256(plan.ProjectIntegrationManifest.ProposedContent));
        }

        foreach (var issue in plan.Issues)
        {
            AppendTokenValue(signature, issue.Code);
            AppendTokenValue(signature, issue.Path);
            AppendTokenValue(signature, issue.Message);
        }
        return ComputeSha256(signature.ToString());
    }

    private static void AppendTokenValue(StringBuilder signature, string? value)
    {
        var text = value ?? string.Empty;
        signature.Append(text.Length).Append(':').Append(text).Append('|');
    }

    private ValidationResponse ValidateJson(string json) =>
        ToResponse(new ProjectValidator().Validate(json, schemaJson));

    private static ValidationResponse ToResponse(ProjectValidationResult result) =>
        new(
            result.IsValid,
            result.Issues.Select(issue => new ValidationIssueResponse(
                issue.Code,
                issue.Path,
                issue.Message)).ToArray());

    private string ResolveProjectPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new EditorRequestException("PROJECT_PATH_REQUIRED", "A project path is required.");
        }

        var fullPath = Path.GetFullPath(
            Path.IsPathRooted(path) ? path : Path.Combine(WorkspaceRoot, path));
        if (!fullPath.EndsWith(".etab.json", StringComparison.OrdinalIgnoreCase))
        {
            throw new EditorRequestException(
                "PROJECT_EXTENSION",
                "ETAB Engineering project files must use the .etab.json extension.");
        }

        return fullPath;
    }

    private string ResolveProjectRoot(string? projectRoot, string? projectPath)
    {
        var candidate = !string.IsNullOrWhiteSpace(projectRoot)
            ? projectRoot
            : !string.IsNullOrWhiteSpace(projectPath)
                ? Path.GetDirectoryName(ResolveProjectPath(projectPath))
                : WorkspaceRoot;

        return Path.GetFullPath(
            Path.IsPathRooted(candidate!)
                ? candidate!
                : Path.Combine(WorkspaceRoot, candidate!));
    }

    private static JsonNode ParseDocument(string json)
    {
        try
        {
            return JsonNode.Parse(json) ?? throw new JsonException("The project document is empty.");
        }
        catch (JsonException exception)
        {
            throw new EditorRequestException("JSON_PARSE", exception.Message);
        }
    }

    private static string NormalizeJson(JsonNode document) =>
        document.ToJsonString(WriteOptions).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";

    private static string ComputeSha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static void RejectReparsePoint(string path)
    {
        if (File.Exists(path) &&
            File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
        {
            throw new EditorRequestException(
                "PROJECT_REPARSE_POINT",
                "Saving through a file-system reparse point is not allowed.");
        }
    }
}
