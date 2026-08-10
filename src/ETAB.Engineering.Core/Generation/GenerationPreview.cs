namespace ETAB.Engineering.Core.Generation;

public sealed record GenerationPreview(
    string ProjectId,
    string ProjectName,
    IReadOnlyList<GeneratedArtifact> Artifacts);
