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
    /// <summary>Every 6 months - e.g. property tax billed in two installments a year (March/September).</summary>
    SemiAnnual,
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
/// Suggested Repeats/Autopay/Institution defaults per category, applied by
/// AccountEditForm only while adding a brand-new entry (never overwrites an existing
/// entry's own values on edit) - purely a convenience so e.g. a new CreditCard entry
/// doesn't start at "Repeats: None" when it's almost always monthly, or a "Work"
/// category always fills in the same employer/vendor name. Institution is null for
/// anything that doesn't have one obvious fixed value - leave it null rather than
/// guessing. Anything not listed here (or a custom category) just keeps the form's
/// normal blank/None/unchecked defaults - these are starting points, not requirements,
/// and can be changed freely before Save.
/// </summary>
public static class CategoryEntryDefaults
{
    public static readonly IReadOnlyDictionary<string, (RecurrenceType Recurrence, bool Autopay, string? Institution)> Defaults =
        new Dictionary<string, (RecurrenceType, bool, string?)>(StringComparer.OrdinalIgnoreCase)
        {
            ["CreditCard"] = (RecurrenceType.Monthly, false, null),
            ["Mortgage"] = (RecurrenceType.Monthly, false, null),
            ["CarLoan"] = (RecurrenceType.Monthly, false, null),
            ["Utility"] = (RecurrenceType.Monthly, false, null),
            ["Membership"] = (RecurrenceType.Yearly, false, null),
            ["Insurance"] = (RecurrenceType.Monthly, false, null),
            ["HomeTax"] = (RecurrenceType.SemiAnnual, false, null),
            ["ApartmentRental"] = (RecurrenceType.Monthly, false, null),
            ["Work"] = (RecurrenceType.None, false, "Porter Lee"),
        };

    public static (RecurrenceType Recurrence, bool Autopay, string? Institution)? For(string? category) =>
        !string.IsNullOrWhiteSpace(category) && Defaults.TryGetValue(category, out var defaults)
            ? defaults
            : null;
}

/// <summary>
/// One category's Repeats/Autopay/Institution defaults, configured by hand in Profile
/// -> "Category Defaults..." (Forms/CategoryDefaultsForm.cs) and stored in
/// VaultData.CustomCategoryDefaults. Unlike the hardcoded CategoryEntryDefaults above,
/// these are vault-specific, editable from the app without a rebuild, travel with the
/// vault via Drive sync, and take priority over the built-in ones for the same
/// category - see AccountEditForm.ApplyCategoryDefaults.
/// </summary>
public class CategoryDefault
{
    public RecurrenceType Recurrence { get; set; } = RecurrenceType.None;
    public bool Autopay { get; set; }
    public string? Institution { get; set; }
}

/// <summary>
/// The three kinds of control a custom (non-reserved) field can render as in Account
/// Details - see FieldDefinition/CategoryFieldSetDefaults below. Reserved/built-in captions
/// (Name, Password, Due Date, etc.) ignore this entirely, since they already have
/// their own specially-behaved controls.
/// </summary>
public enum FieldDataType { String, DropDown, Memo }

/// <summary>
/// One field's definition within a category's field set - a caption, how it should be
/// rendered if it's a custom field, and a size hint. See CategoryFieldSetDefaults' doc comment
/// for the full picture of how these drive Account Details.
/// </summary>
public class FieldDefinition
{
    public string Caption { get; set; } = "";
    public FieldDataType DataType { get; set; } = FieldDataType.String;
    public int Size { get; set; } = 150;
}

/// <summary>
/// Defines, per category, which fields appear on Account Details and in what order -
/// configured by hand in Profile -> "Field Sets..." (Forms/CategoryFieldSetsForm.cs)
/// and stored in VaultData.CategoryFieldSets, or falling back to the built-in starting
/// points here when a category has no explicit vault-level entry.
///
/// A caption matching one of ReservedCaptions (case-insensitive) maps to an existing,
/// specially-behaved control already in AccountEditForm (password mask+generate,
/// website open button, the Due Date/Repeats/Autopay group, etc.) - the field set only
/// controls whether that control is shown and where, never how it renders. Any other
/// caption is a genuinely custom field, rendered per its DataType (String/DropDown/
/// Memo) and stored in AccountEntry.ExtraFields keyed by that caption - reusing
/// storage that already round-trips through save/load/Drive sync/CSV, so adding a
/// custom field is zero schema risk.
///
/// Hiding a reserved field from a category's set never clears its value - Account
/// Details' Save only ever writes back from a control that's actually visible, so an
/// older entry's data for a since-hidden field just sits there untouched, ready to
/// reappear if the field set changes back.
/// </summary>
public static class CategoryFieldSetDefaults
{
    /// <summary>
    /// Captions that map to an existing control/group in AccountEditForm rather than
    /// being rendered generically. Order here is also the order they'd appear in if
    /// everything were enabled - BuiltInDefaults below reorders/omits per category.
    /// "Account #" is deliberately not included here - CreditCard's Card Details popup
    /// writes the card number directly into that textbox regardless of category, so it
    /// always stays visible in AccountEditForm and always saves.
    /// </summary>
    public static readonly string[] ReservedCaptions =
    {
        "Name", "Institution", "Owner", "Sub Category", "Username", "Password",
        "Website", "Phone", "Due Date", "Amount Due", "Current Balance", "Asset Value",
        "Bank Accounts", "Card Details", "Notes", "Extra Info"
    };

