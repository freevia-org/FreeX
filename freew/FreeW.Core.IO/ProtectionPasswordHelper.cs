using System.Security.Cryptography;
using System.Text;
using FreeW.Core.Model;

namespace FreeW.Core.IO;

/// <summary>
/// Computes and verifies the OOXML legacy password hash for word/settings.xml's w:documentProtection
/// element. The algorithm is the ECMA-376 Part 4 §14.7.2 "Password Verifier" scheme used by Microsoft
/// Word for document protection passwords:
/// <list type="number">
///   <item>Generate a random 16-byte salt (base64-encoded as w:salt).</item>
///   <item>Encode the password as UTF-16LE bytes (the same encoding Word uses).</item>
///   <item>hash₀ = SHA-1(salt_bytes || password_bytes)</item>
///   <item>For i = 0 … spinCount-1: hashᵢ₊₁ = SHA-1(hashᵢ || i_as_4_bytes_little_endian)</item>
///   <item>Base64-encode the final hash → w:hash.</item>
/// </list>
/// Word emits w:cryptAlgorithmSid="4" (SHA-1) and w:cryptSpinCount="50000" (the conventional count).
/// </summary>
public static class ProtectionPasswordHelper
{
    /// <summary>Default spin count Word uses for protection passwords.</summary>
    public const int DefaultSpinCount = 50000;

    /// <summary>
    /// Computes the OOXML legacy SHA-1 password hash and returns a <see cref="ProtectionSettings"/>
    /// with the hash, salt, and spin-count populated. The <paramref name="mode"/> is carried through.
    /// </summary>
    public static ProtectionSettings CreateWithPassword(ProtectionMode mode, string password, int spinCount = DefaultSpinCount)
    {
        ArgumentNullException.ThrowIfNull(password);

        // Generate a random 16-byte salt.
        var saltBytes = RandomNumberGenerator.GetBytes(16);
        var hash = ComputeHash(password, saltBytes, spinCount);

        return new ProtectionSettings(mode)
        {
            PasswordHash = Convert.ToBase64String(hash),
            PasswordSalt = Convert.ToBase64String(saltBytes),
            SpinCount = spinCount
        };
    }

    /// <summary>
    /// Verifies that <paramref name="password"/> matches the hash stored in
    /// <paramref name="protection"/>. Returns false immediately if the settings carry no hash.
    /// </summary>
    public static bool VerifyPassword(ProtectionSettings protection, string password)
    {
        if (!protection.HasPassword)
            return false;
        if (!TryDecodeBase64(protection.PasswordSalt!, out var saltBytes))
            return false;

        var computed = ComputeHash(password, saltBytes, protection.SpinCount);
        if (!TryDecodeBase64(protection.PasswordHash!, out var storedHash))
            return false;

        return CryptographicOperations.FixedTimeEquals(computed, storedHash);
    }

    // ── internal helpers ──────────────────────────────────────────────────────

    private static byte[] ComputeHash(string password, byte[] saltBytes, int spinCount)
    {
        // Password is encoded as UTF-16LE (Word's convention).
        var passwordBytes = Encoding.Unicode.GetBytes(password);

        // Initial hash: SHA-1(salt || password).
        var hash = SHA1.HashData([..saltBytes, ..passwordBytes]);

        // Spin: hashᵢ₊₁ = SHA-1(hashᵢ || i_le32).
        var iterBuf = new byte[4];
        for (var i = 0; i < spinCount; i++)
        {
            iterBuf[0] = (byte)(i & 0xFF);
            iterBuf[1] = (byte)((i >> 8) & 0xFF);
            iterBuf[2] = (byte)((i >> 16) & 0xFF);
            iterBuf[3] = (byte)((i >> 24) & 0xFF);
            hash = SHA1.HashData([..hash, ..iterBuf]);
        }
        return hash;
    }

    private static bool TryDecodeBase64(string base64, out byte[] bytes)
    {
        try
        {
            bytes = Convert.FromBase64String(base64);
            return true;
        }
        catch
        {
            bytes = [];
            return false;
        }
    }
}
