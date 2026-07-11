using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression coverage for R23-style-theme-render-1: negative theme tint ("Darker X%")
/// must go through Excel's HSL-luminance transform (RgbToHls -> L' = L*(1+tint) -> HlsToRgb),
/// not a plain per-channel RGB*=k shade scale. Real Excel resolves
/// ResolveColor((100,150,200), tint=-0.25) to #3B70A6 (59,112,166); the buggy linear-RGB
/// path previously produced #4B7096 (75,112,150).
/// </summary>
public sealed class WorkbookThemeTintDarkerLuminanceTests
{
    [Fact]
    public void ResolveColor_NegativeTint_UsesExcelHslLuminanceTransform_NotLinearRgbShade()
    {
        var theme = WorkbookTheme.Office.WithColor(
            WorkbookThemeColorSlot.Accent1,
            new CellColor(100, 150, 200));

        var resolved = theme.ResolveColor(WorkbookThemeColorSlot.Accent1, -0.25);

        resolved.Should().Be(new CellColor(59, 112, 166), because: "Excel's Darker 25% theme tint scales HSL luminance, yielding #3B70A6");
        resolved.Should().NotBe(new CellColor(75, 112, 150), because: "the buggy per-channel RGB*=k shade formula produced the wrong #4B7096");
    }

    [Fact]
    public void ResolveColor_NegativeTint_MatchesSharedApplyLuminanceHelper()
    {
        var theme = WorkbookTheme.Office.WithColor(
            WorkbookThemeColorSlot.Accent1,
            new CellColor(100, 150, 200));

        var expected = DrawingMlColorTransform.ApplyLuminance(new DrawingMlRgbColor(100, 150, 200), 0.75, 0.0);

        theme.ResolveColor(WorkbookThemeColorSlot.Accent1, -0.25)
            .Should().Be(new CellColor(expected.R, expected.G, expected.B));
    }
}
