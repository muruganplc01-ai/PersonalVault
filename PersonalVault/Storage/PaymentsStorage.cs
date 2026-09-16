using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using PersonalVault.Models;
using PersonalVault.Security;

namespace PersonalVault.Storage;

/// <summary>
/// Same encrypted-file pattern as VaultStorage (AES-256-GCM via VaultCrypto, same
/// master secret, atomic temp-file-then-swap writes), applied to the separate payment
/// history file (AppPaths.PaymentsLocalPath) instead of the vault. Kept as its own
/// file/class rather than folded into VaultData, per the "mark as paid" feature - it
/// gets its own save and its own Drive upload, so recording a payment never has to
/// rewrite (or risk corrupting) the whole account vault.
/// </summary>
public static class PaymentsStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() }
    };

    public static bool LocalPaymentsExist() => File.Exists(AppPaths.PaymentsLocalPath);

    /// <summary>
    /// Loads the local payments file, or hands back a fresh empty one if it doesn't
    /// exist yet - the normal case until the very first bill is ever marked paid on
    /// this PC.
    /// </summary>
    public static PaymentsData LoadLocalOrEmpty(string secret)
    {
        if (!LocalPaymentsExist()) return new PaymentsData();
        byte[] blob = File.ReadAllBytes(AppPaths.PaymentsLocalPath);
        return LoadFromBytes(blob, secret);
    }

    public static PaymentsData LoadFromBytes(byte[] blob, string secret)
    {
        byte[] plaintext = VaultCrypto.Decrypt(blob, secret);
        try
        {
            return JsonSerializer.Deserialize<PaymentsData>(plaintext, JsonOptions) ?? new PaymentsData();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public static void SaveLocal(PaymentsData data, string secret)
    {
        AppPaths.EnsureFoldersExist();
        data.ModifiedUtc = DateTime.UtcNow;

        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(data, JsonOptions);
        byte[] blob;
        try
        {
            blob = VaultCrypto.Encrypt(plaintext, secret);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }

        // Write to a temp file and swap it in, same as VaultStorage - a crash or power
        // loss mid-write can never leave payments.pvlt half-written / corrupt.
        string tempPath = AppPaths.PaymentsLocalPath + ".tmp";
        File.WriteAllBytes(tempPath, blob);
        if (File.Exists(AppPaths.PaymentsLocalPath))
            File.Replace(tempPath, AppPaths.PaymentsLocalPath, null);
        else
            File.Move(tempPath, AppPaths.PaymentsLocalPath);
    }
}
