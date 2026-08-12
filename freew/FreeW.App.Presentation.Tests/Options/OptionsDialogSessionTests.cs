using System.Globalization;
using Free.Shared.AppServices;
using FreeW.App.Presentation.Options;

namespace FreeW.App.Presentation.Tests.Options;

public sealed class OptionsDialogSessionTests
{
    [Fact]
    public void Session_owns_initial_surface_toggle_and_replacement_projection()
    {
        var options = new FreeWOptions
        {
            RecentFilesCap = 8,
            UiLanguage = "uk-UA",
            AutoCorrectEnabled = true,
            AutoFormat = new AutoFormatOptions
            {
                SmartQuotes = true,
                Hyperlinks = true,
            },
            AutoCorrect = new AutoCorrectOptions
            {
                ReplaceText = true,
                Replacements = [new AutoCorrectReplacement("teh", "the")],
            },
        };

        var session = new OptionsDialogSession(options, CultureInfo.GetCultureInfo("en-GB"));

        session.InitialResult.Should().BeSameAs(options);
        session.Surface.Title.Should().Be(OptionsDialogPlanner.Title);
        session.Surface.General.UiLanguageHint.Should().Contain("en-GB");
        session.InitialState.RecentFilesCapText.Should().Be("8");
        session.InitialState.SelectedFormat.Should().Be(FreeWOptions.DocxDefaultFormat);
        session.InitialState.UiLanguage.Should().Be("uk-UA");
        session.InitialState.CheckedToggles.Should().Contain(new[]
        {
            OptionsDialogToggleKind.AutoCorrectEnabled,
            OptionsDialogToggleKind.SmartQuotes,
            OptionsDialogToggleKind.Hyperlinks,
            OptionsDialogToggleKind.ReplaceText,
        });
        session.InitialState.Replacements.Should().Equal(
            new AutoCorrectReplacement("teh", "the"));
    }

    [Fact]
    public void Session_builds_apply_and_persist_plan_from_native_input()
    {
        var session = new OptionsDialogSession(new FreeWOptions(), CultureInfo.InvariantCulture);
        var input = new OptionsDialogInput(
            "7",
            FreeWOptions.DocxDefaultFormat,
            "  de-DE  ",
            [
                OptionsDialogToggleKind.AutoCorrectEnabled,
                OptionsDialogToggleKind.SmartQuotes,
                OptionsDialogToggleKind.ReplaceText,
            ],
            [new OptionsDialogReplacementInput("  teh  ", "the")]);

        var plan = session.PlanAcceptance(input);

        plan.ShouldApply.Should().BeTrue();
        plan.ShouldPersist.Should().BeTrue();
        plan.Validation.Should().BeNull();
        plan.Result.Should().NotBeNull();
        plan.Result!.RecentFilesCap.Should().Be(7);
        plan.Result.UiLanguage.Should().Be("de-DE");
        plan.Result.AutoCorrectEnabled.Should().BeTrue();
        plan.Result.AutoFormat.SmartQuotes.Should().BeTrue();
        plan.Result.AutoCorrect.ReplaceText.Should().BeTrue();
        plan.Result.AutoCorrect.Replacements.Should().Equal(new AutoCorrectReplacement("teh", "the"));
    }

    [Fact]
    public void Session_returns_renderer_neutral_validation_without_a_commit_plan()
    {
        var session = new OptionsDialogSession(new FreeWOptions(), CultureInfo.InvariantCulture);

        var plan = session.PlanAcceptance(new OptionsDialogInput("invalid", null, null, [], []));

        plan.ShouldApply.Should().BeFalse();
        plan.ShouldPersist.Should().BeFalse();
        plan.Result.Should().BeNull();
        plan.Validation.Should().Be(new BasicApplicationOptionsDialogValidation(
            BasicApplicationOptionsValidationTarget.RecentFilesCap,
            OptionsDialogWorkflowPlanner.RecentFilesCapValidationMessage));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Session_owns_dependent_control_state(bool autoCorrectEnabled, bool replaceTextEnabled)
    {
        var session = new OptionsDialogSession(new FreeWOptions(), CultureInfo.InvariantCulture);

        var state = session.PlanEnabledState(autoCorrectEnabled, replaceTextEnabled);

        state.AutoFormatRulesEnabled.Should().Be(autoCorrectEnabled);
        state.ReplacementsEnabled.Should().Be(replaceTextEnabled);
    }
}
