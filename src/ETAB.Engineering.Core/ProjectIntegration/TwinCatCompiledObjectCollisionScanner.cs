using System.Xml;
using System.Xml.Linq;
using ETAB.Engineering.Core.Generation;
using ETAB.Engineering.Core.Planning;

namespace ETAB.Engineering.Core.ProjectIntegration;

internal static class TwinCatCompiledObjectCollisionScanner
{
    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    public static IReadOnlyList<GenerationPlanIssue> FindCollisions(
        string projectRoot,
        XDocument projectDocument,
        XNamespace projectNamespace,
        IReadOnlyCollection<GeneratedArtifact> artifacts,
        IReadOnlyCollection<string> managedCompileIncludes)
    {
        var issues = new List<GenerationPlanIssue>();
        var generatedByName = artifacts
            .GroupBy(artifact => artifact.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        foreach (var duplicate in generatedByName.Values.Where(group => group.Length > 1))
        {
            issues.Add(new GenerationPlanIssue(
                "PLC_GENERATED_OBJECT_NAME_DUPLICATE",
                duplicate[0].RelativePath,
                $"More than one generated artifact defines the IEC object '{duplicate[0].Name}'."));
        }

        if (issues.Count > 0)
        {
            return issues;
        }

        var desiredPaths = artifacts.ToDictionary(
            artifact => NormalizePath(artifact.RelativePath),
            artifact => artifact,
            PathComparer);
        var managedPaths = managedCompileIncludes
            .Select(NormalizePath)
            .ToHashSet(PathComparer);
        var includes = projectDocument
            .Descendants(projectNamespace + "Compile")
            .Select(element => (string?)element.Attribute("Include"))
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => include!)
            .Where(IsTwinCatIecObjectPath)
            .Distinct(PathComparer)
            .OrderBy(include => include, StringComparer.Ordinal)
            .ToArray();

        foreach (var include in includes)
        {
            var normalizedInclude = NormalizePath(include);
            if (desiredPaths.ContainsKey(normalizedInclude) || managedPaths.Contains(normalizedInclude))
            {
                continue;
            }

            if (!TryResolveCompiledObjectPath(
                    projectRoot,
                    include,
                    out var objectPath,
                    out var pathError))
            {
                issues.Add(new GenerationPlanIssue(
                    "PLC_OBJECT_SCAN_UNSAFE_PATH",
                    include,
                    pathError!));
                continue;
            }

            if (!TryReadObjectName(objectPath!, out var objectName, out var readError))
            {
                issues.Add(new GenerationPlanIssue(
                    "PLC_OBJECT_SCAN_FAILED",
                    include,
                    readError!));
                continue;
            }

            if (!generatedByName.TryGetValue(objectName!, out var generated))
            {
                continue;
            }

            foreach (var artifact in generated)
            {
                issues.Add(new GenerationPlanIssue(
                    "PLC_OBJECT_NAME_CONFLICT",
                    include,
                    $"The generated artifact '{artifact.RelativePath}' defines IEC object " +
                    $"'{artifact.Name}', which is already compiled from '{include}'."));
            }
        }

        return issues;
    }

    private static bool IsTwinCatIecObjectPath(string include)
    {
        var extension = Path.GetExtension(include);
        return extension.Equals(".TcDUT", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".TcPOU", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".TcGVL", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryResolveCompiledObjectPath(
        string projectRoot,
        string include,
        out string? objectPath,
        out string? error)
    {
        objectPath = null;
        error = null;

        try
        {
            var normalized = NormalizePath(include);
            if (Path.IsPathRooted(normalized) ||
                normalized.Split(
                        [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                        StringSplitOptions.RemoveEmptyEntries)
                    .Any(segment => segment is "." or ".."))
            {
                error = "Compiled TwinCAT object paths must stay inside the selected project root.";
                return false;
            }

            var root = Path.GetFullPath(projectRoot);
            var candidate = Path.GetFullPath(normalized, root);
            var relative = Path.GetRelativePath(root, candidate);
            if (Path.IsPathRooted(relative) ||
                relative.Split(
                        [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                        StringSplitOptions.RemoveEmptyEntries)
                    .Any(segment => segment == ".."))
            {
                error = "Compiled TwinCAT object paths must stay inside the selected project root.";
                return false;
            }

            if (!File.Exists(candidate))
            {
                error = $"The compiled TwinCAT object does not exist: {candidate}";
                return false;
            }

            var current = root;
            foreach (var segment in relative.Split(
                         [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                         StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                {
                    error = "Compiled TwinCAT objects reached through a reparse point cannot be scanned safely.";
                    return false;
                }
            }

            objectPath = candidate;
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

    private static bool TryReadObjectName(
        string path,
        out string? objectName,
        out string? error)
    {
        objectName = null;
        error = null;

        var expectedElement = Path.GetExtension(path).ToUpperInvariant() switch
        {
            ".TCDUT" => "DUT",
            ".TCPOU" => "POU",
            ".TCGVL" => "GVL",
            _ => null
        };

        try
        {
            var document = XDocument.Load(path, LoadOptions.None);
            var objectElement = document.Descendants()
                .FirstOrDefault(element =>
                    string.Equals(element.Name.LocalName, expectedElement, StringComparison.Ordinal));
            objectName = (string?)objectElement?.Attribute("Name");
            if (string.IsNullOrWhiteSpace(objectName))
            {
                error = $"The compiled TwinCAT file does not contain a named {expectedElement} object.";
                return false;
            }

            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or XmlException)
        {
            error = exception.Message;
            return false;
        }
    }

    private static string NormalizePath(string path) =>
        path.Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
}
