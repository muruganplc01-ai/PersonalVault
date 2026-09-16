using System.Diagnostics;

namespace PersonalVault.Utils;

/// <summary>
/// Opens a URL either through Windows' normal "open with default browser" behavior, or
/// through a specific browser .exe if the user set one in the Profile form
/// (AppSettings.DefaultBrowserPath). Centralized here so AccountEditForm's Website
/// "Open" button and MainForm's Accounts-tab "Open URL" button behave identically
/// instead of duplicating this logic in two places.
/// </summary>
public static class BrowserLauncher
{
    /// <summary>
    /// Normalizes a possibly-bare domain (e.g. "statefarm.com") into a full https://
    /// URL and validates it's a real http(s) address. Returns null - rather than
    /// throwing - if the text isn't usable; callers show their own message in that case.
    /// </summary>
    public static Uri? TryParseUrl(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var trimmed = text.Trim();
        if (!trimmed.Contains("://"))
            trimmed = "https://" + trimmed;

        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri
            : null;
    }

    /// <summary>
    /// Launches the given URL. If browserExecutablePath points at a real file, launches
    /// that browser directly with the URL as its argument; otherwise falls back to
    /// Windows' normal default-browser behavior (also the fallback if the configured
    /// path has gone stale, e.g. that browser was uninstalled or moved).
    /// </summary>
    public static void Open(Uri url, string? browserExecutablePath)
    {
        if (!string.IsNullOrWhiteSpace(browserExecutablePath) && File.Exists(browserExecutablePath))
        {
            Process.Start(new ProcessStartInfo(browserExecutablePath, $"\"{url.AbsoluteUri}\"") { UseShellExecute = true });
        }
        else
        {
            // UseShellExecute is required here (defaults to false on .NET Core/5+) so this
            // hands the URL to Windows' own "open with default browser" behavior instead
            // of trying to execute it as if it were a local program.
            Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
        }
    }
}
