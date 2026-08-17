using ETAB.Engineering.Core.Generation;

namespace ETAB.Engineering.Core.Planning;

public sealed record GenerationPlanIssue(string Code, string Path, string Message);

public sealed record PlannedArtifactChange(
    GenerationChangeKind ChangeKind,
    GeneratedArtifactKind ArtifactKind,
    string SourceModelId,
    string RelativePath,
    string? PreviousRelativePath,
    string? ExpectedExistingHash,
    string? Message,
    GeneratedArtifact? PlannedArtifact);

public sealed record PlannedManifestChange(
    GenerationChangeKind ChangeKind,
    string RelativePath,
    string ProposedContent,
    string? ExpectedExistingHash,
    string? Message);

public sealed record PlannedProjectFileChange(
    GenerationChangeKind ChangeKind,
    string RelativePath,
    string AbsolutePath,
    string ProposedContent,
    string ProposedHash,
    string? ExpectedExistingHash,
    string? Message);

public sealed record PlannedProjectIntegrationManifestChange(
    GenerationChangeKind ChangeKind,
    string RelativePath,
    string ProposedContent,
    string? ExpectedExistingHash,
    string? Message);

public sealed class GenerationPlan
{
    public GenerationPlan(
        string projectRoot,
        string generatedRoot,
        IReadOnlyList<PlannedArtifactChange> changes,
        PlannedManifestChange manifest,
        IReadOnlyList<GenerationPlanIssue> issues,
        PlannedProjectFileChange? projectFile = null,
        PlannedProjectIntegrationManifestChange? projectIntegrationManifest = null,
        PlannedProjectFileChange? taskFile = null)
    {
        ProjectRoot = projectRoot;
        GeneratedRoot = generatedRoot;
        Changes = changes;
        Manifest = manifest;
        Issues = issues;
        ProjectFile = projectFile;
        ProjectIntegrationManifest = projectIntegrationManifest;
        TaskFile = taskFile;
    }

    public string ProjectRoot { get; }

    public string GeneratedRoot { get; }

    public IReadOnlyList<PlannedArtifactChange> Changes { get; }

    public PlannedManifestChange Manifest { get; }

    public IReadOnlyList<GenerationPlanIssue> Issues { get; }

    public PlannedProjectFileChange? ProjectFile { get; }

    public PlannedProjectIntegrationManifestChange? ProjectIntegrationManifest { get; }

    public PlannedProjectFileChange? TaskFile { get; }

    public bool HasConflicts =>
        Issues.Count > 0 ||
        Manifest.ChangeKind == GenerationChangeKind.Conflict ||
        ProjectFile?.ChangeKind == GenerationChangeKind.Conflict ||
        TaskFile?.ChangeKind == GenerationChangeKind.Conflict ||
        ProjectIntegrationManifest?.ChangeKind == GenerationChangeKind.Conflict ||
        Changes.Any(change => change.ChangeKind == GenerationChangeKind.Conflict);
}
