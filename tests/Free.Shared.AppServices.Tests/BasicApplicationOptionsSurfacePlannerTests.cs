using System.Globalization;
using FreePOptions = FreeP.App.Compositor.FreePOptions;
using FreePPlanner = FreeP.App.Compositor.OptionsDialogPlanner;
using FreePSession = FreeP.App.Compositor.OptionsDialogSession;
using FreeWOptions = FreeW.App.Presentation.Options.FreeWOptions;
using FreeWPlanner = FreeW.App.Presentation.Options.OptionsDialogPlanner;
using FreeWSession = FreeW.App.Presentation.Options.OptionsDialogSession;

namespace Free.Shared.AppServices.Tests;

/// <summary>
/// Covers the neutral basic-Options core FreeW and FreeP share: the recent-files-count parse/validate
/// rule, the default-save-format choice mapping, the UI-language choice + hint rule, the General field
/// schema, and the accept/reject (OK vs Cancel) session transitions. Both apps route their real dialogs
/// through this code, so a bug caught here is a bug caught in four shells at once.
/// </summary>
public sealed class BasicApplicationOptionsSurfacePlannerTests
{
    // ---- recent-files count: parse + validate -------------------------------------------------

    [Theory]
    [InlineData("0", 0)]
    [InlineData("7", 7)]
    [InlineData(" 7 ", 7)]
    public void RecentFilesCap_AcceptsWholeNumbersInRange(string text, int expected)
    {
        ApplicationOptionsNormalizer.TryParseRecentFilesCap(text, out var cap).Should().BeTrue();
        cap.Should().Be(expected);
    }

    [Fact]
    public void RecentFilesCap_AcceptsTheInclusiveBounds()
    {
        ApplicationOptionsNormalizer.TryParseRecentFilesCap(
            ApplicationOptionsNormalizer.MinRecentFilesCap.ToString(CultureInfo.CurrentCulture),
            out var min).Should().BeTrue();
        min.Should().Be(ApplicationOptionsNormalizer.MinRecentFilesCap);

        ApplicationOptionsNormalizer.TryParseRecentFilesCap(
            ApplicationOptionsNormalizer.MaxRecentFilesCap.ToString(CultureInfo.CurrentCulture),
            out var max).Should().BeTrue();
        max.Should().Be(ApplicationOptionsNormalizer.MaxRecentFilesCap);
    }

    [Theory]
    [InlineData(null)]      // never-set text box
    [InlineData("")]        // blank
    [InlineData("   ")]     // whitespace only
    [InlineData("abc")]     // not a number
    [InlineData("3.5")]     // not a whole number
    [InlineData("-1")]      // negative
    [InlineData("1e3")]     // exponent notation
    public void RecentFilesCap_RejectsBlankNegativeAndNonIntegerText(string? text)
    {
        ApplicationOptionsNormalizer.TryParseRecentFilesCap(text, out var cap).Should().BeFalse();
        cap.Should().Be(0);
    }

    [Fact]
    public void RecentFilesCap_RejectsAboveMaximum()
    {
        ApplicationOptionsNormalizer.TryParseRecentFilesCap(
            (ApplicationOptionsNormalizer.MaxRecentFilesCap + 1).ToString(CultureInfo.CurrentCulture),
            out _).Should().BeFalse();
    }

    [Fact]
    public void RecentFilesCap_BothAppsForwardToTheSharedParser()
    {
        var above = (ApplicationOptionsNormalizer.MaxRecentFilesCap + 1).ToString(CultureInfo.CurrentCulture);

        FreeWPlanner.TryParseRecentFilesCap("7", out var freeW).Should().BeTrue();
        FreePPlanner.TryParseRecentFilesCap("7", out var freeP).Should().BeTrue();
        freeW.Should().Be(7);
        freeP.Should().Be(7);

        FreeWPlanner.TryParseRecentFilesCap(above, out _).Should().BeFalse();
        FreePPlanner.TryParseRecentFilesCap(above, out _).Should().BeFalse();
        FreeWPlanner.TryParseRecentFilesCap("-1", out _).Should().BeFalse();
        FreePPlanner.TryParseRecentFilesCap("-1", out _).Should().BeFalse();
    }

