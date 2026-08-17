namespace ETAB.Engineering.Core.Manifest;

public sealed class GenerationManifest
{
    public required string ManifestVersion { get; init; }

    public required string GeneratorVersion { get; init; }

    public required string SchemaVersion { get; init; }

    public required string ProjectId { get; init; }

    public required string SemanticModelHash { get; init; }

    public required List<GenerationManifestArtifact> Artifacts { get; init; }
}

public sealed class GenerationManifestArtifact
{
    public required string SourceModelId { get; init; }

    public required string Kind { get; init; }

    public required string Name { get; init; }

    public required string TwinCatGuid { get; init; }

    public required string RelativePath { get; init; }

    public required string ContentHash { get; init; }

    public bool PreserveUserEdits { get; init; }
}
