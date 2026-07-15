using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class ProtectionPasswordHelperTests
{
    [Fact]
    public void VerifyStoredPassword_AllowsUnprotectedBlankPasswordState()
    {
        ProtectionPasswordHelper.VerifyStoredPassword(null, "anything").Should().BeTrue();
        ProtectionPasswordHelper.VerifyStoredPassword("", "anything").Should().BeTrue();
    }

    [Fact]
    public void HashNativePassword_ReturnsStableSha256StorageValue()
    {
        var stored = ProtectionPasswordHelper.HashNativePassword("secret");

        stored.Should().StartWith("sha256:");
        stored.Should().HaveLength("sha256:".Length + 64);
        ProtectionPasswordHelper.HashNativePassword(stored.ToLowerInvariant())
            .Should()
            .Be(stored);
    }

    [Fact]
    public void VerifyStoredPassword_AcceptsNativeSha256AndRejectsWrongPassword()
    {
        var stored = ProtectionPasswordHelper.HashNativePassword("secret");

        ProtectionPasswordHelper.VerifyStoredPassword(stored, "secret").Should().BeTrue();
        ProtectionPasswordHelper.VerifyStoredPassword(stored, "wrong").Should().BeFalse();
    }

    [Fact]
    public void VerifyStoredPassword_AcceptsLegacyPlaintext()
    {
        ProtectionPasswordHelper.VerifyStoredPassword("legacy-pass", "legacy-pass").Should().BeTrue();
        ProtectionPasswordHelper.VerifyStoredPassword("legacy-pass", "wrong").Should().BeFalse();
    }

    [Fact]
    public void VerifyStoredPassword_AcceptsXlsxLegacyHash()
    {
        ProtectionPasswordHelper.ToLegacyPasswordHash("password").Should().Be("83AF");
        ProtectionPasswordHelper.VerifyStoredPassword("83AF", "password").Should().BeTrue();
        ProtectionPasswordHelper.VerifyStoredPassword("83AF", "wrong").Should().BeFalse();
    }

    [Fact]
    public void VerifyStoredPassword_AcceptsLegacyPlaintextThatLooksLikeHash()
    {
        ProtectionPasswordHelper.VerifyStoredPassword("ABCD", "ABCD").Should().BeTrue();
    }

    [Theory]
    [InlineData("ABCD", true)]
    [InlineData("0000", true)]
    [InlineData("abc", false)]
    [InlineData(null, false)]
    public void IsLegacyPasswordHash_UsesExactFourHexShape(string? value, bool expected)
    {
        ProtectionPasswordHelper.IsLegacyPasswordHash(value).Should().Be(expected);
    }
}