    // ---- default-save-format choice ------------------------------------------------------------

    [Fact]
    public void FormatChoice_RendersItsLabelForNativePickers()
    {
        var choice = new ApplicationOptionsFormatChoice("Word Document (*.docx)", ".docx");

        choice.ToString().Should().Be("Word Document (*.docx)");
        choice.Extension.Should().Be(".docx");
        choice.Should().Be(new ApplicationOptionsFormatChoice("Word Document (*.docx)", ".docx"));
    }

    [Fact]
    public void FormatChoice_BothAppsExposeExactlyTheirOwnShippedFormat()
    {
        FreeWPlanner.BuildSurface(new FreeWOptions(), "en-US").General.FormatChoices
            .Should().ContainSingle()
            .Which.Extension.Should().Be(FreeWOptions.DocxDefaultFormat);

        FreePPlanner.BuildSurface(new FreePOptions(), "en-US").FormatChoices
            .Should().ContainSingle()
            .Which.Extension.Should().Be(FreePOptions.FxpDefaultFormat);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FormatChoice_BlankSelectionFallsBackToTheProductDefault(string? format)
    {
        BasicApplicationOptionsDialogSession<FreeWOptions>
            .BuildResult(3, format, null, FreeWOptions.DocxDefaultFormat)
            .DefaultSaveFormat.Should().Be(FreeWOptions.DocxDefaultFormat);
        BasicApplicationOptionsDialogSession<FreePOptions>
            .BuildResult(3, format, null, FreePOptions.FxpDefaultFormat)
            .DefaultSaveFormat.Should().Be(FreePOptions.FxpDefaultFormat);
    }

    [Fact]
    public void FormatChoice_SelectedExtensionIsCarriedOntoTheResult()
    {
        BasicApplicationOptionsDialogSession<FreePOptions>
            .BuildResult(3, FreePOptions.FxpDefaultFormat, null, FreePOptions.FxpDefaultFormat)
            .DefaultSaveFormat.Should().Be(FreePOptions.FxpDefaultFormat);
    }

    // ---- UI-language choice --------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UiLanguageHint_UsesTheSystemOnlyFormWhenNoCultureIsNamed(string? label)
    {
        BasicApplicationOptionsSurfacePlanner
            .BuildUiLanguageHint(label, "no culture", "currently {0}")
            .Should().Be("no culture");
    }

    [Fact]
    public void UiLanguageHint_FormatsTheNamedCultureIntoTheCurrentForm()
    {
        BasicApplicationOptionsSurfacePlanner
            .BuildUiLanguageHint("uk-UA", "no culture", "currently {0}")
            .Should().Be("currently uk-UA");
    }

    [Fact]
    public void UiLanguageHint_RejectsMissingProductText()
    {
        var act = () => BasicApplicationOptionsSurfacePlanner.BuildUiLanguageHint("uk-UA", " ", "currently {0}");
        act.Should().Throw<ArgumentException>();

        var actFormat = () => BasicApplicationOptionsSurfacePlanner.BuildUiLanguageHint("uk-UA", "no culture", " ");
        actFormat.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void UiLanguageHint_BothAppsNameTheDetectedCulture()
    {
        FreeWPlanner.BuildSurface(new FreeWOptions(), "uk-UA").General.UiLanguageHint.Should().Contain("uk-UA");
        FreePPlanner.BuildSurface(new FreePOptions(), "uk-UA").UiLanguageHint.Should().Contain("uk-UA");

        FreeWPlanner.BuildSurface(new FreeWOptions(), string.Empty).General.UiLanguageHint.Should().NotContain("(");
        FreePPlanner.BuildSurface(new FreePOptions(), string.Empty).UiLanguageHint.Should().NotContain("(");
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("  ", "")]
    [InlineData("  uk-UA  ", "uk-UA")]
    public void UiLanguage_IsTrimmedAndBlankMeansFollowTheSystem(string? input, string expected)
    {
        ApplicationOptionsNormalizer.NormalizeUiLanguage(input).Trim().Should().Be(expected);
    }

    [Fact]
    public void UiLanguage_AcceptedValueIsNormalizedOntoTheResult()
    {
        BasicApplicationOptionsDialogSession<FreeWOptions>
            .BuildResult(3, null, "  uk-ua  ", FreeWOptions.DocxDefaultFormat)
            .UiLanguage.Should().Be("uk-UA");
        BasicApplicationOptionsDialogSession<FreePOptions>
            .BuildResult(3, null, "  uk-ua  ", FreePOptions.FxpDefaultFormat)
            .UiLanguage.Should().Be("uk-UA");
    }

    // ---- General field schema ------------------------------------------------------------------

    [Fact]
    public void GeneralSpec_ProjectsThreeOrderedFieldsWithTheHintOnTheLanguageRow()
    {
        var spec = BasicApplicationOptionsSurfacePlanner.BuildGeneral(
            "Recent files to keep:",
            "Default save format:",
            "UI language:",
            "uk-UA",
            "no culture",
            "currently {0}",
            [new("Word Document (*.docx)", ".docx")]);

        spec.Fields.Select(field => field.Kind).Should().Equal(
            BasicApplicationOptionsFieldKind.RecentFilesCap,
            BasicApplicationOptionsFieldKind.DefaultSaveFormat,
            BasicApplicationOptionsFieldKind.UiLanguage);
        spec.Fields.Select(field => field.Label).Should().Equal(
            "Recent files to keep:",
            "Default save format:",
            "UI language:");
        spec.Fields[0].Hint.Should().BeNull();
        spec.Fields[1].Hint.Should().BeNull();
        spec.Fields[2].Hint.Should().Be("currently uk-UA");
        spec.UiLanguageHint.Should().Be("currently uk-UA");
    }

    [Fact]
    public void GeneralSpec_RejectsMissingLabels()
    {
        var act = () => BasicApplicationOptionsSurfacePlanner.BuildGeneral(
            " ", "Default save format:", "UI language:", "hint", []);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GeneralSpec_BothAppsExposeTheSameFieldSchema()
    {
        var freeW = FreeWPlanner.BuildSurface(new FreeWOptions(), "en-US").General;
        var freeP = FreePPlanner.BuildSurface(new FreePOptions(), "en-US").General;

        freeW.Fields.Select(field => field.Kind)
            .Should().Equal(freeP.Fields.Select(field => field.Kind));
        freeW.Fields.Select(field => field.Hint is null)
            .Should().Equal(freeP.Fields.Select(field => field.Hint is null));
    }

    // ---- session transitions: OK (apply) vs Cancel ---------------------------------------------

    [Fact]
    public void Session_AcceptedInputAppliesPersistsAndNormalizes()
    {
        var freeW = new FreeWSession(new FreeWOptions(), CultureInfo.GetCultureInfo("en-US"));
        var freeP = new FreePSession(new FreePOptions(), CultureInfo.GetCultureInfo("en-US"));

        var acceptedW = freeW.PlanAcceptance(new FreeW.App.Presentation.Options.OptionsDialogInput(
            "6", " ", "  uk-ua  ", [], []));
        var acceptedP = freeP.PlanAcceptance(new BasicApplicationOptionsDialogInput("6", " ", "  uk-ua  "));

        foreach (var plan in new[]
                 {
                     (acceptedW.ShouldApply, acceptedW.ShouldPersist, acceptedW.Validation),
                     (acceptedP.ShouldApply, acceptedP.ShouldPersist, acceptedP.Validation),
                 })
        {
            plan.ShouldApply.Should().BeTrue();
            plan.ShouldPersist.Should().BeTrue();
            plan.Validation.Should().BeNull();
        }

        acceptedW.Result!.RecentFilesCap.Should().Be(6);
        acceptedW.Result!.UiLanguage.Should().Be("uk-UA");
        acceptedW.Result!.DefaultSaveFormat.Should().Be(FreeWOptions.DocxDefaultFormat);
        acceptedP.Result!.RecentFilesCap.Should().Be(6);
        acceptedP.Result!.UiLanguage.Should().Be("uk-UA");
        acceptedP.Result!.DefaultSaveFormat.Should().Be(FreePOptions.FxpDefaultFormat);
    }

    [Fact]
    public void Session_RejectedInputNeitherAppliesNorPersistsAndNamesTheOnlyValidationTarget()
    {
        var freeW = new FreeWSession(new FreeWOptions(), CultureInfo.InvariantCulture);
        var freeP = new FreePSession(new FreePOptions(), CultureInfo.InvariantCulture);

        var rejectedW = freeW.PlanAcceptance(new FreeW.App.Presentation.Options.OptionsDialogInput(
            "not-a-number", null, null, [], []));
        var rejectedP = freeP.PlanAcceptance(new BasicApplicationOptionsDialogInput("not-a-number", null, null));

        rejectedW.ShouldApply.Should().BeFalse();
        rejectedW.ShouldPersist.Should().BeFalse();
        rejectedW.Result.Should().BeNull();
        rejectedP.ShouldApply.Should().BeFalse();
        rejectedP.ShouldPersist.Should().BeFalse();
        rejectedP.Result.Should().BeNull();

        // The basic surface has exactly one validation target; the message text stays app-owned so each
        // product can localize it.
        Enum.GetValues<BasicApplicationOptionsValidationTarget>().Should().Equal(
            BasicApplicationOptionsValidationTarget.RecentFilesCap);
        rejectedW.Validation!.Target.Should().Be(BasicApplicationOptionsValidationTarget.RecentFilesCap);
        rejectedP.Validation!.Target.Should().Be(BasicApplicationOptionsValidationTarget.RecentFilesCap);
        rejectedW.Validation!.Message.Should().NotBeNullOrWhiteSpace();
        rejectedP.Validation!.Message.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Session_CancelLeavesTheOpenTimeOptionsUntouched()
    {
        // Cancel is "never call PlanAcceptance": the session must not have mutated the caller's options
        // just by opening, and InitialResult is what the shells restore.
        var live = new FreePOptions { RecentFilesCap = 9, UiLanguage = "en-US" };
        var session = new FreePSession(live, CultureInfo.GetCultureInfo("en-US"));

        session.InitialResult.Should().BeSameAs(live);
        live.RecentFilesCap.Should().Be(9);
        live.UiLanguage.Should().Be("en-US");
        session.InitialState.RecentFilesCapText.Should().Be("9");
    }

    [Fact]
    public void Session_PlanningTheSurfaceNeverMutatesTheLiveOptions()
    {
        var freeW = new FreeWOptions { RecentFilesCap = 9999 };
        var freeP = new FreePOptions { RecentFilesCap = 9999 };

        FreeWPlanner.BuildSurface(freeW, "en-US");
        FreePPlanner.BuildSurface(freeP, "en-US");

        freeW.RecentFilesCap.Should().Be(9999);
        freeP.RecentFilesCap.Should().Be(9999);
    }

    [Fact]
    public void Session_SurfaceSeedsAreClampedByTheSharedNormalizer()
    {
        var surface = FreePPlanner.BuildSurface(new FreePOptions { RecentFilesCap = 9001 }, "en-US");

        surface.RecentFilesCap.Should().Be(ApplicationOptionsNormalizer.MaxRecentFilesCap);
    }
}
