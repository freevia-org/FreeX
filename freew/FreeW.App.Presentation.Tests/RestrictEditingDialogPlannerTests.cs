using FreeW.App.Presentation.Dialogs;
using FreeW.Core.IO;

namespace FreeW.App.Presentation.Tests;

public sealed class RestrictEditingDialogPlannerTests
{
    [Fact]
    public void Mode_options_match_word_style_restrict_editing_modes()
    {
        RestrictEditingDialogPlanner.ModeOptions
            .Select(option => (option.Label, option.Mode))
            .Should().Equal(
                ("No changes (Read only)", ProtectionMode.ReadOnly),
                ("Tracked changes", ProtectionMode.TrackChangesOnly),
                ("Comments", ProtectionMode.CommentsOnly),
                ("Filling in forms", ProtectionMode.FillingForms));
    }

    [Fact]
    public void Presentation_matches_the_WPF_authority_action_and_geometry_contract()
    {
        var presentation = RestrictEditingDialogPlanner.Presentation;

        presentation.DialogWidth.Should().Be(360);
        presentation.ContentMargin.Should().Be(14);
        presentation.RadioButtonHeight.Should().Be(16);
        presentation.TextBoxHeight.Should().Be(20);
        presentation.ShowStatusText.Should().BeFalse();
        presentation.DefaultButtonText.Should().BeNull();
        presentation.InitialFocusTarget.Should().Be("first-mode");
        presentation.ActionButtonOrder.Should().Equal(
            RestrictEditingDialogPlanner.StartButtonText,
            RestrictEditingDialogPlanner.StopButtonText,
            RestrictEditingDialogPlanner.CancelButtonText);
    }

    [Fact]
    public void BuildPlan_normalizes_unprotected_state_to_read_only_start_workflow()
    {
        var plan = RestrictEditingDialogPlanner.BuildPlan(ProtectionSettings.Unprotected);

        plan.SelectedModeIndex.Should().Be(0);
        plan.CanStartProtection.Should().BeTrue();
        plan.CanStopProtection.Should().BeFalse();
        plan.ShowStartPasswordFields.Should().BeTrue();
        plan.ShowStopPasswordField.Should().BeFalse();
        plan.StatusText.Should().Be("Protection is not enforced.");
    }

    [Fact]
    public void BuildPlan_reports_protected_password_state_for_stop_workflow()
    {
        var current = ProtectionPasswordHelper.CreateWithPassword(ProtectionMode.CommentsOnly, "secret");

        var plan = RestrictEditingDialogPlanner.BuildPlan(current);

        plan.SelectedModeIndex.Should().Be(2);
        plan.CanStartProtection.Should().BeFalse();
        plan.CanStopProtection.Should().BeTrue();
        plan.ShowStartPasswordFields.Should().BeFalse();
        plan.ShowStopPasswordField.Should().BeTrue();
        plan.StatusText.Should().Contain("Comments");
        plan.StatusText.Should().Contain("password is required");
    }

    [Fact]
    public void TryCreateStartSettings_rejects_password_mismatch()
    {
        var succeeded = RestrictEditingDialogPlanner.TryCreateStartSettings(
            ProtectionMode.ReadOnly,
            "one",
            "two",
            out var settings,
            out var validationMessage);

        succeeded.Should().BeFalse();
        settings.Should().Be(ProtectionSettings.Unprotected);
        validationMessage.Should().Be(RestrictEditingDialogPlanner.PasswordMismatchMessage);
    }

    [Fact]
    public void TryCreateStartSettings_with_password_creates_verifiable_settings()
    {
        var succeeded = RestrictEditingDialogPlanner.TryCreateStartSettings(
            ProtectionMode.FillingForms,
            "secret",
            "secret",
            out var settings,
            out var validationMessage);

        succeeded.Should().BeTrue();
        validationMessage.Should().BeNull();
        settings.Mode.Should().Be(ProtectionMode.FillingForms);
        settings.HasPassword.Should().BeTrue();
        ProtectionPasswordHelper.VerifyPassword(settings, "secret").Should().BeTrue();
    }

    [Theory]
    [InlineData("secret", true)]
    [InlineData("wrong", false)]
    public void TryCreateStopSettings_validates_password_protected_documents(string password, bool expected)
    {
        var current = ProtectionPasswordHelper.CreateWithPassword(ProtectionMode.ReadOnly, "secret");

        var succeeded = RestrictEditingDialogPlanner.TryCreateStopSettings(
            current,
            password,
            out var settings,
            out var validationMessage);

        succeeded.Should().Be(expected);
        if (expected)
        {
            settings.Should().Be(ProtectionSettings.Unprotected);
            validationMessage.Should().BeNull();
        }
        else
        {
            settings.Should().Be(current);
            validationMessage.Should().Be(RestrictEditingDialogPlanner.IncorrectPasswordMessage);
        }
    }

    [Fact]
    public void TryCreateStopSettings_without_password_returns_unprotected()
    {
        var current = new ProtectionSettings(ProtectionMode.TrackChangesOnly);

        var succeeded = RestrictEditingDialogPlanner.TryCreateStopSettings(
            current,
            null,
            out var settings,
            out var validationMessage);

        succeeded.Should().BeTrue();
        settings.Should().Be(ProtectionSettings.Unprotected);
        validationMessage.Should().BeNull();
    }
}
