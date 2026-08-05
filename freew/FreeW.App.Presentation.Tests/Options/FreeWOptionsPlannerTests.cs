using Free.Shared.AppServices;
using FreeW.App.Presentation.Options;

namespace FreeW.App.Presentation.Tests.Options;

public sealed class FreeWOptionsPlannerTests
{
    [Fact]
    public void Defaults_AreSensible()
    {
        var options = new FreeWOptions();

        options.RecentFilesCap.Should().Be(FreeWOptions.DefaultRecentFilesCap);
        options.DefaultSaveFormat.Should().Be(FreeWOptions.DocxDefaultFormat);
        options.UiLanguage.Should().BeEmpty();
    }

    [Fact]
    public void Normalize_ClampsRecentFilesAndDefaultsFormat()
    {
        var options = new FreeWOptions
        {
            RecentFilesCap = 9999,
            DefaultSaveFormat = " ",
            UiLanguage = "  en-GB  ",
        };

        options.Normalize();

        options.RecentFilesCap.Should().Be(ApplicationOptionsNormalizer.MaxRecentFilesCap);
        options.DefaultSaveFormat.Should().Be(FreeWOptions.DocxDefaultFormat);
        options.UiLanguage.Should().Be("en-GB");
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("15", 15)]
    [InlineData(" 7 ", 7)]
    public void TryParseRecentFilesCap_AcceptsInRange(string text, int expected)
    {
        OptionsDialogPlanner.TryParseRecentFilesCap(text, out var cap).Should().BeTrue();
        cap.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("99999")]
    [InlineData("3.5")]
    public void TryParseRecentFilesCap_RejectsInvalidOrOutOfRange(string text)
    {
        OptionsDialogPlanner.TryParseRecentFilesCap(text, out _).Should().BeFalse();
    }

    [Fact]
    public void BuildResult_NormalizesAndPreservesAutoCorrectObjects()
    {
        var autoFormat = AutoFormatOptions.Default with { Hyperlinks = false };
        var autoCorrect = new AutoCorrectOptions { ReplaceText = false };

        var result = OptionsDialogPlanner.BuildResult(
            recentFilesCap: 4,
            format: " ",
            uiLanguage: "  uk-UA  ",
            autoCorrectEnabled: false,
            autoFormat: autoFormat,
            autoCorrect: autoCorrect);

        result.RecentFilesCap.Should().Be(4);
        result.DefaultSaveFormat.Should().Be(FreeWOptions.DocxDefaultFormat);
        result.UiLanguage.Should().Be("uk-UA");
        result.AutoCorrectEnabled.Should().BeFalse();
        result.AutoFormat.Hyperlinks.Should().BeFalse();
        result.AutoCorrect.ReplaceText.Should().BeFalse();
    }

    [Fact]
    public void BuildSurface_ExposesGeneralAutoCorrectAndAutoFormatSections()
    {
        var options = new FreeWOptions
        {
            RecentFilesCap = 9999,
            AutoCorrectEnabled = false,
            AutoFormat = AutoFormatOptions.Default with { Hyperlinks = false },
            AutoCorrect = new AutoCorrectOptions
            {
                ReplaceText = true,
                Replacements = [new AutoCorrectReplacement("teh", "the")],
            },
        };

        var surface = OptionsDialogPlanner.BuildSurface(options, "uk-UA");

        surface.Title.Should().Be("FreeW Options");
        surface.Tabs.Select(tab => tab.Header)
            .Should().Equal("General", "AutoCorrect", "AutoFormat As You Type");
        surface.General.UiLanguageHint.Should().Contain("uk-UA");
        surface.General.FormatChoices.Single().Extension.Should().Be(FreeWOptions.DocxDefaultFormat);
        surface.AutoCorrect.Toggles.Select(toggle => toggle.Kind)
            .Should().Contain([
                OptionsDialogToggleKind.CorrectTwoInitialCapitals,
                OptionsDialogToggleKind.CapitalizeDayNames,
                OptionsDialogToggleKind.ReplaceText,
            ]);
        surface.AutoCorrect.ReplacementsText.Should().Contain("teh => the");
        surface.AutoFormat.MasterToggle.Kind.Should().Be(OptionsDialogToggleKind.AutoCorrectEnabled);
        surface.AutoFormat.MasterToggle.IsChecked.Should().BeFalse();
        surface.AutoFormat.RuleToggles.Single(toggle => toggle.Kind == OptionsDialogToggleKind.Hyperlinks)
            .IsChecked.Should().BeFalse();

        options.RecentFilesCap.Should().Be(9999, "planning the surface must not normalize and mutate live options");
    }

