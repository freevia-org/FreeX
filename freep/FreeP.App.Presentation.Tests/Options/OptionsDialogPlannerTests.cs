using FluentAssertions;
using Free.Shared.Shell;
using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Covers the portable decision logic behind FreeP's Options editor (R128): parsing/validating the
/// recent-files count, building the dialog surface from a seed <see cref="FreePOptions"/>, and normalizing
/// the dialog's raw inputs back into a store-ready options object. WPF and Avalonia both route through
/// this planner (see FreeP.App.Host/OptionsDialog.cs and FreeP.App.Avalonia/OptionsDialog.cs) so a bug
/// caught here is a bug caught in both shells at once.
/// </summary>
public sealed class OptionsDialogPlannerTests
{
    [Fact]
    public void R128_TryParseRecentFilesCap_AcceptsInRangeWholeNumber()
    {
        OptionsDialogPlanner.TryParseRecentFilesCap("7", out var cap).Should().BeTrue();
        cap.Should().Be(7);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-number")]
    [InlineData("-1")]
    [InlineData("3.5")]
    public void R128_TryParseRecentFilesCap_RejectsInvalidText(string text)
    {
        OptionsDialogPlanner.TryParseRecentFilesCap(text, out _).Should().BeFalse();
    }

    [Fact]
    public void R128_TryParseRecentFilesCap_RejectsAboveMax()
    {
        OptionsDialogPlanner.TryParseRecentFilesCap(
            (FreePOptions.MaxRecentFilesCap + 1).ToString(),
            out _).Should().BeFalse();
    }

    [Fact]
    public void R128_BuildSurface_SeedsFieldsFromNormalizedOptions()
    {
        var options = new FreePOptions
        {
            RecentFilesCap = 9001, // above MaxRecentFilesCap, must be clamped by Normalize
            DefaultSaveFormat = FreePOptions.FxpDefaultFormat,
            UiLanguage = "fr-FR",
        };

        var surface = OptionsDialogPlanner.BuildSurface(options, "en-US");

        surface.RecentFilesCap.Should().Be(FreePOptions.MaxRecentFilesCap);
        surface.UiLanguage.Should().Be("fr-FR");
        surface.FormatChoices.Should().ContainSingle(choice => choice.Extension == FreePOptions.FxpDefaultFormat);
        surface.UiLanguageHint.Should().Contain("en-US");
        surface.AcceptLabel.Should().Be(ShellStrings.Current.Ok);
        surface.CancelLabel.Should().Be(ShellStrings.Current.Cancel);
        ShellStringText.NormalizeAccessText(surface.AcceptLabel).Should().Be("OK");
        ShellStringText.NormalizeAccessText(surface.CancelLabel).Should().Be("Cancel");
    }

    [Fact]
    public void R128_BuildSurface_NullOptions_FallsBackToDefaults()
    {
        var surface = OptionsDialogPlanner.BuildSurface(null, "invariant");

        surface.RecentFilesCap.Should().Be(FreePOptions.DefaultRecentFilesCap);
        surface.UiLanguage.Should().Be(FreePOptions.SystemDefaultLanguage);
    }

    [Fact]
    public void R128_BuildResult_NormalizesAndFillsBlankFormat()
    {
        var result = OptionsDialogPlanner.BuildResult(recentFilesCap: 3, format: "  ", uiLanguage: "uk-UA");

        result.RecentFilesCap.Should().Be(3);
        result.DefaultSaveFormat.Should().Be(FreePOptions.FxpDefaultFormat);
        result.UiLanguage.Should().Be("uk-UA");
    }

    [Fact]
    public void R128_BuildResult_ClampsOutOfRangeRecentFilesCap()
    {
        var result = OptionsDialogPlanner.BuildResult(
            recentFilesCap: FreePOptions.MaxRecentFilesCap + 50,
            format: FreePOptions.FxpDefaultFormat,
            uiLanguage: null);

        result.RecentFilesCap.Should().Be(FreePOptions.MaxRecentFilesCap);
        result.UiLanguage.Should().Be(FreePOptions.SystemDefaultLanguage);
    }
}
