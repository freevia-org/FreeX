using Avalonia.Media;
using Avalonia.Media.Immutable;
using Free.Shared.Theme;
using Free.Shared.Theme.Avalonia;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Proves the Avalonia theme applier produces correct colors for FreeX's brand palette.
/// </summary>
public sealed class ThemeApplierAvaloniaTests
{
    [Fact]
    public void AvaloniaApplier_FreeXAccentBrush_MatchesPalette()
    {
        // FreeXAccentBrush should be #0F6D8C (A=255)
        var dict = AvaloniaThemeApplier.BuildResources(BrandThemes.FreeX, "FreeX");
        var brush = (ImmutableSolidColorBrush)dict["FreeXAccentBrush"]!;
        brush.Color.A.Should().Be(255);
        brush.Color.R.Should().Be(0x0F);
        brush.Color.G.Should().Be(0x6D);
        brush.Color.B.Should().Be(0x8C);
    }

    [Fact]
    public void AvaloniaApplier_FreeXTitleBarBrush_MatchesPalette()
    {
        // FreeXTitleBarBrush should be #17324D (A=255)
        var dict = AvaloniaThemeApplier.BuildResources(BrandThemes.FreeX, "FreeX");
        var brush = (ImmutableSolidColorBrush)dict["FreeXTitleBarBrush"]!;
        brush.Color.A.Should().Be(255);
        brush.Color.R.Should().Be(0x17);
        brush.Color.G.Should().Be(0x32);
        brush.Color.B.Should().Be(0x4D);
    }

    [Fact]
    public void AvaloniaApplier_ToColor_RoundTripsAlpha()
    {
        var tc = ThemeColor.FromHex("#55FFFFFF");
        var avColor = AvaloniaThemeApplier.ToColor(tc);
        avColor.A.Should().Be(0x55);
        avColor.R.Should().Be(0xFF);
        avColor.G.Should().Be(0xFF);
        avColor.B.Should().Be(0xFF);
    }
}
