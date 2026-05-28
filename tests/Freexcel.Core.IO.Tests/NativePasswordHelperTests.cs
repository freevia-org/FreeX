using FluentAssertions;
using Freexcel.Core.IO;

namespace Freexcel.Core.IO.Tests;

public sealed class NativePasswordHelperTests
{
    [Fact]
    public void HashPassword_ReturnsValueWithSha256Prefix()
    {
        var result = NativePasswordHelper.HashPassword("secret");

        result.Should().StartWith("sha256:");
    }

    [Fact]
    public void HashPassword_IsDeterministic()
    {
        var first  = NativePasswordHelper.HashPassword("hello");
        var second = NativePasswordHelper.HashPassword("hello");

        first.Should().Be(second);
    }

    [Fact]
    public void HashPassword_DifferentInputsProduceDifferentHashes()
    {
        var a = NativePasswordHelper.HashPassword("password1");
        var b = NativePasswordHelper.HashPassword("password2");

        a.Should().NotBe(b);
    }

    [Fact]
    public void VerifyPassword_CorrectPasswordReturnsTrue_ForHashedStored()
    {
        var stored = NativePasswordHelper.HashPassword("correct");

        NativePasswordHelper.VerifyPassword(stored, "correct").Should().BeTrue();
    }

    [Fact]
    public void VerifyPassword_WrongPasswordReturnsFalse_ForHashedStored()
    {
        var stored = NativePasswordHelper.HashPassword("correct");

        NativePasswordHelper.VerifyPassword(stored, "wrong").Should().BeFalse();
    }

    [Fact]
    public void VerifyPassword_LegacyPlaintext_MatchesExact()
    {
        // Legacy files store the raw password string (no "sha256:" prefix).
        NativePasswordHelper.VerifyPassword("legacy-pass", "legacy-pass").Should().BeTrue();
    }

    [Fact]
    public void VerifyPassword_LegacyPlaintext_RejectsMismatch()
    {
        NativePasswordHelper.VerifyPassword("legacy-pass", "other").Should().BeFalse();
    }

    [Fact]
    public void HashAndVerify_RoundTrip()
    {
        const string plain = "P@ssw0rd!";
        var stored = NativePasswordHelper.HashPassword(plain);

        NativePasswordHelper.VerifyPassword(stored, plain).Should().BeTrue();
        NativePasswordHelper.VerifyPassword(stored, plain + "x").Should().BeFalse();
    }
}
