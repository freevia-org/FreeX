using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;

using Free.Shared.Ribbon;
using FreeX.App.Avalonia.Ribbon;
using FreeX.App.Presentation.Ribbon;
using FreeX.Ribbon.Definitions;
using Free.Shared.Ribbon.Avalonia;
using Free.Shared.Theme;
using Free.Shared.Theme.Avalonia;

using Xunit;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Guards the ribbon visual theme: the brand-accent / surface palette, and that <c>BuildRibbon</c> applies
/// the theme styles (active-tab accent + button hover) to the tab control. Appearance specifics are visual,
/// but the palette values and that the styles are wired are pinned here so a regression is caught. Control-
/// building tests run on the headless UI thread (icon geometry needs the render interface).
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class AvaloniaRibbonThemeTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    private static Task RunOnUiThread(Action action) =>
        Session.Dispatch(action, CancellationToken.None);

    public static IEnumerable<object[]> BrandThemeCases()
    {
        yield return new object[] { BrandThemes.FreeX };
        yield return new object[] { BrandThemes.FreeW };
        yield return new object[] { BrandThemes.FreeP };
        yield return new object[] { BrandThemes.FreeXMidnight };
    }

    [Theory]
    [MemberData(nameof(BrandThemeCases))]
    public void Palette_DerivesEveryVisualRoleFromBrandTheme(Theme theme)
    {
        var palette = AvaloniaRibbonRenderer.ResolvePalette(RibbonVisualPalette.FromTheme(theme));
        var colors = theme.Colors;

        Assert.Equal(AvaloniaThemeApplier.ToColor(colors.RibbonSurface), palette.SurfaceColor);
        Assert.Equal(AvaloniaThemeApplier.ToColor(colors.Accent), palette.AccentColor);
        Assert.Equal(AvaloniaThemeApplier.ToColor(colors.Border), palette.DividerColor);
        Assert.Equal(AvaloniaThemeApplier.ToColor(colors.RibbonInlineDivider), palette.InlineDividerColor);
        Assert.Equal(AvaloniaThemeApplier.ToColor(colors.MutedText), palette.GroupLabelColor);
        Assert.Equal(AvaloniaThemeApplier.ToColor(colors.RibbonButtonHover), palette.HoverColor);
        Assert.Equal(AvaloniaThemeApplier.ToColor(colors.BorderStrong), palette.HoverBorderColor);
        Assert.Equal(AvaloniaThemeApplier.ToColor(colors.AccentPressed), palette.CheckedColor);
        Assert.Equal(AvaloniaThemeApplier.ToColor(colors.AccentSoft), palette.TabHoverColor);
        Assert.Equal(AvaloniaThemeApplier.ToColor(colors.ChromeSurface), palette.TabStripColor);
        Assert.Equal(AvaloniaThemeApplier.ToColor(colors.Text), palette.TabTextColor);
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
        var ribbonSurface = AvaloniaRibbonRenderer
            .ResolvePalette(RibbonVisualPalette.FromTheme(BrandThemes.FreeX))
            .SurfaceColor;
        Assert.NotEqual(ribbonSurface, MainWindow.ChromeSurfaceColor);
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
            .First(c => (string?)c.Tag == FreeXRibbonCommandCatalog.GetRequired("Font Size").Value);

        // Initial index 0 was suppressed at build; a user pick (index change) applies the chosen size.
        combo.SelectedIndex = combo.SelectedIndex + 1;

        Assert.False(string.IsNullOrEmpty(applied));
    });
}
