using System.Text.Json.Nodes;

namespace ETAB.Engineering.Service;

public sealed record ValidationIssueResponse(string Code, string Path, string Message);

public sealed record ValidationResponse(
    bool IsValid,
    IReadOnlyList<ValidationIssueResponse> Issues);

public sealed record SessionResponse(
    string WorkspaceRoot,
    string ExampleProjectPath);

public sealed record OpenProjectRequest(string Path);

public sealed record OpenProjectResponse(
    string Path,
    string ProjectRoot,
    JsonNode Document,
    ValidationResponse Validation);

public sealed record SaveProjectRequest(string Path, JsonNode Document);

public sealed record SaveProjectResponse(
    string Path,
    string ProjectRoot,
    string Sha256,
    ValidationResponse Validation);

public sealed record ValidateProjectRequest(JsonNode Document);

public sealed record PreviewProjectRequest(
    JsonNode Document,
    string? ProjectPath,
    string? ProjectRoot);

public sealed record ArtifactPreviewResponse(
    string SourceModelId,
    string Kind,
    string Name,
    string TwinCatGuid,
    string RelativePath,
    string Sha256,
    string Content);

public sealed record PlannedChangeResponse(
    string ChangeKind,
    string ArtifactKind,
    string SourceModelId,
    string RelativePath,
    string? PreviousRelativePath,
    string? Message);

public sealed record ManifestPreviewResponse(
    string ChangeKind,
    string RelativePath,
    string? Message,
    string Content);

public sealed record GenerationPlanIssueResponse(string Code, string Path, string Message);

public sealed record PreviewResponse(
    ValidationResponse Validation,
    string? ProjectId,
    string? ProjectName,
    string? ProjectRoot,
    string? GeneratedRoot,
    bool HasConflicts,
    IReadOnlyList<ArtifactPreviewResponse> Artifacts,
    IReadOnlyList<PlannedChangeResponse> Changes,
    ManifestPreviewResponse? Manifest,
    IReadOnlyList<GenerationPlanIssueResponse> Issues);

public sealed record ApiErrorResponse(string Code, string Message);
