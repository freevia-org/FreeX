using System.Windows.Media;
using FluentAssertions;
using Free.Shared.Theme;
using Free.Shared.Theme.Wpf;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Proves the WPF theme applier produces brushes that are byte-identical to
/// the values in <c>ThemeResources.xaml</c>.
/// </summary>
public sealed class ThemeApplierWpfTests
{
    [Fact]
    public void WpfApplier_FreeXTitleBarBrush_MatchesThemeResourcesXaml()
    {
        // ThemeResources.xaml: FreeXTitleBarBrush Color="#17324D"
        var dict = WpfThemeApplier.BuildResources(BrandThemes.FreeX, "FreeX");
        var brush = (SolidColorBrush)dict["FreeXTitleBarBrush"];
        brush.Color.Should().Be(Color.FromRgb(0x17, 0x32, 0x4D));
    }

    [Fact]
    public void WpfApplier_FreeXAccentBrush_MatchesThemeResourcesXaml()
    {
        // ThemeResources.xaml: FreeXAccentBrush Color="#0F6D8C"
        var dict = WpfThemeApplier.BuildResources(BrandThemes.FreeX, "FreeX");
        var brush = (SolidColorBrush)dict["FreeXAccentBrush"];
        brush.Color.Should().Be(Color.FromRgb(0x0F, 0x6D, 0x8C));
    }

    [Fact]
    public void WpfApplier_FreeXTitleBarButtonBorderBrush_HasCorrectAlpha()
    {
        // ThemeResources.xaml: FreeXTitleBarButtonBorderBrush Color="#55FFFFFF"
        var dict = WpfThemeApplier.BuildResources(BrandThemes.FreeX, "FreeX");
        var brush = (SolidColorBrush)dict["FreeXTitleBarButtonBorderBrush"];
        brush.Color.A.Should().Be(0x55);
        brush.Color.R.Should().Be(0xFF);
        brush.Color.G.Should().Be(0xFF);
        brush.Color.B.Should().Be(0xFF);
    }

    [Fact]
    public void WpfApplier_AllTwentyOneBrushKeysPresent()
    {
        var dict = WpfThemeApplier.BuildResources(BrandThemes.FreeX, "FreeX");
        var expected = new[]
        {
            "FreeXAccentBrush", "FreeXAccentDarkBrush", "FreeXAccentSoftBrush",
            "FreeXAccentPressedBrush", "FreeXTitleBarBrush", "FreeXTitleBarHoverBrush",
            "FreeXTitleBarPressedBrush", "FreeXTitleBarDisabledBrush",
            "FreeXTitleBarButtonBorderBrush", "FreeXRibbonButtonHoverBrush",
            "FreeXTextBrush", "FreeXMutedTextBrush", "FreeXSubtleTextBrush",
            "FreeXRibbonSurfaceBrush", "FreeXChromeSurfaceBrush", "FreeXSheetSurfaceBrush",
            "FreeXStatusSurfaceBrush", "FreeXBorderBrush", "FreeXBorderStrongBrush",
            "FreeXDangerBrush", "FreeXWhiteBrush"
        };
        foreach (var key in expected)
        {
            dict.Contains(key).Should().BeTrue(because: $"key '{key}' should be present");
        }
    }

    [Fact]
    public void WpfApplier_MidnightAccentBrush_DiffersFromFreeXAccentBrush()
    {
        var freexDict    = WpfThemeApplier.BuildResources(BrandThemes.FreeX,         "FreeX");
        var midnightDict = WpfThemeApplier.BuildResources(BrandThemes.FreeXMidnight, "FreeX");

        var freexAccent    = ((SolidColorBrush)freexDict["FreeXAccentBrush"]).Color;
        var midnightAccent = ((SolidColorBrush)midnightDict["FreeXAccentBrush"]).Color;
        midnightAccent.Should().NotBe(freexAccent);
    }
}
