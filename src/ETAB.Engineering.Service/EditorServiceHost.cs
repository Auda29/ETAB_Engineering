using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;

namespace ETAB.Engineering.Service;

public static class EditorServiceHost
{
    private const string DevelopmentCorsPolicy = "editor-development";

    public static WebApplication Build(
        string[] args,
        EditorServiceHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var workspaceRoot = Path.GetFullPath(options.WorkspaceRoot);
        var schemaPath = Path.GetFullPath(options.SchemaPath);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = workspaceRoot
        });

        if (options.ListenUrls.Count > 0)
        {
            builder.WebHost.UseUrls(options.ListenUrls.ToArray());
        }

        builder.Services.AddSingleton(new EditorProjectService(workspaceRoot, schemaPath));
        if (options.EnableDevelopmentCors)
        {
            builder.Services.AddCors(cors => cors.AddPolicy(
                DevelopmentCorsPolicy,
                policy => policy
                    .WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
                    .AllowAnyHeader()
                    .AllowAnyMethod()));
        }

        var app = builder.Build();
        if (options.EnableDevelopmentCors)
        {
            app.UseCors(DevelopmentCorsPolicy);
        }

        MapApi(app);
        if (options.FrontendFiles is not null)
        {
            MapFrontend(app, options.FrontendFiles);
        }

        return app;
    }

    private static void MapApi(WebApplication app)
    {
        app.MapGet("/api/session", (EditorProjectService service) =>
            Results.Ok(new SessionResponse(service.WorkspaceRoot, service.ExampleProjectPath)));

        app.MapPost("/api/projects/open", async (
            OpenProjectRequest request,
            EditorProjectService service,
            CancellationToken cancellationToken) =>
            await HandleAsync(() => service.OpenAsync(request.Path, cancellationToken)));

        app.MapPost("/api/projects/save", async (
            SaveProjectRequest request,
            EditorProjectService service,
            CancellationToken cancellationToken) =>
            await HandleAsync(() => service.SaveAsync(
                request.Path,
                request.Document,
                cancellationToken)));

        app.MapPost("/api/projects/validate", (
            ValidateProjectRequest request,
            EditorProjectService service) =>
            Handle(() => service.Validate(request.Document)));

        app.MapPost("/api/projects/preview", (
            PreviewProjectRequest request,
            EditorProjectService service) =>
            Handle(() => service.Preview(
                request.Document,
                request.ProjectPath,
                request.ProjectRoot)));
    }

    private static void MapFrontend(
        WebApplication app,
        Microsoft.Extensions.FileProviders.IFileProvider frontendFiles)
    {
        var defaultFiles = new DefaultFilesOptions
        {
            FileProvider = frontendFiles
        };
        defaultFiles.DefaultFileNames.Clear();
        defaultFiles.DefaultFileNames.Add("index.html");

        app.UseDefaultFiles(defaultFiles);
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = frontendFiles,
            ContentTypeProvider = new FileExtensionContentTypeProvider()
        });
        app.MapFallback(async context =>
        {
            var index = frontendFiles.GetFileInfo("index.html");
            if (!index.Exists)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            context.Response.ContentType = "text/html; charset=utf-8";
            await using var stream = index.CreateReadStream();
            await stream.CopyToAsync(context.Response.Body, context.RequestAborted);
        });
    }

    private static IResult Handle<T>(Func<T> action)
    {
        try
        {
            return Results.Ok(action());
        }
        catch (EditorRequestException exception)
        {
            return Results.BadRequest(new ApiErrorResponse(exception.Code, exception.Message));
        }
    }

    private static async Task<IResult> HandleAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return Results.Ok(await action());
        }
        catch (EditorRequestException exception)
        {
            return Results.BadRequest(new ApiErrorResponse(exception.Code, exception.Message));
        }
    }
}
