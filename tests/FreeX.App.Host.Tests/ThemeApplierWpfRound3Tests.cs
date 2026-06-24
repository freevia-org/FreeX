using System.Windows;
using System.Windows.Media;
using FluentAssertions;
using Free.Shared.Theme;
using Free.Shared.Theme.Wpf;

namespace FreeX.App.Host.Tests;

/// <summary>
/// WS-G round 3: proves the WPF applier emits typography font-size/weight doubles
/// and the new metric doubles for status bar + title bar caption height.
/// Default values are byte-identical to the captured chrome baseline (2026-06-24).
/// </summary>
public sealed class ThemeApplierWpfRound3Tests
{
    // ── StatusBarText typography resources ────────────────────────────────────

    [Fact]
    public void WpfApplier_FreeXStatusBarTextFontSize_Is_12()
    {
        // Baseline: MainWindow.xaml status bar TextBlocks all use FontSize="12" (now DynamicResource).
        var dict = WpfThemeApplier.BuildResources(BrandThemes.FreeX, "FreeX");
        var size = (double)dict["FreeXStatusBarTextFontSize"];
        size.Should().Be(12.0);
    }

    [Fact]
    public void WpfApplier_FreeXStatusBarTextFontWeight_Is_Normal()
    {
        var dict = WpfThemeApplier.BuildResources(BrandThemes.FreeX, "FreeX");
        var weight = (FontWeight)dict["FreeXStatusBarTextFontWeight"];
        weight.Should().Be(FontWeights.Normal);
    }

    [Fact]
    public void WpfApplier_FreeXStatusBarTextFontFamily_IsPresent()
    {
        // Family is FontFamily.Default when the token FontFamily is empty (inherits system default).
        var dict = WpfThemeApplier.BuildResources(BrandThemes.FreeX, "FreeX");
        dict.Contains("FreeXStatusBarTextFontFamily").Should().BeTrue();
        dict["FreeXStatusBarTextFontFamily"].Should().BeOfType<FontFamily>();
    }

    // ── Metric resources ──────────────────────────────────────────────────────

    [Fact]
    public void WpfApplier_FreeXStatusBarHeight_Is_28()
    {
        // Baseline: Avalonia Height=28 / WPF implicit 28px (Padding 8,3 + FontSize 12)
        var dict = WpfThemeApplier.BuildResources(BrandThemes.FreeX, "FreeX");
        var h = (double)dict["FreeXStatusBarHeight"];
        h.Should().Be(28.0);
    }

    [Fact]
    public void WpfApplier_FreeXTitleBarCaptionHeight_Is_34()
    {
        // Baseline: WPF WindowChrome.CaptionHeight="34" (MainWindow.xaml:25)
        var dict = WpfThemeApplier.BuildResources(BrandThemes.FreeX, "FreeX");
        var h = (double)dict["FreeXTitleBarCaptionHeight"];
        h.Should().Be(34.0);
    }

    // ── Existing 4 metrics still present ─────────────────────────────────────

    [Fact]
    public void WpfApplier_AllSixMetricKeysPresent()
    {
        var dict = WpfThemeApplier.BuildResources(BrandThemes.FreeX, "FreeX");
        var expectedMetrics = new[]
        {
            "FreeXRibbonRowHeight", "FreeXControlHeight", "FreeXIconSize", "FreeXCornerRadius",
            "FreeXStatusBarHeight", "FreeXTitleBarCaptionHeight",
        };
        foreach (var key in expectedMetrics)
            dict.Contains(key).Should().BeTrue(because: $"metric key '{key}' should be present");
    }

    // ── Typography key completeness ───────────────────────────────────────────

    [Fact]
    public void WpfApplier_AllFiveTypographyRoles_HaveThreeKeys()
    {
        var dict = WpfThemeApplier.BuildResources(BrandThemes.FreeX, "FreeX");
        var roles = new[] { "Body", "Caption", "RibbonLabel", "Heading", "StatusBarText" };
        foreach (var role in roles)
        {
            dict.Contains($"FreeX{role}FontFamily").Should().BeTrue(because: $"'{role}FontFamily' should be present");
            dict.Contains($"FreeX{role}FontSize").Should().BeTrue(because: $"'{role}FontSize' should be present");
            dict.Contains($"FreeX{role}FontWeight").Should().BeTrue(because: $"'{role}FontWeight' should be present");
        }
    }

    // ── ToFontWeight helper ───────────────────────────────────────────────────

    [Fact]
    public void WpfApplier_ToFontWeight_Normal_IsNormal()
        => WpfThemeApplier.ToFontWeight(ThemeFontWeight.Normal).Should().Be(FontWeights.Normal);

    [Fact]
    public void WpfApplier_ToFontWeight_SemiBold_IsSemiBold()
        => WpfThemeApplier.ToFontWeight(ThemeFontWeight.SemiBold).Should().Be(FontWeights.SemiBold);

    [Fact]
    public void WpfApplier_ToFontWeight_Bold_IsBold()
        => WpfThemeApplier.ToFontWeight(ThemeFontWeight.Bold).Should().Be(FontWeights.Bold);
}
