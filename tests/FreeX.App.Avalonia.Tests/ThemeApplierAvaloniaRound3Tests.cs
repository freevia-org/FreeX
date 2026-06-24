using Avalonia.Media;
using FluentAssertions;
using Free.Shared.Theme;
using Free.Shared.Theme.Avalonia;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// WS-G round 3: proves the Avalonia applier emits typography font-size/weight resources
/// and the new metric doubles for status bar height.
/// Default values are byte-identical to the captured chrome baseline (2026-06-24).
/// </summary>
public sealed class ThemeApplierAvaloniaRound3Tests
{
    // ── StatusBarText typography resources ────────────────────────────────────

    [Fact]
    public void AvaloniaApplier_FreeXStatusBarTextFontSize_Is_12()
    {
        // Baseline: Avalonia MainWindow.cs BuildStatusBar sets FontSize=12 for status texts.
        var dict = AvaloniaThemeApplier.BuildResources(BrandThemes.FreeX, "FreeX");
        var size = (double)dict["FreeXStatusBarTextFontSize"]!;
        size.Should().Be(12.0);
    }

    [Fact]
    public void AvaloniaApplier_FreeXStatusBarTextFontWeight_Is_Normal()
    {
        var dict = AvaloniaThemeApplier.BuildResources(BrandThemes.FreeX, "FreeX");
        var weight = (FontWeight)dict["FreeXStatusBarTextFontWeight"]!;
        weight.Should().Be(FontWeight.Normal);
    }

    [Fact]
    public void AvaloniaApplier_FreeXStatusBarTextFontFamily_IsPresent()
    {
        var dict = AvaloniaThemeApplier.BuildResources(BrandThemes.FreeX, "FreeX");
        dict["FreeXStatusBarTextFontFamily"].Should().BeOfType<FontFamily>();
    }

    // ── Metric resources ──────────────────────────────────────────────────────

    [Fact]
    public void AvaloniaApplier_FreeXStatusBarHeight_Is_28()
    {
        // Baseline: Avalonia MainWindow.cs:3388 Height=28 (MATCHED with WPF implicit height)
        var dict = AvaloniaThemeApplier.BuildResources(BrandThemes.FreeX, "FreeX");
        var h = (double)dict["FreeXStatusBarHeight"]!;
        h.Should().Be(28.0);
    }

    [Fact]
    public void AvaloniaApplier_FreeXTitleBarCaptionHeight_IsPresent()
    {
        // Emitted for symmetric key-set parity with WPF; not consumed by Avalonia chrome
        // (native OS title bar). Value should match WPF baseline (34).
        var dict = AvaloniaThemeApplier.BuildResources(BrandThemes.FreeX, "FreeX");
        ((double)dict["FreeXTitleBarCaptionHeight"]!).Should().Be(34.0);
    }

    // ── All six metric keys present ───────────────────────────────────────────

    [Fact]
    public void AvaloniaApplier_AllSixMetricKeysPresent()
    {
        var dict = AvaloniaThemeApplier.BuildResources(BrandThemes.FreeX, "FreeX");
        var expectedMetrics = new[]
        {
            "FreeXRibbonRowHeight", "FreeXControlHeight", "FreeXIconSize", "FreeXCornerRadius",
            "FreeXStatusBarHeight", "FreeXTitleBarCaptionHeight",
        };
        // ResourceDictionary indexer throws on missing key — non-null proves presence.
        foreach (var key in expectedMetrics)
            dict[key].Should().NotBeNull(because: $"metric key '{key}' should be present");
    }

    // ── Typography key completeness ───────────────────────────────────────────

    [Fact]
    public void AvaloniaApplier_AllFiveTypographyRoles_HaveThreeKeys()
    {
        var dict = AvaloniaThemeApplier.BuildResources(BrandThemes.FreeX, "FreeX");
        var roles = new[] { "Body", "Caption", "RibbonLabel", "Heading", "StatusBarText" };
        foreach (var role in roles)
        {
            // ResourceDictionary indexer throws on missing key — accessing proves presence.
            dict[$"FreeX{role}FontFamily"].Should().NotBeNull(because: $"'{role}FontFamily' should be present");
            dict[$"FreeX{role}FontSize"].Should().NotBeNull(because: $"'{role}FontSize' should be present");
            dict[$"FreeX{role}FontWeight"].Should().NotBeNull(because: $"'{role}FontWeight' should be present");
        }
    }

    // ── ToFontWeight helper ───────────────────────────────────────────────────

    [Fact]
    public void AvaloniaApplier_ToFontWeight_Normal_IsNormal()
        => AvaloniaThemeApplier.ToFontWeight(ThemeFontWeight.Normal).Should().Be(FontWeight.Normal);

    [Fact]
    public void AvaloniaApplier_ToFontWeight_SemiBold_IsSemiBold()
        => AvaloniaThemeApplier.ToFontWeight(ThemeFontWeight.SemiBold).Should().Be(FontWeight.SemiBold);

    [Fact]
    public void AvaloniaApplier_ToFontWeight_Bold_IsBold()
        => AvaloniaThemeApplier.ToFontWeight(ThemeFontWeight.Bold).Should().Be(FontWeight.Bold);
}
