using System.Security.Cryptography;
using System.Text;

namespace PersonalVault.Security;

/// <summary>
/// Encrypts/decrypts the vault's bytes using AES-256-GCM (authenticated encryption -
/// this both keeps the data confidential and detects tampering/corruption) with a key
/// derived from the user's master secret via PBKDF2-HMAC-SHA256.
///
/// .NET's AesGcm/Rfc2898DeriveBytes classes call into the OS's native crypto library -
/// on Windows that is CNG/BCrypt, i.e. "Windows AES", so no separate crypto library is
/// required.
///
/// File layout (all fields fixed-size except the ciphertext, so parsing needs no extra
/// framing):
///   "PVLT" (4 bytes) | version (1 byte) | salt (16 bytes) | nonce (12 bytes)
///   | tag (16 bytes) | ciphertext (remaining bytes)
/// </summary>
public static class VaultCrypto
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("PVLT");
    private const byte FormatVersion = 1;

    private const int SaltSize = 16;
    private const int NonceSize = 12; // standard/recommended size for AES-GCM
    private const int TagSize = 16;
    private const int KeySize = 32;   // 256-bit key
    private const int Iterations = 300_000;

    private const int HeaderSize = 4 /*magic*/ + 1 /*version*/ + SaltSize + NonceSize + TagSize;

    public static byte[] Encrypt(byte[] plaintext, string secret)
    {
        if (string.IsNullOrEmpty(secret))
            throw new ArgumentException("Secret cannot be empty.", nameof(secret));

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] key = DeriveKey(secret, salt);

        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[TagSize];

        try
        {
            using var aesGcm = new AesGcm(key, TagSize);
            aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        using var ms = new MemoryStream(HeaderSize + ciphertext.Length);
        ms.Write(Magic);
        ms.WriteByte(FormatVersion);
        ms.Write(salt);
        ms.Write(nonce);
        ms.Write(tag);
        ms.Write(ciphertext);
        return ms.ToArray();
    }

    public static byte[] Decrypt(byte[] blob, string secret)
    {
        if (string.IsNullOrEmpty(secret))
            throw new ArgumentException("Secret cannot be empty.", nameof(secret));

        if (blob.Length < HeaderSize)
            throw new InvalidDataException("Vault file is corrupt or too short.");

        int offset = 0;

        var magic = blob.AsSpan(offset, Magic.Length);
        offset += Magic.Length;
        if (!magic.SequenceEqual(Magic))
            throw new InvalidDataException("This does not look like a Personal Vault file.");

        byte version = blob[offset];
        offset += 1;
        if (version != FormatVersion)
            throw new InvalidDataException($"Unsupported vault file version: {version}.");

        byte[] salt = blob.AsSpan(offset, SaltSize).ToArray();
        offset += SaltSize;
        byte[] nonce = blob.AsSpan(offset, NonceSize).ToArray();
        offset += NonceSize;
        byte[] tag = blob.AsSpan(offset, TagSize).ToArray();
        offset += TagSize;
        byte[] ciphertext = blob.AsSpan(offset).ToArray();

        byte[] key = DeriveKey(secret, salt);
        byte[] plaintext = new byte[ciphertext.Length];
        try
        {
            using var aesGcm = new AesGcm(key, TagSize);
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);
        }
        catch (CryptographicException)
        {
            // AES-GCM tag mismatch: either the secret is wrong, or the file was altered/corrupted.
            throw new CryptographicException("Incorrect secret, or the vault file has been tampered with.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        return plaintext;
    }

    private static byte[] DeriveKey(string secret, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(secret),
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            KeySize);
}
