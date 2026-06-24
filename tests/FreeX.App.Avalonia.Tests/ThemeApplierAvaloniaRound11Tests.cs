using Avalonia.Media;
using Avalonia.Media.Immutable;
using FluentAssertions;
using Free.Shared.Theme;
using Free.Shared.Theme.Avalonia;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// WS-G round 11 (FreeX-Avalonia chrome tokens).
///
/// Verifies that:
/// (a) The Avalonia applier emits the chrome brush tokens consumed by the Avalonia chrome surfaces.
/// (b) The token values are byte-identical to the existing inline literals (so the default appearance
///     is unchanged when <c>FREEX_THEME</c> is not set).
/// (c) The token-fallback path in <see cref="MainWindow"/> returns the matching literal color when no
///     Application is running (unit-test guard).
///
/// MATCH set (tokenized in this round):
///   ChromeSurface  = FreeXChromeSurfaceBrush  / FreeXChromeSurfaceColor  (#F7F8F8)
///   SheetTabContour = FreeXAccentBrush         (#0F6D8C)
///   CheckedCommand  = FreeXAccentSoftBrush     (#E6F6FA)
///   StatusBarFore   = FreeXWhiteBrush          (#FFFFFF)
///   DialogAccent    = FreeXAccentBrush         (#0F6D8C)
///   DialogSelFore   = FreeXTextBrush           (#1F1F1F)
///
/// DIVERGENT set (NOT changed; documented in docs/parity/theme-token-baseline.md):
///   WindowBackground (#F6F7F9) — no matching token role
///   ToolbarBorder    (#DADES4 / RGB 218,222,228) — Border token is #DADCE0 (218,220,224); NOT byte-identical
///   PrimaryInk       (#191F28 / RGB 25,31,40)  — Text token is #1F1F1F (31,31,31); NOT byte-identical
///   SecondaryInk     (#5E6774 / RGB 94,103,116) — MutedText is #5F6368 (95,99,104); NOT byte-identical
///   DialogBorderBrush (#ABABAB) — no matching token role
///   SelectionBrush   (AccentSoft@alpha 0x40) — no standalone token role
/// </summary>
public sealed class ThemeApplierAvaloniaRound11Tests
{
    // ── Token presence + value (the applier emits the resources MainWindow reads) ──────────────────

    [Fact]
    public void AvaloniaApplier_FreeXChromeSurfaceBrush_Is_F7F8F8()
    {
        // Byte-identical to MainWindow.ChromeSurfaceColor literal (0xF7,0xF8,0xF8).
        var dict = AvaloniaThemeApplier.BuildResources(BrandThemes.FreeX, "FreeX");
        var brush = (ImmutableSolidColorBrush)dict["FreeXChromeSurfaceBrush"]!;
        brush.Color.A.Should().Be(255);
        brush.Color.R.Should().Be(0xF7);
        brush.Color.G.Should().Be(0xF8);
        brush.Color.B.Should().Be(0xF8);
    }

    [Fact]
    public void AvaloniaApplier_FreeXChromeSurfaceColor_Is_F7F8F8()
    {
        // The Color variant is consumed by MainWindow.ChromeSurfaceColor for headless test assertions.
        var dict = AvaloniaThemeApplier.BuildResources(BrandThemes.FreeX, "FreeX");
        var color = (Color)dict["FreeXChromeSurfaceColor"]!;
        color.R.Should().Be(0xF7);
        color.G.Should().Be(0xF8);
        color.B.Should().Be(0xF8);
    }

    [Fact]
    public void AvaloniaApplier_FreeXAccentBrush_Is_0F6D8C()
    {
        // Byte-identical to SheetTabContourBrush literal Brush(15, 109, 140).
        var dict = AvaloniaThemeApplier.BuildResources(BrandThemes.FreeX, "FreeX");
        var brush = (ImmutableSolidColorBrush)dict["FreeXAccentBrush"]!;
        brush.Color.A.Should().Be(255);
        brush.Color.R.Should().Be(0x0F);
        brush.Color.G.Should().Be(0x6D);
        brush.Color.B.Should().Be(0x8C);
    }

