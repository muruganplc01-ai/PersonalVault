namespace PersonalVault.Models;

/// <summary>Which second factor (if any) is required after the master secret. See MfaSetupForm/MfaVerifyForm.</summary>
public enum MfaMethod
{
    None,
    Totp,
    Email
}

/// <summary>
/// The vault owner's display identity - a name and an optional picture. This lives
/// inside VaultData, so it's encrypted with everything else and rides along
/// automatically whenever the vault syncs to Drive. There is deliberately no separate,
/// unencrypted upload of the picture anywhere - see ProfileForm/README for why.
/// </summary>
public class VaultProfile
{
    public string Name { get; set; } = string.Empty;

    /// <summary>PNG bytes, base64-encoded so this serializes cleanly as JSON text. Empty = no picture set.</summary>
    public string PictureBase64 { get; set; } = string.Empty;

    public bool HasPicture => !string.IsNullOrEmpty(PictureBase64);

    /// <summary>Defaults to None for every existing vault - opting in is required, unlock behavior is unchanged unless this is set.</summary>
    public MfaMethod MfaMethod { get; set; } = MfaMethod.None;

    /// <summary>Only meaningful when MfaMethod is Email. Safe to store here (unlike a secret) - knowing the address doesn't help an attacker without access to that inbox.</summary>
    public string MfaEmailAddress { get; set; } = string.Empty;

    /// <summary>
    /// SHA-256 hex hashes of unused TOTP backup codes - never the codes themselves.
    /// Deliberately stored here (inside the vault, unlike the TOTP secret itself in
    /// Storage/MfaSecretStorage.cs) so a backup code still works after a disaster-recovery
    /// restore on a brand-new machine, where the per-machine TOTP secret does not exist
    /// yet. One hash is removed the moment its code is used, making each one-time-use.
    /// </summary>
    public List<string> MfaBackupCodeHashes { get; set; } = new();
}
