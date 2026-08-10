using Free.Shared.IO;

namespace Free.Shared.Opc;

/// <summary>
/// Helpers for hashing and verifying protection passwords stored in .fxl files.
/// New files store passwords as "sha256:&lt;hex&gt;" to avoid persisting plaintext credentials.
/// Legacy files that contain a bare plaintext value are still accepted for backward compatibility.
/// </summary>
internal static class NativePasswordHelper
{
    /// <summary>
    /// Returns a stored representation of <paramref name="plain"/> as
    /// <c>"sha256:&lt;uppercased-hex&gt;"</c>.
    /// </summary>
    public static string HashPassword(string plain) =>
        Sha256PasswordStorage.Encode(plain);

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
        if (Sha256PasswordStorage.HasPrefix(stored))
            return Sha256PasswordStorage.Verify(stored, provided);

        // Legacy plaintext — compare as-is
        return string.Equals(stored, provided, StringComparison.Ordinal);
    }

}
