using System.Text.Json;
using System.Text.Json.Serialization;
using ETAB.Engineering.Core.Generation;
using ETAB.Engineering.Core.Model;

namespace ETAB.Engineering.Core.Manifest;

public static class GenerationManifestSerializer
{
    public const string CurrentManifestVersion = "0.1";
    public const string CurrentGeneratorVersion = "0.1.0.8";
    public const string FileName = "etab-generation-manifest.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static GenerationManifest Create(
        EtabProjectDocument project,
        GenerationPreview preview)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(preview);

        return new GenerationManifest
        {
            ManifestVersion = CurrentManifestVersion,
            GeneratorVersion = CurrentGeneratorVersion,
            SchemaVersion = project.SchemaVersion,
            ProjectId = project.Project.Id,
            SemanticModelHash = SemanticModelHasher.Compute(project),
            Artifacts = preview.Artifacts
                .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
                .Select(artifact => new GenerationManifestArtifact
                {
                    SourceModelId = artifact.SourceModelId,
                    Kind = artifact.Kind.ToContractName(),
                    Name = artifact.Name,
                    TwinCatGuid = artifact.TwinCatGuid.ToString("D"),
                    RelativePath = artifact.RelativePath,
                    ContentHash = artifact.Sha256,
                    PreserveUserEdits = artifact.PreserveUserEdits
                })
                .ToList()
        };
    }

    public static string Serialize(GenerationManifest manifest)
    {
        var json = JsonSerializer.Serialize(manifest, SerializerOptions);
        return json.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n') + "\n";
    }

    public static GenerationManifest Deserialize(string json) =>
        JsonSerializer.Deserialize<GenerationManifest>(json, SerializerOptions)
        ?? throw new JsonException("The generation manifest could not be deserialized.");
}
