namespace PersonalVault.Models;

/// <summary>
/// The whole plaintext contents of the vault, before encryption. This is the object
/// that gets JSON-serialized and then AES-encrypted by VaultStorage.
/// </summary>
public class VaultData
{
    public int SchemaVersion { get; set; } = 2;
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;
    public List<AccountEntry> Accounts { get; set; } = new();

    /// <summary>The vault owner's name/picture - see VaultProfile. Never null; starts empty.</summary>
    public VaultProfile Profile { get; set; } = new();
}
