namespace PersonalVault.Models;

/// <summary>
/// The plaintext JSON shape encrypted by ShareCrypto.Encrypt for one "Share Account"
/// link. This is the exact contract docs/share/index.html's JS must deserialize after
/// decrypting - keep the two in sync if this ever changes. ExpiresUtc rides along
/// inside the encrypted payload purely so the viewer page can display it; the real
/// enforcement is that the Drive file itself stops existing once ShareExpiryService (or
/// a manual revoke) deletes it - see README.md's "Known limitations".
/// </summary>
public class SharedAccountPayload
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Institution { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string Website { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public Dictionary<string, string> ExtraFields { get; set; } = new();
    public DateTime ExpiresUtc { get; set; }
}
