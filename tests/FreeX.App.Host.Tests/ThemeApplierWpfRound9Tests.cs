using System.Windows.Media;
using FluentAssertions;
using Free.Shared.Theme;
using Free.Shared.Theme.Wpf;

namespace FreeX.App.Host.Tests;

/// <summary>
/// WS-G round 9: proves that <see cref="WpfThemeApplier.BuildResources"/> emits the five
/// <c>ThemeAccent*Brush</c> / <c>ThemeRibbonButtonHoverBrush</c> keys needed by the shared ribbon
/// renderer, and that:
/// <list type="bullet">
/// <item>FreeX vs FreeP yield <em>different</em> <c>ThemeAccentBrush</c> values (teal vs brick),
///   confirming the per-app accent reskin works.</item>
/// <item>FreeX vs FreeW yield the <em>same</em> <c>ThemeAccentBrush</c> (both teal),
///   confirming byte-identical behaviour for apps that share the teal palette.</item>
/// </list>
/// </summary>
public sealed class ThemeApplierWpfRound9Tests
{
    // ── All five accent keys are present ──────────────────────────────────────

    [Fact]
    public void WpfApplier_AllFiveAccentKeysPresent_ForFreeX()
    {
        var dict = WpfThemeApplier.BuildResources(BrandThemes.FreeX, "FreeX");
        var expectedAccentKeys = new[]
        {
            "ThemeAccentBrush",
            "ThemeAccentDarkBrush",
            "ThemeAccentSoftBrush",
            "ThemeAccentPressedBrush",
            "ThemeRibbonButtonHoverBrush",
        };
        foreach (var key in expectedAccentKeys)
            dict.Contains(key).Should().BeTrue(because: $"accent key '{key}' must be registered");
    }

    [Fact]
    public void WpfApplier_AllFiveAccentKeysPresent_ForFreeP()
    {
        var dict = WpfThemeApplier.BuildResources(BrandThemes.FreeP, "FreeP");
        var expectedAccentKeys = new[]
        {
            "ThemeAccentBrush",
            "ThemeAccentDarkBrush",
            "ThemeAccentSoftBrush",
            "ThemeAccentPressedBrush",
            "ThemeRibbonButtonHoverBrush",
        };
        foreach (var key in expectedAccentKeys)
            dict.Contains(key).Should().BeTrue(because: $"accent key '{key}' must be registered for FreeP");
    }

    // ── FreeX accent key values (teal — byte-identical to FreeX palette) ─────

    [Fact]
    public void WpfApplier_FreeX_ThemeAccentBrush_Is_Teal_0F6D8C()
    {
        var dict = WpfThemeApplier.BuildResources(BrandThemes.FreeX, "FreeX");
        var brush = (SolidColorBrush)dict["ThemeAccentBrush"];
        brush.Color.Should().Be(Color.FromRgb(0x0F, 0x6D, 0x8C));
    }

    [Fact]
    public void WpfApplier_FreeX_ThemeAccentSoftBrush_Is_E6F6FA()
    {
        var dict = WpfThemeApplier.BuildResources(BrandThemes.FreeX, "FreeX");
        var brush = (SolidColorBrush)dict["ThemeAccentSoftBrush"];
        brush.Color.Should().Be(Color.FromRgb(0xE6, 0xF6, 0xFA));
    }

    [Fact]
    public void WpfApplier_FreeX_ThemeAccentPressedBrush_Is_CCEAF2()
    {
        var dict = WpfThemeApplier.BuildResources(BrandThemes.FreeX, "FreeX");
        var brush = (SolidColorBrush)dict["ThemeAccentPressedBrush"];
        brush.Color.Should().Be(Color.FromRgb(0xCC, 0xEA, 0xF2));
    }

    [Fact]
    public void WpfApplier_FreeX_ThemeRibbonButtonHoverBrush_Is_BEE6FD()
    {
        var dict = WpfThemeApplier.BuildResources(BrandThemes.FreeX, "FreeX");
        var brush = (SolidColorBrush)dict["ThemeRibbonButtonHoverBrush"];
        brush.Color.Should().Be(Color.FromRgb(0xBE, 0xE6, 0xFD));
    }

    // ── FreeP owned berry/plum accent values ───────────────────────────────────

    [Fact]
    public void WpfApplier_FreeP_ThemeAccentBrush_Is_Berry_A23B72()
    {
        var dict = WpfThemeApplier.BuildResources(BrandThemes.FreeP, "FreeP");
        var brush = (SolidColorBrush)dict["ThemeAccentBrush"];
        brush.Color.Should().Be(Color.FromRgb(0xA2, 0x3B, 0x72));
    }

    [Fact]
    public void WpfApplier_FreeP_ThemeAccentSoftBrush_Is_BerrySoft_F9E7F1()
    {
        var dict = WpfThemeApplier.BuildResources(BrandThemes.FreeP, "FreeP");
        var brush = (SolidColorBrush)dict["ThemeAccentSoftBrush"];
        brush.Color.Should().Be(Color.FromRgb(0xF9, 0xE7, 0xF1));
    }

