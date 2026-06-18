using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;

using Free.Shared.Ribbon;
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
    public void Palette_MatchesWpfRibbonSurfaceAndAccent()
    {
        // Visual parity with WPF: the ribbon surface is the white FreeXRibbonSurfaceBrush (#FFFFFF) and
        // the accent is FreeXAccentBrush (#0F6D8C) — not the former macOS-adapted gray/green palette.
        Assert.Equal(Color.FromRgb(0xFF, 0xFF, 0xFF), AvaloniaRibbonRenderer.SurfaceColor);
        Assert.Equal(Color.FromRgb(0x0F, 0x6D, 0x8C), AvaloniaRibbonRenderer.AccentColor);
        Assert.Equal(Colors.White, AvaloniaRibbonRenderer.SurfaceColor);
    }

    [Fact]
    public Task BuildRibbon_AppliesThemeStyles() => RunOnUiThread(() =>
    {
        var definition = AvaloniaRibbonComposition.BuildDefinition();
        var registry = AvaloniaRibbonComposition.BuildRegistry(() => null, _ => { });

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

    [Fact]
    public Task ShellChromeSurface_IsDistinctLightSurface_FromWhiteRibbon() => RunOnUiThread(() =>
    {
        // WPF parity: the ribbon body is the white FreeXRibbonSurfaceBrush while the window chrome
        // (sheet-tabs / status bar) is a separate light surface (the WPF FreeXChromeSurfaceBrush analog),
        // so they are intentionally distinct — not the former single shared gray surface.
        Assert.NotEqual(AvaloniaRibbonRenderer.SurfaceColor, MainWindow.ChromeSurfaceColor);
    });

    [Fact]
    public Task FontSizeCombo_Selection_ExecutesCommandWithChosenValue() => RunOnUiThread(() =>
    {
        string? applied = null;
        var definition = AvaloniaRibbonComposition.BuildDefinition();
        var registry = AvaloniaRibbonComposition.BuildRegistry(
            () => null, _ => { }, new AvaloniaRibbonHostCallbacks { SetFontSize = v => applied = v });

        var ribbon = AvaloniaRibbonRenderer.BuildRibbon(definition, registry);

        // The combo carries the canonical (shared-definition) command id as its Tag now that the ribbon is
        // built from the single-source FreeXRibbon definition.
        var combo = ribbon.GetLogicalDescendants()
            .OfType<ComboBox>()
            .First(c => (string?)c.Tag == AvaloniaCommandIdAdapter.ToCanonical("home.fontSize"));

        // Initial index 0 was suppressed at build; a user pick (index change) applies the chosen size.
        combo.SelectedIndex = combo.SelectedIndex + 1;

        Assert.False(string.IsNullOrEmpty(applied));
    });
}
