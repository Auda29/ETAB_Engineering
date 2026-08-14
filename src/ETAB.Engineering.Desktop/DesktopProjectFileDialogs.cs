using System.IO;
using System.Windows;
using System.Windows.Threading;
using ETAB.Engineering.Service;
using Microsoft.Win32;

namespace ETAB.Engineering.Desktop;

internal sealed class DesktopProjectFileDialogs : IProjectFileDialogService
{
    private const string ProjectFilter =
        "ETAB project (*.etab.json)|*.etab.json|JSON document (*.json)|*.json|All files (*.*)|*.*";

    private const string TwinCatPlcProjectFilter =
        "TwinCAT PLC project (*.plcproj)|*.plcproj";

    public Task<string?> SelectTwinCatPlcProjectAsync(CancellationToken cancellationToken) =>
        InvokeOnUiThreadAsync(() =>
        {
            var dialog = new OpenFileDialog
            {
                AddExtension = true,
                CheckFileExists = true,
                DefaultExt = ".plcproj",
                Filter = TwinCatPlcProjectFilter,
                Multiselect = false,
                RestoreDirectory = true,
                Title = "Connect TwinCAT PLC Project"
            };

            return ShowDialog(dialog) == true
                ? Path.GetFullPath(dialog.FileName)
                : null;
        }, cancellationToken);

    public Task<string?> SelectOpenProjectAsync(CancellationToken cancellationToken) =>
        InvokeOnUiThreadAsync(() =>
        {
            var dialog = new OpenFileDialog
            {
                AddExtension = true,
                CheckFileExists = true,
                DefaultExt = ".etab.json",
                Filter = ProjectFilter,
                Multiselect = false,
                RestoreDirectory = true,
                Title = "Open ETAB Project"
            };

            return ShowDialog(dialog) == true
                ? Path.GetFullPath(dialog.FileName)
                : null;
        }, cancellationToken);

    public Task<string?> SelectSaveProjectAsync(
        string suggestedFileName,
        CancellationToken cancellationToken) =>
        InvokeOnUiThreadAsync(() =>
        {
            var dialog = new SaveFileDialog
            {
                AddExtension = true,
                CheckPathExists = true,
                DefaultExt = ".etab.json",
                FileName = EnsureProjectExtension(suggestedFileName),
                Filter = ProjectFilter,
                OverwritePrompt = true,
                RestoreDirectory = true,
                Title = "Save ETAB Project As"
            };

            return ShowDialog(dialog) == true
                ? Path.GetFullPath(dialog.FileName)
                : null;
        }, cancellationToken);

    private static async Task<string?> InvokeOnUiThreadAsync(
        Func<string?> action,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var application = Application.Current
            ?? throw new InvalidOperationException("The desktop application is not available.");
        return await application.Dispatcher.InvokeAsync(
            action,
            DispatcherPriority.Normal,
            cancellationToken);
    }

    private static bool? ShowDialog(FileDialog dialog)
    {
        var owner = Application.Current.MainWindow;
        return owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
    }

    private static string EnsureProjectExtension(string fileName) =>
        fileName.EndsWith(".etab.json", StringComparison.OrdinalIgnoreCase)
            ? fileName
            : $"{fileName}.etab.json";
}
