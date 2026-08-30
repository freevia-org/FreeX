using System.Linq;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R174 (freex-styles-themes F1): the WPF print/print-preview/PDF-XPS
/// export path (<c>PrintRenderer.RenderWorksheet</c> -&gt; <c>DrawPrintedGridCells</c>, in
/// <c>PrintRenderer.GridCells.cs</c>) read <c>CellStyle.FontColor</c>/<c>FillColor</c>/
/// <c>FillPatternColor</c> directly instead of resolving them against the workbook theme via
/// <c>CellStyle.ResolveFontColor</c>/<c>ResolveFillColor</c>/<c>ResolveFillPatternColor</c> -- the
/// exact same methods the interactive on-screen grid resolves through (GridView.Rendering.cs:419/934
/// and CellFillMaterializationPlanner.cs:85/98). A cell styled via the ribbon's "Theme Colors" picker
/// only sets the *ThemeColor field, leaving the plain field at its prior/default value, so pre-fix
/// the printed page showed plain black text, a missing fill (the theme-blind visibility gate skipped
/// drawing it entirely), and black pattern stripes -- regardless of what the theme color actually
/// resolved to on screen.
///
/// Each test below asserts the printed/exported color against <c>style.Resolve*Color(theme)</c> --
/// the actual screen-resolution answer for the same themed cell -- not a hard-coded RGB literal.
/// </summary>
public sealed class R174_PrintedThemeColorResolutionTests
{
    [Fact]
    public void RenderWorksheet_ThemeFontColorOnly_PrintsScreenResolvedColorNotBlack()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Theme font color");
            var sheet = workbook.AddSheet("Sheet1");