    [Fact]
    public void WpfApplier_FreeP_ThemeAccentPressedBrush_Is_BerryPressed_F1CDE0()
    {
        var dict = WpfThemeApplier.BuildResources(BrandThemes.FreeP, "FreeP");
        var brush = (SolidColorBrush)dict["ThemeAccentPressedBrush"];
        brush.Color.Should().Be(Color.FromRgb(0xF1, 0xCD, 0xE0));
    }

    [Fact]
    public void WpfApplier_FreeP_ThemeRibbonButtonHoverBrush_Is_BerryHover_F3D7E6()
    {
        var dict = WpfThemeApplier.BuildResources(BrandThemes.FreeP, "FreeP");
        var brush = (SolidColorBrush)dict["ThemeRibbonButtonHoverBrush"];
        brush.Color.Should().Be(Color.FromRgb(0xF3, 0xD7, 0xE6));
    }

    // ── Core guarantee: every product accent family is distinct ───────────────

    [Fact]
    public void ThemeAccentBrush_FreeX_vs_FreeP_AreDifferent()
    {
        var freexDict = WpfThemeApplier.BuildResources(BrandThemes.FreeX, "FreeX");
        var freepDict = WpfThemeApplier.BuildResources(BrandThemes.FreeP, "FreeP");

        var freexColor = ((SolidColorBrush)freexDict["ThemeAccentBrush"]).Color;
        var freepColor = ((SolidColorBrush)freepDict["ThemeAccentBrush"]).Color;

        freepColor.Should().NotBe(freexColor,
            because: "FreeP uses berry (#A23B72) while FreeX uses teal (#0F6D8C)");
    }

    [Fact]
    public void ThemeAccentBrush_FreeX_vs_FreeW_AreDifferent()
    {
        var freexDict = WpfThemeApplier.BuildResources(BrandThemes.FreeX, "FreeX");
        var freewDict = WpfThemeApplier.BuildResources(BrandThemes.FreeW, "FreeW");

        var freexColor = ((SolidColorBrush)freexDict["ThemeAccentBrush"]).Color;
        var freewColor = ((SolidColorBrush)freewDict["ThemeAccentBrush"]).Color;

        freewColor.Should().NotBe(freexColor,
            because: "FreeW uses amber (#A26714) while FreeX uses teal (#0F6D8C)");
    }

    [Theory]
    [InlineData("ThemeAccentBrush")]
    [InlineData("ThemeAccentDarkBrush")]
    [InlineData("ThemeAccentSoftBrush")]
    public void AccentBrush_FreeX_vs_FreeW_AreDifferent_ForBaseAccentKeys(string key)
    {
        var freexDict = WpfThemeApplier.BuildResources(BrandThemes.FreeX, "FreeX");
        var freewDict = WpfThemeApplier.BuildResources(BrandThemes.FreeW, "FreeW");

        var freexColor = ((SolidColorBrush)freexDict[key]).Color;
        var freewColor = ((SolidColorBrush)freewDict[key]).Color;

        freewColor.Should().NotBe(freexColor,
            because: $"FreeW owns an amber/umber palette — '{key}' must differ from FreeX");
    }

    [Theory]
    [InlineData("ThemeAccentBrush")]
    [InlineData("ThemeAccentSoftBrush")]
    [InlineData("ThemeAccentPressedBrush")]
    [InlineData("ThemeRibbonButtonHoverBrush")]
    public void AccentBrush_FreeX_vs_FreeP_AreDifferent_ForMainAccentKeys(string key)
    {
        var freexDict = WpfThemeApplier.BuildResources(BrandThemes.FreeX, "FreeX");
        var freepDict = WpfThemeApplier.BuildResources(BrandThemes.FreeP, "FreeP");

        var freexColor = ((SolidColorBrush)freexDict[key]).Color;
        var freepColor = ((SolidColorBrush)freepDict[key]).Color;

        freepColor.Should().NotBe(freexColor,
            because: $"FreeP uses a distinct berry/plum palette — '{key}' must differ from FreeX teal");
    }

    // ── Round 8 neutral keys are unaffected (regression guard) ───────────────

    [Fact]
    public void WpfApplier_Round8NeutralKeys_StillPresent_AfterRound9()
    {
        var dict = WpfThemeApplier.BuildResources(BrandThemes.FreeX, "FreeX");
        var neutralKeys = new[]
        {
            "ThemeNeutralTextBrush",
            "ThemeNeutralMutedTextBrush",
            "ThemeNeutralWhiteBrush",
            "ThemeNeutralDangerBrush",
            "ThemeNeutralSheetSurfaceBrush",
            "ThemeNeutralBorderBrush",
            "ThemeNeutralBorderStrongBrush",
        };
        foreach (var key in neutralKeys)
            dict.Contains(key).Should().BeTrue(because: $"round-8 neutral key '{key}' must still be present after round 9");
    }
}
