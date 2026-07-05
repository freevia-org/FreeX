using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression tests for the P-protection review-5 fixes:
/// K18 (workbook structure protection loaded from the modern ISO 29500 salted-hash scheme must not
/// yield a null password that any password unprotects) and K34 (same for worksheet protection).
/// Both route through <see cref="ProtectionPasswordHelper.VerifyStoredPassword"/> once the IO layer
/// encodes the modern hash via <see cref="ProtectionPasswordHelper.EncodeIso29500Hash"/>.
/// </summary>
public sealed class PProtectionFixesTests
{
    // Independent reference implementation of the ECMA-376 §18.3.1.85 iterated hash, so these tests
    // do not simply exercise the same code path they are meant to verify.
    private static (string SaltBase64, string HashBase64) ComputeReferenceHash(
        string password, string algorithmName, int spinCount, byte[] salt)
    {
        using HashAlgorithm algorithm = algorithmName switch
        {
            "SHA-512" => SHA512.Create(),
            "SHA-1" => SHA1.Create(),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithmName))
        };

        var passwordBytes = Encoding.Unicode.GetBytes(password);
        var buffer = new byte[salt.Length + passwordBytes.Length];
        salt.CopyTo(buffer, 0);
        passwordBytes.CopyTo(buffer, salt.Length);
        var digest = algorithm.ComputeHash(buffer);

        for (var i = 0; i < spinCount; i++)
        {
            var iterationBuffer = new byte[digest.Length + 4];
            digest.CopyTo(iterationBuffer, 0);
            BitConverter.GetBytes(i).CopyTo(iterationBuffer, digest.Length);
            digest = algorithm.ComputeHash(iterationBuffer);
        }

        return (Convert.ToBase64String(salt), Convert.ToBase64String(digest));
    }

    [Fact]
    public void VerifyStoredPassword_AcceptsCorrectPasswordAgainstIso29500Sha512Hash()
    {
        var salt = new byte[] { 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20 };
        const int spinCount = 100_000;
        var (saltBase64, hashBase64) = ComputeReferenceHash("correct horse", "SHA-512", spinCount, salt);
        var stored = ProtectionPasswordHelper.EncodeIso29500Hash(
            "SHA-512", spinCount.ToString(), saltBase64, hashBase64);

        ProtectionPasswordHelper.VerifyStoredPassword(stored, "correct horse").Should().BeTrue();
    }

    [Fact]
    public void VerifyStoredPassword_RejectsWrongPasswordAgainstIso29500Sha512Hash()
    {
        var salt = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
        const int spinCount = 100_000;
        var (saltBase64, hashBase64) = ComputeReferenceHash("correct horse", "SHA-512", spinCount, salt);
        var stored = ProtectionPasswordHelper.EncodeIso29500Hash(
            "SHA-512", spinCount.ToString(), saltBase64, hashBase64);

        ProtectionPasswordHelper.VerifyStoredPassword(stored, "wrong password").Should().BeFalse();
        ProtectionPasswordHelper.VerifyStoredPassword(stored, "").Should().BeFalse();
        ProtectionPasswordHelper.VerifyStoredPassword(stored, null).Should().BeFalse();
    }

    [Fact]
    public void VerifyStoredPassword_AcceptsCorrectPasswordAgainstIso29500Sha1Hash()
    {
        // Older Excel builds (and some third-party writers) use SHA-1 instead of SHA-512.
        var salt = new byte[] { 42, 42, 42, 42, 42, 42, 42, 42 };
        const int spinCount = 50_000;
        var (saltBase64, hashBase64) = ComputeReferenceHash("hunter2", "SHA-1", spinCount, salt);
        var stored = ProtectionPasswordHelper.EncodeIso29500Hash(
            "SHA-1", spinCount.ToString(), saltBase64, hashBase64);

        ProtectionPasswordHelper.VerifyStoredPassword(stored, "hunter2").Should().BeTrue();
        ProtectionPasswordHelper.VerifyStoredPassword(stored, "not-hunter2").Should().BeFalse();
    }

    [Fact]
    public void VerifyStoredPassword_DoesNotTreatIso29500HashAsAlwaysUnprotectable()
    {
        // The core failure scenario in K18/K34: a null/blank stored password unprotects with any
        // input. An encoded modern hash must never collapse to that permissive null-password path.
        var salt = new byte[] { 9, 9, 9, 9 };
        const int spinCount = 1000;
        var (saltBase64, hashBase64) = ComputeReferenceHash("secret", "SHA-512", spinCount, salt);
        var stored = ProtectionPasswordHelper.EncodeIso29500Hash(
            "SHA-512", spinCount.ToString(), saltBase64, hashBase64);

        stored.Should().NotBeNullOrEmpty();
        ProtectionPasswordHelper.IsIso29500Hash(stored).Should().BeTrue();
        ProtectionPasswordHelper.VerifyStoredPassword(stored, "any random guess").Should().BeFalse();
    }

    [Fact]
    public void IsIso29500Hash_ReturnsFalseForOtherStoredForms()
    {
        ProtectionPasswordHelper.IsIso29500Hash(null).Should().BeFalse();
        ProtectionPasswordHelper.IsIso29500Hash("").Should().BeFalse();
        ProtectionPasswordHelper.IsIso29500Hash("83AF").Should().BeFalse();
        ProtectionPasswordHelper.IsIso29500Hash(ProtectionPasswordHelper.HashNativePassword("secret")).Should().BeFalse();
    }
}
