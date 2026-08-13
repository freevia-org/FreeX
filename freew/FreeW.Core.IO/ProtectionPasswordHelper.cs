using System.Security.Cryptography;
using Free.Shared.IO;
using FreeW.Core.Model;

namespace FreeW.Core.IO;

/// <summary>
/// Creates and verifies the OOXML salted, iterated SHA-1 password data used by
/// word/settings.xml document protection.
/// </summary>
public static class ProtectionPasswordHelper
{
    public const int DefaultSpinCount = 50000;

    public static ProtectionSettings CreateWithPassword(
        ProtectionMode mode,
        string password,
        int spinCount = DefaultSpinCount)
    {
        ArgumentNullException.ThrowIfNull(password);

        var saltBytes = RandomNumberGenerator.GetBytes(16);
        var hash = OoxmlProtectionPasswordHash.Derive(
            "SHA-1",
            password,
            saltBytes,
            spinCount);

        return new ProtectionSettings(mode)
        {
            PasswordHash = Convert.ToBase64String(hash),
            PasswordSalt = Convert.ToBase64String(saltBytes),
            SpinCount = spinCount
        };
    }

    public static bool VerifyPassword(ProtectionSettings protection, string password)
    {
        if (!protection.HasPassword ||
            !TryDecodeBase64(protection.PasswordSalt!, out var saltBytes) ||
            !TryDecodeBase64(protection.PasswordHash!, out var storedHash))
        {
            return false;
        }

        return OoxmlProtectionPasswordHash.Verify(
            "SHA-1",
            password,
            saltBytes,
            protection.SpinCount,
            storedHash);
    }

    private static bool TryDecodeBase64(string base64, out byte[] bytes)
    {
        try
        {
            bytes = Convert.FromBase64String(base64);
            return true;
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }
}
