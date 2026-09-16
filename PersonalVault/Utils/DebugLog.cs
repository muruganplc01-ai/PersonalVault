namespace PersonalVault.Utils;

/// <summary>
/// Opt-in diagnostic logging for troubleshooting things like "why didn't this sync to
/// Google Drive". Off by default and adds no overhead beyond a File.Exists check per
/// call - turns on the moment a file named "debug.txt" is placed next to
/// PersonalVault.exe (no restart required), and turns back off the moment it's removed.
///
/// Writes to a new log file every hour, so a log never grows unbounded across a long
/// "always running" tray-app session: PersonalVault-{yyyy-MM-dd}-{HH}.log, in a "logs"
/// folder next to the .exe (i.e. right beside debug.txt itself, easy to find without
/// digging into %AppData%).
///
/// Every call is wrapped so a logging failure (disk full, folder not writable, etc.)
/// can never be the thing that crashes the app it's trying to help debug.
/// </summary>
public static class DebugLog
{
    private const string ApplicationName = "PersonalVault";

    private static readonly string TriggerFilePath = Path.Combine(AppContext.BaseDirectory, "debug.txt");
    private static readonly string LogsFolder = Path.Combine(AppContext.BaseDirectory, "logs");
    private static readonly object WriteLock = new();

    /// <summary>Checked fresh on every call (a plain File.Exists is cheap) so toggling debug.txt takes effect immediately without restarting the app.</summary>
    public static bool IsEnabled
    {
        get
        {
            try { return File.Exists(TriggerFilePath); }
            catch { return false; }
        }
    }

    public static void Write(string message)
    {
        if (!IsEnabled) return;

        try
        {
            var now = DateTime.Now;
            string fileName = $"{ApplicationName}-{now:yyyy-MM-dd}-{now:HH}.log";
            string path = Path.Combine(LogsFolder, fileName);
            string line = $"[{now:yyyy-MM-dd HH:mm:ss.fff}] [thread {Environment.CurrentManagedThreadId}] {message}{Environment.NewLine}";

            // A lock (rather than just relying on FileStream sharing) keeps concurrent
            // fire-and-forget async calls (e.g. two saves in quick succession) from
            // interleaving or losing writes to each other.
            lock (WriteLock)
            {
                Directory.CreateDirectory(LogsFolder);
                File.AppendAllText(path, line);
            }
        }
        catch
        {
            // See class doc comment - logging must never be why the app crashes.
        }
    }

    /// <summary>Logs an exception with its full details (message, type, stack trace) - always use this over Write(ex.Message) so nothing important gets lost.</summary>
    public static void WriteException(string context, Exception ex) => Write($"{context} - EXCEPTION: {ex}");
}
