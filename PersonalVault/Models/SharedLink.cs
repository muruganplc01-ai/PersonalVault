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

    /// <summary>The Drive file holding the encrypted single-account payload.</summary>
    public string DriveFileId { get; set; } = string.Empty;

    /// <summary>The "anyone with the link" permission id Drive returned when sharing DriveFileId - not currently needed to revoke (deleting the file removes it too), kept for reference.</summary>
    public string? PermissionId { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Once this passes, ShareExpiryService deletes the Drive file on its next sweep and sets Revoked.</summary>
    public DateTime ExpiresUtc { get; set; }

    /// <summary>True once the Drive file has been deleted, whether by expiry or a manual "Revoke Now".</summary>
    public bool Revoked { get; set; }
}
