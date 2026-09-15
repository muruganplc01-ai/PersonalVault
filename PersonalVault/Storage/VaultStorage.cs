using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using PersonalVault.Models;
using PersonalVault.Security;

namespace PersonalVault.Storage;

/// <summary>
/// Turns a VaultData object into encrypted bytes on disk and back. This is the only
/// place that touches VaultCrypto directly - forms and the tray context always go
/// through here so the JSON shape and encryption stay consistent.
/// </summary>
public static class VaultStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() }
    };

    public static bool LocalVaultExists() => File.Exists(AppPaths.VaultLocalPath);

    public static VaultData LoadLocal(string secret)
    {
        byte[] blob = File.ReadAllBytes(AppPaths.VaultLocalPath);
        return LoadFromBytes(blob, secret);
    }

    public static VaultData LoadFromBytes(byte[] blob, string secret)
    {
        byte[] plaintext = VaultCrypto.Decrypt(blob, secret);
        try
        {
            return JsonSerializer.Deserialize<VaultData>(plaintext, JsonOptions) ?? new VaultData();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public static void SaveLocal(VaultData data, string secret)
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

        // Write to a temp file and swap it in, so a crash or power loss mid-write can
        // never leave vault.pvlt half-written / corrupt.
        string tempPath = AppPaths.VaultLocalPath + ".tmp";
        File.WriteAllBytes(tempPath, blob);
        if (File.Exists(AppPaths.VaultLocalPath))
            File.Replace(tempPath, AppPaths.VaultLocalPath, null);
        else
            File.Move(tempPath, AppPaths.VaultLocalPath);
    }
}
