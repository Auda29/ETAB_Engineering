namespace ETAB.Engineering.Core.ProjectIntegration;

public sealed class ProjectIntegrationManifest
{
    public required string ManifestVersion { get; init; }

    public required string ProjectId { get; init; }

    public required string PlcProject { get; init; }

    public required List<string> ManagedCompileIncludes { get; init; }

    public required List<string> ManagedFolderIncludes { get; init; }

    public ManagedPlaceholderReference? ManagedPlaceholderReference { get; init; }

    public ManagedPlaceholderReference? ManagedEngineeringToolboxPlaceholderReference { get; init; }

    public ManagedPlaceholderResolution? ManagedPlaceholderResolution { get; init; }

    public ManagedTaskPouCall? ManagedTaskPouCall { get; init; }
}

public sealed class ManagedPlaceholderReference
{
    public required string Include { get; init; }

    public required string DefaultResolution { get; init; }

    public required string Namespace { get; init; }
}

public sealed class ManagedPlaceholderResolution
{
    public required string Include { get; init; }

    public required string Resolution { get; init; }
}

public sealed class ManagedTaskPouCall
{
    public required string TaskFile { get; init; }

    public required string ProgramName { get; init; }
}
