using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ETAB.Engineering.Service;

namespace ETAB.Engineering.Desktop;

internal static class DesktopSmokeTest
{
    public static async Task<string> RunAsync(
        Uri address,
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient
        {
            BaseAddress = address,
            Timeout = TimeSpan.FromSeconds(20)
        };

        var html = await client.GetStringAsync("/", cancellationToken);
        Require(html.Contains("<div id=\"root\"></div>", StringComparison.Ordinal),
            "The embedded React index was not served.");

        var session = await client.GetFromJsonAsync<SessionResponse>(
            "/api/session",
            cancellationToken)
            ?? throw new InvalidOperationException("The desktop session response was empty.");
        var opened = await PostAsync<OpenProjectRequest, OpenProjectResponse>(
            client,
            "/api/projects/open",
            new OpenProjectRequest(session.ExampleProjectPath),
            cancellationToken);
        Require(opened.Validation.IsValid, "The bundled BrushMachine model is invalid.");

        var preview = await PostAsync<PreviewProjectRequest, PreviewResponse>(
            client,
            "/api/projects/preview",
            new PreviewProjectRequest(opened.Document, opened.Path, opened.ProjectRoot),
            cancellationToken);
        Require(preview.Validation.IsValid, "The bundled BrushMachine preview is invalid.");
        Require(preview.Artifacts.Count == 14,
            $"Expected 14 preview artifacts, received {preview.Artifacts.Count}.");

        var temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            "ETAB Engineering Desktop Smoke",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);
        try
        {
            var savePath = Path.Combine(temporaryRoot, "BrushMachine.smoke.etab.json");
            var saved = await PostAsync<SaveProjectRequest, SaveProjectResponse>(
                client,
                "/api/projects/save",
                new SaveProjectRequest(savePath, opened.Document),
                cancellationToken);
            Require(saved.Validation.IsValid, "The desktop save response is invalid.");

            var reopened = await PostAsync<OpenProjectRequest, OpenProjectResponse>(
                client,
                "/api/projects/open",
                new OpenProjectRequest(savePath),
                cancellationToken);
            Require(JsonNode.DeepEquals(opened.Document, reopened.Document),
                "The packaged desktop project did not round-trip losslessly.");
        }
        finally
        {
            DeleteTemporaryRoot(temporaryRoot);
        }

        return string.Join(
            Environment.NewLine,
            "ETAB Engineering desktop smoke test passed.",
            $"Address: {address}",
            $"Example: {session.ExampleProjectPath}",
            $"Preview artifacts: {preview.Artifacts.Count}",
            "Save/reopen: lossless");
    }

    private static async Task<TResponse> PostAsync<TRequest, TResponse>(
        HttpClient client,
        string path,
        TRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(path, request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken)
            ?? throw new InvalidOperationException($"The response from '{path}' was empty.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void DeleteTemporaryRoot(string path)
    {
        var expectedParent = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            "ETAB Engineering Desktop Smoke"));
        var resolved = Path.GetFullPath(path);
        if (!resolved.StartsWith(
                expectedParent + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Refusing to remove a desktop smoke-test directory outside the expected root.");
        }

        if (Directory.Exists(resolved))
        {
            Directory.Delete(resolved, recursive: true);
        }
    }
}
