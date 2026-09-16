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
/// Suggested extra-field labels per category, e.g. APR/term for a loan, policy/premium
/// for insurance. These are just the keys AccountEditForm pre-populates into
/// ExtraFields for a given category - not a schema change, so older vault files with
/// arbitrary ExtraFields keys keep working exactly as before, and any field can still
/// be renamed or removed freely in the edit form.
/// </summary>
public static class CategoryFieldSpec
{
    public static readonly IReadOnlyDictionary<AccountCategory, string[]> SuggestedFields =
        new Dictionary<AccountCategory, string[]>
        {
            [AccountCategory.BankAccount] = new[] { "Routing Number", "Account Type" },
            [AccountCategory.CreditCard] = new[] { "Credit Limit", "APR (%)", "Rewards Program" },
            [AccountCategory.Mortgage] = new[] { "Loan Amount", "Interest Rate (%)", "Term (years)", "Lender Contact" },
            [AccountCategory.CarLoan] = new[] { "Loan Amount", "APR (%)", "Term (months)", "Lender Contact" },
            [AccountCategory.Insurance] = new[] { "Policy Number", "Premium Amount", "Coverage Type" },
            [AccountCategory.Utility] = new[] { "Meter / Account #", "Provider", "Service Address" },
            [AccountCategory.Membership] = new[] { "Membership ID", "Plan / Tier", "Renewal Fee" },
            [AccountCategory.HomeTax] = new[] { "Parcel / Property ID", "Assessed Value", "Tax Authority" },
            [AccountCategory.ApartmentRental] = new[] { "Lease Start", "Lease End", "Monthly Rent", "Landlord Contact" },
            [AccountCategory.Investment] = new[] { "Account Type (401k/IRA/Brokerage)", "Advisor Contact" },
            [AccountCategory.Other] = Array.Empty<string>(),
        };

    public static string[] For(AccountCategory category) =>
        SuggestedFields.TryGetValue(category, out var fields) ? fields : Array.Empty<string>();
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

    /// <summary>Whose account this is - defaults to the vault profile's name for new entries, editable per-entry.</summary>
    public string Owner { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string Website { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>Next amount-due / renewal date, if this account has one.</summary>
    public DateTime? DueDate { get; set; }

    public RecurrenceType Recurrence { get; set; } = RecurrenceType.None;

    /// <summary>
    /// True if this bill is paid automatically (e.g. a credit card or utility on
    /// autopay) rather than something you pay by hand each cycle. Purely informational -
    /// it doesn't change due-date reminders or the Dues tab's filtering, it just shows
    /// up as an "Autopay" indicator so it's obvious at a glance which accounts you don't
    /// need to go pay manually.
    /// </summary>
    public bool IsAutomaticPayment { get; set; }

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