    [Fact]
    public void AvaloniaApplier_FreeXAccentSoftBrush_Is_E6F6FA()
    {
        // Byte-identical to CheckedCommandBackground literal Brush(230, 246, 250).
        var dict = AvaloniaThemeApplier.BuildResources(BrandThemes.FreeX, "FreeX");
        var brush = (ImmutableSolidColorBrush)dict["FreeXAccentSoftBrush"]!;
        brush.Color.A.Should().Be(255);
        brush.Color.R.Should().Be(0xE6);
        brush.Color.G.Should().Be(0xF6);
        brush.Color.B.Should().Be(0xFA);
    }

    [Fact]
    public void AvaloniaApplier_FreeXWhiteBrush_Is_FFFFFF()
    {
        // Byte-identical to StatusBarForeground literal Brushes.White.
        var dict = AvaloniaThemeApplier.BuildResources(BrandThemes.FreeX, "FreeX");
        var brush = (ImmutableSolidColorBrush)dict["FreeXWhiteBrush"]!;
        brush.Color.A.Should().Be(255);
        brush.Color.R.Should().Be(0xFF);
        brush.Color.G.Should().Be(0xFF);
        brush.Color.B.Should().Be(0xFF);
    }

    [Fact]
    public void AvaloniaApplier_FreeXTextBrush_Is_1F1F1F()
    {
        // Byte-identical to DialogControlStyles.SelectionForegroundBrush literal (0x1F,0x1F,0x1F).
        var dict = AvaloniaThemeApplier.BuildResources(BrandThemes.FreeX, "FreeX");
        var brush = (ImmutableSolidColorBrush)dict["FreeXTextBrush"]!;
        brush.Color.A.Should().Be(255);
        brush.Color.R.Should().Be(0x1F);
        brush.Color.G.Should().Be(0x1F);
        brush.Color.B.Should().Be(0x1F);
    }

    // ── Midnight theme: tokenized chrome surfaces change with FREEX_THEME=midnight ─────────────────

    [Fact]
    public void AvaloniaApplier_FreeXChromeSurfaceBrush_Midnight_DiffersFromDefault()
    {
        // FreeXMidnight has ChromeSurface #F5F5F5 vs default #F7F8F8.
        var defaultDict  = AvaloniaThemeApplier.BuildResources(BrandThemes.FreeX,         "FreeX");
        var midnightDict = AvaloniaThemeApplier.BuildResources(BrandThemes.FreeXMidnight, "FreeX");
        var defaultColor  = ((ImmutableSolidColorBrush)defaultDict["FreeXChromeSurfaceBrush"]!).Color;
        var midnightColor = ((ImmutableSolidColorBrush)midnightDict["FreeXChromeSurfaceBrush"]!).Color;
        midnightColor.Should().NotBe(defaultColor, because: "FreeXMidnight has a different chrome surface");
    }

    [Fact]
    public void AvaloniaApplier_FreeXAccentBrush_Midnight_DiffersFromDefault()
    {
        // FreeXMidnight has Accent #C8651B (orange) vs default #0F6D8C (teal).
        var defaultDict  = AvaloniaThemeApplier.BuildResources(BrandThemes.FreeX,         "FreeX");
        var midnightDict = AvaloniaThemeApplier.BuildResources(BrandThemes.FreeXMidnight, "FreeX");
        var defaultColor  = ((ImmutableSolidColorBrush)defaultDict["FreeXAccentBrush"]!).Color;
        var midnightColor = ((ImmutableSolidColorBrush)midnightDict["FreeXAccentBrush"]!).Color;
        midnightColor.Should().NotBe(defaultColor, because: "FreeXMidnight has an orange accent, not teal");
    }

    // ── Fallback guard: MainWindow's inline literal matches the token value ───────────────────────
    // (In unit tests Application.Current == null, so the literal fallback is what's used.
    //  This test ensures the literal == the token value so default appearance is byte-identical.)

    [Fact]
    public void ChromeSurfaceColor_FallbackLiteral_MatchesToken()
    {
        // The static fallback literal in MainWindow must equal the token value for byte-identity.
        var dict = AvaloniaThemeApplier.BuildResources(BrandThemes.FreeX, "FreeX");
        var tokenColor = (Color)dict["FreeXChromeSurfaceColor"]!;
        // MainWindow.ChromeSurfaceColor in a no-Application context uses the literal 0xF7,0xF8,0xF8.
        MainWindow.ChromeSurfaceColor.R.Should().Be(tokenColor.R);
        MainWindow.ChromeSurfaceColor.G.Should().Be(tokenColor.G);
        MainWindow.ChromeSurfaceColor.B.Should().Be(tokenColor.B);
    }
}
