using System.Security.Cryptography;

namespace PersonalVault.Storage;

/// <summary>
/// Stores the raw TOTP secret DPAPI-protected (Windows' built-in per-user encryption,
/// via CryptProtectData/CryptUnprotectData under the hood), in its own file OUTSIDE the
/// encrypted vault entirely - this is a deliberate, load-bearing design choice, not an
/// oversight. If the TOTP secret lived inside vault.pvlt, then decrypting the vault
/// with the master secret would hand you everything needed to also generate a valid
/// TOTP code - "two-factor" would just be a UI formality, not a real second factor.
/// Keeping it here means someone who steals vault.pvlt (or a Drive copy) and even
/// correctly guesses the master secret still cannot compute a valid code without also
/// being logged into this exact Windows account on this exact machine.
///
/// Consequence, stated plainly: this does NOT travel to a new PC or survive a disaster
/// recovery restore - TOTP has to be set up fresh there. See Models/VaultProfile.cs's
/// MfaBackupCodeHashes for the recovery path that DOES travel with the vault.
/// </summary>
public static class MfaSecretStorage
{
    private static string FilePath => Path.Combine(AppPaths.RootFolder, "mfa-totp.dat");

    public static bool Exists() => File.Exists(FilePath);

    public static void Save(byte[] rawSecret)
    {
        AppPaths.EnsureFoldersExist();
        byte[] protectedBytes = ProtectedData.Protect(rawSecret, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(FilePath, protectedBytes);
    }

    /// <summary>Returns null if no TOTP secret has been set up on this machine, or if the protected file can't be read/unprotected (e.g. copied from a different Windows account - DPAPI ties it to the account that protected it).</summary>
    public static byte[]? Load()
    {
        if (!File.Exists(FilePath)) return null;

        try
        {
            byte[] protectedBytes = File.ReadAllBytes(FilePath);
            return ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException)
        {
            // Belongs to a different Windows user/machine, or is corrupt - treat as
            // "no secret available here" rather than crashing; the caller falls back
            // to requiring a backup code, same as if TOTP had never been set up on
            // this machine at all.
            return null;
        }
    }

    public static void Delete()
    {
        if (File.Exists(FilePath)) File.Delete(FilePath);
    }
}
