namespace ETAB.Engineering.Core.Planning;

internal sealed record ResolvedGenerationPaths(
    string ProjectRoot,
    string GeneratedRoot,
    string ManifestPath,
    string ManifestRelativePath);

internal static class GenerationPathResolver
{
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    public static bool TryResolve(
        string projectRoot,
        string configuredGeneratedRoot,
        string manifestFileName,
        out ResolvedGenerationPaths? paths,
        out string? error)
    {
        paths = null;
        error = null;

        try
        {
            var resolvedProjectRoot = Path.GetFullPath(projectRoot);
            if (!Directory.Exists(resolvedProjectRoot))
            {
                error = $"Project root '{resolvedProjectRoot}' does not exist or is not a directory.";
                return false;
            }

            var normalizedConfiguredRoot = configuredGeneratedRoot
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);

            if (ContainsTraversalSegment(normalizedConfiguredRoot))
            {
                error = "project.generation.generatedRoot cannot contain '.' or '..' path segments.";
                return false;
            }

            if (Path.IsPathRooted(normalizedConfiguredRoot))
            {
                error = "project.generation.generatedRoot must be a relative path.";
                return false;
            }

            var resolvedGeneratedRoot = Path.GetFullPath(
                normalizedConfiguredRoot,
                resolvedProjectRoot);

            if (!IsStrictDescendant(resolvedGeneratedRoot, resolvedProjectRoot))
            {
                error = "project.generation.generatedRoot must resolve to a child directory of the project root.";
                return false;
            }

            if (File.Exists(resolvedGeneratedRoot))
            {
                error = $"The generated root '{resolvedGeneratedRoot}' is occupied by a file.";
                return false;
            }

            var manifestPath = Path.Combine(resolvedGeneratedRoot, manifestFileName);
            var manifestRelativePath = NormalizeRelativePath(
                Path.GetRelativePath(resolvedProjectRoot, manifestPath));

            paths = new ResolvedGenerationPaths(
                resolvedProjectRoot,
                resolvedGeneratedRoot,
                manifestPath,
                manifestRelativePath);
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = exception.Message;
            return false;
        }
    }

    public static bool TryResolveArtifactPath(
        ResolvedGenerationPaths paths,
        string relativePath,
        out string? resolvedPath,
        out string? error)
    {
        resolvedPath = null;
        error = null;

        try
        {
            var platformPath = relativePath
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);

            if (ContainsTraversalSegment(platformPath))
            {
                error = $"Artifact path '{relativePath}' cannot contain '.' or '..' path segments.";
                return false;
            }

            if (Path.IsPathRooted(platformPath))
            {
                error = $"Artifact path '{relativePath}' must be relative.";
                return false;
            }

            var candidate = Path.GetFullPath(platformPath, paths.ProjectRoot);
            if (!IsStrictDescendant(candidate, paths.GeneratedRoot))
            {
                error = $"Artifact path '{relativePath}' resolves outside the generated root.";
                return false;
            }

            resolvedPath = candidate;
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = exception.Message;
            return false;
        }
    }

    private static bool IsStrictDescendant(string candidate, string parent)
    {
        var relative = Path.GetRelativePath(parent, candidate);
        return relative != "." &&
               !Path.IsPathRooted(relative) &&
               !relative.Equals("..", PathComparison) &&
               !relative.StartsWith($"..{Path.DirectorySeparatorChar}", PathComparison) &&
               !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", PathComparison);
    }

    private static string NormalizeRelativePath(string path) =>
        path.Replace('\\', '/');

    private static bool ContainsTraversalSegment(string path) =>
        path.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment is "." or "..");
}
