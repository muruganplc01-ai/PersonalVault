using PersonalVault.Storage;

namespace PersonalVault.Utils;

/// <summary>
/// Lets a "portable distribution" folder carry its own credentials.json alongside the
/// .exe (e.g. when you're handing PersonalVault to a family member), so they never have
/// to find %AppData% and copy a file in themselves. On first launch, if this machine
/// doesn't already have a credentials.json under %AppData%\PersonalVault, but one was
/// shipped next to the .exe, it's copied in automatically - completely transparent.
///
/// This is safe to ship widely: credentials.json only identifies the *app* (an OAuth
/// "Desktop" client), not any person's data. Each family member still authorizes with
/// their own separate Google account and gets their own separate Drive file - sharing
/// this file does not give anyone access to anyone else's vault or Drive contents.
/// </summary>
public static class CredentialBootstrap
{
    public static void EnsureCredentialsCopiedFromAppFolder()
    {
        if (File.Exists(AppPaths.CredentialsJsonPath))
            return; // Already set up - most common case after the first run.

        string bundledPath = Path.Combine(AppContext.BaseDirectory, "credentials.json");
        if (!File.Exists(bundledPath))
            return; // Nothing shipped alongside the .exe - fine, sign-in just won't work until one's added.

        try
        {
            AppPaths.EnsureFoldersExist();
            File.Copy(bundledPath, AppPaths.CredentialsJsonPath, overwrite: false);
        }
        catch
        {
            // Non-fatal: worst case, "Sign in to Google Drive" reports credentials are
            // missing later, same as if this bootstrap step didn't exist at all.
        }
    }
}
