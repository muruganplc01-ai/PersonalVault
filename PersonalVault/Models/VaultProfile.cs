namespace PersonalVault.Models;

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
}
