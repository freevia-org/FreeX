using System.Security.Cryptography;
using System.Text;

namespace Free.Shared.IO;

public static class Sha256PasswordStorage
{
    public const string Prefix = "sha256:";

    public static string Encode(string plaintextOrEncoded)
    {
        if (IsEncoded(plaintextOrEncoded))
            return Prefix + plaintextOrEncoded[Prefix.Length..].ToUpperInvariant();

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(plaintextOrEncoded));
        return Prefix + Convert.ToHexString(hash);
    }

    public static bool HasPrefix(string value) =>
        value.StartsWith(Prefix, StringComparison.Ordinal);

    public static bool Verify(string encoded, string provided)
    {
        if (!HasPrefix(encoded))
            return false;

        byte[] expectedHash;
        try
        {
            expectedHash = Convert.FromHexString(encoded[Prefix.Length..]);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (expectedHash.Length != SHA256.HashSizeInBytes)
            return false;

        Span<byte> actualHash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(Encoding.UTF8.GetBytes(provided), actualHash);
        return CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
    }

    private static bool IsEncoded(string value)
    {
        if (!HasPrefix(value) || value.Length != Prefix.Length + SHA256.HashSizeInBytes * 2)
            return false;

        for (var index = Prefix.Length; index < value.Length; index++)
        {
            var ch = value[index];
            if (ch is not (>= '0' and <= '9') &&
                ch is not (>= 'A' and <= 'F') &&
                ch is not (>= 'a' and <= 'f'))
            {
                return false;
            }
        }

        return true;
    }
}
