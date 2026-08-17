using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using ETAB.Engineering.Core.Generation;
using ETAB.Engineering.Core.Model;
using ETAB.Engineering.Core.Planning;

namespace ETAB.Engineering.Core.ProjectIntegration;

public sealed class TwinCatProjectIntegrationPlanBuilder
{
    private const string MsBuildNamespace = "http://schemas.microsoft.com/developer/msbuild/2003";
    private const string EtabLibraryName = "EngineeringToolboxAutomationBase";
    private const string EtabLibraryPublisher = "NiklasW";

    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private sealed record RuntimeTaskPlan(
        PlannedProjectFileChange? Change,
        ManagedTaskPouCall? ManagedCall);

    public GenerationPlan Build(
        GenerationPlan basePlan,
        EtabProjectDocument project,
        GenerationPreview preview)
    {
        ArgumentNullException.ThrowIfNull(basePlan);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(preview);

        if (basePlan.HasConflicts)
        {
            return basePlan;
        }

        if (!TryResolveProjectFile(
                basePlan.ProjectRoot,
                project.Project.TwinCat.PlcProject,
                out var projectFilePath,
                out var projectFileRelativePath,
                out var pathError))
        {
            return WithIssues(
                basePlan,
                [new GenerationPlanIssue(
                    "PLC_PROJECT_INVALID",
                    "/project/twinCAT/plcProject",
                    pathError!)]);
        }

        string projectFileContent;
        string projectFileHash;
        XDocument document;
        try
        {
            projectFileContent = File.ReadAllText(projectFilePath!, Encoding.UTF8);
            projectFileHash = ComputeFileHash(projectFilePath!);
            document = XDocument.Parse(
                projectFileContent,
                LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or XmlException)
        {
            return WithIssues(
                basePlan,
                [new GenerationPlanIssue(
                    "PLC_PROJECT_READ_FAILED",
                    projectFileRelativePath!,
                    exception.Message)]);
        }

        var ns = XNamespace.Get(MsBuildNamespace);
        if (document.Root?.Name != ns + "Project")
        {
            return WithIssues(
                basePlan,
                [new GenerationPlanIssue(
                    "PLC_PROJECT_FORMAT",
                    projectFileRelativePath!,
                    "The PLC project must use the TwinCAT MSBuild project namespace.")]);
        }

        var manifestRelativePath = NormalizeManifestPath(
            Path.GetRelativePath(
                basePlan.ProjectRoot,
                Path.Combine(
                    basePlan.GeneratedRoot,
                    ProjectIntegrationManifestSerializer.FileName)));
        var manifestPath = Path.GetFullPath(
            manifestRelativePath.Replace('/', Path.DirectorySeparatorChar),
            basePlan.ProjectRoot);

        if (Directory.Exists(manifestPath))
        {
            return WithIssues(
                basePlan,
                [new GenerationPlanIssue(
                    "PROJECT_INTEGRATION_MANIFEST_OCCUPIED",
                    manifestRelativePath,
                    "The project integration manifest path is occupied by a directory.")]);
        }

        ProjectIntegrationManifest? existingManifest = null;
        string? existingManifestContent = null;
        string? existingManifestHash = null;
        if (File.Exists(manifestPath))
        {
            try
            {
                existingManifestContent = File.ReadAllText(manifestPath, Encoding.UTF8);
                existingManifestHash = ComputeFileHash(manifestPath);
                existingManifest = ProjectIntegrationManifestSerializer.Deserialize(
                    existingManifestContent);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or JsonException)
            {
                return WithIssues(
                    basePlan,
                    [new GenerationPlanIssue(
                        "PROJECT_INTEGRATION_MANIFEST_INVALID",
                        manifestRelativePath,
                        exception.Message)]);
            }
        }

        var issues = ValidateManifest(
            existingManifest,
            project,
            projectFileRelativePath!).ToList();
        var managedCompileIncludes = new HashSet<string>(
            existingManifest?.ManagedCompileIncludes ?? [],
            PathComparer);
        issues.AddRange(TwinCatCompiledObjectCollisionScanner.FindCollisions(
            basePlan.ProjectRoot,
            document,
            ns,
            preview.Artifacts,
            managedCompileIncludes));
        if (issues.Count > 0)
        {
            return WithProjectConflict(
                basePlan,
                projectFileRelativePath!,
                projectFilePath!,
                projectFileContent,
                projectFileHash,
                manifestRelativePath,
                existingManifestHash,
                issues);
        }

        var desiredCompileIncludes = preview.Artifacts
            .Select(artifact => ToProjectInclude(artifact.RelativePath))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        var desiredFolderIncludes = BuildFolderIncludes(desiredCompileIncludes);
        var managedFolderIncludes = new HashSet<string>(
            existingManifest?.ManagedFolderIncludes ?? [],
            PathComparer);
        var proposedManagedCompileIncludes = new HashSet<string>(PathComparer);
        var proposedManagedFolderIncludes = new HashSet<string>(PathComparer);
        var newline = DetectNewline(projectFileContent);
        var projectMutations = new List<string>();
        var proposedProjectContent = projectFileContent;

        PlanCompileEntries(
            document,
            ns,
            ref proposedProjectContent,
            desiredCompileIncludes,
            managedCompileIncludes,
            proposedManagedCompileIncludes,
            newline,
            projectMutations,
            issues);
        PlanFolderEntries(
            document,
            ns,
            ref proposedProjectContent,
            desiredFolderIncludes,
            managedFolderIncludes,
            proposedManagedFolderIncludes,
            newline,
            projectMutations,
            issues);

        var proposedReference = PlanPlaceholderReference(
            document,
            ns,
            ref proposedProjectContent,
            project.Project.EtabLibrary.Placeholder,
            existingManifest?.ManagedPlaceholderReference,
            newline,
            projectMutations,
            issues);
        var proposedResolution = PlanPlaceholderResolution(
            document,
            ns,
            ref proposedProjectContent,
            project.Project.EtabLibrary.Placeholder,
            project.Project.EtabLibrary.Version,
            existingManifest?.ManagedPlaceholderResolution,
            newline,
            projectMutations,
            issues);
        var taskPlan = PlanRuntimeTask(
            basePlan.ProjectRoot,
            document,
            ns,
            project,
            preview,
            existingManifest?.ManagedTaskPouCall,
            issues);

        if (issues.Count > 0)
        {
            return WithProjectConflict(
                basePlan,
                projectFileRelativePath!,
                projectFilePath!,
                projectFileContent,
                projectFileHash,
                manifestRelativePath,
                existingManifestHash,
                issues);
        }

        try
        {
            _ = XDocument.Parse(proposedProjectContent, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException exception)
        {
            return WithProjectConflict(
                basePlan,
                projectFileRelativePath!,
                projectFilePath!,
                projectFileContent,
                projectFileHash,
                manifestRelativePath,
                existingManifestHash,
                [new GenerationPlanIssue(
                    "PLC_PROJECT_XML_INVALID",
                    projectFileRelativePath!,
                    exception.Message)]);
        }

        var proposedManifest = new ProjectIntegrationManifest
        {
            ManifestVersion = ProjectIntegrationManifestSerializer.CurrentManifestVersion,
            ProjectId = project.Project.Id,
            PlcProject = projectFileRelativePath!,
            ManagedCompileIncludes = proposedManagedCompileIncludes
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList(),
            ManagedFolderIncludes = proposedManagedFolderIncludes
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList(),
            ManagedPlaceholderReference = proposedReference,
            ManagedPlaceholderResolution = proposedResolution,
            ManagedTaskPouCall = taskPlan.ManagedCall
        };
        var proposedManifestContent = ProjectIntegrationManifestSerializer.Serialize(
            proposedManifest);

        var projectChangeKind = string.Equals(
            projectFileContent,
            proposedProjectContent,
            StringComparison.Ordinal)
            ? GenerationChangeKind.Unchanged
            : GenerationChangeKind.Update;
        var manifestChangeKind = existingManifestContent is null
            ? GenerationChangeKind.Create
            : string.Equals(
                existingManifestContent,
                proposedManifestContent,
                StringComparison.Ordinal)
                ? GenerationChangeKind.Unchanged
                : GenerationChangeKind.Update;

        return new GenerationPlan(
            basePlan.ProjectRoot,
            basePlan.GeneratedRoot,
            basePlan.Changes,
            basePlan.Manifest,
            basePlan.Issues,
            new PlannedProjectFileChange(
                projectChangeKind,
                projectFileRelativePath!,
                projectFilePath!,
                proposedProjectContent,
                ComputeContentHash(proposedProjectContent),
                projectFileHash,
                projectMutations.Count == 0
                    ? null
                    : string.Join("; ", projectMutations)),
            new PlannedProjectIntegrationManifestChange(
                manifestChangeKind,
                manifestRelativePath,
                proposedManifestContent,
                existingManifestHash,
                null),
            taskPlan.Change);
    }

    private static RuntimeTaskPlan PlanRuntimeTask(
        string projectRoot,
        XDocument projectDocument,
        XNamespace projectNamespace,
        EtabProjectDocument project,
        GenerationPreview preview,
        ManagedTaskPouCall? existingManagedCall,
        ICollection<GenerationPlanIssue> issues)
    {
        var runtimeEnabled = project.Project.Generation.RuntimeExecution;
        if (!runtimeEnabled && existingManagedCall is null)
        {
            return new RuntimeTaskPlan(null, null);
        }

        var programName = $"PRG_{project.Project.Prefix}_Generated";
        if (runtimeEnabled && !preview.Artifacts.Any(
                artifact => artifact.Kind == GeneratedArtifactKind.ProgramCallStructure &&
                            string.Equals(
                                artifact.Name,
                                programName,
                                StringComparison.Ordinal)))
        {
            issues.Add(new GenerationPlanIssue(
                "RUNTIME_PROGRAM_MISSING",
                "/project/generation/runtimeExecution",
                $"Runtime execution requires the generated program '{programName}'."));
            return new RuntimeTaskPlan(null, existingManagedCall);
        }

        var compiledTaskIncludes = projectDocument
            .Descendants(projectNamespace + "Compile")
            .Select(element => (string?)element.Attribute("Include"))
            .Where(include =>
                !string.IsNullOrWhiteSpace(include) &&
                include.EndsWith(".TcTTO", StringComparison.OrdinalIgnoreCase))
            .Select(include => include!)
            .Distinct(PathComparer)
            .OrderBy(include => include, StringComparer.Ordinal)
            .ToArray();

        string? taskInclude;
        if (existingManagedCall is not null)
        {
            taskInclude = ToProjectInclude(existingManagedCall.TaskFile);
            if (!compiledTaskIncludes.Contains(taskInclude, PathComparer))
            {
                issues.Add(new GenerationPlanIssue(
                    "PLC_TASK_MANAGED_CHANGED",
                    existingManagedCall.TaskFile,
                    "The manifest-managed TwinCAT task is no longer compiled by the PLC project."));
                return new RuntimeTaskPlan(null, existingManagedCall);
            }
        }
        else if (!runtimeEnabled)
        {
            return new RuntimeTaskPlan(null, null);
        }
        else
        {
            taskInclude = DetectRuntimeTask(
                projectRoot,
                compiledTaskIncludes,
                issues);
            if (taskInclude is null)
            {
                return new RuntimeTaskPlan(null, null);
            }
        }

        if (!TryResolveTaskFile(
                projectRoot,
                taskInclude!,
                out var taskPath,
                out var taskRelativePath,
                out var taskPathError))
        {
            issues.Add(new GenerationPlanIssue(
                "PLC_TASK_INVALID",
                taskInclude!,
                taskPathError!));
            return new RuntimeTaskPlan(null, existingManagedCall);
        }

        string taskContent;
        string taskHash;
        XDocument taskDocument;
        try
        {
            taskContent = File.ReadAllText(taskPath!, Encoding.UTF8);
            taskHash = ComputeFileHash(taskPath!);
            taskDocument = XDocument.Parse(
                taskContent,
                LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or XmlException)
        {
            issues.Add(new GenerationPlanIssue(
                "PLC_TASK_READ_FAILED",
                taskRelativePath!,
                exception.Message));
            return new RuntimeTaskPlan(null, existingManagedCall);
        }

        var taskElements = taskDocument.Descendants("Task").ToArray();
        if (taskDocument.Root?.Name.LocalName != "TcPlcObject" || taskElements.Length != 1)
        {
            issues.Add(new GenerationPlanIssue(
                "PLC_TASK_FORMAT",
                taskRelativePath!,
                "The selected TwinCAT task file must contain exactly one Task element."));
            return new RuntimeTaskPlan(null, existingManagedCall);
        }

        var taskElement = taskElements[0];
        var taskName = (string?)taskElement.Attribute("Name") ??
                       Path.GetFileNameWithoutExtension(taskRelativePath);
        var proposedTaskContent = taskContent;
        var taskMutations = new List<string>();
        var proposedManagedCall = existingManagedCall;

        if (existingManagedCall is not null)
        {
            var managedMatches = FindPouCalls(taskElement, existingManagedCall.ProgramName);
            if (managedMatches.Count != 1 || !IsStandardPouCall(managedMatches.SingleOrDefault()))
            {
                issues.Add(new GenerationPlanIssue(
                    "PLC_TASK_MANAGED_CHANGED",
                    taskRelativePath!,
                    $"The managed call to '{existingManagedCall.ProgramName}' is missing, duplicated, or was changed outside ETAB Engineering."));
                return new RuntimeTaskPlan(null, existingManagedCall);
            }

            if (!runtimeEnabled || !string.Equals(
                    existingManagedCall.ProgramName,
                    programName,
                    StringComparison.Ordinal))
            {
                RemovePouCallText(
                    ref proposedTaskContent,
                    existingManagedCall.ProgramName);
                managedMatches[0].Remove();
                taskMutations.Add($"remove runtime call {existingManagedCall.ProgramName}");
                proposedManagedCall = null;
            }
        }

        if (runtimeEnabled && proposedManagedCall is null)
        {
            var desiredMatches = FindPouCalls(taskElement, programName);
            if (desiredMatches.Count > 1 ||
                (desiredMatches.Count == 1 && !IsStandardPouCall(desiredMatches[0])))
            {
                issues.Add(new GenerationPlanIssue(
                    "PLC_TASK_CALL_CONFLICT",
                    taskRelativePath!,
                    $"The TwinCAT task contains a duplicated or non-standard call to '{programName}'."));
                return new RuntimeTaskPlan(null, existingManagedCall);
            }

            if (desiredMatches.Count == 0)
            {
                AppendPouCallText(ref proposedTaskContent, programName);
                taskElement.Add(new XElement(
                    "PouCall",
                    new XElement("Name", programName)));
                taskMutations.Add($"add {programName} to task {taskName}");
                proposedManagedCall = new ManagedTaskPouCall
                {
                    TaskFile = taskRelativePath!,
                    ProgramName = programName
                };
            }
        }

        if (issues.Count > 0)
        {
            return new RuntimeTaskPlan(null, existingManagedCall);
        }

        try
        {
            _ = XDocument.Parse(proposedTaskContent, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException exception)
        {
            issues.Add(new GenerationPlanIssue(
                "PLC_TASK_XML_INVALID",
                taskRelativePath!,
                exception.Message));
            return new RuntimeTaskPlan(null, existingManagedCall);
        }

        var changeKind = string.Equals(
            taskContent,
            proposedTaskContent,
            StringComparison.Ordinal)
            ? GenerationChangeKind.Unchanged
            : GenerationChangeKind.Update;
        return new RuntimeTaskPlan(
            new PlannedProjectFileChange(
                changeKind,
                taskRelativePath!,
                taskPath!,
                proposedTaskContent,
                ComputeContentHash(proposedTaskContent),
                taskHash,
                taskMutations.Count == 0
                    ? $"Runtime program is already assigned to task {taskName}."
                    : string.Join("; ", taskMutations)),
            proposedManagedCall);
    }

    private static string? DetectRuntimeTask(
        string projectRoot,
        IReadOnlyList<string> compiledTaskIncludes,
        ICollection<GenerationPlanIssue> issues)
    {
        if (compiledTaskIncludes.Count == 0)
        {
            issues.Add(new GenerationPlanIssue(
                "PLC_TASK_NOT_FOUND",
                "/project/generation/runtimeExecution",
                "No compiled TwinCAT task (.TcTTO) was found in the linked PLC project."));
            return null;
        }
        if (compiledTaskIncludes.Count == 1)
        {
            return compiledTaskIncludes[0];
        }

        var mainTasks = new List<string>();
        foreach (var include in compiledTaskIncludes)
        {
            if (!TryResolveTaskFile(
                    projectRoot,
                    include,
                    out var taskPath,
                    out _,
                    out _))
            {
                continue;
            }

            try
            {
                var document = XDocument.Load(taskPath!, LoadOptions.PreserveWhitespace);
                if (document.Descendants("PouCall").Any(call => string.Equals(
                        call.Element("Name")?.Value,
                        "MAIN",
                        StringComparison.OrdinalIgnoreCase)))
                {
                    mainTasks.Add(include);
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or XmlException)
            {
                issues.Add(new GenerationPlanIssue(
                    "PLC_TASK_READ_FAILED",
                    include,
                    exception.Message));
                return null;
            }
        }

        if (mainTasks.Count == 1)
        {
            return mainTasks[0];
        }

        issues.Add(new GenerationPlanIssue(
            "PLC_TASK_AMBIGUOUS",
            "/project/generation/runtimeExecution",
            "Multiple compiled TwinCAT tasks were found and no unique task calling MAIN could be selected automatically."));
        return null;
    }

    private static bool TryResolveTaskFile(
        string projectRoot,
        string configuredPath,
        out string? absolutePath,
        out string? relativePath,
        out string? error)
    {
        absolutePath = null;
        relativePath = null;
        error = null;

        try
        {
            var normalized = configuredPath
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(normalized) ||
                normalized.Split(
                        [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                        StringSplitOptions.RemoveEmptyEntries)
                    .Any(segment => segment is "." or "..") ||
                !normalized.EndsWith(".TcTTO", StringComparison.OrdinalIgnoreCase))
            {
                error = "The TwinCAT task must be a safe relative .TcTTO path.";
                return false;
            }

            var root = Path.GetFullPath(projectRoot).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            var candidate = Path.GetFullPath(normalized, root);
            var boundary = root + Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(boundary, StringComparison.OrdinalIgnoreCase))
            {
                error = "The TwinCAT task resolves outside the selected PLC project root.";
                return false;
            }
            if (!File.Exists(candidate))
            {
                error = $"The compiled TwinCAT task does not exist: {candidate}";
                return false;
            }

            if (File.GetAttributes(candidate).HasFlag(FileAttributes.ReparsePoint))
            {
                error = "TwinCAT task integration through a reparse point is not allowed.";
                return false;
            }

            var current = new DirectoryInfo(Path.GetDirectoryName(candidate)!);
            while (current.FullName.StartsWith(boundary, StringComparison.OrdinalIgnoreCase))
            {
                if (current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    error = "TwinCAT task integration through a reparse point is not allowed.";
                    return false;
                }
                if (current.Parent is null ||
                    string.Equals(current.FullName, root, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
                current = current.Parent;
            }

            absolutePath = candidate;
            relativePath = NormalizeManifestPath(Path.GetRelativePath(root, candidate));
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException or
                IOException or UnauthorizedAccessException)
        {
            error = exception.Message;
            return false;
        }
    }

    private static List<XElement> FindPouCalls(XElement task, string programName) =>
        task.Elements("PouCall")
            .Where(call => string.Equals(
                call.Element("Name")?.Value,
                programName,
                StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static bool IsStandardPouCall(XElement? call) =>
        call is not null &&
        !call.HasAttributes &&
        call.Elements().Count() == 1 &&
        call.Element("Name") is { HasAttributes: false } name &&
        !name.HasElements;

    private static void RemovePouCallText(ref string content, string programName)
    {
        var escapedName = Regex.Escape(programName);
        var expression =
            $@"(?ms)^[ \t]*<PouCall>[ \t]*(?:\r?\n)?[ \t]*<Name>[ \t]*{escapedName}[ \t]*</Name>[ \t]*(?:\r?\n)?[ \t]*</PouCall>[ \t]*(?:\r?\n)?";
        var matches = Regex.Matches(
            content,
            expression,
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        if (matches.Count != 1)
        {
            throw new InvalidOperationException(
                $"Could not locate the managed task call '{programName}' in the original XML text.");
        }
        content = content.Remove(matches[0].Index, matches[0].Length);
    }

    private static void AppendPouCallText(ref string content, string programName)
    {
        var newline = DetectNewline(content);
        var existingCallMatches = Regex.Matches(
            content,
            @"(?m)^(?<indent>[ \t]*)</PouCall>[ \t]*\r?$",
            RegexOptions.CultureInvariant);
        string indentation;
        int insertionIndex;
        if (existingCallMatches.Count > 0)
        {
            var last = existingCallMatches[^1];
            indentation = last.Groups["indent"].Value;
            insertionIndex = last.Index + last.Length;
            if (insertionIndex < content.Length && content[insertionIndex] == '\n')
            {
                insertionIndex++;
            }
        }
        else
        {
            var taskEnd = content.LastIndexOf("</Task>", StringComparison.Ordinal);
            if (taskEnd < 0)
            {
                throw new InvalidOperationException("Could not locate the TwinCAT Task closing tag.");
            }
            var taskIndent = DetectLineIndentation(content, taskEnd);
            indentation = taskIndent + "  ";
            insertionIndex = content.LastIndexOf('\n', Math.Max(0, taskEnd - 1));
            insertionIndex = insertionIndex < 0 ? 0 : insertionIndex + 1;
        }

        var block =
            $"{indentation}<PouCall>{newline}" +
            $"{indentation}  <Name>{EscapeText(programName)}</Name>{newline}" +
            $"{indentation}</PouCall>{newline}";
        content = content.Insert(insertionIndex, block);
    }

    private static string DetectLineIndentation(string content, int position)
    {
        var lineStart = content.LastIndexOf('\n', Math.Max(0, position - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        var length = 0;
        while (lineStart + length < content.Length &&
               content[lineStart + length] is ' ' or '\t')
        {
            length++;
        }
        return content.Substring(lineStart, length);
    }

    private static void PlanCompileEntries(
        XDocument document,
        XNamespace ns,
        ref string projectContent,
        IReadOnlyCollection<string> desiredIncludes,
        IReadOnlySet<string> managedIncludes,
        ISet<string> proposedManagedIncludes,
        string newline,
        ICollection<string> projectMutations,
        ICollection<GenerationPlanIssue> issues)
    {
        foreach (var oldManaged in managedIncludes.OrderBy(path => path, StringComparer.Ordinal))
        {
            var matches = FindByInclude(document, ns + "Compile", oldManaged);
            if (matches.Count != 1 || !IsStandardCompile(matches.SingleOrDefault(), ns))
            {
                issues.Add(new GenerationPlanIssue(
                    "PLC_COMPILE_MANAGED_CHANGED",
                    oldManaged,
                    "A manifest-managed Compile entry is missing, duplicated, or was changed outside ETAB Engineering."));
                continue;
            }

            if (!desiredIncludes.Contains(oldManaged, PathComparer))
            {
                RemoveElementText(ref projectContent, "Compile", oldManaged);
                RemoveElementWithIndentation(matches[0]);
                projectMutations.Add($"remove Compile {oldManaged}");
            }
        }

        foreach (var desired in desiredIncludes)
        {
            var matches = FindByInclude(document, ns + "Compile", desired);
            if (matches.Count > 1)
            {
                issues.Add(new GenerationPlanIssue(
                    "PLC_COMPILE_DUPLICATE",
                    desired,
                    "The PLC project contains the same Compile Include more than once."));
                continue;
            }

            if (managedIncludes.Contains(desired))
            {
                if (matches.Count == 1 && IsStandardCompile(matches[0], ns))
                {
                    proposedManagedIncludes.Add(desired);
                }
                continue;
            }

            if (matches.Count == 0)
            {
                AppendElementText(
                    ref projectContent,
                    "Compile",
                    RenderCompile(desired));
                var element = new XElement(
                    ns + "Compile",
                    new XAttribute("Include", desired),
                    new XText(newline + "      "),
                    new XElement(ns + "SubType", "Code"),
                    new XText(newline + "    "));
                AppendToItemGroup(document, ns, "Compile", element, newline);
                proposedManagedIncludes.Add(desired);
                projectMutations.Add($"add Compile {desired}");
            }
        }
    }

    private static void PlanFolderEntries(
        XDocument document,
        XNamespace ns,
        ref string projectContent,
        IReadOnlyCollection<string> desiredIncludes,
        IReadOnlySet<string> managedIncludes,
        ISet<string> proposedManagedIncludes,
        string newline,
        ICollection<string> projectMutations,
        ICollection<GenerationPlanIssue> issues)
    {
        foreach (var oldManaged in managedIncludes
                     .OrderByDescending(path => path.Count(character => character == '\\'))
                     .ThenBy(path => path, StringComparer.Ordinal))
        {
            var matches = FindByInclude(document, ns + "Folder", oldManaged);
            if (matches.Count != 1 || !IsStandardFolder(matches.SingleOrDefault()))
            {
                issues.Add(new GenerationPlanIssue(
                    "PLC_FOLDER_MANAGED_CHANGED",
                    oldManaged,
                    "A manifest-managed Folder entry is missing, duplicated, or was changed outside ETAB Engineering."));
                continue;
            }

            if (!desiredIncludes.Contains(oldManaged, PathComparer))
            {
                var containsUnmanagedChild = document
                    .Descendants()
                    .Where(element => element.Name == ns + "Compile" || element.Name == ns + "Folder")
                    .Select(element => (string?)element.Attribute("Include"))
                    .Where(include => include is not null)
                    .Any(include => include!.StartsWith(oldManaged + "\\", StringComparison.OrdinalIgnoreCase));
                if (!containsUnmanagedChild)
                {
                    RemoveElementText(ref projectContent, "Folder", oldManaged);
                    RemoveElementWithIndentation(matches[0]);
                    projectMutations.Add($"remove Folder {oldManaged}");
                }
            }
        }

        foreach (var desired in desiredIncludes)
        {
            var matches = FindByInclude(document, ns + "Folder", desired);
            if (matches.Count > 1)
            {
                issues.Add(new GenerationPlanIssue(
                    "PLC_FOLDER_DUPLICATE",
                    desired,
                    "The PLC project contains the same Folder Include more than once."));
                continue;
            }

            if (managedIncludes.Contains(desired))
            {
                if (matches.Count == 1 && IsStandardFolder(matches[0]))
                {
                    proposedManagedIncludes.Add(desired);
                }
                continue;
            }

            if (matches.Count == 0)
            {
                AppendElementText(
                    ref projectContent,
                    "Folder",
                    RenderFolder(desired));
                AppendToItemGroup(
                    document,
                    ns,
                    "Folder",
                    new XElement(ns + "Folder", new XAttribute("Include", desired)),
                    newline);
                proposedManagedIncludes.Add(desired);
                projectMutations.Add($"add Folder {desired}");
            }
        }
    }

    private static ManagedPlaceholderReference? PlanPlaceholderReference(
        XDocument document,
        XNamespace ns,
        ref string projectContent,
        string placeholder,
        ManagedPlaceholderReference? previouslyManaged,
        string newline,
        ICollection<string> projectMutations,
        ICollection<GenerationPlanIssue> issues)
    {
        var desired = new ManagedPlaceholderReference
        {
            Include = placeholder,
            DefaultResolution = $"{EtabLibraryName}, * ({EtabLibraryPublisher})",
            Namespace = placeholder
        };

        if (previouslyManaged is not null)
        {
            var previousMatches = FindByInclude(
                document,
                ns + "PlaceholderReference",
                previouslyManaged.Include);
            if (previousMatches.Count != 1 ||
                !MatchesReference(previousMatches.SingleOrDefault(), ns, previouslyManaged))
            {
                issues.Add(new GenerationPlanIssue(
                    "PLC_LIBRARY_REFERENCE_MANAGED_CHANGED",
                    previouslyManaged.Include,
                    "The manifest-managed ETAB PlaceholderReference was changed outside ETAB Engineering."));
                return null;
            }

            if (PathComparer.Equals(previouslyManaged.Include, desired.Include))
            {
                if (!MatchesReference(previousMatches[0], ns, desired))
                {
                    ReplaceElementText(
                        ref projectContent,
                        "PlaceholderReference",
                        previouslyManaged.Include,
                        RenderPlaceholderReference(desired));
                    SetReference(previousMatches[0], ns, desired);
                    projectMutations.Add($"update PlaceholderReference {desired.Include}");
                }
                return desired;
            }

            RemoveElementText(
                ref projectContent,
                "PlaceholderReference",
                previouslyManaged.Include);
            RemoveElementWithIndentation(previousMatches[0]);
            projectMutations.Add($"remove PlaceholderReference {previouslyManaged.Include}");
        }

        var matches = FindByInclude(document, ns + "PlaceholderReference", desired.Include);
        if (matches.Count > 1 ||
            (matches.Count == 1 && !MatchesReference(matches[0], ns, desired)))
        {
            issues.Add(new GenerationPlanIssue(
                "PLC_LIBRARY_REFERENCE_CONFLICT",
                desired.Include,
                "An incompatible or duplicate PlaceholderReference already uses the configured ETAB placeholder."));
            return null;
        }

        if (matches.Count == 1)
        {
            return null;
        }

        var element = new XElement(
            ns + "PlaceholderReference",
            new XAttribute("Include", desired.Include),
            new XText(newline + "      "),
            new XElement(ns + "DefaultResolution", desired.DefaultResolution),
            new XText(newline + "      "),
            new XElement(ns + "Namespace", desired.Namespace),
            new XText(newline + "    "));
        AppendElementText(
            ref projectContent,
            "PlaceholderReference",
            RenderPlaceholderReference(desired));
        AppendToItemGroup(document, ns, "PlaceholderReference", element, newline);
        projectMutations.Add($"add PlaceholderReference {desired.Include}");
        return desired;
    }

    private static ManagedPlaceholderResolution? PlanPlaceholderResolution(
        XDocument document,
        XNamespace ns,
        ref string projectContent,
        string placeholder,
        string version,
        ManagedPlaceholderResolution? previouslyManaged,
        string newline,
        ICollection<string> projectMutations,
        ICollection<GenerationPlanIssue> issues)
    {
        var desired = new ManagedPlaceholderResolution
        {
            Include = placeholder,
            Resolution = $"{EtabLibraryName}, {version} ({EtabLibraryPublisher})"
        };

        if (previouslyManaged is not null)
        {
            var previousMatches = FindByInclude(
                document,
                ns + "PlaceholderResolution",
                previouslyManaged.Include);
            if (previousMatches.Count != 1 ||
                !MatchesResolution(previousMatches.SingleOrDefault(), ns, previouslyManaged))
            {
                issues.Add(new GenerationPlanIssue(
                    "PLC_LIBRARY_RESOLUTION_MANAGED_CHANGED",
                    previouslyManaged.Include,
                    "The manifest-managed ETAB PlaceholderResolution was changed outside ETAB Engineering."));
                return null;
            }

            if (PathComparer.Equals(previouslyManaged.Include, desired.Include))
            {
                if (!MatchesResolution(previousMatches[0], ns, desired))
                {
                    ReplaceElementText(
                        ref projectContent,
                        "PlaceholderResolution",
                        previouslyManaged.Include,
                        RenderPlaceholderResolution(desired));
                    SetResolution(previousMatches[0], ns, desired);
                    projectMutations.Add($"update PlaceholderResolution {desired.Include}");
                }
                return desired;
            }

            RemoveElementText(
                ref projectContent,
                "PlaceholderResolution",
                previouslyManaged.Include);
            RemoveElementWithIndentation(previousMatches[0]);
            projectMutations.Add($"remove PlaceholderResolution {previouslyManaged.Include}");
        }

        var matches = FindByInclude(document, ns + "PlaceholderResolution", desired.Include);
        if (matches.Count > 1 ||
            (matches.Count == 1 && !MatchesResolution(matches[0], ns, desired)))
        {
            issues.Add(new GenerationPlanIssue(
                "PLC_LIBRARY_RESOLUTION_CONFLICT",
                desired.Include,
                "An incompatible or duplicate PlaceholderResolution already uses the configured ETAB placeholder."));
            return null;
        }

        if (matches.Count == 1)
        {
            return null;
        }

        var element = new XElement(
            ns + "PlaceholderResolution",
            new XAttribute("Include", desired.Include),
            new XText(newline + "      "),
            new XElement(ns + "Resolution", desired.Resolution),
            new XText(newline + "    "));
        AppendElementText(
            ref projectContent,
            "PlaceholderResolution",
            RenderPlaceholderResolution(desired));
        AppendToItemGroup(document, ns, "PlaceholderResolution", element, newline);
        projectMutations.Add($"add PlaceholderResolution {desired.Include}");
        return desired;
    }

    private static IReadOnlyList<GenerationPlanIssue> ValidateManifest(
        ProjectIntegrationManifest? manifest,
        EtabProjectDocument project,
        string projectFileRelativePath)
    {
        if (manifest is null)
        {
            return [];
        }

        var issues = new List<GenerationPlanIssue>();
        if (manifest.ManifestVersion != ProjectIntegrationManifestSerializer.CurrentManifestVersion)
        {
            issues.Add(new GenerationPlanIssue(
                "PROJECT_INTEGRATION_MANIFEST_VERSION",
                "/manifestVersion",
                $"Unsupported project integration manifest version '{manifest.ManifestVersion}'."));
        }
        if (!string.Equals(manifest.ProjectId, project.Project.Id, StringComparison.Ordinal))
        {
            issues.Add(new GenerationPlanIssue(
                "PROJECT_INTEGRATION_MANIFEST_PROJECT",
                "/projectId",
                "The project integration manifest belongs to a different ETAB project."));
        }
        if (!PathComparer.Equals(manifest.PlcProject, projectFileRelativePath))
        {
            issues.Add(new GenerationPlanIssue(
                "PROJECT_INTEGRATION_MANIFEST_TARGET",
                "/plcProject",
                "The managed PLC project path does not match the configured target."));
        }
        ValidateManagedPaths(manifest.ManagedCompileIncludes, "/managedCompileIncludes", issues);
        ValidateManagedPaths(manifest.ManagedFolderIncludes, "/managedFolderIncludes", issues);
        ValidateManagedTaskCall(manifest.ManagedTaskPouCall, issues);
        return issues;
    }

    private static void ValidateManagedTaskCall(
        ManagedTaskPouCall? managedCall,
        ICollection<GenerationPlanIssue> issues)
    {
        if (managedCall is null)
        {
            return;
        }

        var taskPath = managedCall.TaskFile?.Replace('/', '\\') ?? string.Empty;
        if (string.IsNullOrWhiteSpace(taskPath) ||
            Path.IsPathRooted(taskPath) ||
            !taskPath.EndsWith(".TcTTO", StringComparison.OrdinalIgnoreCase) ||
            taskPath.Split('\\', StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => segment is "." or ".."))
        {
            issues.Add(new GenerationPlanIssue(
                "PROJECT_INTEGRATION_MANIFEST_TASK",
                "/managedTaskPouCall/taskFile",
                "The managed TwinCAT task path is invalid."));
        }

        if (string.IsNullOrWhiteSpace(managedCall.ProgramName) ||
            !Regex.IsMatch(
                managedCall.ProgramName,
                @"^[A-Za-z_][A-Za-z0-9_]*$",
                RegexOptions.CultureInvariant))
        {
            issues.Add(new GenerationPlanIssue(
                "PROJECT_INTEGRATION_MANIFEST_TASK",
                "/managedTaskPouCall/programName",
                "The managed runtime program name is invalid."));
        }
    }

    private static void ValidateManagedPaths(
        IReadOnlyList<string>? paths,
        string issuePath,
        ICollection<GenerationPlanIssue> issues)
    {
        if (paths is null)
        {
            issues.Add(new GenerationPlanIssue(
                "PROJECT_INTEGRATION_MANIFEST_REQUIRED",
                issuePath,
                "The managed path list is required."));
            return;
        }

        var unique = new HashSet<string>(PathComparer);
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path) ||
                path.StartsWith('\\') ||
                path.Split('\\', StringSplitOptions.RemoveEmptyEntries)
                    .Any(segment => segment is "." or "..") ||
                !unique.Add(path))
            {
                issues.Add(new GenerationPlanIssue(
                    "PROJECT_INTEGRATION_MANIFEST_PATH",
                    issuePath,
                    $"Managed path '{path}' is invalid or duplicated."));
            }
        }
    }

    private static bool TryResolveProjectFile(
        string projectRoot,
        string? configuredPath,
        out string? absolutePath,
        out string? relativePath,
        out string? error)
    {
        absolutePath = null;
        relativePath = null;
        error = null;

        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            error = "project.twinCAT.plcProject is required for TwinCAT project integration.";
            return false;
        }

        try
        {
            var normalized = configuredPath
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(normalized) ||
                normalized.Split(
                        [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                        StringSplitOptions.RemoveEmptyEntries)
                    .Any(segment => segment is "." or ".."))
            {
                error = "project.twinCAT.plcProject must be a safe relative path.";
                return false;
            }

            var root = Path.GetFullPath(projectRoot);
            if (!Directory.Exists(root))
            {
                error = $"The selected project root does not exist: {root}";
                return false;
            }
            if (File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint))
            {
                error = "TwinCAT project integration through a reparse-point project root is not allowed.";
                return false;
            }
            var candidate = Path.GetFullPath(normalized, root);
            if (!string.Equals(
                    Path.GetDirectoryName(candidate),
                    root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
            {
                error = "Phase 3 v0.1 requires the .plcproj file directly inside the selected project root.";
                return false;
            }
            if (!candidate.EndsWith(".plcproj", StringComparison.OrdinalIgnoreCase))
            {
                error = "The configured TwinCAT project must use the .plcproj extension.";
                return false;
            }
            if (!File.Exists(candidate))
            {
                error = $"The configured TwinCAT project does not exist: {candidate}";
                return false;
            }
            if (File.GetAttributes(candidate).HasFlag(FileAttributes.ReparsePoint))
            {
                error = "TwinCAT project integration through a reparse point is not allowed.";
                return false;
            }

            absolutePath = candidate;
            relativePath = NormalizeManifestPath(Path.GetRelativePath(root, candidate));
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException or
                IOException or UnauthorizedAccessException)
        {
            error = exception.Message;
            return false;
        }
    }

    private static string[] BuildFolderIncludes(IEnumerable<string> compileIncludes)
    {
        var folders = new HashSet<string>(PathComparer);
        foreach (var include in compileIncludes)
        {
            var segments = include.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            for (var length = 1; length < segments.Length; length++)
            {
                folders.Add(string.Join('\\', segments.Take(length)));
            }
        }
        return folders.OrderBy(path => path, StringComparer.Ordinal).ToArray();
    }

    private static string ToProjectInclude(string artifactPath) =>
        artifactPath.Replace('/', '\\');

    private static List<XElement> FindByInclude(
        XDocument document,
        XName name,
        string include) =>
        document.Descendants(name)
            .Where(element => PathComparer.Equals(
                (string?)element.Attribute("Include"),
                include))
            .ToList();

    private static bool IsStandardCompile(XElement? element, XNamespace ns) =>
        element is not null &&
        element.Attributes().Count() == 1 &&
        element.Attribute("Include") is not null &&
        element.Elements().Count() == 1 &&
        element.Element(ns + "SubType")?.Value == "Code" &&
        element.Nodes().OfType<XText>().All(text => string.IsNullOrWhiteSpace(text.Value));

    private static bool IsStandardFolder(XElement? element) =>
        element is not null &&
        element.Attributes().Count() == 1 &&
        element.Attribute("Include") is not null &&
        !element.Nodes().Any();

    private static bool MatchesReference(
        XElement? element,
        XNamespace ns,
        ManagedPlaceholderReference expected) =>
        element is not null &&
        element.Attributes().Count() == 1 &&
        PathComparer.Equals((string?)element.Attribute("Include"), expected.Include) &&
        element.Elements().Count() == 2 &&
        element.Element(ns + "DefaultResolution")?.Value == expected.DefaultResolution &&
        element.Element(ns + "Namespace")?.Value == expected.Namespace &&
        element.Nodes().OfType<XText>().All(text => string.IsNullOrWhiteSpace(text.Value));

    private static bool MatchesResolution(
        XElement? element,
        XNamespace ns,
        ManagedPlaceholderResolution expected) =>
        element is not null &&
        element.Attributes().Count() == 1 &&
        PathComparer.Equals((string?)element.Attribute("Include"), expected.Include) &&
        element.Elements().Count() == 1 &&
        element.Element(ns + "Resolution")?.Value == expected.Resolution &&
        element.Nodes().OfType<XText>().All(text => string.IsNullOrWhiteSpace(text.Value));

    private static void SetReference(
        XElement element,
        XNamespace ns,
        ManagedPlaceholderReference desired)
    {
        element.SetAttributeValue("Include", desired.Include);
        element.Element(ns + "DefaultResolution")!.Value = desired.DefaultResolution;
        element.Element(ns + "Namespace")!.Value = desired.Namespace;
    }

    private static void SetResolution(
        XElement element,
        XNamespace ns,
        ManagedPlaceholderResolution desired)
    {
        element.SetAttributeValue("Include", desired.Include);
        element.Element(ns + "Resolution")!.Value = desired.Resolution;
    }

    private static string RenderCompile(string include) =>
        $"    <Compile Include=\"{EscapeAttribute(include)}\">\n" +
        "      <SubType>Code</SubType>\n" +
        "    </Compile>";

    private static string RenderFolder(string include) =>
        $"    <Folder Include=\"{EscapeAttribute(include)}\" />";

    private static string RenderPlaceholderReference(ManagedPlaceholderReference reference) =>
        $"    <PlaceholderReference Include=\"{EscapeAttribute(reference.Include)}\">\n" +
        $"      <DefaultResolution>{EscapeText(reference.DefaultResolution)}</DefaultResolution>\n" +
        $"      <Namespace>{EscapeText(reference.Namespace)}</Namespace>\n" +
        "    </PlaceholderReference>";

    private static string RenderPlaceholderResolution(ManagedPlaceholderResolution resolution) =>
        $"    <PlaceholderResolution Include=\"{EscapeAttribute(resolution.Include)}\">\n" +
        $"      <Resolution>{EscapeText(resolution.Resolution)}</Resolution>\n" +
        "    </PlaceholderResolution>";

    private static void RemoveElementText(
        ref string content,
        string elementName,
        string include)
    {
        var match = FindElementTextMatch(content, elementName, include);
        content = content.Remove(match.Index, match.Length);
    }

    private static void ReplaceElementText(
        ref string content,
        string elementName,
        string include,
        string replacement)
    {
        var match = FindElementTextMatch(content, elementName, include);
        var lineEnding = match.Groups["eol"].Value;
        var newline = DetectNewline(match.Value);
        var normalizedReplacement = NormalizeNewlines(replacement, newline) + lineEnding;
        content = content.Remove(match.Index, match.Length)
            .Insert(match.Index, normalizedReplacement);
    }

    private static Match FindElementTextMatch(
        string content,
        string elementName,
        string include)
    {
        var escapedName = Regex.Escape(elementName);
        var escapedInclude = Regex.Escape(EscapeAttribute(include));
        var pattern = elementName == "Folder"
            ? $"^[\\t ]*<{escapedName}[\\t ]+Include=\"{escapedInclude}\"[\\t ]*/>[\\t ]*(?<eol>\\r\\n|\\n|$)"
            : $"^[\\t ]*<{escapedName}[\\t ]+Include=\"{escapedInclude}\"[\\t ]*>.*?^[\\t ]*</{escapedName}>[\\t ]*(?<eol>\\r\\n|\\n|$)";
        var matches = Regex.Matches(
            content,
            pattern,
            RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        if (matches.Count != 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one standard {elementName} element for '{include}', found {matches.Count}.");
        }
        return matches[0];
    }

    private static void AppendElementText(
        ref string content,
        string childName,
        string renderedElement)
    {
        var itemGroups = Regex.Matches(
            content,
            "<ItemGroup(?:\\s[^>]*)?>(?<body>.*?)</ItemGroup>",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);
        var childPattern = $"<{Regex.Escape(childName)}(?:\\s|>)";
        var group = itemGroups
            .Cast<Match>()
            .FirstOrDefault(match => Regex.IsMatch(
                match.Groups["body"].Value,
                childPattern,
                RegexOptions.CultureInvariant));

        if (group is null)
        {
            var anchorIndex = content.IndexOf(
                "<ProjectExtensions",
                StringComparison.Ordinal);
            if (anchorIndex < 0)
            {
                anchorIndex = content.LastIndexOf("</Project>", StringComparison.Ordinal);
            }
            if (anchorIndex < 0)
            {
                throw new InvalidOperationException(
                    $"Cannot locate an insertion point for {childName} entries.");
            }

            while (anchorIndex > 0 && content[anchorIndex - 1] != '\n')
            {
                anchorIndex--;
            }

            var newline = DetectNewlineNear(content, anchorIndex);
            var normalizedElement = NormalizeNewlines(renderedElement, newline);
            var itemGroup =
                $"  <ItemGroup>{newline}{normalizedElement}{newline}  </ItemGroup>{newline}";
            content = content.Insert(anchorIndex, itemGroup);
            return;
        }

        const string closingTag = "</ItemGroup>";
        var closingIndex = group.Index + group.Value.LastIndexOf(
            closingTag,
            StringComparison.Ordinal);
        var insertionIndex = closingIndex;
        while (insertionIndex > group.Index &&
               content[insertionIndex - 1] is ' ' or '\t')
        {
            insertionIndex--;
        }
        if (insertionIndex > group.Index && content[insertionIndex - 1] == '\n')
        {
            insertionIndex--;
            if (insertionIndex > group.Index && content[insertionIndex - 1] == '\r')
            {
                insertionIndex--;
            }
        }

        var groupNewline = DetectNewlineNear(content, closingIndex);
        var normalized = NormalizeNewlines(renderedElement, groupNewline);
        var hasClosingWhitespaceLine = insertionIndex < closingIndex;
        var insertion = hasClosingWhitespaceLine
            ? groupNewline + normalized
            : groupNewline + normalized + groupNewline + "  ";
        content = content.Insert(insertionIndex, insertion);
    }

    private static string NormalizeNewlines(string value, string newline) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("\n", newline, StringComparison.Ordinal);

    private static string DetectNewlineNear(string content, int position)
    {
        var previousLf = content.LastIndexOf('\n', Math.Max(0, position - 1));
        if (previousLf > 0 && content[previousLf - 1] == '\r')
        {
            return "\r\n";
        }
        return "\n";
    }

    private static string EscapeAttribute(string value) =>
        EscapeText(value).Replace("\"", "&quot;", StringComparison.Ordinal);

    private static string EscapeText(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

    private static void AppendToItemGroup(
        XDocument document,
        XNamespace ns,
        string childName,
        XElement element,
        string newline)
    {
        var group = document.Root!
            .Elements(ns + "ItemGroup")
            .FirstOrDefault(candidate => candidate.Elements(ns + childName).Any());
        if (group is null)
        {
            group = new XElement(ns + "ItemGroup");
            var anchor = document.Root!.Element(ns + "ProjectExtensions");
            if (anchor is not null)
            {
                anchor.AddBeforeSelf(
                    new XText(newline + "  "),
                    group);
            }
            else
            {
                document.Root.Add(
                    new XText(newline + "  "),
                    group,
                    new XText(newline));
            }
        }

        var closingWhitespace = group.LastNode as XText;
        if (closingWhitespace is not null && string.IsNullOrWhiteSpace(closingWhitespace.Value))
        {
            closingWhitespace.AddBeforeSelf(new XText(newline + "    "), element);
        }
        else
        {
            group.Add(new XText(newline + "    "), element, new XText(newline + "  "));
        }
    }

    private static void RemoveElementWithIndentation(XElement element)
    {
        if (element.PreviousNode is XText previous && string.IsNullOrWhiteSpace(previous.Value))
        {
            previous.Remove();
        }
        element.Remove();
    }

    private static string DetectNewline(string content) =>
        content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    private static GenerationPlan WithIssues(
        GenerationPlan basePlan,
        IReadOnlyList<GenerationPlanIssue> issues) =>
        new(
            basePlan.ProjectRoot,
            basePlan.GeneratedRoot,
            basePlan.Changes,
            basePlan.Manifest,
            basePlan.Issues.Concat(issues).ToArray());

    private static GenerationPlan WithProjectConflict(
        GenerationPlan basePlan,
        string projectFileRelativePath,
        string projectFilePath,
        string projectFileContent,
        string projectFileHash,
        string manifestRelativePath,
        string? existingManifestHash,
        IReadOnlyList<GenerationPlanIssue> issues) =>
        new(
            basePlan.ProjectRoot,
            basePlan.GeneratedRoot,
            basePlan.Changes,
            basePlan.Manifest,
            basePlan.Issues.Concat(issues).ToArray(),
            new PlannedProjectFileChange(
                GenerationChangeKind.Conflict,
                projectFileRelativePath,
                projectFilePath,
                projectFileContent,
                projectFileHash,
                projectFileHash,
                "The TwinCAT project cannot be updated until all integration conflicts are resolved."),
            new PlannedProjectIntegrationManifestChange(
                GenerationChangeKind.Conflict,
                manifestRelativePath,
                string.Empty,
                existingManifestHash,
                "The integration manifest cannot be updated while project conflicts exist."));

    private static string NormalizeManifestPath(string path) =>
        path.Replace('\\', '/');

    private static string ComputeFileHash(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static string ComputeContentHash(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

}
