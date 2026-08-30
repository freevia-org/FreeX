using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R175 meta-F3: PrintRenderer.GridCells.cs's DrawPrintedBorderEdge built its brush straight from
/// <see cref="CellBorder.Color"/> (the RGB baked in at load time) and never called
/// <see cref="CellBorder.ResolveColor"/>, unlike its sibling DrawPrintedCellFill/
/// ResolvePrintedTextBrush in this same file, which r174 already wired to re-resolve
/// <see cref="CellStyle.ResolveFillColor"/>/<see cref="CellStyle.ResolveFontColor"/> against the
/// CURRENT workbook theme. A border set via the ribbon's Theme Colors picker therefore kept
/// printing the color baked in when the workbook was authored/loaded, even after the workbook's
/// theme was changed to one with different accent colors. The fix threads a
/// <see cref="WorkbookTheme"/> parameter through DrawPrintedCellBorders/DrawPrintedBorderEdge and
/// resolves through <see cref="CellBorder.ResolveColor"/>, exactly mirroring the font/fill fix.
/// </summary>
public sealed class R175_PrintedCellBorderThemeColorTests
{
    private static readonly Point P1 = new(20, 2);
    private static readonly Point P2 = new(20, 22);

    /// <summary>
    /// Renders a single vertical border edge via the print path's private DrawPrintedBorderEdge and
    /// returns the RGB sampled from the middle of the painted line.
    /// </summary>
    private static Color RenderBorderLineColor(CellBorder border, WorkbookTheme theme)
    {
        var method = typeof(PrintRenderer).GetMethod("DrawPrintedBorderEdge", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            method!.Invoke(null, [dc, border, P1, P2, false, theme]);
        }

        const int width = 40;
        const int height = 24;
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);

        // Sample at the line's nominal X (20) and mid-Y (12): the pen is centered there regardless
        // of thickness/anti-aliasing.
        var i = (12 * width + 20) * 4;
        return Color.FromRgb(pixels[i + 2], pixels[i + 1], pixels[i]);
    }

    private static bool ColorsClose(Color a, CellColor b, byte tolerance = 12) =>
        Math.Abs(a.R - b.R) <= tolerance && Math.Abs(a.G - b.G) <= tolerance && Math.Abs(a.B - b.B) <= tolerance;

    [Fact]
    public void ThemeColoredBorder_PrintsCurrentThemeColor_NotTheColorBakedAtLoadTime()
    {
        StaTestRunner.Run(() =>
        {
            // The workbook's theme is changed AFTER this border was authored/loaded: the border's
            // baked Color field still reflects the OLD theme's Accent1 (the default Office theme's),
            // while ThemeColor(Accent1) must be re-resolved against the NEW theme at print time --
            // exactly like a real Format Cells > Border > Theme Colors border behaves when Page
            // Layout > Colors switches the workbook's theme.
            var oldTheme = WorkbookTheme.Office;
            var staleBakedColor = oldTheme.GetColor(WorkbookThemeColorSlot.Accent1);
            var newTheme = oldTheme.WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(200, 20, 20));

            var border = new CellBorder(
                BorderStyle.Thick,
                staleBakedColor,
                new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1));

            // Ground truth: the same re-resolution API CellBorder.ResolveColor exposes specifically
            // for this purpose (and that the on-screen grid should use identically) -- not a
            // hard-coded RGB literal.
            var expected = border.ResolveColor(newTheme);
            expected.Should().NotBe(staleBakedColor, "the test theme swap must actually change Accent1");

            var rendered = RenderBorderLineColor(border, newTheme);

            ColorsClose(rendered, expected).Should().BeTrue(
                $"the printed border must follow the CURRENT theme's Accent1 ({expected.R},{expected.G},{expected.B}), " +
                $"not the color baked in at load time ({staleBakedColor.R},{staleBakedColor.G},{staleBakedColor.B}); " +
                $"rendered was ({rendered.R},{rendered.G},{rendered.B})");
            ColorsClose(rendered, staleBakedColor).Should().BeFalse(
                "the printed border must NOT still show the stale load-time color after the theme changed");
        });
    }

    [Fact]
    public void ExplicitRgbBorder_StillPrintsItsOwnColor_NoRegression()
    {
        StaTestRunner.Run(() =>
        {
            // Sibling/no-regression case: a border with NO ThemeColor (an explicit Format Cells >
            // Border > [plain RGB swatch] color, not a Theme Color) must keep printing its own
            // authored color regardless of the workbook theme -- ResolveColor falls back to Color
            // when ThemeColor is null, so changing the theme must never repaint it.
            var explicitColor = new CellColor(10, 200, 30);
            var border = new CellBorder(BorderStyle.Thick, explicitColor, ThemeColor: null);

            var themeA = WorkbookTheme.Office;
            var themeB = themeA.WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(200, 20, 20));

            var renderedA = RenderBorderLineColor(border, themeA);
            var renderedB = RenderBorderLineColor(border, themeB);

            ColorsClose(renderedA, explicitColor).Should().BeTrue("an explicit-RGB border must print its own authored color");
            ColorsClose(renderedB, explicitColor).Should().BeTrue(
                "an explicit-RGB border must keep printing its own authored color even after the workbook theme changes");
        });
    }
}
