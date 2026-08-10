using System.Text.Json;
using System.Text.Json.Serialization;
using ETAB.Engineering.Core.Model;
using Json.Schema;

namespace ETAB.Engineering.Core.Validation;

public sealed class ProjectValidator
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public async Task<ProjectValidationResult> ValidateFilesAsync(
        string projectPath,
        string schemaPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var projectJson = await File.ReadAllTextAsync(projectPath, cancellationToken);
            var schemaJson = await File.ReadAllTextAsync(schemaPath, cancellationToken);
            return Validate(projectJson, schemaJson);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Invalid(new ValidationIssue("INPUT_READ_ERROR", "/", exception.Message));
        }
    }

    public ProjectValidationResult Validate(string projectJson, string schemaJson)
    {
        JsonDocument projectDocument;

        try
        {
            projectDocument = JsonDocument.Parse(projectJson);
        }
        catch (JsonException exception)
        {
            return Invalid(new ValidationIssue(
                "JSON_PARSE",
                BuildJsonExceptionPath(exception),
                exception.Message));
        }

        using (projectDocument)
        {
            JsonSchema schema;

            try
            {
                using var schemaDocument = JsonDocument.Parse(schemaJson);
                schema = JsonSchema.Build(
                    schemaDocument.RootElement.Clone(),
                    new BuildOptions
                    {
                        Dialect = Dialect.Draft202012,
                        SchemaRegistry = new SchemaRegistry()
                    });
            }
            catch (Exception exception)
            {
                return Invalid(new ValidationIssue("SCHEMA_DEFINITION", "/", exception.Message));
            }

            var evaluation = schema.Evaluate(
                projectDocument.RootElement,
                new EvaluationOptions
                {
                    OutputFormat = OutputFormat.List,
                    RequireFormatValidation = true
                });

            if (!evaluation.IsValid)
            {
                var schemaIssues = FlattenSchemaIssues(evaluation)
                    .Distinct()
                    .OrderBy(issue => issue.Path, StringComparer.Ordinal)
                    .ThenBy(issue => issue.Message, StringComparer.Ordinal)
                    .ToArray();

                return schemaIssues.Length > 0
                    ? new ProjectValidationResult(schemaIssues)
                    : Invalid(new ValidationIssue("SCHEMA", "/", "The project does not match the JSON schema."));
            }

            EtabProjectDocument? project;

            try
            {
                project = projectDocument.RootElement.Deserialize<EtabProjectDocument>(SerializerOptions);
            }
            catch (JsonException exception)
            {
                return Invalid(new ValidationIssue(
                    "MODEL_DESERIALIZATION",
                    exception.Path ?? "/",
                    exception.Message));
            }

            if (project is null)
            {
                return Invalid(new ValidationIssue(
                    "MODEL_DESERIALIZATION",
                    "/",
                    "The project document could not be deserialized."));
            }

            var semanticIssues = new SemanticValidator()
                .Validate(project, projectDocument.RootElement)
                .OrderBy(issue => issue.Path, StringComparer.Ordinal)
                .ThenBy(issue => issue.Code, StringComparer.Ordinal)
                .ThenBy(issue => issue.Message, StringComparer.Ordinal)
                .ToArray();

            return new ProjectValidationResult(semanticIssues, project);
        }
    }

    private static ProjectValidationResult Invalid(ValidationIssue issue) =>
        new([issue]);

    private static string BuildJsonExceptionPath(JsonException exception)
    {
        if (!string.IsNullOrWhiteSpace(exception.Path))
        {
            return exception.Path;
        }

        return exception.LineNumber is not null
            ? $"line {exception.LineNumber + 1}, byte {exception.BytePositionInLine + 1}"
            : "/";
    }

    private static IEnumerable<ValidationIssue> FlattenSchemaIssues(EvaluationResults result)
    {
        if (result.Errors is { Count: > 0 })
        {
            var path = string.IsNullOrEmpty(result.InstanceLocation.ToString())
                ? "/"
                : result.InstanceLocation.ToString();

            foreach (var error in result.Errors)
            {
                yield return new ValidationIssue(
                    "SCHEMA",
                    path,
                    $"{error.Key}: {error.Value}");
            }
        }

        foreach (var detail in result.Details ?? [])
        {
            foreach (var issue in FlattenSchemaIssues(detail))
            {
                yield return issue;
            }
        }
    }
}
