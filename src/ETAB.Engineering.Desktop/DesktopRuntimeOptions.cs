using System.Net;

namespace ETAB.Engineering.Desktop;

internal static class DesktopRuntimeOptions
{
    private const string ListenUrlVariable = "ETAB_ENGINEERING_URL";

    public static string ResolveListenUrl()
    {
        var configured = Environment.GetEnvironmentVariable(ListenUrlVariable);
        if (string.IsNullOrWhiteSpace(configured))
        {
            return "http://127.0.0.1:0";
        }

        if (!Uri.TryCreate(configured, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
            !IsLoopback(uri) ||
            !string.IsNullOrEmpty(uri.AbsolutePath.Trim('/')) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException(
                $"{ListenUrlVariable} must be an HTTP loopback URL such as http://127.0.0.1:5087.");
        }

        return uri.GetLeftPart(UriPartial.Authority);
    }

    private static bool IsLoopback(Uri uri) =>
        string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
        IPAddress.TryParse(uri.Host, out var address) && IPAddress.IsLoopback(address);
}
