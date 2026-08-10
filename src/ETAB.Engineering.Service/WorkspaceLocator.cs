namespace ETAB.Engineering.Service;

internal static class WorkspaceLocator
{
    public static string Find(string startPath)
    {
        var current = new DirectoryInfo(Path.GetFullPath(startPath));
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ETAB.Engineering.sln")) &&
                File.Exists(Path.Combine(current.FullName, "schemas", "etab-project.schema.json")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate the ETAB Engineering workspace from '{startPath}'.");
    }
}