    public const string DefaultsKey = "[Defaults]";

    private static List<FieldDefinition> Fields(params string[] captions) =>
        captions.Select(c => new FieldDefinition { Caption = c }).ToList();

    /// <summary>
    /// Built-in starting points, fully editable afterward from Profile -> "Field
    /// Sets...". [Defaults] applies to any category with no explicit entry here or in
    /// VaultData.CategoryFieldSets - including brand-new/custom categories like "Work"
    /// or "Azure", which is exactly why Due Date/Amount Due/Current Balance are left
    /// out of it: those don't make sense for a credential-only entry.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, List<FieldDefinition>> BuiltInDefaults =
        new Dictionary<string, List<FieldDefinition>>(StringComparer.OrdinalIgnoreCase)
        {
            [DefaultsKey] = Fields("Name", "Institution", "Owner", "Sub Category",
                "Username", "Password", "Website", "Phone", "Notes", "Extra Info"),

            ["BankAccount"] = Fields("Name", "Institution", "Owner", "Sub Category",
                "Username", "Password", "Website", "Phone", "Bank Accounts", "Notes", "Extra Info"),

            ["CreditCard"] = Fields("Name", "Institution", "Owner", "Sub Category",
                "Username", "Password", "Website", "Phone", "Card Details",
                "Due Date", "Amount Due", "Current Balance", "Notes", "Extra Info"),

            ["Mortgage"] = Fields("Name", "Institution", "Owner", "Sub Category",
                "Username", "Password", "Website", "Phone",
                "Due Date", "Amount Due", "Current Balance", "Asset Value", "Notes", "Extra Info"),

            ["CarLoan"] = Fields("Name", "Institution", "Owner", "Sub Category",
                "Username", "Password", "Website", "Phone",
                "Due Date", "Amount Due", "Current Balance", "Asset Value", "Notes", "Extra Info"),

            ["ApartmentRental"] = Fields("Name", "Institution", "Owner", "Sub Category",
                "Username", "Password", "Website", "Phone", "Due Date", "Amount Due", "Notes", "Extra Info"),

            ["Utility"] = Fields("Name", "Institution", "Owner", "Sub Category",
                "Username", "Password", "Website", "Phone", "Due Date", "Amount Due", "Notes", "Extra Info"),

            ["Membership"] = Fields("Name", "Institution", "Owner", "Sub Category",
                "Username", "Password", "Website", "Phone", "Due Date", "Amount Due", "Notes", "Extra Info"),

            ["Insurance"] = Fields("Name", "Institution", "Owner", "Sub Category",
                "Username", "Password", "Website", "Phone", "Due Date", "Amount Due", "Notes", "Extra Info"),

            ["HomeTax"] = Fields("Name", "Institution", "Owner", "Sub Category",
                "Website", "Phone", "Due Date", "Amount Due", "Notes", "Extra Info"),

            ["Investment"] = Fields("Name", "Institution", "Owner", "Sub Category",
                "Username", "Password", "Website", "Phone", "Current Balance", "Notes", "Extra Info"),
        };

    /// <summary>Vault-level override if present, else the built-in for this exact category, else [Defaults].</summary>
    public static List<FieldDefinition> Resolve(string? category, IReadOnlyDictionary<string, List<FieldDefinition>> vaultOverrides)
    {
        if (!string.IsNullOrWhiteSpace(category))
        {
            var vaultMatch = vaultOverrides.FirstOrDefault(kv => string.Equals(kv.Key, category, StringComparison.OrdinalIgnoreCase));
            if (vaultMatch.Value != null) return vaultMatch.Value;

            var builtInMatch = BuiltInDefaults.FirstOrDefault(kv => string.Equals(kv.Key, category, StringComparison.OrdinalIgnoreCase));
            if (builtInMatch.Value != null) return builtInMatch.Value;
        }

        var vaultDefaults = vaultOverrides.FirstOrDefault(kv => string.Equals(kv.Key, DefaultsKey, StringComparison.OrdinalIgnoreCase));
        return vaultDefaults.Value ?? BuiltInDefaults[DefaultsKey];
    }
}

/// <summary>
/// Whether a category's Current Balance represents money you have (Asset - a bank or
/// investment balance) or money you owe (Liability - a loan's remaining payoff). The
/// Overview tab needs this distinction to avoid exactly the bug that prompted adding
/// it: a car loan's remaining balance was being added to "Total On Hand" as if it were
/// cash, inflating net worth instead of reducing it. See CategoryBalanceTypeDefaults
/// for the built-in classification and MainForm.RefreshOverview for how it's used.
/// </summary>
public enum BalanceType { Asset, Liability }

