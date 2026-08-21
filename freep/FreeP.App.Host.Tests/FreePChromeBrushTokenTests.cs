using System.Windows.Media;
using Free.Shared.Theme;
using Free.Shared.Theme.Wpf;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Verifies that WS-G round 10 wiring is correct: <see cref="WpfThemeApplier"/> produces the
/// expected <c>FreeP*Brush</c> keys so that code-side <c>MainWindow.ResolveTokenColor</c> /
/// <c>ResolveTokenBrush</c> calls will pick up the active theme at runtime.
/// These tests use <see cref="WpfThemeApplier.BuildResources"/> directly (no
/// <see cref="System.Windows.Application"/> needed) so they are thread-safe and order-independent.
/// </summary>
public sealed class FreePChromeBrushTokenTests
{
    [Theory]
    [InlineData("FreePAccentBrush", 0xA2, 0x3B, 0x72)]
    [InlineData("FreePAccentDarkBrush", 0x4E, 0x21, 0x3B)]
    [InlineData("FreePSheetSurfaceBrush", 0xF3, 0xF3, 0xF3)]
    [InlineData("FreePWhiteBrush", 0xFF, 0xFF, 0xFF)]
    public void BuildResources_RegistersRendererConsumedBrushes(
        string key,
        byte red,
        byte green,
        byte blue)
    {
        var dict = WpfThemeApplier.BuildResources(BrandThemes.FreeP, "FreeP");

        var brush = dict[key].Should().BeOfType<SolidColorBrush>().Subject;
        brush.Color.Should().Be(Color.FromRgb(red, green, blue));
    }

    /// <summary>
    /// Applying the default <see cref="BrandThemes.FreeP"/> theme with prefix "FreeP" produces
    /// <c>FreePTitleBarBrush</c> uses the canonical plum #4E213B.
    /// </summary>
    [Fact]
    public void BuildResources_FreePTheme_RegistersDefaultTitleBarBrush()
    {
        var dict = WpfThemeApplier.BuildResources(BrandThemes.FreeP, "FreeP");

        var brush = dict["FreePTitleBarBrush"] as SolidColorBrush;
        brush.Should().NotBeNull("applier must register FreePTitleBarBrush");
        brush!.Color.R.Should().Be(0x4E);
        brush.Color.G.Should().Be(0x21);
        brush.Color.B.Should().Be(0x3B);
    }

    /// <summary>
    /// Applying the default <see cref="BrandThemes.FreeP"/> theme produces
    /// <c>FreePStatusSurfaceBrush</c> uses the canonical plum #4E213B.
    /// </summary>
    [Fact]
    public void BuildResources_FreePTheme_RegistersDefaultStatusSurfaceBrush()
    {
        var dict = WpfThemeApplier.BuildResources(BrandThemes.FreeP, "FreeP");

        var brush = dict["FreePStatusSurfaceBrush"] as SolidColorBrush;
        brush.Should().NotBeNull("applier must register FreePStatusSurfaceBrush");
        brush!.Color.R.Should().Be(0x4E);
        brush.Color.G.Should().Be(0x21);
        brush.Color.B.Should().Be(0x3B);
    }

    /// <summary>
    /// Applying an alternate theme with prefix "FreeP" changes <c>FreePTitleBarBrush</c> and
    /// <c>FreePStatusSurfaceBrush</c> away from the brick defaults, demonstrating that the
    /// chrome reskins under a different theme.
    /// Uses <see cref="BrandThemes.FreeXMidnight"/> as a convenient alternate with clearly
    /// different TitleBar (#202124) and StatusSurface (#202124) values.
    /// </summary>
    [Fact]
    public void BuildResources_AlternateTheme_ChangesTitleBarAndStatusBrushes()
    {
        var defaultDict  = WpfThemeApplier.BuildResources(BrandThemes.FreeP,         "FreeP");
        var midnightDict = WpfThemeApplier.BuildResources(BrandThemes.FreeXMidnight, "FreeP");

        var defaultTitle   = defaultDict["FreePTitleBarBrush"]       as SolidColorBrush;
        var midnightTitle  = midnightDict["FreePTitleBarBrush"]      as SolidColorBrush;
        var defaultStatus  = defaultDict["FreePStatusSurfaceBrush"]  as SolidColorBrush;
        var midnightStatus = midnightDict["FreePStatusSurfaceBrush"] as SolidColorBrush;

        defaultTitle.Should().NotBeNull();
        midnightTitle.Should().NotBeNull(
            "alternate theme must register FreePTitleBarBrush so reskin works");

        midnightTitle!.Color.Should().NotBe(defaultTitle!.Color,
            "alternate theme must produce a different title bar brush to prove reskin is observable");

        defaultStatus.Should().NotBeNull();
        midnightStatus.Should().NotBeNull(
            "alternate theme must register FreePStatusSurfaceBrush so reskin works");

        midnightStatus!.Color.Should().NotBe(defaultStatus!.Color,
            "alternate theme must produce a different status surface brush to prove reskin is observable");
    }
}
