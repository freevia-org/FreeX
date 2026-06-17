using FreeX.App.Presentation.Protection;
using FluentAssertions;

namespace FreeX.App.Presentation.Tests.Protection;

public sealed class ProtectionPasswordTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("secret", true)]
    public void IsSet_reports_presence(string? password, bool expected) =>
        ProtectionPassword.IsSet(password).Should().Be(expected);

    [Fact]
    public void Matching_confirmation_is_valid_and_set()
    {
        var result = ProtectionPassword.Validate("hunter2", "hunter2");

        result.IsValid.Should().BeTrue();
        result.IsPasswordSet.Should().BeTrue();
        result.ConfirmationMismatch.Should().BeFalse();
    }

    [Fact]
    public void Mismatched_confirmation_is_invalid()
    {
        var result = ProtectionPassword.Validate("hunter2", "hunter3");

        result.IsValid.Should().BeFalse();
        result.IsPasswordSet.Should().BeTrue();
        result.ConfirmationMismatch.Should().BeTrue();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("", null)]
    public void Empty_password_needs_no_confirmation(string? password, string? confirmation)
    {
        var result = ProtectionPassword.Validate(password, confirmation);

        result.IsValid.Should().BeTrue();
        result.IsPasswordSet.Should().BeFalse();
        result.ConfirmationMismatch.Should().BeFalse();
    }

    [Fact]
    public void Confirmation_is_case_sensitive()
    {
        ProtectionPassword.ConfirmationMatches("Secret", "secret").Should().BeFalse();
        ProtectionPassword.ConfirmationMatches("Secret", "Secret").Should().BeTrue();
    }

    [Fact]
    public void Set_password_with_empty_confirmation_is_a_mismatch()
    {
        var result = ProtectionPassword.Validate("secret", "");

        result.IsValid.Should().BeFalse();
        result.ConfirmationMismatch.Should().BeTrue();
    }
}
