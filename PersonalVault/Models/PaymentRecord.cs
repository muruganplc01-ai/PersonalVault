namespace PersonalVault.Models;

/// <summary>
/// A single "I paid this" record, created when the user clicks "Mark as Paid..." on the
/// Dues tab. Stored separately from the main vault - see Storage/PaymentsStorage.cs -
/// so payment history accumulates on its own file and its own Drive sync cycle instead
/// of growing vault.pvlt every time a bill is paid.
/// </summary>
public class PaymentRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Links back to the AccountEntry this payment was for.</summary>
    public Guid AccountId { get; set; }

    /// <summary>
    /// Snapshot of the account's name at the moment it was paid, so payment history
    /// still reads sensibly even if that account is later renamed or deleted.
    /// </summary>
    public string AccountName { get; set; } = string.Empty;

    public decimal AmountPaid { get; set; }

    /// <summary>The date the user says they paid - as entered, not necessarily "now" (logging a past payment is fine).</summary>
    public DateTime PaidDate { get; set; }

    /// <summary>The account's due date at the moment it was marked paid, kept for reference/history even after the due date itself moves on.</summary>
    public DateTime? DueDateAtPayment { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>The whole plaintext contents of the payments file, before encryption (see PaymentsStorage).</summary>
public class PaymentsData
{
    public int SchemaVersion { get; set; } = 1;
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;
    public List<PaymentRecord> Payments { get; set; } = new();
}
