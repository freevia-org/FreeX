using System.Windows.Media;
using FluentAssertions;
using Free.Shared.Theme;
using Free.Shared.Theme.Wpf;

namespace FreeX.App.Host.Tests;

/// <summary>
/// WS-G round 8: proves that <see cref="WpfThemeApplier.BuildResources"/> emits the shared
/// <c>ThemeNeutral*Brush</c> keys needed by the shared ribbon renderer, and that applying any
/// of the three brand themes yields <em>byte-identical</em> neutral brush values — confirming
/// that the shared ribbon neutral chrome is truly app-neutral.
/// </summary>
public sealed class ThemeApplierWpfRound8Tests
{
    // ── FreeX: neutral key values ─────────────────────────────────────────────

    [Fact]
    public void WpfApplier_FreeX_NeutralTextBrush_Is_1F1F1F()
    {
        var dict = WpfThemeApplier.BuildResources(BrandThemes.FreeX, "FreeX");
        var brush = (SolidColorBrush)dict["ThemeNeutralTextBrush"];
        brush.Color.Should().Be(Color.FromRgb(0x1F, 0x1F, 0x1F));
    }

    [Fact]
    public void WpfApplier_FreeX_NeutralMutedTextBrush_Is_5F6368()
    {
        var dict = WpfThemeApplier.BuildResources(BrandThemes.FreeX, "FreeX");
        var brush = (SolidColorBrush)dict["ThemeNeutralMutedTextBrush"];
        brush.Color.Should().Be(Color.FromRgb(0x5F, 0x63, 0x68));
    }

    [Fact]
    public void WpfApplier_FreeX_NeutralWhiteBrush_Is_FFFFFF()
    {
        var dict = WpfThemeApplier.BuildResources(BrandThemes.FreeX, "FreeX");
        var brush = (SolidColorBrush)dict["ThemeNeutralWhiteBrush"];
        brush.Color.Should().Be(Color.FromRgb(0xFF, 0xFF, 0xFF));
    }

    [Fact]
    public void WpfApplier_FreeX_NeutralDangerBrush_Is_C42B1C()
    {
        var dict = WpfThemeApplier.BuildResources(BrandThemes.FreeX, "FreeX");
        var brush = (SolidColorBrush)dict["ThemeNeutralDangerBrush"];
        brush.Color.Should().Be(Color.FromRgb(0xC4, 0x2B, 0x1C));
    }

    [Fact]
    public void WpfApplier_FreeX_NeutralSheetSurfaceBrush_Is_F3F3F3()
    {
        var dict = WpfThemeApplier.BuildResources(BrandThemes.FreeX, "FreeX");
        var brush = (SolidColorBrush)dict["ThemeNeutralSheetSurfaceBrush"];
        brush.Color.Should().Be(Color.FromRgb(0xF3, 0xF3, 0xF3));
    }

    [Fact]
    public void WpfApplier_FreeX_NeutralBorderBrush_Is_DADCE0()
    {
        var dict = WpfThemeApplier.BuildResources(BrandThemes.FreeX, "FreeX");
        var brush = (SolidColorBrush)dict["ThemeNeutralBorderBrush"];
        brush.Color.Should().Be(Color.FromRgb(0xDA, 0xDC, 0xE0));
    }

    [Fact]
    public void WpfApplier_FreeX_NeutralBorderStrongBrush_Is_C8CCD0()
    {
        var dict = WpfThemeApplier.BuildResources(BrandThemes.FreeX, "FreeX");
        var brush = (SolidColorBrush)dict["ThemeNeutralBorderStrongBrush"];
        brush.Color.Should().Be(Color.FromRgb(0xC8, 0xCC, 0xD0));
    }

    // ── All five keys are present ─────────────────────────────────────────────

    [Fact]
    public void WpfApplier_AllNeutralKeysPresent_ForFreeX()
    {
        var dict = WpfThemeApplier.BuildResources(BrandThemes.FreeX, "FreeX");
        var expectedNeutralKeys = new[]
        {
            "ThemeNeutralTextBrush",
            "ThemeNeutralMutedTextBrush",
            "ThemeNeutralWhiteBrush",
            "ThemeNeutralDangerBrush",
            "ThemeNeutralSheetSurfaceBrush",
            "ThemeNeutralBorderBrush",
            "ThemeNeutralBorderStrongBrush",
        };
        foreach (var key in expectedNeutralKeys)
            dict.Contains(key).Should().BeTrue(because: $"neutral key '{key}' must be registered");
    }

    // ── Byte-identical across all three brand themes ───────────────────────────
    // This is the core guarantee of WS-G round 8: the shared ribbon neutral chrome must
    // look the same regardless of which sister app loaded it.

    [Theory]
    [InlineData("ThemeNeutralTextBrush")]
    [InlineData("ThemeNeutralMutedTextBrush")]
    [InlineData("ThemeNeutralWhiteBrush")]
    [InlineData("ThemeNeutralDangerBrush")]
    [InlineData("ThemeNeutralSheetSurfaceBrush")]
    [InlineData("ThemeNeutralBorderBrush")]
    [InlineData("ThemeNeutralBorderStrongBrush")]
    public void NeutralBrush_IsIdentical_AcrossFreeX_FreeW_FreeP(string key)
    {
        var freexDict = WpfThemeApplier.BuildResources(BrandThemes.FreeX, "FreeX");
        var freewDict = WpfThemeApplier.BuildResources(BrandThemes.FreeW, "FreeW");
        var freepDict = WpfThemeApplier.BuildResources(BrandThemes.FreeP, "FreeP");

        var freexColor = ((SolidColorBrush)freexDict[key]).Color;
        var freewColor = ((SolidColorBrush)freewDict[key]).Color;
        var freepColor = ((SolidColorBrush)freepDict[key]).Color;

        freewColor.Should().Be(freexColor,
            because: $"FreeW '{key}' must be byte-identical to FreeX (shared neutral role)");
        freepColor.Should().Be(freexColor,
            because: $"FreeP '{key}' must be byte-identical to FreeX (shared neutral role)");
    }

    // ── Prefix-keyed brushes are unaffected (regression guard) ───────────────

    [Fact]
    public void WpfApplier_ExistingPrefixedBrushKeys_StillPresent_AfterRound8()
    {
        // Ensure adding neutral keys didn't remove or rename the 21 prefixed keys.
        var dict = WpfThemeApplier.BuildResources(BrandThemes.FreeX, "FreeX");
        var expected = new[]
        {
            "FreeXAccentBrush", "FreeXAccentDarkBrush", "FreeXAccentSoftBrush",
            "FreeXAccentPressedBrush", "FreeXTitleBarBrush", "FreeXTitleBarForegroundBrush", "FreeXTitleBarHoverBrush",
            "FreeXTitleBarPressedBrush", "FreeXTitleBarDisabledBrush",
            "FreeXTitleBarButtonBorderBrush", "FreeXRibbonButtonHoverBrush",
            "FreeXTextBrush", "FreeXMutedTextBrush", "FreeXSubtleTextBrush",
            "FreeXRibbonSurfaceBrush", "FreeXChromeSurfaceBrush", "FreeXSheetSurfaceBrush",
            "FreeXStatusSurfaceBrush", "FreeXStatusForegroundBrush", "FreeXBorderBrush", "FreeXBorderStrongBrush",
            "FreeXDangerBrush", "FreeXWhiteBrush",
        };
        foreach (var key in expected)
            dict.Contains(key).Should().BeTrue(because: $"pre-existing key '{key}' must still be present");
    }
}