            // Only FontThemeColor is set -- exactly what the ribbon's Theme Colors font picker does
            // (WorkbookSession.cs's StyleDiff(FontThemeColor: theme)); FontColor stays at its CellStyle
            // default (Black), which is the field the pre-fix print path read directly.
            var style = new CellStyle { FontThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2) };
            var styleId = workbook.RegisterStyle(style);
            var cell = Cell.FromValue(new TextValue("Themed"));
            cell.StyleId = styleId;
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

            var expected = style.ResolveFontColor(workbook.Theme);
            expected.IsBlack.Should().BeFalse("the test fixture must exercise a genuinely non-black theme color");

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = (FixedPage)document.Pages[0].GetPageRoot(forceReload: false)!;

            var overlay = PdfTextOverlayExtractor.Extract(page)
                .Should().ContainSingle(o => o.Text == "Themed")
                .Subject;

            // Pre-fix, ResolvePrintedTextBrush read style.FontColor (Black) directly and this would
            // report (0,0,0). Post-fix it must match the same ResolveFontColor(theme) answer the
            // on-screen grid renders.
            overlay.Color.Should().Be(Color.FromRgb(expected.R, expected.G, expected.B),
                "printed text must resolve FontThemeColor against the workbook theme, matching the on-screen renderer");
        });
    }

    [Fact]
    public void RenderWorksheet_ThemeFillColorOnly_PrintsScreenResolvedFillInsteadOfSkippingIt()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Theme fill color");
            var sheet = workbook.AddSheet("Sheet1");

            // Only FillThemeColor is set (FillColor stays null) -- exactly what the ribbon's Theme
            // Colors fill picker produces. Pre-fix, WorksheetPrintCellGeometryPlanner.HasVisibleFill
            // (theme-blind) returned false for this cell, so DrawPrintedCellFill was never even
            // invoked and the fill was dropped entirely, not merely mis-colored.
            var style = new CellStyle { FillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent3) };
            var styleId = workbook.RegisterStyle(style);
            for (uint r = 1; r <= 3; r++)
            {
                var cell = Cell.FromValue(new TextValue($"R{r}"));
                cell.StyleId = styleId;
                sheet.SetCell(new CellAddress(sheet.Id, r, 1), cell);
            }

            var expected = style.ResolveFillColor(workbook.Theme);
            expected.Should().NotBeNull("the test fixture must exercise a genuine theme-only fill");

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = (FixedPage)document.Pages[0].GetPageRoot(forceReload: false)!;

            var matchCount = CountMatchingPixels(page, expected!.Value.R, expected.Value.G, expected.Value.B);

            // Pre-fix this is 0: the fill was never drawn at all because the visibility gate itself
            // was theme-blind. Post-fix the resolved theme color must actually appear on the page,
            // matching what CellFillMaterializationPlanner.Plan resolves for the on-screen grid.
            matchCount.Should().BeGreaterThan(0,
                "a cell whose fill is theme-only must still be drawn, resolved against the workbook theme like the on-screen grid");
        });
    }

    [Fact]
    public void RenderWorksheet_ThemeFillPatternColorOnly_PrintsScreenResolvedPatternNotBlack()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Theme fill pattern color");
            var sheet = workbook.AddSheet("Sheet1");

            // Only FillPatternThemeColor is set (FillPatternColor stays null). Pre-fix,
            // DrawPrintedFillPattern read `style.FillPatternColor ?? CellColor.Black`, so a theme-only
            // pattern color always fell back to hard black stripes.
            var style = new CellStyle
            {
                FillPatternStyle = CellFillPatternStyle.LightGrid,
                FillPatternThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent4)
            };
            var styleId = workbook.RegisterStyle(style);
            for (uint r = 1; r <= 4; r++)
            for (uint c = 1; c <= 4; c++)
            {
                var cell = Cell.FromValue(new TextValue(""));
                cell.StyleId = styleId;
                sheet.SetCell(new CellAddress(sheet.Id, r, c), cell);
            }

            var expected = style.ResolveFillPatternColor(workbook.Theme);
            expected.Should().NotBeNull();
            expected!.Value.IsBlack.Should().BeFalse("the test fixture must exercise a genuinely non-black theme pattern color");
            var expectedColor = Color.FromRgb(expected.Value.R, expected.Value.G, expected.Value.B);

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = (FixedPage)document.Pages[0].GetPageRoot(forceReload: false)!;

            // The pattern stroke is a thin (0.75pt) line, easy to lose to anti-aliasing in a rasterized
            // pixel scan, so read the exact stroke brush color out of the retained drawing graph instead
            // (VisualTreeHelper.GetDrawing) -- the same ink-level inspection R97/R79/R91's print-color
            // tests already use for this file.
            var strokeColors = ExtractStrokeColors(page);

            // Pre-fix, DrawPrintedFillPattern read `style.FillPatternColor ?? CellColor.Black` -- a
            // theme-only pattern color (FillPatternColor null) always fell back to hard black, so this
            // would contain Colors.Black instead of the resolved theme color.
            strokeColors.Should().Contain(expectedColor,
                "the pattern stroke must resolve FillPatternThemeColor against the workbook theme, matching the on-screen grid");
        });
    }

    private static List<Color> ExtractStrokeColors(FixedPage page)
    {
        var results = new List<Color>();
        foreach (var host in page.Children.OfType<VisualHost>())
        {
            if (host.Visual is null)
                continue;

            CollectStrokeColors(VisualTreeHelper.GetDrawing(host.Visual), results);
        }

        return results;
    }

    private static void CollectStrokeColors(Drawing? drawing, List<Color> results)
    {
        switch (drawing)
        {
            case GeometryDrawing { Pen.Brush: SolidColorBrush solid }:
                results.Add(solid.Color);
                break;
            case GeometryDrawing:
                break;
            case DrawingGroup group:
                foreach (var child in group.Children)
                    CollectStrokeColors(child, results);
                break;
        }
    }

    [Fact]
    public void RenderWorksheet_ExplicitNonThemeColors_StillPrintTheirOwnBakedColors()
    {
        // Sibling/no-regression case: a cell with ordinary, non-theme-backed colors (the
        // overwhelming majority of real workbooks) must keep printing exactly those baked RGB
        // values after routing font/fill/pattern through Resolve*Color -- ResolveFontColor etc.
        // fall back to the plain field whenever the *ThemeColor field is null, so this must be
        // unaffected by the fix.
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Explicit colors");
            var sheet = workbook.AddSheet("Sheet1");

            var fontColor = new CellColor(10, 20, 200);
            var fillColor = new CellColor(200, 10, 20);
            var style = new CellStyle
            {
                FontColor = fontColor,
                FillColor = fillColor,
            };
            var styleId = workbook.RegisterStyle(style);
            var cell = Cell.FromValue(new TextValue("Plain"));
            cell.StyleId = styleId;
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = (FixedPage)document.Pages[0].GetPageRoot(forceReload: false)!;

            var overlay = PdfTextOverlayExtractor.Extract(page)
                .Should().ContainSingle(o => o.Text == "Plain")
                .Subject;
            overlay.Color.Should().Be(Color.FromRgb(fontColor.R, fontColor.G, fontColor.B),
                "an explicit, non-theme font color must still print exactly as authored");

            var fillMatchCount = CountMatchingPixels(page, fillColor.R, fillColor.G, fillColor.B);
            fillMatchCount.Should().BeGreaterThan(0,
                "an explicit, non-theme fill color must still be drawn exactly as authored");
        });
    }

    private static int CountMatchingPixels(FixedPage page, byte expectedRed, byte expectedGreen, byte expectedBlue)
    {
        var width = Math.Max(1, (int)Math.Ceiling(page.Width));
        var height = Math.Max(1, (int)Math.Ceiling(page.Height));
        var size = new System.Windows.Size(width, height);
        page.Measure(size);
        page.Arrange(new System.Windows.Rect(size));
        page.UpdateLayout();

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(page);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);

        var count = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var blue = pixels[i];
            var green = pixels[i + 1];
            var red = pixels[i + 2];
            var alpha = pixels[i + 3];
            if (alpha == 0) continue;

            if (Math.Abs(red - expectedRed) <= 3 &&
                Math.Abs(green - expectedGreen) <= 3 &&
                Math.Abs(blue - expectedBlue) <= 3)
            {
                count++;
            }
        }

        return count;
    }
}
