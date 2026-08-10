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

public sealed class GenerationPlan
{
    public GenerationPlan(
        string projectRoot,
        string generatedRoot,
        IReadOnlyList<PlannedArtifactChange> changes,
        PlannedManifestChange manifest,
        IReadOnlyList<GenerationPlanIssue> issues)
    {
        ProjectRoot = projectRoot;
        GeneratedRoot = generatedRoot;
        Changes = changes;
        Manifest = manifest;
        Issues = issues;
    }

    public string ProjectRoot { get; }

    public string GeneratedRoot { get; }

    public IReadOnlyList<PlannedArtifactChange> Changes { get; }

    public PlannedManifestChange Manifest { get; }

    public IReadOnlyList<GenerationPlanIssue> Issues { get; }

    public bool HasConflicts =>
        Issues.Count > 0 ||
        Manifest.ChangeKind == GenerationChangeKind.Conflict ||
        Changes.Any(change => change.ChangeKind == GenerationChangeKind.Conflict);
}
