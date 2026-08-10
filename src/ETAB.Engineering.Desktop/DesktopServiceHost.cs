using ETAB.Engineering.Service;
using System.IO;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

namespace ETAB.Engineering.Desktop;

public sealed class DesktopServiceHost : IAsyncDisposable
{
    private readonly WebApplication application;
    private int disposed;

    private DesktopServiceHost(WebApplication application, Uri address)
    {
        this.application = application;
        Address = address;
    }

    public Uri Address { get; }

    public static async Task<DesktopServiceHost> StartAsync(
        IFileProvider frontendFiles,
        CancellationToken cancellationToken)
    {
        var workspaceRoot = Path.GetFullPath(AppContext.BaseDirectory);
        var schemaPath = Path.Combine(workspaceRoot, "schemas", "etab-project.schema.json");
        var examplePath = Path.Combine(
            workspaceRoot,
            "examples",
            "BrushMachine.reference.etab.json");

        RequireFile(schemaPath, "JSON schema");
        RequireFile(examplePath, "BrushMachine reference model");
        if (!frontendFiles.GetFileInfo("index.html").Exists)
        {
            throw new InvalidOperationException(
                "The embedded React editor is missing from the desktop bundle.");
        }

        var application = EditorServiceHost.Build(
            [],
            new EditorServiceHostOptions
            {
                WorkspaceRoot = workspaceRoot,
                SchemaPath = schemaPath,
                ListenUrls = [DesktopRuntimeOptions.ResolveListenUrl()],
                FrontendFiles = frontendFiles
            });

        await application.StartAsync(cancellationToken);
        var server = application.Services.GetRequiredService<IServer>();
        var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;
        var address = addresses?
            .Select(value => Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null)
            .FirstOrDefault(uri => uri is not null)
            ?? throw new InvalidOperationException("The desktop service did not expose a local address.");

        return new DesktopServiceHost(application, address);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await application.StopAsync(timeout.Token);
        await application.DisposeAsync();
    }

    private static void RequireFile(string path, string description)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"The desktop bundle is incomplete: {description} was not found at '{path}'.");
        }
    }
}
