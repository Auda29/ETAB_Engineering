using System.Security.Cryptography;
using System.Text;
using ETAB.Engineering.Core.Planning;

namespace ETAB.Engineering.Core.Execution;

public sealed class GenerationExecutor
{
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static readonly UTF8Encoding Utf8WithoutBom = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private const string StagedProjectFileName = ".etab-project/plc-project.staged";
    private const string BackedUpProjectFileName = ".etab-project/plc-project.backup";
    private const string StagedTaskFileName = ".etab-project/plc-task.staged";
    private const string BackedUpTaskFileName = ".etab-project/plc-task.backup";

    private readonly Action<int>? _afterArtifactOperation;

    public GenerationExecutor()
    {
    }

    internal GenerationExecutor(Action<int> afterArtifactOperation)
    {
        _afterArtifactOperation = afterArtifactOperation ??
            throw new ArgumentNullException(nameof(afterArtifactOperation));
    }

    public GenerationExecutionResult Execute(GenerationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var preflightIssues = ValidatePreflight(plan);
        if (preflightIssues.Count > 0)
        {
            return Failed(preflightIssues);
        }

        var requiresWrite = plan.Changes.Any(
                                change => change.ChangeKind != GenerationChangeKind.Unchanged) ||
                            plan.Manifest.ChangeKind != GenerationChangeKind.Unchanged ||
                            (plan.ProjectFile is not null &&
                             plan.ProjectFile.ChangeKind != GenerationChangeKind.Unchanged) ||
                            (plan.TaskFile is not null &&
                             plan.TaskFile.ChangeKind != GenerationChangeKind.Unchanged) ||
                            (plan.ProjectIntegrationManifest is not null &&
                             plan.ProjectIntegrationManifest.ChangeKind != GenerationChangeKind.Unchanged);
        if (!requiresWrite)
        {
            return new GenerationExecutionResult(true, 0, 0, 0, 0, []);
        }

        var generatedRootAlreadyExisted = Directory.Exists(plan.GeneratedRoot);
        var transactionId = Guid.NewGuid().ToString("N");
        var stagingRoot = Path.Combine(plan.GeneratedRoot, $".etab-staging-{transactionId}");
        var backupRoot = Path.Combine(plan.GeneratedRoot, $".etab-backup-{transactionId}");
        var backups = new List<(string BackupPath, string OriginalPath)>();
        var writtenTargets = new List<string>();
        var appliedArtifactOperations = 0;
        var committed = false;

        try
        {
            Directory.CreateDirectory(plan.GeneratedRoot);
            EnsureTransactionDirectory(stagingRoot, plan.GeneratedRoot);
            EnsureTransactionDirectory(backupRoot, plan.GeneratedRoot);
            Directory.CreateDirectory(stagingRoot);
            Directory.CreateDirectory(backupRoot);

            StageContents(plan, stagingRoot);

            foreach (var change in plan.Changes)
            {
                ApplyArtifactChange(
                    plan,
                    change,
                    stagingRoot,
                    backupRoot,
                    backups,
                    writtenTargets);

                if (change.ChangeKind != GenerationChangeKind.Unchanged)
                {
                    _afterArtifactOperation?.Invoke(++appliedArtifactOperations);
                }
            }

            ApplyProjectFileChange(
                plan,
                stagingRoot,
                backupRoot,
                backups,
                writtenTargets);
            if (plan.ProjectFile?.ChangeKind == GenerationChangeKind.Update)
            {
                _afterArtifactOperation?.Invoke(++appliedArtifactOperations);
            }

            ApplyTaskFileChange(
                plan,
                stagingRoot,
                backupRoot,
                backups,
                writtenTargets);
            if (plan.TaskFile?.ChangeKind == GenerationChangeKind.Update)
            {
                _afterArtifactOperation?.Invoke(++appliedArtifactOperations);
            }

            ApplyManifestChange(
                plan,
                stagingRoot,
                backupRoot,
                backups,
                writtenTargets);
            ApplyProjectIntegrationManifestChange(
                plan,
                stagingRoot,
                backupRoot,
                backups,
                writtenTargets);
            committed = true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            var rollbackIssues = RollBack(
                plan,
                stagingRoot,
                backupRoot,
                backups,
                writtenTargets,
                generatedRootAlreadyExisted);
            var issues = new List<GenerationExecutionIssue>
            {
                new("GENERATION_WRITE_FAILED", exception.Message)
            };
            issues.AddRange(rollbackIssues);
            return Failed(issues);
        }

        var cleanupIssues = committed
            ? CleanTransactionDirectories(plan.GeneratedRoot, stagingRoot, backupRoot)
            : [];

        return new GenerationExecutionResult(
            cleanupIssues.Count == 0,
            plan.Changes.Count(change => change.ChangeKind == GenerationChangeKind.Create),
            plan.Changes.Count(change => change.ChangeKind == GenerationChangeKind.Update),
            plan.Changes.Count(change => change.ChangeKind == GenerationChangeKind.Rename),
            plan.Changes.Count(change => change.ChangeKind == GenerationChangeKind.Delete),
            cleanupIssues);
    }

