using System.IO;
using System.Windows;
using Microsoft.Extensions.FileProviders;

namespace ETAB.Engineering.Desktop;

public partial class App : Application
{
    private DesktopServiceHost? serviceHost;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var smokeTest = e.Args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase);
        var smokeLogPath = GetOption(e.Args, "--smoke-test-log");

        try
        {
            var frontendFiles = new PhysicalFileProvider(
                Path.Combine(AppContext.BaseDirectory, "wwwroot"));
            serviceHost = await DesktopServiceHost.StartAsync(
                frontendFiles,
                CancellationToken.None);

            if (smokeTest)
            {
                var report = await DesktopSmokeTest.RunAsync(
                    serviceHost.Address,
                    CancellationToken.None);
                WriteSmokeLog(smokeLogPath, report);
                await serviceHost.DisposeAsync();
                Shutdown(0);
                return;
            }

            var window = new MainWindow(serviceHost);
            MainWindow = window;
            window.Show();
        }
        catch (Exception exception)
        {
            if (smokeTest)
            {
                WriteSmokeLog(smokeLogPath, $"Desktop smoke test failed.{Environment.NewLine}{exception}");
            }
            else
            {
                MessageBox.Show(
                    $"ETAB Engineering could not start.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                    "ETAB Engineering",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            if (serviceHost is not null)
            {
                await serviceHost.DisposeAsync();
            }

            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (serviceHost is not null)
        {
            serviceHost.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        base.OnExit(e);
    }

    private static string? GetOption(IReadOnlyList<string> args, string name)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static void WriteSmokeLog(string? path, string report)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, report);
    }
}
