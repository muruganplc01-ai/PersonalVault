using System.Security.Cryptography;
using System.Text;

namespace PersonalVault.Security;

/// <summary>
/// Encrypts a single shared-account payload with a fresh, random (never
/// password-derived) AES-256-GCM key, for the "Share Account" one-time-link feature.
/// Unlike VaultCrypto, there's no PBKDF2/salt here - the key itself is thrown away as
/// soon as it's embedded in the share URL's fragment, never stored anywhere, so there's
/// nothing to derive it from later. The whole point is that possessing the link (the
/// Drive file id in the path, the key in the fragment) is both necessary and sufficient
/// to read the one account it points to.
///
/// File layout (note: deliberately NOT the same field order as VaultCrypto - ciphertext
/// before tag here, so the docs/share/index.html viewer can hand
/// "everything after the nonce" straight to the browser's crypto.subtle.decrypt as one
/// combined buffer, which is what the Web Crypto AES-GCM API expects):
///   "PVSH" (4 bytes) | version (1 byte) | nonce (12 bytes) | ciphertext (N bytes) | tag (16 bytes)
/// </summary>
public static class ShareCrypto
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("PVSH");
    private const byte FormatVersion = 1;

    private const int NonceSize = 12;
    private const int TagSize = 16;
    public const int KeySize = 32; // 256-bit key

    private const int HeaderSize = 4 /*magic*/ + 1 /*version*/ + NonceSize;

    /// <summary>Encrypts plaintext under a freshly generated random key. Returns the blob to upload and the key to embed in the share link - the caller is responsible for not persisting the key anywhere.</summary>
    public static (byte[] Blob, byte[] Key) Encrypt(byte[] plaintext)
    {
        byte[] key = RandomNumberGenerator.GetBytes(KeySize);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);

        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[TagSize];

        using (var aesGcm = new AesGcm(key, TagSize))
        {
            aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);
        }

        using var ms = new MemoryStream(HeaderSize + ciphertext.Length + TagSize);
        ms.Write(Magic);
        ms.WriteByte(FormatVersion);
        ms.Write(nonce);
        ms.Write(ciphertext);
        ms.Write(tag);
        return (ms.ToArray(), key);
    }

    /// <summary>
    /// Decrypts a blob produced by Encrypt. Nothing in the running app currently calls
    /// this in production (the whole point is that only the browser-side viewer, given
    /// the key from the link, ever decrypts a share) - kept so the exact blob format can
    /// be round-trip tested against docs/share/index.html's JS implementation.
    /// </summary>
    public static byte[] Decrypt(byte[] blob, byte[] key)
    {
        if (blob.Length < HeaderSize + TagSize)
            throw new InvalidDataException("Share blob is corrupt or too short.");

        int offset = 0;

        var magic = blob.AsSpan(offset, Magic.Length);
        offset += Magic.Length;
        if (!magic.SequenceEqual(Magic))
            throw new InvalidDataException("This does not look like a Personal Vault share blob.");

        byte version = blob[offset];
        offset += 1;
        if (version != FormatVersion)
            throw new InvalidDataException($"Unsupported share blob version: {version}.");

        byte[] nonce = blob.AsSpan(offset, NonceSize).ToArray();
        offset += NonceSize;

        int ciphertextLength = blob.Length - offset - TagSize;
        byte[] ciphertext = blob.AsSpan(offset, ciphertextLength).ToArray();
        offset += ciphertextLength;
        byte[] tag = blob.AsSpan(offset, TagSize).ToArray();

        byte[] plaintext = new byte[ciphertext.Length];
        using var aesGcm = new AesGcm(key, TagSize);
        aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }
}
