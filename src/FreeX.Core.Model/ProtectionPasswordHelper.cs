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

        // A stored value that is literally identical to what the user typed is always a valid
        // match, independent of whether "stored" happens to also look like a legacy hex hash (a
        // freshly-set plaintext password such as "1234"/"abcd"/"c0de" is indistinguishable in
        // shape from a genuine 4-hex-digit legacy hash — see IsLegacyPasswordHash). Checking
        // plaintext equality first means that ambiguity can never cause a *correct* password to
        // be rejected; the legacy-hash interpretation below is only a fallback for the case where
        // "stored" really is a hash produced by a different password than the one just supplied.
        if (string.Equals(stored, provided, StringComparison.Ordinal))
            return true;

        return IsLegacyPasswordHash(stored) &&
            string.Equals(ComputeLegacyPasswordHash(provided), stored, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Encodes a legacy (pre-2013, XOR/rotate) password verifier for the ISO/IEC 29500
    /// <c>password</c> attribute. <paramref name="passwordOrHash"/> must be a value already known
    /// by the caller to be a genuine 4-hex-digit legacy hash (e.g. one read verbatim from an
    /// existing file's <c>password</c> attribute and being round-tripped unchanged) — callers must
    /// NOT pass a freshly-typed plaintext password here on the strength of it merely looking like
    /// hex, because a real password such as "1234"/"abcd"/"c0de" is indistinguishable in shape
    /// from an actual hash (see <see cref="IsLegacyPasswordHash"/>). When in doubt, callers should
    /// track provenance explicitly (e.g. "this string came from XML we just read" vs. "this string
    /// is what the user typed into the Protect dialog") rather than relying on this method to guess.
    /// </summary>
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

    /// <summary>
    /// True when <paramref name="value"/> has the exact shape of a legacy (pre-2013) Excel
    /// password verifier: <see cref="ComputeLegacyPasswordHash"/> always formats its result with
    /// <c>"X4"</c>, i.e. exactly 4 hex digits, zero-padded. A shorter hex-looking string (1-3
    /// characters, e.g. "1", "ab", "abc") can therefore never be a genuine legacy hash and is
    /// always a real plaintext password — that case is unambiguous and rejected here.
    /// <para>
    /// A plaintext password that happens to be exactly 4 hex characters (e.g. "1234", "abcd",
    /// "c0de", "dead", "beef") is genuinely indistinguishable from a real hash by shape alone: this
    /// is a structural ambiguity in the legacy format itself, not something a smarter predicate can
    /// resolve from the string in isolation. Callers that know the provenance of the value (loaded
    /// verbatim from a file's <c>password</c> attribute vs. freshly typed by the user) must use
    /// that context instead of relying on this heuristic — see the caller notes on
    /// <see cref="ToLegacyPasswordHash"/> and <see cref="VerifyStoredPassword"/>, the latter of
    /// which checks literal equality before ever consulting this predicate so an exact-match
    /// password is never rejected because of the ambiguity.
    /// </para>
    /// </summary>
    private static bool IsLegacyPasswordHash(string value) =>
        value.Length == 4 &&
        value.All(ch =>
            ch is >= '0' and <= '9' ||
            ch is >= 'A' and <= 'F' ||
            ch is >= 'a' and <= 'f');
}
