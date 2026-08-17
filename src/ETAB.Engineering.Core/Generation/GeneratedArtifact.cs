namespace ETAB.Engineering.Core.Generation;

public sealed record GeneratedArtifact(
    string SourceModelId,
    GeneratedArtifactKind Kind,
    string Name,
    Guid TwinCatGuid,
    string RelativePath,
    string Content,
    string Sha256,
    bool PreserveUserEdits = false);
