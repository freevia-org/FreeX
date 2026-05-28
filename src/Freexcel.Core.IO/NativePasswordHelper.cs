using System.Security.Cryptography;
using System.Text;

namespace Freexcel.Core.IO;

/// <summary>
/// Helpers for hashing and verifying protection passwords stored in .fxl files.
/// New files store passwords as "sha256:&lt;hex&gt;" to avoid persisting plaintext credentials.
/// Legacy files that contain a bare plaintext value are still accepted for backward compatibility.
/// </summary>
internal static class NativePasswordHelper
{
    private const string Sha256Prefix = "sha256:";

    /// <summary>
    /// Returns a stored representation of <paramref name="plain"/> as
    /// <c>"sha256:&lt;uppercased-hex&gt;"</c>.
    /// </summary>
    public static string HashPassword(string plain)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(plain));
        return Sha256Prefix + Convert.ToHexString(hash);
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="provided"/> matches
    /// <paramref name="stored"/>.
    /// <list type="bullet">
    ///   <item>If <paramref name="stored"/> starts with <c>"sha256:"</c> the provided
    ///         value is hashed and the hex digests are compared.</item>
    ///   <item>Otherwise the stored value is treated as a legacy plaintext password and
    ///         compared directly (case-sensitive).</item>
    /// </list>
    /// </summary>
    public static bool VerifyPassword(string stored, string provided)
    {
        if (stored.StartsWith(Sha256Prefix, StringComparison.Ordinal))
        {
            var expectedHex = stored[Sha256Prefix.Length..];
            Span<byte> expectedHash = stackalloc byte[SHA256.HashSizeInBytes];
            if (!Convert.TryFromHexString(expectedHex, expectedHash, out var bytesWritten) ||
                bytesWritten != SHA256.HashSizeInBytes)
            {
                return false;
            }

            Span<byte> actualHash = stackalloc byte[SHA256.HashSizeInBytes];
            SHA256.HashData(Encoding.UTF8.GetBytes(provided), actualHash);
            return CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
        }

        // Legacy plaintext — compare as-is
        return string.Equals(stored, provided, StringComparison.Ordinal);
    }
}
