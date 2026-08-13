using System.Text.Json;
using System.Text.Json.Serialization;

namespace ETAB.Engineering.Core.ProjectIntegration;

public static class ProjectIntegrationManifestSerializer
{
    public const string CurrentManifestVersion = "0.1";
    public const string FileName = "etab-project-integration-manifest.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static string Serialize(ProjectIntegrationManifest manifest)
    {
        var json = JsonSerializer.Serialize(manifest, SerializerOptions);
        return json.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n') + "\n";
    }

    public static ProjectIntegrationManifest Deserialize(string json) =>
        JsonSerializer.Deserialize<ProjectIntegrationManifest>(json, SerializerOptions)
        ?? throw new JsonException("The project integration manifest could not be deserialized.");
}
