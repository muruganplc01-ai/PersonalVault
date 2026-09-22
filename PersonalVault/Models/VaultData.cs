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

    /// <summary>
    /// User-defined "suggested fields" templates for custom (non-built-in) account
    /// categories - e.g. "Tuition" -> ["Term", "Amount Per Term", "Payment Portal"].
    /// Filled in from Account Details' "+ Category Fields" button the first time
    /// someone defines fields for a category that isn't one of the built-in defaults
    /// (see Models/AccountEntry.cs -> CategoryFieldSpec for those). Keys are matched
    /// case-insensitively in code, not via this dictionary's own comparer, since JSON
    /// deserialization always rebuilds it with the default comparer.
    /// </summary>
    public Dictionary<string, string[]> CustomCategoryFields { get; set; } = new();
}