    private static IReadOnlyList<GenerationExecutionIssue> ValidatePreflight(GenerationPlan plan)
    {
        var issues = new List<GenerationExecutionIssue>();

        if (plan.HasConflicts)
        {
            issues.Add(new GenerationExecutionIssue(
                "GENERATION_CONFLICT",
                "Generation is blocked because the plan contains conflicts."));
            return issues;
        }

        if (!Directory.Exists(plan.ProjectRoot))
        {
            issues.Add(new GenerationExecutionIssue(
                "PROJECT_ROOT_MISSING",
                $"Project root '{plan.ProjectRoot}' does not exist."));
            return issues;
        }

        if (File.GetAttributes(plan.ProjectRoot).HasFlag(FileAttributes.ReparsePoint))
        {
            issues.Add(new GenerationExecutionIssue(
                "REPARSE_POINT_BLOCKED",
                "The selected project root is a symbolic link, junction or other reparse point."));
            return issues;
        }

        if (!IsSameOrStrictDescendant(plan.GeneratedRoot, plan.ProjectRoot))
        {
            issues.Add(new GenerationExecutionIssue(
                "GENERATED_ROOT_INVALID",
                "The generated root is not a child directory of the project root."));
            return issues;
        }

        if (File.Exists(plan.GeneratedRoot))
        {
            issues.Add(new GenerationExecutionIssue(
                "GENERATED_ROOT_OCCUPIED",
                "The generated root is occupied by a file."));
            return issues;
        }

        if (ContainsReparsePointBelowRoot(plan.ProjectRoot, plan.GeneratedRoot))
        {
            issues.Add(new GenerationExecutionIssue(
                "REPARSE_POINT_BLOCKED",
                "The generated path traverses a symbolic link, junction or other reparse point."));
            return issues;
        }

        foreach (var change in plan.Changes)
        {
            ValidateArtifactChange(plan, change, issues);
        }

        ValidateProjectFileChange(plan, issues);
        ValidateTaskFileChange(plan, issues);
        ValidateManifestChange(plan, issues);
        ValidateProjectIntegrationManifestChange(plan, issues);
        return issues;
    }

    private static void ValidateProjectFileChange(
        GenerationPlan plan,
        ICollection<GenerationExecutionIssue> issues)
    {
        if (plan.ProjectFile is null)
        {
            return;
        }

        if (!TryResolveProjectFilePath(plan, out var expectedPath, out var pathError) ||
            !string.Equals(
                expectedPath,
                plan.ProjectFile.AbsolutePath,
                PathComparison))
        {
            issues.Add(new GenerationExecutionIssue(
                "PLC_PROJECT_PATH_INVALID",
                pathError ?? "The planned PLC project path does not match the project root."));
            return;
        }

        if (ContainsReparsePointBelowRoot(plan.ProjectRoot, expectedPath!))
        {
            issues.Add(new GenerationExecutionIssue(
                "REPARSE_POINT_BLOCKED",
                $"PLC project path '{plan.ProjectFile.RelativePath}' traverses a reparse point."));
            return;
        }

        switch (plan.ProjectFile.ChangeKind)
        {
            case GenerationChangeKind.Update:
            case GenerationChangeKind.Unchanged:
                VerifyExistingFile(
                    expectedPath!,
                    plan.ProjectFile.ExpectedExistingHash,
                    plan.ProjectFile.RelativePath,
                    issues);
                break;
            case GenerationChangeKind.Conflict:
                issues.Add(new GenerationExecutionIssue(
                    "GENERATION_CONFLICT",
                    "The TwinCAT PLC project is in conflict."));
                break;
            default:
                issues.Add(new GenerationExecutionIssue(
                    "PLC_PROJECT_CHANGE_INVALID",
                    $"Unsupported PLC project change '{plan.ProjectFile.ChangeKind}'."));
                break;
        }
    }