/// <summary>
/// Built-in Asset/Liability classification per category - configurable per-vault from
/// Profile -> "Field Sets..." (stored in VaultData.CustomCategoryBalanceTypes, which
/// takes priority over this when set). A category with no entry either place (and
/// most don't - Utility/Membership/Insurance/HomeTax/ApartmentRental/Other/any custom
/// category don't carry a Current Balance concept at all in their field sets) is
/// treated as Asset by MainForm.GetCategoryBalanceType's fallback - deliberately the
/// same behavior this app always had before this classification existed, so nothing
/// silently starts subtracting from net worth without an explicit Liability setting.
/// </summary>
public static class CategoryBalanceTypeDefaults
{
    public static readonly IReadOnlyDictionary<string, BalanceType> Defaults =
        new Dictionary<string, BalanceType>(StringComparer.OrdinalIgnoreCase)
        {
            ["BankAccount"] = BalanceType.Asset,
            ["Investment"] = BalanceType.Asset,
            ["CreditCard"] = BalanceType.Liability,
            ["Mortgage"] = BalanceType.Liability,
            ["CarLoan"] = BalanceType.Liability,
        };

    public static BalanceType? For(string? category) =>
        !string.IsNullOrWhiteSpace(category) && Defaults.TryGetValue(category, out var type)
            ? type
            : null;
}

/// <summary>
/// One sub-account under a BankAccount-category AccountEntry - e.g. "Checking",
/// "Savings", "Money Market" all under the same "Chase Bank" entry. Edited via Account
/// Details' "Bank Accounts..." button (see Forms/BankAccountsForm.cs,
/// Forms/BankSubAccountForm.cs), shown only for the BankAccount category. A plain data
/// holder like every other model in this file - no behavior of its own.
/// </summary>
public class BankSubAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Free text, e.g. "Checking", "Savings", "Money Market" - not a fixed enum, same reasoning as AccountEntry.Category.</summary>
    public string Label { get; set; } = string.Empty;

    public string AccountNumber { get; set; } = string.Empty;
    public string RoutingNumber { get; set; } = string.Empty;

    /// <summary>Null means "not tracked", same 0-vs-not-entered distinction as AccountEntry.CurrentBalance.</summary>
    public decimal? Balance { get; set; }

    /// <summary>Only meaningful when Balance is set.</summary>
    public DateTime? BalanceAsOf { get; set; }

    public string Notes { get; set; } = string.Empty;
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

    /// <summary>
    /// Free text, optional - e.g. "Work"/"Personal" under an Email category. Not a
    /// fixed enum, same reasoning as Category; suggestions in Account Details are
    /// scoped to other entries sharing the same Category, so one category's
    /// sub-categories don't clutter another's list. Shown/hidden per category via
    /// CategoryFieldSetDefaults like any other reserved field.
    /// </summary>
    public string SubCategory { get; set; } = string.Empty;

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

    /// <summary>
    /// What the underlying thing is worth - distinct from CurrentBalance, which for a
    /// Mortgage/CarLoan-type entry is what you still OWE (a liability), not what the
    /// home/car is worth (an asset). Only meaningful for categories where that
    /// distinction applies; null means "not tracked." Totalled separately from Total On
    /// Hand in the Overview tab - see MainForm.RefreshOverview.
    /// </summary>
    public decimal? AssetValue { get; set; }

    /// <summary>The date AssetValue was accurate as of. Only meaningful when AssetValue is set.</summary>
    public DateTime? AssetValueAsOf { get; set; }

    public string Notes { get; set; } = string.Empty;

    /// <summary>
    /// Free-form key/value pairs for anything that doesn't fit the fields above -
    /// e.g. "Policy Number=12345", "Routing Number=...", "Lease End=...".
    /// </summary>
    public Dictionary<string, string> ExtraFields { get; set; } = new();

    /// <summary>
    /// One bank can have several accounts (Checking, Savings, Money Market, ...) -
    /// this holds each one's own account/routing number and balance, edited via
    /// Account Details' "Bank Accounts..." button (BankAccount category only). Empty
    /// for every other category and for older entries created before this existed. When
    /// any sub-account here has a balance set, MainForm's Overview tab uses the sum of
    /// them instead of this entry's own CurrentBalance (see MainForm.EffectiveBalance) -
    /// so CurrentBalance stops mattering for an account once its sub-accounts are filled
    /// in, rather than needing to be kept in sync with them by hand.
    /// </summary>
    public List<BankSubAccount> SubAccounts { get; set; } = new();

    // --- Internal bookkeeping, not shown directly in the edit form ---

    /// <summary>The last calendar day a due-date reminder was shown for this entry.</summary>
    public DateOnly? LastNotifiedOn { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;

    public override string ToString() => Name;
}
