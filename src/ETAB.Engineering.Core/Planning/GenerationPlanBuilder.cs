using System.Security.Cryptography;
using System.Text.Json;
using ETAB.Engineering.Core.Generation;
using ETAB.Engineering.Core.Manifest;
using ETAB.Engineering.Core.Model;

namespace ETAB.Engineering.Core.Planning;

public sealed class GenerationPlanBuilder
{
    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    public GenerationPlan Build(
        string projectRoot,
        EtabProjectDocument project,
        GenerationPreview preview)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(preview);

        var proposedManifest = GenerationManifestSerializer.Create(project, preview);
        var proposedManifestContent = GenerationManifestSerializer.Serialize(proposedManifest);

        if (!GenerationPathResolver.TryResolve(
                projectRoot,
                project.Project.Generation.GeneratedRoot,
                GenerationManifestSerializer.FileName,
                out var paths,
                out var pathError))
        {
            var issue = new GenerationPlanIssue(
                "OUTPUT_ROOT_INVALID",
                "/project/generation/generatedRoot",
                pathError!);

            return BuildGlobalConflictPlan(
                Path.GetFullPath(projectRoot),
                project.Project.Generation.GeneratedRoot,
                preview,
                proposedManifestContent,
                issue);
        }

        if (Directory.Exists(paths!.ManifestPath))
        {
            var issue = new GenerationPlanIssue(
                "MANIFEST_PATH_OCCUPIED",
                paths.ManifestRelativePath,
                "The manifest path is occupied by a directory.");
            return BuildGlobalConflictPlan(
                paths.ProjectRoot,
                paths.GeneratedRoot,
                preview,
                proposedManifestContent,
                issue,
                paths.ManifestRelativePath);
        }

        if (!File.Exists(paths.ManifestPath))
        {
            return BuildWithoutExistingManifest(paths, preview, proposedManifestContent);
        }

        string existingManifestContent;
        string existingManifestHash;
        GenerationManifest existingManifest;

        try
        {
            existingManifestContent = File.ReadAllText(paths.ManifestPath);
            existingManifestHash = ComputeFileHash(paths.ManifestPath);
            existingManifest = GenerationManifestSerializer.Deserialize(existingManifestContent);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            var issue = new GenerationPlanIssue(
                "MANIFEST_INVALID",
                paths.ManifestRelativePath,
                exception.Message);
            return BuildGlobalConflictPlan(
                paths.ProjectRoot,
                paths.GeneratedRoot,
                preview,
                proposedManifestContent,
                issue,
                paths.ManifestRelativePath);
        }

        var manifestIssues = ValidateManifest(existingManifest, project);
        if (manifestIssues.Count > 0)
        {
            return BuildGlobalConflictPlan(
                paths.ProjectRoot,
                paths.GeneratedRoot,
                preview,
                proposedManifestContent,
                manifestIssues,
                paths.ManifestRelativePath);
        }

        var changes = CompareArtifacts(paths, preview, existingManifest);
        var hasArtifactConflicts = changes.Any(
            change => change.ChangeKind == GenerationChangeKind.Conflict);
        var manifestChangeKind = hasArtifactConflicts
            ? GenerationChangeKind.Conflict
            : string.Equals(existingManifestContent, proposedManifestContent, StringComparison.Ordinal)
                ? GenerationChangeKind.Unchanged
                : GenerationChangeKind.Update;

        var manifestMessage = hasArtifactConflicts
            ? "The manifest cannot be updated while artifact conflicts exist."
            : null;

