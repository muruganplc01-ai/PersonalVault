namespace PersonalVault.Models;

/// <summary>
/// The kinds of accounts this vault knows about. Add more here any time -
/// existing entries are unaffected, and the edit form picks up new values automatically.
/// </summary>
public enum AccountCategory
{
    BankAccount,
    CreditCard,
    Mortgage,
    CarLoan,
    Insurance,
    Utility,
    Membership,
    HomeTax,
    ApartmentRental,
    Investment,
    Other
}

public enum RecurrenceType
{
    None,
    Weekly,
    Monthly,
    Quarterly,
    Yearly
}

/// <summary>
/// A single stored account/credential record. Everything here is written to the
/// encrypted vault file as-is (see Storage/VaultStorage.cs) - nothing in this class
/// is persisted anywhere unencrypted.
/// </summary>
public class AccountEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public AccountCategory Category { get; set; } = AccountCategory.Other;

    /// <summary>Friendly label, e.g. "Chase Checking" or "Toyota Car Loan".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>e.g. "Chase Bank", "State Farm", "City of Austin Utilities".</summary>
    public string Institution { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string Website { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>Next amount-due / renewal date, if this account has one.</summary>
    public DateTime? DueDate { get; set; }

    public RecurrenceType Recurrence { get; set; } = RecurrenceType.None;

    public string Notes { get; set; } = string.Empty;

    /// <summary>
    /// Free-form key/value pairs for anything that doesn't fit the fields above -
    /// e.g. "Policy Number=12345", "Routing Number=...", "Lease End=...".
    /// </summary>
    public Dictionary<string, string> ExtraFields { get; set; } = new();

    // --- Internal bookkeeping, not shown directly in the edit form ---

    /// <summary>The last calendar day a due-date reminder was shown for this entry.</summary>
    public DateOnly? LastNotifiedOn { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;

    public override string ToString() => Name;
}
