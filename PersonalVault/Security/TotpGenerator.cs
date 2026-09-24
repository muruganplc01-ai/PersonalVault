using System.Security.Cryptography;

namespace PersonalVault.Security;

/// <summary>
/// RFC 6238 TOTP (Time-based One-Time Password), the same algorithm every mainstream
/// authenticator app (Google Authenticator, Authy, Microsoft Authenticator, ...)
/// implements - HMAC-SHA1 over a 30-second time counter, 6-digit output. Built directly
/// with .NET's own HMACSHA1 rather than a third-party TOTP package, matching this
/// project's existing preference (see VaultCrypto/ShareCrypto) for platform-provided
/// crypto primitives over external dependencies.
///
/// The secret this operates on is never itself persisted by this class - see
/// Storage/MfaSecretStorage.cs for where and how it's actually stored (DPAPI, per
/// machine, deliberately outside the encrypted vault - see that file's doc comment for
/// why).
/// </summary>
public static class TotpGenerator
{
    private const int StepSeconds = 30;
    private const int Digits = 6;

    /// <summary>The current 6-digit code for this secret, as a zero-padded string (e.g. "042819").</summary>
    public static string GenerateCode(byte[] secret) => GenerateCode(secret, DateTimeOffset.UtcNow);

    public static string GenerateCode(byte[] secret, DateTimeOffset time)
    {
        long counter = time.ToUnixTimeSeconds() / StepSeconds;
        return ComputeCode(secret, counter);
    }

    /// <summary>
    /// Checks a user-entered code against the current time step and one step on either
    /// side (±30 seconds) - a standard, small allowance for clock drift between this PC
    /// and whatever clock the authenticator app trusts, without meaningfully widening
    /// the window an attacker could brute-force in.
    /// </summary>
    public static bool Validate(byte[] secret, string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        code = code.Trim();

        long currentStep = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / StepSeconds;
        for (long step = currentStep - 1; step <= currentStep + 1; step++)
        {
            if (ComputeCode(secret, step) == code) return true;
        }
        return false;
    }

    private static string ComputeCode(byte[] secret, long counter)
    {
        byte[] counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian) Array.Reverse(counterBytes);

        using var hmac = new HMACSHA1(secret);
        byte[] hash = hmac.ComputeHash(counterBytes);

        int offset = hash[^1] & 0x0F;
        int binaryCode = ((hash[offset] & 0x7F) << 24)
            | ((hash[offset + 1] & 0xFF) << 16)
            | ((hash[offset + 2] & 0xFF) << 8)
            | (hash[offset + 3] & 0xFF);

        int truncated = binaryCode % (int)Math.Pow(10, Digits);
        return truncated.ToString().PadLeft(Digits, '0');
    }
}