    [Fact]
    public void TryParseAutoCorrectReplacements_AcceptsArrowAndTabRows()
    {
        OptionsDialogPlanner.TryParseAutoCorrectReplacements(
            "teh => the\r\nadn\tand\r\n",
            out var replacements,
            out var errorMessage).Should().BeTrue();

        errorMessage.Should().BeNull();
        replacements.Should().Equal(
            new AutoCorrectReplacement("teh", "the"),
            new AutoCorrectReplacement("adn", "and"));
    }

    [Theory]
    [InlineData("teh")]
    [InlineData(" => the")]
    [InlineData("teh => ")]
    public void TryParseAutoCorrectReplacements_RejectsMalformedRows(string text)
    {
        OptionsDialogPlanner.TryParseAutoCorrectReplacements(
            text,
            out var replacements,
            out var errorMessage).Should().BeFalse();

        replacements.Should().BeEmpty();
        errorMessage.Should().Be(OptionsDialogPlanner.ReplacementsValidationMessage);
    }

    [Fact]
    public void Workflow_TryBuildResult_ProjectsAllOptionGroupsAndReplacementRows()
    {
        var checkedToggles = new[]
        {
            OptionsDialogToggleKind.AutoCorrectEnabled,
            OptionsDialogToggleKind.SmartQuotes,
            OptionsDialogToggleKind.Ellipsis,
            OptionsDialogToggleKind.Capitalization,
            OptionsDialogToggleKind.NumberedLists,
            OptionsDialogToggleKind.Fractions,
            OptionsDialogToggleKind.CorrectTwoInitialCapitals,
            OptionsDialogToggleKind.ReplaceText,
        };
        var input = new OptionsDialogInput(
            "7",
            FreeWOptions.DocxDefaultFormat,
            "  uk-UA  ",
            checkedToggles,
            [
                new("  teh  ", "the"),
                new("", "ignored"),
                new("missing-value", null),
            ]);

        OptionsDialogWorkflowPlanner.TryBuildResult(input, out var result, out var validation)
            .Should().BeTrue();

        validation.Should().BeNull();
        result.Should().NotBeNull();
        result!.RecentFilesCap.Should().Be(7);
        result.UiLanguage.Should().Be("uk-UA");
        result.AutoCorrectEnabled.Should().BeTrue();
        result.AutoFormat.SmartQuotes.Should().BeTrue();
        result.AutoFormat.Dashes.Should().BeFalse();
        result.AutoFormat.Ellipsis.Should().BeTrue();
        result.AutoFormat.Capitalization.Should().BeTrue();
        result.AutoFormat.NumberedLists.Should().BeTrue();
        result.AutoFormat.Fractions.Should().BeTrue();
        result.AutoFormat.Hyperlinks.Should().BeFalse();
        result.AutoCorrect.CorrectTwoInitialCapitals.Should().BeTrue();
        result.AutoCorrect.CapitalizeDayNames.Should().BeFalse();
        result.AutoCorrect.ReplaceText.Should().BeTrue();
        result.AutoCorrect.Replacements.Should().Equal(new AutoCorrectReplacement("teh", "the"));
    }

    [Fact]
    public void Workflow_TryBuildResult_ReportsRecentFilesValidationWithoutBuildingOptions()
    {
        var input = new OptionsDialogInput("not-a-number", null, null, [], []);

        OptionsDialogWorkflowPlanner.TryBuildResult(input, out var result, out var validation)
            .Should().BeFalse();

        result.Should().BeNull();
        validation.Should().Be(new OptionsDialogValidation(
            OptionsDialogValidationTarget.RecentFilesCap,
            OptionsDialogWorkflowPlanner.RecentFilesCapValidationMessage));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Workflow_PlanEnabledState_ProjectsIndependentControlGroups(
        bool autoCorrectEnabled,
        bool replaceTextEnabled)
    {
        var state = OptionsDialogWorkflowPlanner.PlanEnabledState(autoCorrectEnabled, replaceTextEnabled);

        state.AutoFormatRulesEnabled.Should().Be(autoCorrectEnabled);
        state.ReplacementsEnabled.Should().Be(replaceTextEnabled);
    }

    [Fact]
    public void OptionsModelAndPlanner_LiveInPresentationNotWpfHost()
    {
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");

        File.Exists(Path.Combine(repoRoot, "freew", "FreeW.App.Presentation", "Options", "FreeWOptions.cs"))
            .Should().BeTrue();
        File.Exists(Path.Combine(repoRoot, "freew", "FreeW.App.Presentation", "Options", "OptionsDialogPlanner.cs"))
            .Should().BeTrue();
        File.Exists(Path.Combine(repoRoot, "freew", "FreeW.App.Host", "FreeWOptions.cs"))
            .Should().BeFalse();
        File.Exists(Path.Combine(repoRoot, "freew", "FreeW.App.Host", "OptionsDialogPlanner.cs"))
            .Should().BeFalse();
    }

}
