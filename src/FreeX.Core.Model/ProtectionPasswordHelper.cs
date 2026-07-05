using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FreeX.Core.Model;

public static class ProtectionPasswordHelper
{
    private const string Sha256Prefix = "sha256:";

    /// <summary>
    /// Prefix for the ISO/IEC 29500 (ECMA-376) "modern" salted, iterated password-verifier scheme
    /// Excel writes as the <c>algorithmName</c>/<c>hashValue</c>/<c>saltValue</c>/<c>spinCount</c>
    /// attributes of <c>&lt;workbookProtection&gt;</c>/<c>&lt;sheetProtection&gt;</c> (the default
    /// scheme since Excel 2013). Stored form: <c>iso29500:{algorithmName}:{spinCount}:{saltBase64}:{hashBase64}</c>.
    /// </summary>
    private const string Iso29500Prefix = "iso29500:";

    public static string HashNativePassword(string plain)
    {
        if (IsStoredSha256Hash(plain))
            return Sha256Prefix + plain[Sha256Prefix.Length..].ToUpperInvariant();

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(plain));
        return Sha256Prefix + Convert.ToHexString(hash);
    }

    /// <summary>
    /// Encodes an ISO 29500 modern hash (as read verbatim from a workbook's/worksheet's protection
    /// element) into the stored-password string form that <see cref="VerifyStoredPassword"/> and
    /// round-trip persistence understand.
    /// </summary>
    public static string EncodeIso29500Hash(string? algorithmName, string? spinCount, string? saltValue, string? hashValue) =>
        string.Join(
            ':',
            Iso29500Prefix[..^1],
            algorithmName ?? "",
            spinCount ?? "",
            saltValue ?? "",
            hashValue ?? "");

    /// <summary>True when <paramref name="stored"/> holds an encoded ISO 29500 modern hash.</summary>
    public static bool IsIso29500Hash(string? stored) =>
        stored is not null && stored.StartsWith(Iso29500Prefix, StringComparison.Ordinal);

    public static bool VerifyStoredPassword(string? stored, string? provided)
    {
        if (string.IsNullOrEmpty(stored))
            return true;

        provided ??= "";
        if (stored.StartsWith(Sha256Prefix, StringComparison.Ordinal))
            return VerifySha256Password(stored, provided);

        if (stored.StartsWith(Iso29500Prefix, StringComparison.Ordinal))
            return VerifyIso29500Password(stored, provided);

        if (IsLegacyPasswordHash(stored) &&
            string.Equals(ComputeLegacyPasswordHash(provided), stored, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(stored, provided, StringComparison.Ordinal);
    }

    public static string ToLegacyPasswordHash(string passwordOrHash)
    {
        if (IsLegacyPasswordHash(passwordOrHash))
            return passwordOrHash.ToUpperInvariant();

        return ComputeLegacyPasswordHash(passwordOrHash);
    }

    private static string ComputeLegacyPasswordHash(string password)
    {
        var hash = 0;
        for (var index = 0; index < password.Length; index++)
        {
            var value = password[index] << (index + 1);
            var rotatedBits = value >> 15;
            value &= 0x7fff;
            hash ^= value | rotatedBits;
        }

        hash ^= password.Length;
        hash ^= 0xCE4B;
        return hash.ToString("X4", CultureInfo.InvariantCulture);
    }

    private static bool VerifySha256Password(string stored, string provided)
    {
        var expectedHex = stored[Sha256Prefix.Length..];
        byte[] expectedHash;
        try
        {
            expectedHash = Convert.FromHexString(expectedHex);
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

    /// <summary>
    /// Verifies a password against the ISO/IEC 29500 "modern" iterated-hash scheme:
    /// H0 = Hash(salt || UTF16LE(password)); Hn = Hash(H(n-1) || LE32(n-1)) for n in [1, spinCount];
    /// the final iterate is compared to the stored hash. See ECMA-376 Part 1 §18.3.1.85/18.11.7.
    /// </summary>
    private static bool VerifyIso29500Password(string stored, string provided)
    {
        var parts = stored.Split(':', 5);
        if (parts.Length != 5)
            return false;

        var algorithmName = parts[1];
        if (!int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var spinCount) || spinCount < 0)
            return false;

        byte[] salt;
        byte[] expectedHash;
        try
        {
            salt = Convert.FromBase64String(parts[3]);
            expectedHash = Convert.FromBase64String(parts[4]);
        }
        catch (FormatException)
        {
            return false;
        }

        using var algorithm = CreateHashAlgorithm(algorithmName);
        if (algorithm is null)
            return false;

        var passwordBytes = Encoding.Unicode.GetBytes(provided);
        var buffer = new byte[salt.Length + passwordBytes.Length];
        salt.CopyTo(buffer, 0);
        passwordBytes.CopyTo(buffer, salt.Length);
        var digest = algorithm.ComputeHash(buffer);

        var iterationBuffer = new byte[digest.Length + 4];
        for (var iteration = 0; iteration < spinCount; iteration++)
        {
            digest.CopyTo(iterationBuffer, 0);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(
                iterationBuffer.AsSpan(digest.Length, 4), iteration);
            digest = algorithm.ComputeHash(iterationBuffer);
        }

        return digest.Length == expectedHash.Length && CryptographicOperations.FixedTimeEquals(digest, expectedHash);
    }

    private static HashAlgorithm? CreateHashAlgorithm(string algorithmName) =>
        algorithmName.Trim().ToUpperInvariant() switch
        {
            "MD5" => MD5.Create(),
            "SHA-1" or "SHA1" => SHA1.Create(),
            "SHA-256" or "SHA256" => SHA256.Create(),
            "SHA-384" or "SHA384" => SHA384.Create(),
            "SHA-512" or "SHA512" => SHA512.Create(),
            _ => null
        };

    private static bool IsStoredSha256Hash(string value)
    {
        if (!value.StartsWith(Sha256Prefix, StringComparison.Ordinal) ||
            value.Length != Sha256Prefix.Length + SHA256.HashSizeInBytes * 2)
        {
            return false;
        }

        for (var index = Sha256Prefix.Length; index < value.Length; index++)
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

    private static bool IsLegacyPasswordHash(string value) =>
        value.Length is > 0 and <= 4 &&
        value.All(ch =>
            ch is >= '0' and <= '9' ||
            ch is >= 'A' and <= 'F' ||
            ch is >= 'a' and <= 'f');
}