    private static void ValidateArtifactChange(
        GenerationPlan plan,
        PlannedArtifactChange change,
        ICollection<GenerationExecutionIssue> issues)
    {
        if (!TryResolveGeneratedPath(plan, change.RelativePath, out var target, out var pathError))
        {
            issues.Add(new GenerationExecutionIssue("ARTIFACT_PATH_INVALID", pathError!));
            return;
        }

        if (ContainsReparsePointBelowRoot(plan.ProjectRoot, target!))
        {
            issues.Add(new GenerationExecutionIssue(
                "REPARSE_POINT_BLOCKED",
                $"Artifact path '{change.RelativePath}' traverses a reparse point."));
            return;
        }

        switch (change.ChangeKind)
        {
            case GenerationChangeKind.Create:
                EnsureUnoccupied(target!, change.RelativePath, issues);
                EnsurePlannedContent(change, issues);
                break;

            case GenerationChangeKind.Update:
            case GenerationChangeKind.Unchanged:
                VerifyExistingFile(target!, change.ExpectedExistingHash, change.RelativePath, issues);
                EnsurePlannedContent(change, issues);
                break;

            case GenerationChangeKind.Rename:
                if (string.IsNullOrWhiteSpace(change.PreviousRelativePath))
                {
                    issues.Add(new GenerationExecutionIssue(
                        "RENAME_SOURCE_INVALID",
                        "The rename source path is missing."));
                    break;
                }

                if (!TryResolveGeneratedPath(
                        plan,
                        change.PreviousRelativePath,
                        out var oldPath,
                        out var oldPathError))
                {
                    issues.Add(new GenerationExecutionIssue(
                        "RENAME_SOURCE_INVALID",
                        oldPathError!));
                    break;
                }

                VerifyExistingFile(
                    oldPath!,
                    change.ExpectedExistingHash,
                    change.PreviousRelativePath,
                    issues);
                EnsureUnoccupied(target!, change.RelativePath, issues);
                EnsurePlannedContent(change, issues);
                break;

            case GenerationChangeKind.Delete:
                VerifyExistingFile(target!, change.ExpectedExistingHash, change.RelativePath, issues);
                break;

            case GenerationChangeKind.Conflict:
                issues.Add(new GenerationExecutionIssue(
                    "GENERATION_CONFLICT",
                    $"Artifact '{change.RelativePath}' is in conflict."));
                break;

            default:
                issues.Add(new GenerationExecutionIssue(
                    "CHANGE_KIND_INVALID",
                    $"Unsupported change kind '{change.ChangeKind}'."));
                break;
        }
    }

    private static void ValidateTaskFileChange(
        GenerationPlan plan,
        ICollection<GenerationExecutionIssue> issues)
    {
        if (plan.TaskFile is null)
        {
            return;
        }

        if (!TryResolveTaskFilePath(plan, out var expectedPath, out var pathError) ||
            !string.Equals(expectedPath, plan.TaskFile.AbsolutePath, PathComparison))
        {
            issues.Add(new GenerationExecutionIssue(
                "PLC_TASK_PATH_INVALID",
                pathError ?? "The planned TwinCAT task path does not match the project root."));
            return;
        }

        if (ContainsReparsePointBelowRoot(plan.ProjectRoot, expectedPath!))
        {
            issues.Add(new GenerationExecutionIssue(
                "REPARSE_POINT_BLOCKED",
                $"TwinCAT task path '{plan.TaskFile.RelativePath}' traverses a reparse point."));
            return;
        }

        switch (plan.TaskFile.ChangeKind)
        {
            case GenerationChangeKind.Update:
            case GenerationChangeKind.Unchanged:
                VerifyExistingFile(
                    expectedPath!,
                    plan.TaskFile.ExpectedExistingHash,
                    plan.TaskFile.RelativePath,
                    issues);
                break;
            case GenerationChangeKind.Conflict:
                issues.Add(new GenerationExecutionIssue(
                    "GENERATION_CONFLICT",
                    "The TwinCAT task is in conflict."));
                break;
            default:
                issues.Add(new GenerationExecutionIssue(
                    "PLC_TASK_CHANGE_INVALID",
                    $"Unsupported TwinCAT task change '{plan.TaskFile.ChangeKind}'."));
                break;
        }
    }

    private static void ValidateManifestChange(
        GenerationPlan plan,
        ICollection<GenerationExecutionIssue> issues)
    {
        if (!TryResolveGeneratedPath(
                plan,
                plan.Manifest.RelativePath,
                out var manifestPath,
                out var pathError))
        {
            issues.Add(new GenerationExecutionIssue("MANIFEST_PATH_INVALID", pathError!));
            return;
        }

        switch (plan.Manifest.ChangeKind)
        {
            case GenerationChangeKind.Create:
                EnsureUnoccupied(manifestPath!, plan.Manifest.RelativePath, issues);
                break;
            case GenerationChangeKind.Update:
            case GenerationChangeKind.Unchanged:
                VerifyExistingFile(
                    manifestPath!,
                    plan.Manifest.ExpectedExistingHash,
                    plan.Manifest.RelativePath,
                    issues);
                break;
            case GenerationChangeKind.Conflict:
                issues.Add(new GenerationExecutionIssue(
                    "GENERATION_CONFLICT",
                    "The manifest is in conflict."));
                break;
            default:
                issues.Add(new GenerationExecutionIssue(
                    "MANIFEST_CHANGE_INVALID",
                    $"Unsupported manifest change '{plan.Manifest.ChangeKind}'."));
                break;
        }
    }

