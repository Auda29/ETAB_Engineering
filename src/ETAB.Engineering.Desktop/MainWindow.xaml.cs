using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace ETAB.Engineering.Desktop;

public partial class MainWindow : Window
{
    private readonly DesktopServiceHost serviceHost;
    private bool closeCompleted;

    public MainWindow(DesktopServiceHost serviceHost)
    {
        this.serviceHost = serviceHost;
        InitializeComponent();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ETAB Engineering",
                "WebView2");
            Directory.CreateDirectory(userDataFolder);

            var environment = await CoreWebView2Environment.CreateAsync(
                userDataFolder: userDataFolder);
            await EditorWebView.EnsureCoreWebView2Async(environment);
            EditorWebView.CoreWebView2.Settings.AreDevToolsEnabled = Debugger.IsAttached;
            EditorWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = Debugger.IsAttached;
            EditorWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            EditorWebView.CoreWebView2.Settings.IsZoomControlEnabled = false;
            EditorWebView.CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;
            EditorWebView.NavigationCompleted += EditorWebView_NavigationCompleted;
            EditorWebView.Source = serviceHost.Address;
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ShowStartupError(
                "Microsoft Edge WebView2 Runtime is required. Install the Evergreen WebView2 Runtime and start ETAB Engineering again.");
        }
        catch (Exception exception)
        {
            ShowStartupError(exception.Message);
        }
    }

    private void CoreWebView2_NavigationStarting(
        object? sender,
        CoreWebView2NavigationStartingEventArgs e)
    {
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var target) ||
            !string.Equals(target.Scheme, serviceHost.Address.Scheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(target.Host, serviceHost.Address.Host, StringComparison.OrdinalIgnoreCase) ||
            target.Port != serviceHost.Address.Port)
        {
            e.Cancel = true;
        }
    }

    private void EditorWebView_NavigationCompleted(
        object? sender,
        CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            ShowStartupError($"The embedded editor could not be loaded ({e.WebErrorStatus}).");
            return;
        }

        LoadingPanel.Visibility = Visibility.Collapsed;
        EditorWebView.Visibility = Visibility.Visible;
    }

    private void ShowStartupError(string message)
    {
        LoadingMessage.Text = message;
        MessageBox.Show(
            message,
            "ETAB Engineering",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        Close();
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (closeCompleted)
        {
            return;
        }

        e.Cancel = true;
        IsEnabled = false;
        try
        {
            EditorWebView.Dispose();
            await serviceHost.DisposeAsync();
        }
        finally
        {
            closeCompleted = true;
            Application.Current.Shutdown();
        }
    }
}
