namespace PersonalVault.Security;

/// <summary>
/// Minimum-strength rule for a brand-new or changed master secret - enforced by
/// UnlockForm (CreateNew mode only) and ChangeSecretForm's "New secret" field.
/// Deliberately NOT applied to unlocking an already-existing vault
/// (UnlockForm.UnlockExisting/RestoreFromBackup) - an older vault's secret may predate
/// this policy, and this app has no password-reset flow, so rejecting a secret that
/// already works would just lock someone out of their own data.
/// </summary>
public static class PasswordPolicy
{
    public const int MinimumLength = 12;

    /// <summary>Plain-English description, reused for both the upfront hint text and the validation error message so the two can never drift out of sync.</summary>
    public const string RequirementsText =
        "at least 12 characters, including uppercase, lowercase, and a symbol";

    public static bool IsValid(string secret) =>
        !string.IsNullOrEmpty(secret)
        && secret.Length >= MinimumLength
        && secret.Any(char.IsUpper)
        && secret.Any(char.IsLower)
        && secret.Any(c => !char.IsLetterOrDigit(c));
}