    private static void ValidateProjectIntegrationManifestChange(
        GenerationPlan plan,
        ICollection<GenerationExecutionIssue> issues)
    {
        if (plan.ProjectIntegrationManifest is null)
        {
            return;
        }

        if (!TryResolveGeneratedPath(
                plan,
                plan.ProjectIntegrationManifest.RelativePath,
                out var manifestPath,
                out var pathError))
        {
            issues.Add(new GenerationExecutionIssue(
                "PROJECT_INTEGRATION_MANIFEST_PATH_INVALID",
                pathError!));
            return;
        }

        switch (plan.ProjectIntegrationManifest.ChangeKind)
        {
            case GenerationChangeKind.Create:
                EnsureUnoccupied(
                    manifestPath!,
                    plan.ProjectIntegrationManifest.RelativePath,
                    issues);
                break;
            case GenerationChangeKind.Update:
            case GenerationChangeKind.Unchanged:
                VerifyExistingFile(
                    manifestPath!,
                    plan.ProjectIntegrationManifest.ExpectedExistingHash,
                    plan.ProjectIntegrationManifest.RelativePath,
                    issues);
                break;
            case GenerationChangeKind.Conflict:
                issues.Add(new GenerationExecutionIssue(
                    "GENERATION_CONFLICT",
                    "The project integration manifest is in conflict."));
                break;
            default:
                issues.Add(new GenerationExecutionIssue(
                    "PROJECT_INTEGRATION_MANIFEST_CHANGE_INVALID",
                    $"Unsupported project integration manifest change '{plan.ProjectIntegrationManifest.ChangeKind}'."));
                break;
        }
    }

    private static void EnsurePlannedContent(
        PlannedArtifactChange change,
        ICollection<GenerationExecutionIssue> issues)
    {
        if (change.PlannedArtifact is null)
        {
            issues.Add(new GenerationExecutionIssue(
                "PLANNED_CONTENT_MISSING",
                $"Artifact '{change.RelativePath}' has no planned content."));
        }
    }

    private static void EnsureUnoccupied(
        string path,
        string relativePath,
        ICollection<GenerationExecutionIssue> issues)
    {
        if (File.Exists(path) || Directory.Exists(path))
        {
            issues.Add(new GenerationExecutionIssue(
                "TARGET_BECAME_OCCUPIED",
                $"Target '{relativePath}' became occupied after preview."));
        }
    }

