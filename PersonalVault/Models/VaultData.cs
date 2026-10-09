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

    /// <summary>
    /// User-configured Repeats/Autopay/Institution defaults per category - see Profile
    /// -> "Category Defaults...". Distinct from the hardcoded, built-in
    /// Models/AccountEntry.cs -> CategoryEntryDefaults: these are vault-specific and
    /// editable without a rebuild, and take priority over the built-in ones when both
    /// exist for the same category. Keys matched case-insensitively in code, same
    /// reasoning as CustomCategoryFields above.
    /// </summary>
    public Dictionary<string, CategoryDefault> CustomCategoryDefaults { get; set; } = new();

    /// <summary>
    /// User-configured field sets per category - see Profile -> "Field Sets..." and
    /// Models/AccountEntry.cs -> CategoryFieldSetDefaults for the full picture (built-in
    /// fallback, reserved vs. custom captions, why hiding a field never deletes its
    /// value). Key "[Defaults]" (CategoryFieldSetDefaults.DefaultsKey) applies to any
    /// category with no entry of its own here. Keys matched case-insensitively in code,
    /// same reasoning as CustomCategoryFields/CustomCategoryDefaults above.
    /// </summary>
    public Dictionary<string, List<FieldDefinition>> CategoryFieldSets { get; set; } = new();

    /// <summary>
    /// User-configured Asset/Liability classification per category - see Profile ->
    /// "Field Sets..." and Models/AccountEntry.cs -> CategoryBalanceTypeDefaults for the
    /// built-in fallback and why this exists (a loan's remaining balance is a debt, not
    /// cash on hand). Takes priority over the built-in classification for the same
    /// category. Keys matched case-insensitively in code, same reasoning as the other
    /// per-category dictionaries above.
    /// </summary>
    public Dictionary<string, BalanceType> CustomCategoryBalanceTypes { get; set; } = new();
}
