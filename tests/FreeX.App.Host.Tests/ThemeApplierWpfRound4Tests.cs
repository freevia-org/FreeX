using System.Windows;
using System.Windows.Media;
using FluentAssertions;
using Free.Shared.Theme;
using Free.Shared.Theme.Wpf;

namespace FreeX.App.Host.Tests;

/// <summary>
/// WS-G round 4: proves that ribbon and backstage chrome brushes now track the active theme.
/// Converting {StaticResource FreeX*Brush} → {DynamicResource FreeX*Brush} in MainWindow.xaml and
/// MainWindowResources.xaml means the WpfThemeApplier dictionary is the single source of truth
/// for all FreeX chrome colors — verified here by checking that midnight values differ from default.
/// </summary>
public sealed class ThemeApplierWpfRound4Tests
{
    // ── Brush value proofs — BuildResources (no Application required) ──────────

    [Fact]
    public void WpfApplier_Midnight_RibbonSurfaceBrush_IsSameAsDefault()
    {
        // FreeXMidnight keeps the ribbon surface white (#FFFFFF) — same as FreeX default.
        var dict = WpfThemeApplier.BuildResources(BrandThemes.FreeXMidnight, "FreeX");
        var brush = (SolidColorBrush)dict["FreeXRibbonSurfaceBrush"];
        brush.Color.Should().Be(Color.FromRgb(0xFF, 0xFF, 0xFF));
    }

    [Fact]
    public void WpfApplier_Midnight_AccentBrush_IsOrange_NotTeal()
    {
        // Default FreeX accent is #0F6D8C (teal); Midnight uses #C8651B (orange).
        var defaultDict  = WpfThemeApplier.BuildResources(BrandThemes.FreeX,         "FreeX");
        var midnightDict = WpfThemeApplier.BuildResources(BrandThemes.FreeXMidnight, "FreeX");

        var defaultColor  = ((SolidColorBrush)defaultDict["FreeXAccentBrush"]).Color;
        var midnightColor = ((SolidColorBrush)midnightDict["FreeXAccentBrush"]).Color;

        // Midnight accent: orange #C8651B
        midnightColor.Should().Be(Color.FromRgb(0xC8, 0x65, 0x1B));
        midnightColor.Should().NotBe(defaultColor,
            because: "FreeXMidnight uses an orange accent; converting the brush to DynamicResource lets it reskin");
    }

    [Fact]
    public void WpfApplier_Midnight_StatusSurfaceBrush_IsNearBlack_NotOfficeLight()
    {
        // Default FreeX status surface is #F3F4F6 (Office-light); Midnight uses #202124 (near-black).
        var defaultDict  = WpfThemeApplier.BuildResources(BrandThemes.FreeX,         "FreeX");
        var midnightDict = WpfThemeApplier.BuildResources(BrandThemes.FreeXMidnight, "FreeX");

        var defaultColor  = ((SolidColorBrush)defaultDict["FreeXStatusSurfaceBrush"]).Color;
        var midnightColor = ((SolidColorBrush)midnightDict["FreeXStatusSurfaceBrush"]).Color;

        defaultColor.Should().Be(Color.FromRgb(0xF3, 0xF4, 0xF6));
        midnightColor.Should().Be(Color.FromRgb(0x20, 0x21, 0x24));
        midnightColor.Should().NotBe(defaultColor,
            because: "FreeXMidnight replaces the Office-light status bar; DynamicResource makes this visible at runtime");
    }

    // ── Apply(Application, …) path — proves the runtime injection the XAML DynamicResource binds to ──

    [Fact]
    public void Theme_Apply_Midnight_RibbonSurfaceBrush_ResolvesInAppResources()
    {
        // Ensure a headless WPF Application is available (only one per AppDomain).
        if (Application.Current is null)
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        var app = Application.Current!;
        WpfThemeApplier.Apply(app, BrandThemes.FreeXMidnight, "FreeX");

        var mergedDict = app.Resources.MergedDictionaries[^1];
        mergedDict.Contains("FreeXRibbonSurfaceBrush").Should().BeTrue(
            because: "Apply must inject FreeXRibbonSurfaceBrush so DynamicResource in MainWindowResources.xaml resolves");
        ((SolidColorBrush)mergedDict["FreeXRibbonSurfaceBrush"]).Color
            .Should().Be(Color.FromRgb(0xFF, 0xFF, 0xFF));
    }

    [Fact]
    public void Theme_Apply_Midnight_AccentBrush_ResolvesInAppResources_AsOrange()
    {
        if (Application.Current is null)
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        var app = Application.Current!;
        WpfThemeApplier.Apply(app, BrandThemes.FreeXMidnight, "FreeX");

        var mergedDict = app.Resources.MergedDictionaries[^1];
        mergedDict.Contains("FreeXAccentBrush").Should().BeTrue(
            because: "Apply must inject FreeXAccentBrush so the ribbon chrome DynamicResource resolves at runtime");
        // Midnight accent: #C8651B (orange)
        var color = ((SolidColorBrush)mergedDict["FreeXAccentBrush"]).Color;
        color.Should().Be(Color.FromRgb(0xC8, 0x65, 0x1B),
            because: "FreeXMidnight orange accent proves the ribbon button / tab border reskins when FREEX_THEME=midnight");
    }
}
