namespace PersonalVault.Models;

/// <summary>
/// Bookkeeping for one active "Share Account" link (see Security/ShareCrypto.cs,
/// Storage/SharesStorage.cs, Services/ShareExpiryService.cs). This is NOT the shared
/// secret itself - the encrypted account data lives in a Drive file (DriveFileId) and
/// the decryption key lives only in the link's URL fragment, never persisted anywhere.
/// This record just tracks enough to show a management list and to revoke/expire the
/// right Drive file later, so it's fine to store unencrypted (see SharesStorage).
/// </summary>
public class SharedLink
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Which account this was shared from, for display only - the account itself may since have been edited or deleted.</summary>
    public Guid AccountEntryId { get; set; }

    /// <summary>Snapshot of the account's name at share time, so the management list still means something if the account is later renamed or deleted.</summary>
    public string AccountName { get; set; } = string.Empty;

    /// <summary>The Drive file holding the encrypted single-account payload. Kept private - never "anyone with the link" - see GoogleDriveSync.UploadShareAsync.</summary>
    public string DriveFileId { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Once this passes, ShareExpiryService deletes the Drive file on its next sweep and sets Revoked.</summary>
    public DateTime ExpiresUtc { get; set; }

    /// <summary>
    /// True once ShareExpiryService or a manual "Revoke Now" has deleted the Drive
    /// file. NOTE: this does NOT necessarily mean nobody ever saw it - the Apps Script
    /// Web App (docs/share/AppsScript/Code.gs) also deletes the file the instant it's
    /// opened, but has no way to report that back to this app, so a link that was
    /// already viewed still shows as "Active" here until its normal expiry passes and
    /// the next sweep discovers the file is already gone.
    /// </summary>
    public bool Revoked { get; set; }
}
