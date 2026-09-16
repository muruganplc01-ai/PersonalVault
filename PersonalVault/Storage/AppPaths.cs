namespace PersonalVault.Storage;

/// <summary>
/// Central place for every file/folder this app touches. Everything lives under
/// %AppData%\PersonalVault so it's per-Windows-user and survives reinstalls.
/// </summary>
public static class AppPaths
{
    public static readonly string RootFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PersonalVault");

    /// <summary>The encrypted vault file itself. Safe to back up; useless without the secret.</summary>
    public static readonly string VaultLocalPath = Path.Combine(RootFolder, "vault.pvlt");

    /// <summary>
    /// Payment history recorded from the Dues tab's "Mark as Paid" button - a separate
    /// encrypted file from the vault (same secret, same AES-256-GCM scheme via
    /// VaultCrypto/PaymentsStorage) so it has its own save/upload cycle.
    /// </summary>
    public static readonly string PaymentsLocalPath = Path.Combine(RootFolder, "payments.pvlt");

    /// <summary>
    /// OAuth client secret downloaded from Google Cloud Console (Desktop app type).
    /// See README.md for how to create this. This file identifies the *app*, not you -
    /// it is not itself a credential to your data.
    /// </summary>
    public static readonly string CredentialsJsonPath = Path.Combine(RootFolder, "credentials.json");

    /// <summary>
    /// Where the Google OAuth library caches your signed-in refresh token after the first
    /// interactive sign-in. Treat this folder as sensitive - anyone with it can access your
    /// Google Drive app-file scope until you revoke access at https://myaccount.google.com/permissions.
    /// </summary>
    public static readonly string TokenStoreFolder = Path.Combine(RootFolder, "drive-token");

    /// <summary>Small non-secret settings file (reminder windows, cached Drive file id, etc).</summary>
    public static readonly string SettingsPath = Path.Combine(RootFolder, "settings.json");

    public static void EnsureFoldersExist()
    {
        Directory.CreateDirectory(RootFolder);
        Directory.CreateDirectory(TokenStoreFolder);
    }
}
