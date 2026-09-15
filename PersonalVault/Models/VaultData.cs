namespace PersonalVault.Models;

/// <summary>
/// The whole plaintext contents of the vault, before encryption. This is the object
/// that gets JSON-serialized and then AES-encrypted by VaultStorage.
/// </summary>
public class VaultData
{
    public int SchemaVersion { get; set; } = 1;
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;
    public List<AccountEntry> Accounts { get; set; } = new();
}
