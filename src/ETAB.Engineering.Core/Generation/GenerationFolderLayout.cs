using ETAB.Engineering.Core.Model;
using System.Text.RegularExpressions;

namespace ETAB.Engineering.Core.Generation;

internal sealed class GenerationFolderLayout
{
    public const string UnassignedAreaName = "Unassigned";

    private readonly IReadOnlyDictionary<string, string> _nodeDirectories;

    private GenerationFolderLayout(
        IReadOnlyDictionary<string, string> nodeDirectories,
        IReadOnlyList<string> folders)
    {
        _nodeDirectories = nodeDirectories;
        Folders = folders;
    }

    public IReadOnlyList<string> Folders { get; }

    public string GetNodeDirectory(EtabNode node) => _nodeDirectories[node.Id];

    public static GenerationFolderLayout Create(EtabProjectDocument project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var applicationRoot = NormalizeApplicationRoot(
            project.Project.Generation.ApplicationRoot);
        var groups = GetAreaDisplayNames(project.Layout);
        var nodeGroups = project.Layout.Nodes
            .ToDictionary(
                layout => layout.NodeId,
                layout => layout.Group,
                StringComparer.Ordinal);
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            applicationRoot,
            $"{applicationRoot}/{UnassignedAreaName}"
        };

        foreach (var area in groups.Values)
        {
            folders.Add($"{applicationRoot}/{area}");
        }

        var nodeDirectories = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var node in project.Nodes)
        {
            var area = nodeGroups.TryGetValue(node.Id, out var groupName) &&
                       !string.IsNullOrWhiteSpace(groupName) &&
                       groups.TryGetValue(groupName, out var groupDisplayName)
                ? groupDisplayName
                : UnassignedAreaName;
            var nodeName = GeneratedFolderName.Require(node.DisplayName);
            var directory = $"{applicationRoot}/{area}/{nodeName}";
            nodeDirectories.Add(node.Id, directory);
            folders.Add(directory);
        }

        return new GenerationFolderLayout(
            nodeDirectories,
            folders.OrderBy(path => path, StringComparer.Ordinal).ToArray());
    }

    internal static IReadOnlyDictionary<string, string> GetAreaDisplayNames(EtabLayout layout)
    {
        var groups = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in layout.Groups ?? [])
        {
            groups[group.Name] = GeneratedFolderName.Require(group.DisplayName);
        }

        foreach (var nodeLayout in layout.Nodes)
        {
            if (string.IsNullOrWhiteSpace(nodeLayout.Group) || groups.ContainsKey(nodeLayout.Group))
            {
                continue;
            }

            groups[nodeLayout.Group] = GeneratedFolderName.Require(
                FormatLegacyAreaName(nodeLayout.Group));
        }

        return groups;
    }

    private static string FormatLegacyAreaName(string name)
    {
        var formatted = Regex.Replace(name, "[_-]+", " ");
        formatted = Regex.Replace(formatted, "([a-z0-9])([A-Z])", "$1 $2");
        return char.ToUpperInvariant(formatted[0]) + formatted[1..];
    }

    private static string NormalizeApplicationRoot(string applicationRoot)
    {
        var normalized = applicationRoot
            .Replace('\\', '/')
            .Trim('/');
        return string.IsNullOrEmpty(normalized) || normalized == "."
            ? "Application"
            : normalized;
    }
}

internal static class GeneratedFolderName
{
    private static readonly char[] InvalidCharacters =
        ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    private static readonly HashSet<string> ReservedNames = new(
        [
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        ],
        StringComparer.OrdinalIgnoreCase);

    public static string Require(string value)
    {
        if (!TryValidate(value, out var error))
        {
            throw new InvalidOperationException(error);
        }

        return value;
    }

    public static bool TryValidate(string value, out string? error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            error = "The folder name must not be empty or whitespace.";
            return false;
        }

        if (value is "." or "..")
        {
            error = $"The folder name '{value}' is not allowed.";
            return false;
        }

        if (value.EndsWith(' ') || value.EndsWith('.'))
        {
            error = "A folder name must not end with a space or period.";
            return false;
        }

        if (value.Any(character => character < 32 || InvalidCharacters.Contains(character)))
        {
            error = "A folder name contains a character that is invalid on Windows.";
            return false;
        }

        var deviceName = value.Split('.', 2)[0];
        if (ReservedNames.Contains(deviceName))
        {
            error = $"The folder name '{value}' is reserved by Windows.";
            return false;
        }

        error = null;
        return true;
    }
}
