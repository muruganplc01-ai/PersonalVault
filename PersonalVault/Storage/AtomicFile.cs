namespace PersonalVault.Storage;

/// <summary>
/// Swaps a freshly-written temp file into place as the real file, tolerating the
/// transient "file in use"/"access denied" errors that Windows Defender real-time
/// scanning (or similar - an indexer, a backup agent, OneDrive, etc.) can briefly throw
/// right after a file is written, before anything in this app ever opens it again.
/// VaultStorage/PaymentsStorage used to call File.Replace/File.Move directly here, which
/// surfaced as a real unhandled crash (UnauthorizedAccessException: "Access to the path
/// is denied") when that brief external lock landed at just the wrong moment - a handful
/// of retries with a short backoff is the standard mitigation for this class of problem,
/// and still throws for a genuine, non-transient failure (e.g. real permission issue)
/// once attempts are exhausted, rather than hiding it.
/// </summary>
internal static class AtomicFile
{
    public static void ReplaceInto(string tempPath, string destPath)
    {
        const int maxAttempts = 6;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                if (File.Exists(destPath))
                    File.Replace(tempPath, destPath, null);
                else
                    File.Move(tempPath, destPath);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts && (ex is IOException || ex is UnauthorizedAccessException))
            {
                Thread.Sleep(150 * attempt);
            }
        }
    }
}
