using System.Windows;
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

    // ── Apply(Application, …) tests ────────────────────────────────────────────
    // These verify the runtime path used at startup: Apply merges the theme dict into
    // Application.Resources.MergedDictionaries so the keys are resolvable via DynamicResource.
    // WPF allows at most one Application per AppDomain — tests share the singleton.

    [Fact]
    public void Theme_Apply_FreeX_TitleBarBrush_InAppResources_IsCorrectColor()
    {
        // Ensure a headless WPF Application is available (only one per AppDomain).
        if (Application.Current is null)
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        var app = Application.Current!;
        WpfThemeApplier.Apply(app, BrandThemes.FreeX, "FreeX");

        // Retrieve from the merged-dict entries added by Apply (last added).
        var mergedDict = app.Resources.MergedDictionaries[^1];
        var brush = (SolidColorBrush)mergedDict["FreeXTitleBarBrush"];
        // Default FreeX theme: #17324D — byte-identical to ThemeResources.xaml
        brush.Color.Should().Be(Color.FromRgb(0x17, 0x32, 0x4D));
    }

    [Fact]
    public void Theme_Apply_FreeXMidnight_TitleBarBrush_InAppResources_DiffersFromDefault()
    {
        if (Application.Current is null)
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        var app = Application.Current!;
        // Apply default then midnight — the two last merged-dict entries will have the values.
        WpfThemeApplier.Apply(app, BrandThemes.FreeX,         "FreeX");
        WpfThemeApplier.Apply(app, BrandThemes.FreeXMidnight, "FreeX");

        var count = app.Resources.MergedDictionaries.Count;
        var defaultColor  = ((SolidColorBrush)app.Resources.MergedDictionaries[count - 2]["FreeXTitleBarBrush"]).Color;
        var midnightColor = ((SolidColorBrush)app.Resources.MergedDictionaries[count - 1]["FreeXTitleBarBrush"]).Color;
        midnightColor.Should().NotBe(defaultColor,
            because: "FreeXMidnight has a near-black title bar, not the default navy");
    }

    // ── Apply(ResourceDictionary, …) tests ────────────────────────────────────
    // These verify the Window.Resources injection path used in MainWindow constructor.

    [Fact]
    public void Theme_Apply_ResourceDictionary_FreeX_TitleBarBrush_IsCorrectColor()
    {
        var dict = new ResourceDictionary();
        WpfThemeApplier.Apply(dict, BrandThemes.FreeX, "FreeX");

        // The key is added to a merged-dict entry; look it up via MergedDictionaries[0].
        var inner = dict.MergedDictionaries[0];
        inner.Contains("FreeXTitleBarBrush").Should().BeTrue();
        ((SolidColorBrush)inner["FreeXTitleBarBrush"]).Color.Should().Be(Color.FromRgb(0x17, 0x32, 0x4D));
    }

    [Fact]
    public void Theme_Apply_ResourceDictionary_FreeXMidnight_TitleBarBrush_DiffersFromDefault()
    {
        var freexDict    = new ResourceDictionary();
        var midnightDict = new ResourceDictionary();
        WpfThemeApplier.Apply(freexDict,    BrandThemes.FreeX,         "FreeX");
        WpfThemeApplier.Apply(midnightDict, BrandThemes.FreeXMidnight, "FreeX");

        var freexColor    = ((SolidColorBrush)freexDict.MergedDictionaries[0]["FreeXTitleBarBrush"]).Color;
        var midnightColor = ((SolidColorBrush)midnightDict.MergedDictionaries[0]["FreeXTitleBarBrush"]).Color;
        midnightColor.Should().NotBe(freexColor);
    }
}
