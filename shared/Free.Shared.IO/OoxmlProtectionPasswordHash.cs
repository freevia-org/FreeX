using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Free.Shared.IO;

public static class OoxmlProtectionPasswordHash
{
    /// <summary>
    /// Ceiling on the iteration count this helper will honour. Office writes 100,000 (Word and Excel
    /// both, since 2013) and FreeW's own writer uses 50,000, so this is 100x anything a real document
    /// carries; it costs ~2.6s of SHA-1 at the top end, against 44ms for Office's default.
    /// </summary>
    /// <remarks>
    /// r164 remediation, unbounded declared quantity: the spin count is a COUNT the document declares
    /// -- xlsx <c>sheetProtection/@spinCount</c>, docx <c>w:documentProtection/@w:cryptSpinCount</c> --
    /// and it reached this loop with only "not negative" checked. A file declaring 2,000,000,000 made
    /// a single password check run two billion hash rounds: it had not returned after 20s when
    /// measured, and extrapolates to hours. That is the classic iteration-count DoS, and it fires on
    /// the ordinary "unprotect this sheet" path, so it is bounded here -- the one place every app's
    /// verify and derive funnel through -- rather than in each reader.
    ///
    /// Beyond the ceiling the derivation is REFUSED rather than truncated: truncating would silently
    /// compute a hash that can never match, reporting a correct password as wrong with no way to tell
    /// why.
    /// </remarks>
    public const int MaximumSpinCount = 10_000_000;

    public static byte[] Derive(
        string algorithmName,
        string password,
        ReadOnlySpan<byte> salt,
        int spinCount)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentOutOfRangeException.ThrowIfNegative(spinCount);
        if (spinCount > MaximumSpinCount)
        {
            throw new NotSupportedException(
                $"OOXML password spin count {spinCount:N0} exceeds the supported maximum of {MaximumSpinCount:N0}.");
        }

        if (!TryDerive(algorithmName, password, salt, spinCount, out var digest))
            throw new ArgumentException($"Unsupported OOXML password hash algorithm '{algorithmName}'.", nameof(algorithmName));

        return digest;
    }

    public static bool TryDerive(
        string algorithmName,
        string password,
        ReadOnlySpan<byte> salt,
        int spinCount,
        out byte[] digest)
    {
        ArgumentNullException.ThrowIfNull(password);
        digest = [];
        // An out-of-range spin count is treated exactly like an unsupported algorithm: the caller
        // cannot verify this document's password, and says so, instead of grinding through a
        // document-declared number of hash rounds (see MaximumSpinCount).
        if (spinCount < 0 || spinCount > MaximumSpinCount || !TryResolveAlgorithm(algorithmName, out var algorithm))
            return false;

        var passwordBytes = Encoding.Unicode.GetBytes(password);
        var initialBuffer = new byte[salt.Length + passwordBytes.Length];
        salt.CopyTo(initialBuffer);
        passwordBytes.CopyTo(initialBuffer, salt.Length);
        digest = Hash(algorithm, initialBuffer);

        var iterationBuffer = new byte[digest.Length + sizeof(int)];
        for (var iteration = 0; iteration < spinCount; iteration++)
        {
            digest.CopyTo(iterationBuffer, 0);
            BinaryPrimitives.WriteInt32LittleEndian(
                iterationBuffer.AsSpan(digest.Length, sizeof(int)),
                iteration);
            digest = Hash(algorithm, iterationBuffer);
        }

        return true;
    }

    public static bool Verify(
        string algorithmName,
        string password,
        ReadOnlySpan<byte> salt,
        int spinCount,
        ReadOnlySpan<byte> expectedHash) =>
        TryDerive(algorithmName, password, salt, spinCount, out var digest) &&
        digest.Length == expectedHash.Length &&
        CryptographicOperations.FixedTimeEquals(digest, expectedHash);

    private static byte[] Hash(OoxmlHashAlgorithm algorithm, ReadOnlySpan<byte> data) => algorithm switch
    {
        OoxmlHashAlgorithm.Md5 => MD5.HashData(data),
        OoxmlHashAlgorithm.Sha1 => SHA1.HashData(data),
        OoxmlHashAlgorithm.Sha256 => SHA256.HashData(data),
        OoxmlHashAlgorithm.Sha384 => SHA384.HashData(data),
        OoxmlHashAlgorithm.Sha512 => SHA512.HashData(data),
        _ => throw new ArgumentOutOfRangeException(nameof(algorithm))
    };

    private static bool TryResolveAlgorithm(string? name, out OoxmlHashAlgorithm algorithm)
    {
        algorithm = name?.Trim().ToUpperInvariant() switch
        {
            "MD5" => OoxmlHashAlgorithm.Md5,
            "SHA-1" or "SHA1" => OoxmlHashAlgorithm.Sha1,
            "SHA-256" or "SHA256" => OoxmlHashAlgorithm.Sha256,
            "SHA-384" or "SHA384" => OoxmlHashAlgorithm.Sha384,
            "SHA-512" or "SHA512" => OoxmlHashAlgorithm.Sha512,
            _ => OoxmlHashAlgorithm.Unsupported
        };
        return algorithm != OoxmlHashAlgorithm.Unsupported;
    }

    private enum OoxmlHashAlgorithm
    {
        Unsupported,
        Md5,
        Sha1,
        Sha256,
        Sha384,
        Sha512
    }
}
