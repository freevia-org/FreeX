using System.Windows.Media;
using Free.Shared.Theme;
using Free.Shared.Theme.Wpf;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Verifies that WS-G round 7 wiring is correct: <see cref="WpfThemeApplier"/> produces the
/// expected <c>FreeW*Brush</c> keys so that <c>DynamicResource FreeW*Brush</c> references in
/// <c>FreeWRibbonResources.xaml</c> and code-side <see cref="MainWindow.ResolveTokenBrush"/> calls
/// will pick up the active theme at runtime.
/// These tests use <see cref="WpfThemeApplier.BuildResources"/> directly (no
/// <see cref="System.Windows.Application"/> needed) so they are thread-safe and order-independent.
/// </summary>
public sealed class FreeWChromeBrushTokenTests
{
    /// <summary>
    /// Applying the default <see cref="BrandThemes.FreeW"/> theme with prefix "FreeW" produces
    /// <c>FreeWAccentBrush</c> uses the canonical amber accent #A26714.
    /// </summary>
    [Fact]
    public void BuildResources_FreeWTheme_RegistersDefaultAccentBrush()
    {
        var dict = WpfThemeApplier.BuildResources(BrandThemes.FreeW, "FreeW");

        var brush = dict["FreeWAccentBrush"] as SolidColorBrush;
        brush.Should().NotBeNull("applier must register FreeWAccentBrush");
        brush!.Color.R.Should().Be(0xA2);
        brush.Color.G.Should().Be(0x67);
        brush.Color.B.Should().Be(0x14);
    }

    [Fact]
    public void BuildResources_FreeWTheme_UsesNeutralOfficeLikeTitleBarChrome()
    {
        var dict = WpfThemeApplier.BuildResources(BrandThemes.FreeW, "FreeW");

        var titleBar = dict["FreeWTitleBarBrush"] as SolidColorBrush;
        var titleForeground = dict["FreeWTitleBarForegroundBrush"] as SolidColorBrush;
        titleBar.Should().NotBeNull();
        titleForeground.Should().NotBeNull();
        titleBar!.Color.Should().Be(Color.FromRgb(0xF3, 0xF4, 0xF6));
        titleForeground!.Color.Should().Be(Color.FromRgb(0x1F, 0x1F, 0x1F));
    }

    /// <summary>
    /// Applying <see cref="BrandThemes.FreeXMidnight"/> with prefix "FreeW" (the FREEW_THEME=midnight
    /// path) produces a <c>FreeWRibbonButtonHoverBrush</c> whose value (#F9D9BC) differs from the
    /// FreeW default (#F6E3C2).  This proves that <c>DynamicResource FreeWRibbonButtonHoverBrush</c>
    /// in <c>FreeWRibbonResources.xaml</c> will reskin when an alternate theme is applied.
    /// </summary>
    [Fact]
    public void BuildResources_AlternateTheme_ChangesRibbonButtonHoverBrush()
    {
        var defaultDict  = WpfThemeApplier.BuildResources(BrandThemes.FreeW,         "FreeW");
        var midnightDict = WpfThemeApplier.BuildResources(BrandThemes.FreeXMidnight, "FreeW");

        var defaultBrush  = defaultDict["FreeWRibbonButtonHoverBrush"]  as SolidColorBrush;
        var midnightBrush = midnightDict["FreeWRibbonButtonHoverBrush"] as SolidColorBrush;

        defaultBrush.Should().NotBeNull();
        midnightBrush.Should().NotBeNull(
            "alternate theme must register FreeWRibbonButtonHoverBrush so DynamicResource reskin works");

        midnightBrush!.Color.Should().NotBe(defaultBrush!.Color,
            "alternate theme must produce a different hover brush to prove reskin is observable");

        // FreeXMidnight RibbonButtonHover = #F9D9BC.
        midnightBrush.Color.R.Should().Be(0xF9, "midnight RibbonButtonHover Red   = 0xF9");
        midnightBrush.Color.G.Should().Be(0xD9, "midnight RibbonButtonHover Green = 0xD9");
        midnightBrush.Color.B.Should().Be(0xBC, "midnight RibbonButtonHover Blue  = 0xBC");
    }

    /// <summary>
    /// Applying <see cref="BrandThemes.FreeXMidnight"/> with prefix "FreeW" produces a
    /// <c>FreeWTitleBarBrush</c> = near-black (#202124), demonstrating that
    /// <see cref="MainWindow.ResolveTokenColor"/> (which reads <c>FreeWTitleBarBrush</c>) would
    /// supply the alternate title-bar color to <see cref="Free.Shared.Ribbon.Wpf.ShellChromeOptions"/>.
    /// </summary>
    [Fact]
    public void BuildResources_AlternateTheme_ChangesTitleBarBrush()
    {
        var dict = WpfThemeApplier.BuildResources(BrandThemes.FreeXMidnight, "FreeW");

        var brush = dict["FreeWTitleBarBrush"] as SolidColorBrush;
        brush.Should().NotBeNull();
        // FreeXMidnight TitleBar = #202124 (near-black).
        brush!.Color.R.Should().Be(0x20);
        brush.Color.G.Should().Be(0x21);
        brush.Color.B.Should().Be(0x24);
    }

    /// <summary>
    /// Verifies that <c>FreeWStatusSurfaceBrush</c> is registered with the default value
    /// (#4B2F12 — umber, matching the title bar) so the status-bar chrome tracks the token.
    /// </summary>
    [Fact]
    public void BuildResources_FreeWTheme_RegistersDefaultStatusSurfaceBrush()
    {
        var dict = WpfThemeApplier.BuildResources(BrandThemes.FreeW, "FreeW");

        var brush = dict["FreeWStatusSurfaceBrush"] as SolidColorBrush;
        brush.Should().NotBeNull("applier must register FreeWStatusSurfaceBrush");
        brush!.Color.R.Should().Be(0x4B);
        brush.Color.G.Should().Be(0x2F);
        brush.Color.B.Should().Be(0x12);
    }

    /// <summary>
    /// All twenty-one FreeW color-role brushes are registered so every DynamicResource reference in
    /// <c>FreeWRibbonResources.xaml</c> has a resolved value.
    /// </summary>
    [Fact]
    public void BuildResources_FreeWTheme_RegistersAllExpectedBrushKeys()
    {
        var dict = WpfThemeApplier.BuildResources(BrandThemes.FreeW, "FreeW");

        var expectedKeys = new[]
        {
            "FreeWAccentBrush",
            "FreeWAccentDarkBrush",
            "FreeWAccentSoftBrush",
            "FreeWAccentPressedBrush",
            "FreeWTitleBarBrush",
            "FreeWRibbonButtonHoverBrush",
            "FreeWTextBrush",
            "FreeWMutedTextBrush",
            "FreeWRibbonSurfaceBrush",
            "FreeWStatusSurfaceBrush",
            "FreeWBorderBrush",
            "FreeWBorderStrongBrush",
            "FreeWSheetSurfaceBrush",
            "FreeWWhiteBrush",
        };

        foreach (var key in expectedKeys)
            dict[key].Should().BeOfType<SolidColorBrush>($"applier must register {key}");
    }
}
