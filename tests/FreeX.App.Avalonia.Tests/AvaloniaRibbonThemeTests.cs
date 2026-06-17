using System.Threading;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;

using FreeX.App.Avalonia.Ribbon;
using FreeX.Ribbon.Avalonia;

using Xunit;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Guards the ribbon visual theme: the brand-accent / surface palette, and that <c>BuildRibbon</c> applies
/// the theme styles (active-tab accent + button hover) to the tab control. Appearance specifics are visual,
/// but the palette values and that the styles are wired are pinned here so a regression is caught. Control-
/// building tests run on the headless UI thread (icon geometry needs the render interface).
/// </summary>
public sealed class AvaloniaRibbonThemeTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    private static Task RunOnUiThread(Action action) =>
        Session.Dispatch(action, CancellationToken.None);

    [Fact]
    public void Palette_UsesPolishedSurfaceAndBrandAccent()
    {
        // Light, low-contrast ribbon surface (not pure white) with the workbook brand-green accent.
        Assert.Equal(Color.FromRgb(0xF5, 0xF6, 0xF7), AvaloniaRibbonRenderer.SurfaceColor);
        Assert.Equal(Color.FromRgb(0x21, 0x73, 0x46), AvaloniaRibbonRenderer.AccentColor);
        Assert.NotEqual(Colors.White, AvaloniaRibbonRenderer.SurfaceColor);
    }

    [Fact]
    public Task BuildRibbon_AppliesThemeStyles() => RunOnUiThread(() =>
    {
        var definition = SampleRibbon.BuildDefinition();
        var registry = SampleRibbon.BuildRegistry(() => null, _ => { });

        var control = AvaloniaRibbonRenderer.BuildRibbon(definition, registry);

        var tabControl = Assert.IsType<TabControl>(control);
        // active-tab accent + button hover + toggle hover.
        Assert.True(tabControl.Styles.Count >= 3);
    });

    [Fact]
    public Task ApplyRibbonTheme_AddsStylesToTabControl() => RunOnUiThread(() =>
    {
        var tabControl = new TabControl();

        AvaloniaRibbonRenderer.ApplyRibbonTheme(tabControl);

        Assert.True(tabControl.Styles.Count >= 3);
    });
}
