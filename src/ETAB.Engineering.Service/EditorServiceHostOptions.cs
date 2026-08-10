using Microsoft.Extensions.FileProviders;

namespace ETAB.Engineering.Service;

public sealed class EditorServiceHostOptions
{
    public required string WorkspaceRoot { get; init; }

    public required string SchemaPath { get; init; }

    public IReadOnlyList<string> ListenUrls { get; init; } = [];

    public IFileProvider? FrontendFiles { get; init; }

    public bool EnableDevelopmentCors { get; init; }
}