    private static void VerifyExistingFile(
        string path,
        string? expectedHash,
        string relativePath,
        ICollection<GenerationExecutionIssue> issues)
    {
        if (!File.Exists(path))
        {
            issues.Add(new GenerationExecutionIssue(
                "MANAGED_FILE_MISSING",
                $"Managed file '{relativePath}' is missing."));
            return;
        }

        if (string.IsNullOrWhiteSpace(expectedHash))
        {
            issues.Add(new GenerationExecutionIssue(
                "EXPECTED_HASH_MISSING",
                $"Managed file '{relativePath}' has no preflight hash."));
            return;
        }

        var actualHash = ComputeFileHash(path);
        if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new GenerationExecutionIssue(
                "MANAGED_FILE_CHANGED",
                $"Managed file '{relativePath}' changed after preview."));
        }
    }

    private static void StageContents(GenerationPlan plan, string stagingRoot)
    {
        foreach (var change in plan.Changes.Where(
                     change => change.ChangeKind is GenerationChangeKind.Create or
                          GenerationChangeKind.Update or
                          GenerationChangeKind.Rename))
        {
            var stagedPath = ResolveTransactionPath(
                plan,
                stagingRoot,
                change.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(stagedPath)!);
            if (change.PreserveExistingContent)
            {
                if (change.ChangeKind != GenerationChangeKind.Rename ||
                    string.IsNullOrWhiteSpace(change.PreviousRelativePath))
                {
                    throw new InvalidOperationException(
                        $"Only a rename can preserve existing content for '{change.RelativePath}'.");
                }

                var sourcePath = ResolveGeneratedPath(plan, change.PreviousRelativePath);
                File.Copy(sourcePath, stagedPath);
                if (!string.Equals(
                        ComputeFileHash(stagedPath),
                        change.ExpectedExistingHash,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException(
                        $"Staged preserved content hash mismatch for '{change.RelativePath}'.");
                }
                continue;
            }

            var artifact = change.PlannedArtifact
                ?? throw new InvalidOperationException(
                    $"Planned content for '{change.RelativePath}' is missing.");
            File.WriteAllText(stagedPath, artifact.Content, Utf8WithoutBom);

            if (!string.Equals(
                    ComputeFileHash(stagedPath),
                    artifact.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException(
                    $"Staged content hash mismatch for '{change.RelativePath}'.");
            }
        }

        if (plan.Manifest.ChangeKind is GenerationChangeKind.Create or GenerationChangeKind.Update)
        {
            var stagedManifest = ResolveTransactionPath(
                plan,
                stagingRoot,
                plan.Manifest.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(stagedManifest)!);
            File.WriteAllText(
                stagedManifest,
                plan.Manifest.ProposedContent,
                Utf8WithoutBom);
        }

        if (plan.ProjectFile?.ChangeKind == GenerationChangeKind.Update)
        {
            var stagedProjectFile = Path.GetFullPath(
                StagedProjectFileName.Replace('/', Path.DirectorySeparatorChar),
                stagingRoot);
            EnsureStrictDescendant(stagedProjectFile, stagingRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(stagedProjectFile)!);
            File.WriteAllText(
                stagedProjectFile,
                plan.ProjectFile.ProposedContent,
                Utf8WithoutBom);
            if (!string.Equals(
                    ComputeFileHash(stagedProjectFile),
                    plan.ProjectFile.ProposedHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("Staged TwinCAT project content hash mismatch.");
            }
        }

        if (plan.TaskFile?.ChangeKind == GenerationChangeKind.Update)
        {
            var stagedTaskFile = Path.GetFullPath(
                StagedTaskFileName.Replace('/', Path.DirectorySeparatorChar),
                stagingRoot);
            EnsureStrictDescendant(stagedTaskFile, stagingRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(stagedTaskFile)!);
            File.WriteAllText(
                stagedTaskFile,
                plan.TaskFile.ProposedContent,
                Utf8WithoutBom);
            if (!string.Equals(
                    ComputeFileHash(stagedTaskFile),
                    plan.TaskFile.ProposedHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("Staged TwinCAT task content hash mismatch.");
            }
        }

        if (plan.ProjectIntegrationManifest?.ChangeKind is
            GenerationChangeKind.Create or GenerationChangeKind.Update)
        {
            var stagedIntegrationManifest = ResolveTransactionPath(
                plan,
                stagingRoot,
                plan.ProjectIntegrationManifest.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(stagedIntegrationManifest)!);
            File.WriteAllText(
                stagedIntegrationManifest,
                plan.ProjectIntegrationManifest.ProposedContent,
                Utf8WithoutBom);
        }
    }

    private static void ApplyProjectFileChange(
        GenerationPlan plan,
        string stagingRoot,
        string backupRoot,
        ICollection<(string BackupPath, string OriginalPath)> backups,
        ICollection<string> writtenTargets)
    {
        if (plan.ProjectFile is null ||
            plan.ProjectFile.ChangeKind == GenerationChangeKind.Unchanged)
        {
            return;
        }
        if (plan.ProjectFile.ChangeKind != GenerationChangeKind.Update)
        {
            throw new InvalidOperationException(
                $"Cannot execute PLC project change '{plan.ProjectFile.ChangeKind}'.");
        }

        var targetPath = plan.ProjectFile.AbsolutePath;
        EnsureProjectFileTarget(plan, targetPath);
        var backupPath = Path.GetFullPath(
            BackedUpProjectFileName.Replace('/', Path.DirectorySeparatorChar),
            backupRoot);
        EnsureStrictDescendant(backupPath, backupRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
        File.Move(targetPath, backupPath);
        backups.Add((backupPath, targetPath));

        var stagedPath = Path.GetFullPath(
            StagedProjectFileName.Replace('/', Path.DirectorySeparatorChar),
            stagingRoot);
        EnsureStrictDescendant(stagedPath, stagingRoot);
        File.Move(stagedPath, targetPath);
        writtenTargets.Add(targetPath);
    }

    private static void ApplyArtifactChange(
        GenerationPlan plan,
        PlannedArtifactChange change,
        string stagingRoot,
        string backupRoot,
        ICollection<(string BackupPath, string OriginalPath)> backups,
        ICollection<string> writtenTargets)
    {
        var targetPath = ResolveGeneratedPath(plan, change.RelativePath);

        switch (change.ChangeKind)
        {
            case GenerationChangeKind.Create:
                MoveStaged(plan, stagingRoot, change.RelativePath, targetPath, writtenTargets);
                break;

            case GenerationChangeKind.Update:
                BackupExisting(plan, targetPath, backupRoot, backups);
                MoveStaged(plan, stagingRoot, change.RelativePath, targetPath, writtenTargets);
                break;

            case GenerationChangeKind.Rename:
                var oldPath = ResolveGeneratedPath(plan, change.PreviousRelativePath!);
                BackupExisting(plan, oldPath, backupRoot, backups);
                MoveStaged(plan, stagingRoot, change.RelativePath, targetPath, writtenTargets);
                break;

            case GenerationChangeKind.Delete:
                BackupExisting(plan, targetPath, backupRoot, backups);
                break;

            case GenerationChangeKind.Unchanged:
                break;

            default:
                throw new InvalidOperationException(
                    $"Cannot execute change kind '{change.ChangeKind}'.");
        }
    }

    private static void ApplyTaskFileChange(
        GenerationPlan plan,
        string stagingRoot,
        string backupRoot,
        ICollection<(string BackupPath, string OriginalPath)> backups,
        ICollection<string> writtenTargets)
    {
        if (plan.TaskFile is null ||
            plan.TaskFile.ChangeKind == GenerationChangeKind.Unchanged)
        {
            return;
        }
        if (plan.TaskFile.ChangeKind != GenerationChangeKind.Update)
        {
            throw new InvalidOperationException(
                $"Cannot execute TwinCAT task change '{plan.TaskFile.ChangeKind}'.");
        }

        var targetPath = plan.TaskFile.AbsolutePath;
        EnsureTaskFileTarget(plan, targetPath);
        var backupPath = Path.GetFullPath(
            BackedUpTaskFileName.Replace('/', Path.DirectorySeparatorChar),
            backupRoot);
        EnsureStrictDescendant(backupPath, backupRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
        File.Move(targetPath, backupPath);
        backups.Add((backupPath, targetPath));

        var stagedPath = Path.GetFullPath(
            StagedTaskFileName.Replace('/', Path.DirectorySeparatorChar),
            stagingRoot);
        EnsureStrictDescendant(stagedPath, stagingRoot);
        File.Move(stagedPath, targetPath);
        writtenTargets.Add(targetPath);
    }

    private static void ApplyManifestChange(
        GenerationPlan plan,
        string stagingRoot,
        string backupRoot,
        ICollection<(string BackupPath, string OriginalPath)> backups,
        ICollection<string> writtenTargets)
    {
        var manifestPath = ResolveGeneratedPath(plan, plan.Manifest.RelativePath);

        switch (plan.Manifest.ChangeKind)
        {
            case GenerationChangeKind.Create:
                MoveStaged(
                    plan,
                    stagingRoot,
                    plan.Manifest.RelativePath,
                    manifestPath,
                    writtenTargets);
                break;

            case GenerationChangeKind.Update:
                BackupExisting(plan, manifestPath, backupRoot, backups);
                MoveStaged(
                    plan,
                    stagingRoot,
                    plan.Manifest.RelativePath,
                    manifestPath,
                    writtenTargets);
                break;

            case GenerationChangeKind.Unchanged:
                break;

            default:
                throw new InvalidOperationException(
                    $"Cannot execute manifest change '{plan.Manifest.ChangeKind}'.");
        }
    }

    private static void ApplyProjectIntegrationManifestChange(
        GenerationPlan plan,
        string stagingRoot,
        string backupRoot,
        ICollection<(string BackupPath, string OriginalPath)> backups,
        ICollection<string> writtenTargets)
    {
        if (plan.ProjectIntegrationManifest is null)
        {
            return;
        }

        var manifestPath = ResolveGeneratedPath(
            plan,
            plan.ProjectIntegrationManifest.RelativePath);
        switch (plan.ProjectIntegrationManifest.ChangeKind)
        {
            case GenerationChangeKind.Create:
                MoveStaged(
                    plan,
                    stagingRoot,
                    plan.ProjectIntegrationManifest.RelativePath,
                    manifestPath,
                    writtenTargets);
                break;
            case GenerationChangeKind.Update:
                BackupExisting(plan, manifestPath, backupRoot, backups);
                MoveStaged(
                    plan,
                    stagingRoot,
                    plan.ProjectIntegrationManifest.RelativePath,
                    manifestPath,
                    writtenTargets);
                break;
            case GenerationChangeKind.Unchanged:
                break;
            default:
                throw new InvalidOperationException(
                    $"Cannot execute project integration manifest change '{plan.ProjectIntegrationManifest.ChangeKind}'.");
        }
    }

    private static void BackupExisting(
        GenerationPlan plan,
        string originalPath,
        string backupRoot,
        ICollection<(string BackupPath, string OriginalPath)> backups)
    {
        var relative = Path.GetRelativePath(plan.GeneratedRoot, originalPath);
        var backupPath = Path.GetFullPath(relative, backupRoot);
        EnsureStrictDescendant(backupPath, backupRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
        File.Move(originalPath, backupPath);
        backups.Add((backupPath, originalPath));
    }

    private static void MoveStaged(
        GenerationPlan plan,
        string stagingRoot,
        string relativePath,
        string targetPath,
        ICollection<string> writtenTargets)
    {
        var stagedPath = ResolveTransactionPath(plan, stagingRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.Move(stagedPath, targetPath);
        writtenTargets.Add(targetPath);
    }

    private static IReadOnlyList<GenerationExecutionIssue> RollBack(
        GenerationPlan plan,
        string stagingRoot,
        string backupRoot,
        IReadOnlyList<(string BackupPath, string OriginalPath)> backups,
        IReadOnlyList<string> writtenTargets,
        bool generatedRootAlreadyExisted)
    {
        var issues = new List<GenerationExecutionIssue>();
        var unresolvedBackup = false;

        for (var index = writtenTargets.Count - 1; index >= 0; index--)
        {
            try
            {
                var target = writtenTargets[index];
                EnsureRollbackTarget(plan, target);
                if (File.Exists(target))
                {
                    File.Delete(target);
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                issues.Add(new GenerationExecutionIssue(
                    "GENERATION_ROLLBACK_FAILED",
                    exception.Message));
            }
        }

        for (var index = backups.Count - 1; index >= 0; index--)
        {
            var (backupPath, originalPath) = backups[index];
            try
            {
                if (!File.Exists(backupPath))
                {
                    continue;
                }

                if (File.Exists(originalPath) || Directory.Exists(originalPath))
                {
                    unresolvedBackup = true;
                    issues.Add(new GenerationExecutionIssue(
                        "GENERATION_ROLLBACK_FAILED",
                        $"Cannot restore '{originalPath}' because the path is occupied. Backup retained at '{backupPath}'."));
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(originalPath)!);
                File.Move(backupPath, originalPath);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                unresolvedBackup = true;
                issues.Add(new GenerationExecutionIssue(
                    "GENERATION_ROLLBACK_FAILED",
                    $"{exception.Message} Backup retained at '{backupPath}'."));
            }
        }

        issues.AddRange(CleanTransactionDirectories(plan.GeneratedRoot, stagingRoot));
        if (!unresolvedBackup)
        {
            issues.AddRange(CleanTransactionDirectories(plan.GeneratedRoot, backupRoot));
        }
        else
        {
            issues.Add(new GenerationExecutionIssue(
                "GENERATION_ROLLBACK_BACKUP_RETAINED",
                $"Rollback is incomplete. Recovery files remain at '{backupRoot}'."));
        }

        if (!generatedRootAlreadyExisted &&
            Directory.Exists(plan.GeneratedRoot) &&
            !Directory.EnumerateFileSystemEntries(plan.GeneratedRoot).Any())
        {
            Directory.Delete(plan.GeneratedRoot, recursive: false);
        }

        return issues;
    }

    private static IReadOnlyList<GenerationExecutionIssue> CleanTransactionDirectories(
        string generatedRoot,
        params string[] transactionDirectories)
    {
        var issues = new List<GenerationExecutionIssue>();

        foreach (var directory in transactionDirectories)
        {
            try
            {
                EnsureTransactionDirectory(directory, generatedRoot);
                if (Directory.Exists(directory))
                {
                    EnsureNoReparsePointsRecursively(directory);
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                issues.Add(new GenerationExecutionIssue(
                    "GENERATION_CLEANUP_FAILED",
                    exception.Message));
            }
        }

        return issues;
    }

    private static void EnsureNoReparsePointsRecursively(string root)
    {
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(root));

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            if (directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new InvalidOperationException(
                    $"Refusing to remove transaction directory containing reparse point '{directory.FullName}'.");
            }

            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    throw new InvalidOperationException(
                        $"Refusing to remove transaction directory containing reparse point '{entry.FullName}'.");
                }

                if (entry is DirectoryInfo childDirectory)
                {
                    pending.Push(childDirectory);
                }
            }
        }
    }

    private static bool TryResolveGeneratedPath(
        GenerationPlan plan,
        string relativePath,
        out string? path,
        out string? error)
    {
        path = null;
        error = null;

        try
        {
            var platformPath = relativePath
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(platformPath) || ContainsTraversalSegment(platformPath))
            {
                error = $"Path '{relativePath}' is not a safe relative artifact path.";
                return false;
            }

            var candidate = Path.GetFullPath(platformPath, plan.ProjectRoot);
            if (!IsStrictDescendant(candidate, plan.GeneratedRoot))
            {
                error = $"Path '{relativePath}' resolves outside the generated root.";
                return false;
            }

            path = candidate;
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = exception.Message;
            return false;
        }
    }

    private static string ResolveGeneratedPath(GenerationPlan plan, string relativePath) =>
        TryResolveGeneratedPath(plan, relativePath, out var path, out var error)
            ? path!
            : throw new InvalidOperationException(error);

    private static bool TryResolveProjectFilePath(
        GenerationPlan plan,
        out string? path,
        out string? error)
    {
        path = null;
        error = null;
        if (plan.ProjectFile is null)
        {
            error = "The generation plan contains no PLC project change.";
            return false;
        }

        try
        {
            var platformPath = plan.ProjectFile.RelativePath
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(platformPath) || ContainsTraversalSegment(platformPath))
            {
                error = "The PLC project path must be a safe relative path.";
                return false;
            }

            var candidate = Path.GetFullPath(platformPath, plan.ProjectRoot);
            if (!string.Equals(
                    Path.GetDirectoryName(candidate),
                    Path.GetFullPath(plan.ProjectRoot).TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar),
                    PathComparison))
            {
                error = "The PLC project must be directly inside the selected project root.";
                return false;
            }

            path = candidate;
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = exception.Message;
            return false;
        }
    }

    private static void EnsureProjectFileTarget(GenerationPlan plan, string path)
    {
        if (!TryResolveProjectFilePath(plan, out var expected, out var error) ||
            !string.Equals(expected, path, PathComparison))
        {
            throw new InvalidOperationException(
                error ?? $"Unexpected PLC project target '{path}'.");
        }
    }

    private static bool TryResolveTaskFilePath(
        GenerationPlan plan,
        out string? path,
        out string? error)
    {
        path = null;
        error = null;
        if (plan.TaskFile is null)
        {
            error = "The generation plan contains no TwinCAT task change.";
            return false;
        }

        try
        {
            var platformPath = plan.TaskFile.RelativePath
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(platformPath) ||
                ContainsTraversalSegment(platformPath) ||
                !platformPath.EndsWith(".TcTTO", StringComparison.OrdinalIgnoreCase))
            {
                error = "The TwinCAT task path must be a safe relative .TcTTO path.";
                return false;
            }

            var candidate = Path.GetFullPath(platformPath, plan.ProjectRoot);
            if (!IsStrictDescendant(candidate, plan.ProjectRoot))
            {
                error = "The TwinCAT task must be inside the selected project root.";
                return false;
            }

            path = candidate;
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = exception.Message;
            return false;
        }
    }

    private static void EnsureTaskFileTarget(GenerationPlan plan, string path)
    {
        if (!TryResolveTaskFilePath(plan, out var expected, out var error) ||
            !string.Equals(expected, path, PathComparison))
        {
            throw new InvalidOperationException(
                error ?? $"Unexpected TwinCAT task target '{path}'.");
        }
    }

    private static void EnsureRollbackTarget(GenerationPlan plan, string target)
    {
        if (IsStrictDescendant(target, plan.GeneratedRoot))
        {
            return;
        }

        if (plan.ProjectFile is not null &&
            string.Equals(target, plan.ProjectFile.AbsolutePath, PathComparison))
        {
            EnsureProjectFileTarget(plan, target);
            return;
        }

        if (plan.TaskFile is not null &&
            string.Equals(target, plan.TaskFile.AbsolutePath, PathComparison))
        {
            EnsureTaskFileTarget(plan, target);
            return;
        }

        throw new InvalidOperationException(
            $"Rollback target '{target}' is outside the managed transaction boundary.");
    }

    private static string ResolveTransactionPath(
        GenerationPlan plan,
        string transactionRoot,
        string artifactRelativePath)
    {
        var targetPath = ResolveGeneratedPath(plan, artifactRelativePath);
        var relativeWithinGenerated = Path.GetRelativePath(plan.GeneratedRoot, targetPath);
        var transactionPath = Path.GetFullPath(relativeWithinGenerated, transactionRoot);
        EnsureStrictDescendant(transactionPath, transactionRoot);
        return transactionPath;
    }

    private static void EnsureTransactionDirectory(string path, string generatedRoot)
    {
        EnsureStrictDescendant(path, generatedRoot);
        var name = Path.GetFileName(path);
        if (!name.StartsWith(".etab-staging-", StringComparison.Ordinal) &&
            !name.StartsWith(".etab-backup-", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Transaction directory '{path}' has an unexpected name.");
        }
    }

    private static void EnsureStrictDescendant(string candidate, string parent)
    {
        if (!IsStrictDescendant(candidate, parent))
        {
            throw new InvalidOperationException(
                $"Path '{candidate}' is outside the expected root '{parent}'.");
        }
    }

    private static bool IsStrictDescendant(string candidate, string parent)
    {
        var relative = Path.GetRelativePath(
            Path.GetFullPath(parent),
            Path.GetFullPath(candidate));
        return relative != "." &&
               !Path.IsPathRooted(relative) &&
               !relative.Equals("..", PathComparison) &&
               !relative.StartsWith($"..{Path.DirectorySeparatorChar}", PathComparison) &&
               !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", PathComparison);
    }

    private static bool IsSameOrStrictDescendant(string candidate, string parent) =>
        string.Equals(
            Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            PathComparison) ||
        IsStrictDescendant(candidate, parent);

    private static bool ContainsTraversalSegment(string path) =>
        path.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment is "." or "..");

    private static bool ContainsReparsePointBelowRoot(string root, string candidate)
    {
        var resolvedRoot = Path.GetFullPath(root);
        var current = File.Exists(candidate)
            ? new FileInfo(candidate).Directory
            : Directory.Exists(candidate)
                ? new DirectoryInfo(candidate)
                : new DirectoryInfo(Path.GetDirectoryName(candidate)!);

        while (current is not null && IsStrictDescendant(current.FullName, resolvedRoot))
        {
            if (current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return true;
            }

            current = current.Parent;
        }

        if (File.Exists(candidate))
        {
            return File.GetAttributes(candidate).HasFlag(FileAttributes.ReparsePoint);
        }

        return false;
    }

    private static string ComputeFileHash(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
            .ToLowerInvariant();

    private static GenerationExecutionResult Failed(
        IReadOnlyList<GenerationExecutionIssue> issues) =>
        new(false, 0, 0, 0, 0, issues);
}
