using ETAB.Engineering.Service;

var builder = WebApplication.CreateBuilder(args);
var workspaceRoot = WorkspaceLocator.Find(builder.Environment.ContentRootPath);
var schemaPath = Path.Combine(AppContext.BaseDirectory, "schemas", "etab-project.schema.json");

builder.Services.AddSingleton(new EditorProjectService(workspaceRoot, schemaPath));
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();
app.UseCors();

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
    await HandleAsync(() => service.SaveAsync(request.Path, request.Document, cancellationToken)));

app.MapPost("/api/projects/validate", (
    ValidateProjectRequest request,
    EditorProjectService service) =>
    Handle(() => service.Validate(request.Document)));

app.MapPost("/api/projects/preview", (
    PreviewProjectRequest request,
    EditorProjectService service) =>
    Handle(() => service.Preview(request.Document, request.ProjectPath, request.ProjectRoot)));

app.Run();

static IResult Handle<T>(Func<T> action)
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

static async Task<IResult> HandleAsync<T>(Func<Task<T>> action)
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
