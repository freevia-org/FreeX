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
    private static readonly (string Alias, string Prefixed)[] SharedAliases =
    [
        ("ThemeNeutralTextBrush", "TextBrush"),
        ("ThemeNeutralMutedTextBrush", "MutedTextBrush"),
        ("ThemeNeutralWhiteBrush", "WhiteBrush"),
        ("ThemeNeutralDangerBrush", "DangerBrush"),
        ("ThemeNeutralSheetSurfaceBrush", "SheetSurfaceBrush"),
        ("ThemeNeutralBorderBrush", "BorderBrush"),
        ("ThemeNeutralBorderStrongBrush", "BorderStrongBrush"),
        ("ThemeAccentBrush", "AccentBrush"),
        ("ThemeAccentDarkBrush", "AccentDarkBrush"),
        ("ThemeAccentSoftBrush", "AccentSoftBrush"),
        ("ThemeAccentPressedBrush", "AccentPressedBrush"),
        ("ThemeRibbonButtonHoverBrush", "RibbonButtonHoverBrush"),
    ];

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

    // ── Status surface / Avalonia registration tests ───────────────────────────

    [Fact]
    public void AvaloniaApplier_FreeXStatusSurfaceBrush_FreeX_IsCorrectColor()
    {
        // FreeX default: StatusSurface = #17324D (same navy as the title bar)
        var dict = AvaloniaThemeApplier.BuildResources(BrandThemes.FreeX, "FreeX");
        var brush = (ImmutableSolidColorBrush)dict["FreeXStatusSurfaceBrush"]!;
        brush.Color.A.Should().Be(255);
        brush.Color.R.Should().Be(0x17);
        brush.Color.G.Should().Be(0x32);
        brush.Color.B.Should().Be(0x4D);
    }

    [Fact]
    public void AvaloniaApplier_FreeXStatusSurfaceBrush_Midnight_DiffersFromDefault()
    {
        var defaultDict  = AvaloniaThemeApplier.BuildResources(BrandThemes.FreeX,         "FreeX");
        var midnightDict = AvaloniaThemeApplier.BuildResources(BrandThemes.FreeXMidnight, "FreeX");

        var defaultColor  = ((ImmutableSolidColorBrush)defaultDict["FreeXStatusSurfaceBrush"]!).Color;
        var midnightColor = ((ImmutableSolidColorBrush)midnightDict["FreeXStatusSurfaceBrush"]!).Color;
        midnightColor.Should().NotBe(defaultColor,
            because: "FreeXMidnight has a near-black status surface, not the default navy");
    }

    [Theory]
    [InlineData("FreeX")]
    [InlineData("FreeW")]
    [InlineData("FreeP")]
    public void AvaloniaApplier_EmitsWpfCompatibleSharedAliases(string product)
    {
        var theme = product switch
        {
            "FreeW" => BrandThemes.FreeW,
            "FreeP" => BrandThemes.FreeP,
            _ => BrandThemes.FreeX,
        };
        var dict = AvaloniaThemeApplier.BuildResources(theme, product);

        foreach (var (alias, prefixed) in SharedAliases)
        {
            var aliasBrush = dict[alias].Should().BeOfType<ImmutableSolidColorBrush>().Subject;
            var productBrush = dict[product + prefixed].Should().BeOfType<ImmutableSolidColorBrush>().Subject;
            aliasBrush.Color.Should().Be(productBrush.Color);
        }
    }

    [Fact]
    public void AvaloniaApplier_ApplyMergesGeneratedResourcesLast()
    {
        var target = new global::Avalonia.Controls.ResourceDictionary();
        var existing = new global::Avalonia.Controls.ResourceDictionary
        {
            ["ThemeAccentBrush"] = Brushes.Black,
        };
        target.MergedDictionaries.Add(existing);

        AvaloniaThemeApplier.Apply(target, BrandThemes.FreeX, "FreeX");

        target.MergedDictionaries.Should().HaveCount(2);
        var applied = (global::Avalonia.Controls.ResourceDictionary)target.MergedDictionaries[1];
        ((ImmutableSolidColorBrush)applied["ThemeAccentBrush"]!).Color
            .Should().Be(AvaloniaThemeApplier.ToColor(BrandThemes.FreeX.Colors.Accent));
    }
}
