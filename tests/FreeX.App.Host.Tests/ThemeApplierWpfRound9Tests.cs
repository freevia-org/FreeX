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

    // ── FreeP accent key values (brick — the intended visual change) ──────────

    [Fact]
    public void WpfApplier_FreeP_ThemeAccentBrush_Is_Brick_B7472A()
    {
        var dict = WpfThemeApplier.BuildResources(BrandThemes.FreeP, "FreeP");
        var brush = (SolidColorBrush)dict["ThemeAccentBrush"];
        brush.Color.Should().Be(Color.FromRgb(0xB7, 0x47, 0x2A));
    }

    [Fact]
    public void WpfApplier_FreeP_ThemeAccentSoftBrush_Is_BrickSoft_F9EAE6()
    {
        var dict = WpfThemeApplier.BuildResources(BrandThemes.FreeP, "FreeP");
        var brush = (SolidColorBrush)dict["ThemeAccentSoftBrush"];
        brush.Color.Should().Be(Color.FromRgb(0xF9, 0xEA, 0xE6));
    }

    [Fact]
    public void WpfApplier_FreeP_ThemeAccentPressedBrush_Is_BrickPressed_F2D2CB()
    {
        var dict = WpfThemeApplier.BuildResources(BrandThemes.FreeP, "FreeP");
        var brush = (SolidColorBrush)dict["ThemeAccentPressedBrush"];
        brush.Color.Should().Be(Color.FromRgb(0xF2, 0xD2, 0xCB));
    }

    [Fact]
    public void WpfApplier_FreeP_ThemeRibbonButtonHoverBrush_Is_BrickHover_FDDDD6()
    {
        var dict = WpfThemeApplier.BuildResources(BrandThemes.FreeP, "FreeP");
        var brush = (SolidColorBrush)dict["ThemeRibbonButtonHoverBrush"];
        brush.Color.Should().Be(Color.FromRgb(0xFD, 0xDD, 0xD6));
    }

    // ── Core guarantee: FreeX vs FreeP DIFFER; FreeX vs FreeW are IDENTICAL ──
    // This is the proof that per-app accent reskin works while FreeX/FreeW remain byte-identical.

    [Fact]
    public void ThemeAccentBrush_FreeX_vs_FreeP_AreDifferent()
    {
        var freexDict = WpfThemeApplier.BuildResources(BrandThemes.FreeX, "FreeX");
        var freepDict = WpfThemeApplier.BuildResources(BrandThemes.FreeP, "FreeP");

        var freexColor = ((SolidColorBrush)freexDict["ThemeAccentBrush"]).Color;
        var freepColor = ((SolidColorBrush)freepDict["ThemeAccentBrush"]).Color;

        freepColor.Should().NotBe(freexColor,
            because: "FreeP uses brick (#B7472A) while FreeX uses teal (#0F6D8C) — per-app accent reskin");
    }

    [Fact]
    public void ThemeAccentBrush_FreeX_vs_FreeW_AreIdentical()
    {
        var freexDict = WpfThemeApplier.BuildResources(BrandThemes.FreeX, "FreeX");
        var freewDict = WpfThemeApplier.BuildResources(BrandThemes.FreeW, "FreeW");

        var freexColor = ((SolidColorBrush)freexDict["ThemeAccentBrush"]).Color;
        var freewColor = ((SolidColorBrush)freewDict["ThemeAccentBrush"]).Color;

        freewColor.Should().Be(freexColor,
            because: "FreeW and FreeX both use the teal accent (#0F6D8C) — byte-identical shared ribbon");
    }

    // Note: only ThemeAccentBrush / ThemeAccentDarkBrush / ThemeAccentSoftBrush are byte-identical
    // between FreeX and FreeW.  ThemeAccentPressedBrush differs (#CCEAF2 vs #CFEAF1) and
    // ThemeRibbonButtonHoverBrush differs (#BEE6FD vs #E6F6FA) — these are preserved per-app values
    // that happen to have slightly different tints despite both apps sharing the same base accent.
    [Theory]
    [InlineData("ThemeAccentBrush")]
    [InlineData("ThemeAccentDarkBrush")]
    [InlineData("ThemeAccentSoftBrush")]
    public void AccentBrush_FreeX_vs_FreeW_AreIdentical_ForBaseAccentKeys(string key)
    {
        var freexDict = WpfThemeApplier.BuildResources(BrandThemes.FreeX, "FreeX");
        var freewDict = WpfThemeApplier.BuildResources(BrandThemes.FreeW, "FreeW");

        var freexColor = ((SolidColorBrush)freexDict[key]).Color;
        var freewColor = ((SolidColorBrush)freewDict[key]).Color;

        freewColor.Should().Be(freexColor,
            because: $"FreeW and FreeX share the same base teal accent — '{key}' must be byte-identical");
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
            because: $"FreeP uses a distinct brick palette — '{key}' must differ from FreeX teal");
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
