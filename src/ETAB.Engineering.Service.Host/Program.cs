using ETAB.Engineering.Service;

var workspaceRoot = WorkspaceLocator.Find(Directory.GetCurrentDirectory());
var schemaPath = Path.Combine(AppContext.BaseDirectory, "schemas", "etab-project.schema.json");

var app = EditorServiceHost.Build(
    args,
    new EditorServiceHostOptions
    {
        WorkspaceRoot = workspaceRoot,
        SchemaPath = schemaPath,
        EnableDevelopmentCors = true
    });

await app.RunAsync();