        return new GenerationPlan(
            paths.ProjectRoot,
            paths.GeneratedRoot,
            changes,
            new PlannedManifestChange(
                manifestChangeKind,
                paths.ManifestRelativePath,
                proposedManifestContent,
                existingManifestHash,
                manifestMessage),
            []);
    }

    private static GenerationPlan BuildWithoutExistingManifest(
        ResolvedGenerationPaths paths,
        GenerationPreview preview,
        string proposedManifestContent)
    {
        var changes = new List<PlannedArtifactChange>();

        foreach (var artifact in preview.Artifacts)
        {
            if (!GenerationPathResolver.TryResolveArtifactPath(
                    paths,
                    artifact.RelativePath,
                    out var targetPath,
                    out var pathError))
            {
                changes.Add(Conflict(artifact, pathError!));
                continue;
            }

            changes.Add(IsOccupied(targetPath!)
                ? Conflict(
                    artifact,
                    "The target path is occupied by a file that is not managed by a manifest.")
                : Planned(GenerationChangeKind.Create, artifact));
        }

        var hasConflicts = changes.Any(change => change.ChangeKind == GenerationChangeKind.Conflict);
        return new GenerationPlan(
            paths.ProjectRoot,
            paths.GeneratedRoot,
            changes,
            new PlannedManifestChange(
                hasConflicts ? GenerationChangeKind.Conflict : GenerationChangeKind.Create,
                paths.ManifestRelativePath,
                proposedManifestContent,
                null,
                hasConflicts
                    ? "The manifest cannot be created while artifact conflicts exist."
                    : null),
            []);
    }

    private static IReadOnlyList<PlannedArtifactChange> CompareArtifacts(
        ResolvedGenerationPaths paths,
        GenerationPreview preview,
        GenerationManifest existingManifest)
    {
        var existingByIdentity = existingManifest.Artifacts.ToDictionary(
            ArtifactIdentity.FromManifest,
            artifact => artifact);
        var plannedIdentities = preview.Artifacts
            .Select(ArtifactIdentity.FromGenerated)
            .ToHashSet();
        var changes = new List<PlannedArtifactChange>();

        foreach (var artifact in preview.Artifacts)
        {
            var identity = ArtifactIdentity.FromGenerated(artifact);
            if (!existingByIdentity.TryGetValue(identity, out var existing))
            {
                changes.Add(PlanNewArtifact(paths, artifact));
                continue;
            }

            changes.Add(CompareManagedArtifact(paths, artifact, existing));
        }

        foreach (var existing in existingManifest.Artifacts
                     .Where(artifact => !plannedIdentities.Contains(ArtifactIdentity.FromManifest(artifact)))
                     .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal))
        {
            changes.Add(PlanDeletedArtifact(paths, existing));
        }

        return changes;
    }

    private static PlannedArtifactChange PlanNewArtifact(
        ResolvedGenerationPaths paths,
        GeneratedArtifact artifact)
    {
        if (!GenerationPathResolver.TryResolveArtifactPath(
                paths,
                artifact.RelativePath,
                out var targetPath,
                out var pathError))
        {
            return Conflict(artifact, pathError!);
        }

        return IsOccupied(targetPath!)
            ? Conflict(
                artifact,
                "The target path is occupied by a file that is not managed for this artifact identity.")
            : Planned(GenerationChangeKind.Create, artifact);
    }

    private static PlannedArtifactChange CompareManagedArtifact(
        ResolvedGenerationPaths paths,
        GeneratedArtifact artifact,
        GenerationManifestArtifact existing)
    {
        if (!GenerationPathResolver.TryResolveArtifactPath(
                paths,
                existing.RelativePath,
                out var oldPath,
                out var oldPathError))
        {
            return Conflict(artifact, oldPathError!, existing.RelativePath);
        }

        if (!File.Exists(oldPath))
        {
            return Conflict(
                artifact,
                "The manifest-managed source file is missing.",
                existing.RelativePath);
        }

        if (!TryComputeFileHash(oldPath!, out var actualHash, out var hashError))
        {
            return Conflict(artifact, hashError!, existing.RelativePath);
        }

        if (!string.Equals(actualHash, existing.ContentHash, StringComparison.OrdinalIgnoreCase))
        {
            return Conflict(
                artifact,
                "The manifest-managed file was changed outside ETAB Engineering.",
                existing.RelativePath);
        }

        if (!Guid.TryParse(existing.TwinCatGuid, out var oldGuid) || oldGuid != artifact.TwinCatGuid)
        {
            return Conflict(
                artifact,
                "The manifest TwinCAT GUID does not match the deterministic model GUID.",
                existing.RelativePath);
        }

        if (PathComparer.Equals(existing.RelativePath, artifact.RelativePath))
        {
            return Planned(
                string.Equals(existing.ContentHash, artifact.Sha256, StringComparison.OrdinalIgnoreCase)
                    ? GenerationChangeKind.Unchanged
                    : GenerationChangeKind.Update,
                artifact,
                existing.RelativePath,
                existing.ContentHash);
        }

        if (!GenerationPathResolver.TryResolveArtifactPath(
                paths,
                artifact.RelativePath,
                out var newPath,
                out var newPathError))
        {
            return Conflict(artifact, newPathError!, existing.RelativePath);
        }

        if (IsOccupied(newPath!))
        {
            return Conflict(
                artifact,
                "The rename target is already occupied.",
                existing.RelativePath);
        }

        return Planned(
            GenerationChangeKind.Rename,
            artifact,
            existing.RelativePath,
            existing.ContentHash);
    }

    private static PlannedArtifactChange PlanDeletedArtifact(
        ResolvedGenerationPaths paths,
        GenerationManifestArtifact existing)
    {
        var kind = ParseKind(existing.Kind);

        if (!GenerationPathResolver.TryResolveArtifactPath(
                paths,
                existing.RelativePath,
                out var oldPath,
                out var pathError))
        {
            return new PlannedArtifactChange(
                GenerationChangeKind.Conflict,
                kind,
                existing.SourceModelId,
                existing.RelativePath,
                existing.RelativePath,
                existing.ContentHash,
                pathError,
                null);
        }

        if (!File.Exists(oldPath))
        {
            return new PlannedArtifactChange(
                GenerationChangeKind.Conflict,
                kind,
                existing.SourceModelId,
                existing.RelativePath,
                existing.RelativePath,
                existing.ContentHash,
                "The manifest-managed file selected for deletion is missing.",
                null);
        }

        if (!TryComputeFileHash(oldPath!, out var actualHash, out var hashError))
        {
            return new PlannedArtifactChange(
                GenerationChangeKind.Conflict,
                kind,
                existing.SourceModelId,
                existing.RelativePath,
                existing.RelativePath,
                existing.ContentHash,
                hashError,
                null);
        }

        return new PlannedArtifactChange(
            string.Equals(actualHash, existing.ContentHash, StringComparison.OrdinalIgnoreCase)
                ? GenerationChangeKind.Delete
                : GenerationChangeKind.Conflict,
            kind,
            existing.SourceModelId,
            existing.RelativePath,
            existing.RelativePath,
            existing.ContentHash,
            string.Equals(actualHash, existing.ContentHash, StringComparison.OrdinalIgnoreCase)
                ? null
                : "The manifest-managed file selected for deletion was changed outside ETAB Engineering.",
            null);
    }

    private static IReadOnlyList<GenerationPlanIssue> ValidateManifest(
        GenerationManifest manifest,
        EtabProjectDocument project)
    {
        var issues = new List<GenerationPlanIssue>();

        if (string.IsNullOrWhiteSpace(manifest.ManifestVersion))
        {
            issues.Add(new GenerationPlanIssue(
                "MANIFEST_REQUIRED_VALUE",
                "/manifestVersion",
                "The manifest version is required."));
        }

        if (manifest.ManifestVersion != GenerationManifestSerializer.CurrentManifestVersion)
        {
            issues.Add(new GenerationPlanIssue(
                "MANIFEST_VERSION",
                "/manifestVersion",
                $"Unsupported manifest version '{manifest.ManifestVersion}'."));
        }

        if (!string.Equals(manifest.ProjectId, project.Project.Id, StringComparison.Ordinal))
        {
            issues.Add(new GenerationPlanIssue(
                "MANIFEST_PROJECT",
                "/projectId",
                "The manifest belongs to a different ETAB Engineering project."));
        }

        if (!string.Equals(manifest.SchemaVersion, project.SchemaVersion, StringComparison.Ordinal))
        {
            issues.Add(new GenerationPlanIssue(
                "MANIFEST_SCHEMA",
                "/schemaVersion",
                "The manifest schema version does not match the project schema version."));
        }

        if (string.IsNullOrWhiteSpace(manifest.GeneratorVersion))
        {
            issues.Add(new GenerationPlanIssue(
                "MANIFEST_REQUIRED_VALUE",
                "/generatorVersion",
                "The generator version is required."));
        }

        if (!IsSha256(manifest.SemanticModelHash))
        {
            issues.Add(new GenerationPlanIssue(
                "MANIFEST_MODEL_HASH",
                "/semanticModelHash",
                "The semantic model hash is not a SHA-256 value."));
        }

        if (manifest.Artifacts is null)
        {
            issues.Add(new GenerationPlanIssue(
                "MANIFEST_REQUIRED_VALUE",
                "/artifacts",
                "The manifest artifact list is required."));
            return issues;
        }

        var identities = new HashSet<ArtifactIdentity>();
        var paths = new HashSet<string>(PathComparer);

        for (var index = 0; index < manifest.Artifacts.Count; index++)
        {
            var artifact = manifest.Artifacts[index];
            var path = $"/artifacts/{index}";

            if (artifact is null ||
                string.IsNullOrWhiteSpace(artifact.SourceModelId) ||
                string.IsNullOrWhiteSpace(artifact.Kind) ||
                string.IsNullOrWhiteSpace(artifact.Name) ||
                string.IsNullOrWhiteSpace(artifact.TwinCatGuid) ||
                string.IsNullOrWhiteSpace(artifact.RelativePath) ||
                string.IsNullOrWhiteSpace(artifact.ContentHash))
            {
                issues.Add(new GenerationPlanIssue(
                    "MANIFEST_REQUIRED_VALUE",
                    path,
                    "Every manifest artifact requires identity, kind, name, GUID, path and content hash."));
                continue;
            }

            if (!TryParseKind(artifact.Kind, out var kind))
            {
                issues.Add(new GenerationPlanIssue(
                    "MANIFEST_ARTIFACT_KIND",
                    $"{path}/kind",
                    $"Unknown artifact kind '{artifact.Kind}'."));
                continue;
            }

            var identity = new ArtifactIdentity(artifact.SourceModelId, kind);
            if (!identities.Add(identity))
            {
                issues.Add(new GenerationPlanIssue(
                    "MANIFEST_IDENTITY_DUPLICATE",
                    path,
                    "The manifest contains the same model ID and artifact kind more than once."));
            }

            if (!paths.Add(artifact.RelativePath))
            {
                issues.Add(new GenerationPlanIssue(
                    "MANIFEST_PATH_DUPLICATE",
                    $"{path}/relativePath",
                    "The manifest contains the same artifact path more than once."));
            }

            if (!Guid.TryParse(artifact.SourceModelId, out _))
            {
                issues.Add(new GenerationPlanIssue(
                    "MANIFEST_SOURCE_ID",
                    $"{path}/sourceModelId",
                    "The manifest source model ID is not a UUID."));
            }

            if (!Guid.TryParse(artifact.TwinCatGuid, out _))
            {
                issues.Add(new GenerationPlanIssue(
                    "MANIFEST_TWINCAT_GUID",
                    $"{path}/twinCatGuid",
                    "The manifest TwinCAT GUID is not a UUID."));
            }

            if (!IsSha256(artifact.ContentHash))
            {
                issues.Add(new GenerationPlanIssue(
                    "MANIFEST_CONTENT_HASH",
                    $"{path}/contentHash",
                    "The manifest content hash is not a SHA-256 value."));
            }
        }

        return issues;
    }

    private static GenerationPlan BuildGlobalConflictPlan(
        string projectRoot,
        string generatedRoot,
        GenerationPreview preview,
        string proposedManifestContent,
        GenerationPlanIssue issue,
        string? manifestRelativePath = null) =>
        BuildGlobalConflictPlan(
            projectRoot,
            generatedRoot,
            preview,
            proposedManifestContent,
            [issue],
            manifestRelativePath);

    private static GenerationPlan BuildGlobalConflictPlan(
        string projectRoot,
        string generatedRoot,
        GenerationPreview preview,
        string proposedManifestContent,
        IReadOnlyList<GenerationPlanIssue> issues,
        string? manifestRelativePath = null)
    {
        var changes = preview.Artifacts
            .Select(artifact => Conflict(
                artifact,
                "A safe comparison is not possible until the planning issue is resolved."))
            .ToArray();

        return new GenerationPlan(
            projectRoot,
            generatedRoot,
            changes,
            new PlannedManifestChange(
                GenerationChangeKind.Conflict,
                manifestRelativePath ?? GenerationManifestSerializer.FileName,
                proposedManifestContent,
                null,
                "The manifest cannot be planned safely."),
            issues);
    }

    private static PlannedArtifactChange Planned(
        GenerationChangeKind kind,
        GeneratedArtifact artifact,
        string? previousRelativePath = null,
        string? expectedExistingHash = null) =>
        new(
            kind,
            artifact.Kind,
            artifact.SourceModelId,
            artifact.RelativePath,
            previousRelativePath,
            expectedExistingHash,
            null,
            artifact);

    private static PlannedArtifactChange Conflict(
        GeneratedArtifact artifact,
        string message,
        string? previousRelativePath = null) =>
        new(
            GenerationChangeKind.Conflict,
            artifact.Kind,
            artifact.SourceModelId,
            artifact.RelativePath,
            previousRelativePath,
            null,
            message,
            artifact);

    private static bool TryComputeFileHash(
        string path,
        out string? contentHash,
        out string? error)
    {
        contentHash = null;
        error = null;

        try
        {
            contentHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
                .ToLowerInvariant();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = exception.Message;
            return false;
        }
    }

    private static bool IsOccupied(string path) =>
        File.Exists(path) || Directory.Exists(path);

    private static string ComputeFileHash(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
            .ToLowerInvariant();

    private static bool IsSha256(string? value)
    {
        if (value is null || value.Length != 64)
        {
            return false;
        }

        try
        {
            _ = Convert.FromHexString(value);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static GeneratedArtifactKind ParseKind(string value) =>
        TryParseKind(value, out var kind)
            ? kind
            : throw new InvalidOperationException($"Unknown artifact kind '{value}'.");

    private static bool TryParseKind(string value, out GeneratedArtifactKind kind)
    {
        switch (value)
        {
            case "command-enum":
                kind = GeneratedArtifactKind.CommandEnum;
                return true;
            case "request-dut":
                kind = GeneratedArtifactKind.RequestDut;
                return true;
            case "status-dut":
                kind = GeneratedArtifactKind.StatusDut;
                return true;
            case "base-function-block":
                kind = GeneratedArtifactKind.BaseFunctionBlock;
                return true;
            case "instance-gvl":
                kind = GeneratedArtifactKind.InstanceGlobalVariableList;
                return true;
            case "relation-wiring":
                kind = GeneratedArtifactKind.RelationWiring;
                return true;
            case "program-call-structure":
                kind = GeneratedArtifactKind.ProgramCallStructure;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    private sealed record ArtifactIdentity(string SourceModelId, GeneratedArtifactKind Kind)
    {
        public static ArtifactIdentity FromGenerated(GeneratedArtifact artifact) =>
            new(artifact.SourceModelId, artifact.Kind);

        public static ArtifactIdentity FromManifest(GenerationManifestArtifact artifact) =>
            new(artifact.SourceModelId, ParseKind(artifact.Kind));
    }
}
