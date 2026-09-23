namespace PersonalVault.Storage;

/// <summary>
/// Central place for every file/folder this app touches. Defaults to a "PersonalVault"
/// subfolder right next to the running .exe (AppContext.BaseDirectory) - a portable,
/// no-installer-needed layout - but can be pointed anywhere via the Profile screen's
/// "Change Data Folder..." (see Utils/DataFolderMover.cs), which copies everything over
/// and remembers the choice in the registry (Utils/DataFolderLocation.cs) so it's known
/// again on the next launch, before this class would otherwise know where to look.
/// </summary>
public static class AppPaths
{
    private static string _rootFolder = ComputeDefaultRootFolder();

    public static string RootFolder => _rootFolder;

    /// <summary>The encrypted vault file itself. Safe to back up; useless without the secret.</summary>
    public static string VaultLocalPath => Path.Combine(_rootFolder, "vault.pvlt");

    /// <summary>
    /// Payment history recorded from the Dues tab's "Mark as Paid" button - a separate
    /// encrypted file from the vault (same secret, same AES-256-GCM scheme via
    /// VaultCrypto/PaymentsStorage) so it has its own save/upload cycle.
    /// </summary>
    public static string PaymentsLocalPath => Path.Combine(_rootFolder, "payments.pvlt");

    /// <summary>
    /// OAuth client secret downloaded from Google Cloud Console (Desktop app type).
    /// See README.md for how to create this. This file identifies the *app*, not you -
    /// it is not itself a credential to your data.
    /// </summary>
    public static string CredentialsJsonPath => Path.Combine(_rootFolder, "credentials.json");

    /// <summary>
    /// Where the Google OAuth library caches your signed-in refresh token after the first
    /// interactive sign-in. Treat this folder as sensitive - anyone with it can access your
    /// Google Drive app-file scope until you revoke access at https://myaccount.google.com/permissions.
    /// </summary>
    public static string TokenStoreFolder => Path.Combine(_rootFolder, "drive-token");

    /// <summary>Small non-secret settings file (reminder windows, cached Drive file id, etc).</summary>
    public static string SettingsPath => Path.Combine(_rootFolder, "settings.json");

    /// <summary>
    /// Bookkeeping for active "Share Account" links (see Models/SharedLink.cs) - which
    /// Drive file, when it expires, whether it's been revoked. Deliberately unencrypted:
    /// it holds no secrets (the shared account data lives encrypted on Drive, and the
    /// decryption key lives only in a share link's URL fragment, never on disk).
    /// </summary>
    public static string SharesLocalPath => Path.Combine(_rootFolder, "shares.json");

    public static void EnsureFoldersExist()
    {
        Directory.CreateDirectory(_rootFolder);
        Directory.CreateDirectory(TokenStoreFolder);
    }

    private static string ComputeDefaultRootFolder() =>
        Path.Combine(AppContext.BaseDirectory, "PersonalVault");

    /// <summary>
    /// Applied once at startup (Program.cs, before anything else touches AppPaths) from
    /// whatever DataFolderLocation.GetOverride() returns. A null/blank override leaves
    /// the default (app-folder-relative) location in place.
    /// </summary>
    public static void ApplyOverride(string? overrideFolder)
    {
        if (!string.IsNullOrWhiteSpace(overrideFolder))
            _rootFolder = overrideFolder;
    }

    /// <summary>
    /// Repoints every path above at a new root, with nothing copied - callers (see
    /// Utils/DataFolderMover.MoveTo) are expected to have already copied the actual
    /// files there first. Every path here is a computed property, not a cached field, so
    /// this takes effect immediately for the rest of this run - no restart needed.
    /// </summary>
    public static void SetRootFolder(string newRootFolder)
    {
        _rootFolder = newRootFolder;
    }
}
