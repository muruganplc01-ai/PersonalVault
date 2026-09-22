namespace PersonalVault.Models;

/// <summary>
/// The built-in starter categories. The vault is NOT limited to these - use the
/// "+ New..." button next to Category in Account Details to add your own (e.g.
/// "Tuition", "College Fees"). Category is stored as plain text rather than a fixed
/// enum specifically so new ones never require a code change; this list only supplies
/// sensible defaults for a brand-new vault and for CategoryFieldSpec's suggestions.
/// </summary>
public static class AccountCategories
{
    public static readonly string[] Defaults =
    {
        "BankAccount", "CreditCard", "Mortgage", "CarLoan", "Insurance", "Utility",
        "Membership", "HomeTax", "ApartmentRental", "Investment", "Other"
    };

    public const string Default = "Other";
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
/// be renamed or removed freely in the edit form. Looked up case-insensitively so it
/// still matches the built-in categories regardless of how they were typed; a
/// user-added custom category simply has no suggestions (an empty list), which is
/// expected - there's no way to know ahead of time what fields "Tuition" should have.
/// </summary>
public static class CategoryFieldSpec
{
    public static readonly IReadOnlyDictionary<string, string[]> SuggestedFields =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["BankAccount"] = new[] { "Routing Number", "Account Type" },
            ["CreditCard"] = new[] { "Credit Limit", "APR (%)", "Rewards Program" },
            ["Mortgage"] = new[] { "Loan Amount", "Interest Rate (%)", "Term (years)", "Lender Contact" },
            ["CarLoan"] = new[] { "Loan Amount", "APR (%)", "Term (months)", "Lender Contact" },
            ["Insurance"] = new[] { "Policy Number", "Premium Amount", "Coverage Type" },
            ["Utility"] = new[] { "Meter / Account #", "Provider", "Service Address" },
            ["Membership"] = new[] { "Membership ID", "Plan / Tier", "Renewal Fee" },
            ["HomeTax"] = new[] { "Parcel / Property ID", "Assessed Value", "Tax Authority" },
            ["ApartmentRental"] = new[] { "Lease Start", "Lease End", "Monthly Rent", "Landlord Contact" },
            ["Investment"] = new[] { "Account Type (401k/IRA/Brokerage)", "Advisor Contact" },
            ["Other"] = Array.Empty<string>(),
        };

    public static string[] For(string? category) =>
        !string.IsNullOrWhiteSpace(category) && SuggestedFields.TryGetValue(category, out var fields)
            ? fields
            : Array.Empty<string>();
}

/// <summary>
/// A single stored account/credential record. Everything here is written to the
/// encrypted vault file as-is (see Storage/VaultStorage.cs) - nothing in this class
/// is persisted anywhere unencrypted.
/// </summary>
public class AccountEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Free text, not a fixed enum - lets a person add their own categories (e.g.
    /// "Tuition") from Account Details rather than being stuck with the built-in list.
    /// Defaults to "Other" so nothing is ever blank.
    /// </summary>
    public string Category { get; set; } = AccountCategories.Default;

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

    /// <summary>
    /// A fixed amount owed on this account - e.g. a remaining tuition/college-fees
    /// balance - separate from DueDate/Recurrence, which are about *when* a bill is
    /// next due rather than a running balance owed. Null means "not tracked for this
    /// account" and is kept nullable (rather than defaulting to 0) so a genuine $0
    /// amount due stays distinguishable from nothing having been entered at all. Shown
    /// on the Dues tab and totalled into the Overview tab's "Total Dues" figure.
    /// </summary>
    public decimal? AmountDue { get; set; }

    /// <summary>
    /// A point-in-time balance snapshot, with the date it was as of - meant for bank
    /// and investment accounts ("how much do I currently have"), but not restricted to
    /// any particular category. Null CurrentBalance means "not tracked," for the same
    /// 0-vs-not-entered reason as AmountDue above. Feeds the Overview tab's "Total On
    /// Hand" figure.
    /// </summary>
    public decimal? CurrentBalance { get; set; }

    /// <summary>The date CurrentBalance was accurate as of. Only meaningful when CurrentBalance is set.</summary>
    public DateTime? CurrentBalanceAsOf { get; set; }

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
