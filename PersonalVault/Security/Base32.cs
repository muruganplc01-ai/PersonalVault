using System.Text;

namespace PersonalVault.Security;

/// <summary>
/// RFC 4648 Base32 encode/decode - .NET has Base64 built in but no Base32, and TOTP
/// secrets are conventionally shown/entered as Base32 (that's what every authenticator
/// app - Google Authenticator, Authy, etc. - expects when you type a secret in by hand
/// instead of scanning a QR code). Used only by TotpGenerator/MfaSetupForm.
/// </summary>
public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Encode(byte[] data)
    {
        var sb = new StringBuilder((data.Length * 8 + 4) / 5);
        int bitBuffer = 0, bitsInBuffer = 0;

        foreach (byte b in data)
        {
            bitBuffer = (bitBuffer << 8) | b;
            bitsInBuffer += 8;
            while (bitsInBuffer >= 5)
            {
                bitsInBuffer -= 5;
                sb.Append(Alphabet[(bitBuffer >> bitsInBuffer) & 0x1F]);
            }
        }

        if (bitsInBuffer > 0)
            sb.Append(Alphabet[(bitBuffer << (5 - bitsInBuffer)) & 0x1F]);

        return sb.ToString();
    }

    public static byte[] Decode(string base32)
    {
        base32 = base32.Trim().TrimEnd('=').ToUpperInvariant();
        var bytes = new List<byte>((base32.Length * 5) / 8);
        int bitBuffer = 0, bitsInBuffer = 0;

        foreach (char c in base32)
        {
            int value = Alphabet.IndexOf(c);
            if (value < 0)
                throw new FormatException($"'{c}' is not a valid Base32 character.");

            bitBuffer = (bitBuffer << 5) | value;
            bitsInBuffer += 5;
            if (bitsInBuffer >= 8)
            {
                bitsInBuffer -= 8;
                bytes.Add((byte)((bitBuffer >> bitsInBuffer) & 0xFF));
            }
        }

        return bytes.ToArray();
    }
}
