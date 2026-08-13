using ETAB.Engineering.Core.Execution;
using ETAB.Engineering.Core.Generation;
using ETAB.Engineering.Core.Planning;
using ETAB.Engineering.Core.ProjectIntegration;
using ETAB.Engineering.Core.Validation;

namespace ETAB.Engineering.Cli;

internal static class Program
{
    private const int ExitSuccess = 0;
    private const int ExitValidationFailed = 1;
    private const int ExitUsageError = 2;
    private const int ExitUnexpectedError = 3;

    public static async Task<int> Main(string[] args)
    {
        if (!TryParseCommand(args, out var options, out var usageError))
        {
            if (!string.IsNullOrWhiteSpace(usageError))
            {
                Console.Error.WriteLine($"Error: {usageError}");
                Console.Error.WriteLine();
            }

            PrintUsage();
            return ExitUsageError;
        }

        try
        {
            var validator = new ProjectValidator();
            var result = await validator.ValidateFilesAsync(options!.ProjectPath, options.SchemaPath);
            var fullProjectPath = Path.GetFullPath(options.ProjectPath);

            if (!result.IsValid)
            {
                Console.Error.WriteLine($"INVALID {fullProjectPath}");
                foreach (var issue in result.Issues)
                {
                    Console.Error.WriteLine($"[{issue.Code}] {issue.Path}: {issue.Message}");
                }

                return ExitValidationFailed;
            }

            if (options.Command == CliCommand.Validate)
            {
                PrintValidationSuccess(fullProjectPath, result);
                return ExitSuccess;
            }

            var preview = new ArtifactPreviewGenerator().Generate(result.Project!);
            var projectRoot = options.ProjectRoot ?? Path.GetDirectoryName(fullProjectPath)!;
            var plan = new GenerationPlanBuilder().Build(projectRoot, result.Project!, preview);
            if (options.IntegrateProject)
            {
                plan = new TwinCatProjectIntegrationPlanBuilder().Build(
                    plan,
                    result.Project!,
                    preview);
            }

            switch (options.Command)
            {
                case CliCommand.Preview:
                    PrintPlan("PREVIEW", fullProjectPath, preview, plan, options.ShowContent);
                    return plan.HasConflicts ? ExitValidationFailed : ExitSuccess;

                case CliCommand.Check:
                    var isSynchronized = IsSynchronized(plan);
                    PrintPlan(
                        isSynchronized ? "CHECK SYNCHRONIZED" : "CHECK OUT-OF-DATE",
                        fullProjectPath,
                        preview,
                        plan,
                        showContent: false);
                    return isSynchronized ? ExitSuccess : ExitValidationFailed;

                case CliCommand.Generate:
                    PrintPlan("GENERATE", fullProjectPath, preview, plan, showContent: false);
                    if (plan.HasConflicts)
                    {
                        Console.Error.WriteLine("Generation blocked: resolve all conflicts first.");
                        return ExitValidationFailed;
                    }

                    var execution = new GenerationExecutor().Execute(plan);
                    foreach (var issue in execution.Issues)
                    {
                        Console.Error.WriteLine($"[{issue.Code}] {issue.Message}");
                    }

                    if (!execution.Success)
                    {
                        return ExitUnexpectedError;
                    }

                    Console.WriteLine(
                        $"GENERATED create={execution.Created} update={execution.Updated} " +
                        $"rename={execution.Renamed} delete={execution.Deleted}");
                    return ExitSuccess;

                default:
                    throw new InvalidOperationException(
                        $"Unsupported command '{options.Command}'.");
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Unexpected error: {exception.Message}");
            return ExitUnexpectedError;
        }
    }

    private static void PrintValidationSuccess(
        string fullProjectPath,
        ProjectValidationResult result)
    {
        Console.WriteLine($"VALID {fullProjectPath}");
        Console.WriteLine($"Project: {result.Project!.Project.Name}");
        Console.WriteLine($"Nodes: {result.Project.Nodes.Count}");
        Console.WriteLine($"Relations: {result.Project.Relations.Count}");
    }

    private static bool IsSynchronized(GenerationPlan plan) =>
        !plan.HasConflicts &&
        plan.Manifest.ChangeKind == GenerationChangeKind.Unchanged &&
        (plan.ProjectFile is null ||
         plan.ProjectFile.ChangeKind == GenerationChangeKind.Unchanged) &&
        (plan.ProjectIntegrationManifest is null ||
         plan.ProjectIntegrationManifest.ChangeKind == GenerationChangeKind.Unchanged) &&
        plan.Changes.All(change => change.ChangeKind == GenerationChangeKind.Unchanged);

    private static void PrintPlan(
        string heading,
        string fullProjectPath,
        GenerationPreview preview,
        GenerationPlan plan,
        bool showContent)
    {
        Console.WriteLine($"{heading} {fullProjectPath}");
        Console.WriteLine($"Project: {preview.ProjectName}");
        Console.WriteLine($"Root: {plan.ProjectRoot}");
        Console.WriteLine($"Artifacts: {preview.Artifacts.Count}");
        Console.WriteLine(
            $"Manifest: [{plan.Manifest.ChangeKind.ToContractName()}] {plan.Manifest.RelativePath}");
        if (plan.ProjectFile is not null)
        {
            Console.WriteLine(
                $"PLC project: [{plan.ProjectFile.ChangeKind.ToContractName()}] " +
                plan.ProjectFile.RelativePath);
            if (!string.IsNullOrWhiteSpace(plan.ProjectFile.Message))
            {
                Console.WriteLine($"  {plan.ProjectFile.Message}");
            }
        }
        if (plan.ProjectIntegrationManifest is not null)
        {
            Console.WriteLine(
                $"Project integration manifest: " +
                $"[{plan.ProjectIntegrationManifest.ChangeKind.ToContractName()}] " +
                plan.ProjectIntegrationManifest.RelativePath);
        }

        foreach (var issue in plan.Issues)
        {
            Console.WriteLine($"[{issue.Code}] {issue.Path}: {issue.Message}");
        }

        foreach (var change in plan.Changes)
        {
            var operation = change.ChangeKind.ToContractName();
            var kind = change.ArtifactKind.ToContractName();
            var path = change.ChangeKind == GenerationChangeKind.Rename
                ? $"{change.PreviousRelativePath} -> {change.RelativePath}"
                : change.RelativePath;

            Console.WriteLine($"[{operation}] [{kind}] {path}");
            if (!string.IsNullOrWhiteSpace(change.Message))
            {
                Console.WriteLine($"  {change.Message}");
            }

            if (change.PlannedArtifact is not null)
            {
                Console.WriteLine($"  TwinCAT GUID: {{{change.PlannedArtifact.TwinCatGuid:D}}}");
                Console.WriteLine($"  SHA-256: {change.PlannedArtifact.Sha256}");
            }
        }

        if (!showContent)
        {
            return;
        }

        foreach (var artifact in preview.Artifacts)
        {
            Console.WriteLine();
            Console.WriteLine($"--- {artifact.RelativePath} ---");
            Console.Write(artifact.Content);
        }

        Console.WriteLine();
        Console.WriteLine($"--- {plan.Manifest.RelativePath} ---");
        Console.Write(plan.Manifest.ProposedContent);
        if (plan.ProjectFile is not null)
        {
            Console.WriteLine();
            Console.WriteLine($"--- {plan.ProjectFile.RelativePath} ---");
            Console.Write(plan.ProjectFile.ProposedContent);
        }
        if (plan.ProjectIntegrationManifest is not null)
        {
            Console.WriteLine();
            Console.WriteLine($"--- {plan.ProjectIntegrationManifest.RelativePath} ---");
            Console.Write(plan.ProjectIntegrationManifest.ProposedContent);
        }
    }

    private static bool TryParseCommand(
        string[] args,
        out CliOptions? options,
        out string? error)
    {
        options = null;
        error = null;

        if (args.Length == 0 || IsHelpArgument(args[0]))
        {
            return false;
        }

        var command = args[0].ToLowerInvariant() switch
        {
            "validate" => CliCommand.Validate,
            "preview" => CliCommand.Preview,
            "check" => CliCommand.Check,
            "generate" => CliCommand.Generate,
            _ => (CliCommand?)null
        };

        if (command is null)
        {
            error = $"Unknown command '{args[0]}'.";
            return false;
        }

        string? projectPath = null;
        var schemaPath = Path.Combine(AppContext.BaseDirectory, "schemas", "etab-project.schema.json");
        var showContent = false;
        string? projectRoot = null;
        var integrateProject = false;

        for (var index = 1; index < args.Length; index++)
        {
            var argument = args[index];
            if (string.Equals(argument, "--schema", StringComparison.OrdinalIgnoreCase))
            {
                if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
                {
                    error = "Option '--schema' requires a file path.";
                    return false;
                }

                schemaPath = args[index];
                continue;
            }

            if (string.Equals(argument, "--content", StringComparison.OrdinalIgnoreCase))
            {
                if (command != CliCommand.Preview)
                {
                    error = "Option '--content' is only valid for the preview command.";
                    return false;
                }

                showContent = true;
                continue;
            }

            if (string.Equals(argument, "--root", StringComparison.OrdinalIgnoreCase))
            {
                if (command == CliCommand.Validate)
                {
                    error = "Option '--root' is valid only for preview, check and generate.";
                    return false;
                }

                if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
                {
                    error = "Option '--root' requires a directory path.";
                    return false;
                }

                projectRoot = args[index];
                continue;
            }

            if (string.Equals(argument, "--integrate-project", StringComparison.OrdinalIgnoreCase))
            {
                if (command == CliCommand.Validate)
                {
                    error = "Option '--integrate-project' is valid only for preview, check and generate.";
                    return false;
                }

                integrateProject = true;
                continue;
            }

            if (argument.StartsWith('-'))
            {
                error = $"Unknown option '{argument}'.";
                return false;
            }

            if (projectPath is not null)
            {
                error = "Only one project file can be processed at a time.";
                return false;
            }

            projectPath = argument;
        }

        if (projectPath is null)
        {
            error = $"The {command.Value.ToString().ToLowerInvariant()} command requires a project file.";
            return false;
        }

        if (integrateProject && string.IsNullOrWhiteSpace(projectRoot))
        {
            error = "Option '--integrate-project' requires an explicit '--root' TwinCAT project directory.";
            return false;
        }

        options = new CliOptions(
            command.Value,
            projectPath,
            schemaPath,
            showContent,
            projectRoot,
            integrateProject);
        return true;
    }

    private static bool IsHelpArgument(string value) =>
        value is "--help" or "-h" or "help";

    private static void PrintUsage()
    {
        Console.WriteLine("ETAB Engineering CLI");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  etab validate <project-file> [--schema <schema-file>]");
        Console.WriteLine("  etab preview  <project-file> [--schema <schema-file>] [--root <directory>] [--integrate-project] [--content]");
        Console.WriteLine("  etab check    <project-file> [--schema <schema-file>] [--root <directory>] [--integrate-project]");
        Console.WriteLine("  etab generate <project-file> [--schema <schema-file>] [--root <directory>] [--integrate-project]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  validate  Validate schema and project semantics");
        Console.WriteLine("  preview   Compare planned DUT artifacts and manifest without writing files");
        Console.WriteLine("  check     Exit successfully only when generated files are synchronized");
        Console.WriteLine("  generate  Apply a conflict-free plan and write the manifest last");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --integrate-project  Manage generated Compile/Folder entries and the ETAB library reference in the configured .plcproj");
        Console.WriteLine();
        Console.WriteLine("Exit codes:");
        Console.WriteLine("  0  Command completed successfully");
        Console.WriteLine("  1  Validation/conflict failure or generated state is out of date");
        Console.WriteLine("  2  Invalid command-line arguments");
        Console.WriteLine("  3  Unexpected execution error");
    }

    private enum CliCommand
    {
        Validate,
        Preview,
        Check,
        Generate
    }

    private sealed record CliOptions(
        CliCommand Command,
        string ProjectPath,
        string SchemaPath,
        bool ShowContent,
        string? ProjectRoot,
        bool IntegrateProject);
}
