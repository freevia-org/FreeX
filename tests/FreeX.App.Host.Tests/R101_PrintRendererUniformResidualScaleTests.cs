using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R101-app-host-uniform-residual-scale-1: <c>PrintRenderer.RenderPageVisual</c>'s defensive
/// residual-overflow shrink (applied after the configured Scale%/fit-to-pages ratio, guarding against
/// content that still overflows the printable area on a page) used to clamp the width and height
/// overflow ratios SEQUENTIALLY -- shrinking by the width ratio first, then multiplying the height
/// ratio on top of the already-width-shrunk value. When both axes overflow at once this computes
/// <c>scaleRatio * widthFitScale * heightFitScale</c> (a PRODUCT), not Excel's actual uniform-scale
/// rule of taking the SMALLER of the two ratios and applying that same single value to both axes
/// (the rule every other tier -- <c>PageContentRenderModelBuilder.ResolveScaleRatio</c>,
/// <c>WorkbookPdfContentBuilder.ComputeActualGridSizes</c>, and the primary fit-to-N-pages
/// resolution in <c>PagePaginationPlanner</c>/<c>SheetPdfPageSetupResolver</c> -- already follow via
/// <c>PageGeometryRules.ResolveUniformScale</c>), so the WPF print/print-preview/PDF-export path
/// over-shrank pages whose content overflowed on both axes simultaneously, distorting the effective
/// scale relative to every other renderer.
///
/// These tests render through the real <see cref="PrintRenderer.RenderWorksheet"/> entry point (not a
/// hand-built visual) with a single, deliberately oversized cell whose width and height overflow the
/// printable area by different, known ratios, and measure the actual rendered pixel size of that
/// cell's fill rectangle -- proving the resolved scale is the uniform MIN of the two overflow ratios,
/// not their product.
/// </summary>
public sealed class R101_PrintRendererUniformResidualScaleTests
{
    private static readonly Color FillColor = Color.FromRgb(20, 140, 210);

    [Fact]
    public void RenderWorksheet_BothAxesOverflowByDifferentRatios_UsesUniformMinScaleNotProduct()
    {
        StaTestRunner.Run(() =>
        {
            var (workbook, sheet, expectedWidth, expectedHeight, buggyWidth, buggyHeight) = BuildOverflowingSheet();

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = document.Pages[0].GetPageRoot(forceReload: false)!;

            var (measuredWidth, measuredHeight) = MeasureFillRectExtent(page, FillColor);

            // Correct (uniform, R101-fixed) behavior: scaleRatio = min(widthFitScale, heightFitScale),
            // so the rendered rect is shrunk by exactly the more constrained axis's ratio on both sides.
            measuredWidth.Should().BeInRange(expectedWidth - 8, expectedWidth + 8,
                "the uniform scale (the smaller of the two per-axis overflow ratios) must shrink width " +
                "to the SAME degree the constrained (height) axis needed, not further via a compounded product");
            measuredHeight.Should().BeInRange(expectedHeight - 8, expectedHeight + 8,
                "the constrained (height) axis must land at exactly its own overflow ratio's target size");

            // No-regression guard: the pre-fix sequential-clamp bug would have produced a visibly
            // smaller rect (product of both ratios instead of their min) -- assert the measured size is
            // clearly larger than what that bug would have produced, so a regression back to the old
            // formula is caught even if the "expected" tolerance above were loosened.
            measuredWidth.Should().BeGreaterThan(buggyWidth + 20,
                "the pre-fix sequential-clamp formula would have over-shrunk width to widthFitScale*heightFitScale");
            measuredHeight.Should().BeGreaterThan(buggyHeight + 20,
                "the pre-fix sequential-clamp formula would have over-shrunk height to widthFitScale*heightFitScale");
        });
    }

    /// <summary>
    /// Builds a Letter/portrait sheet with zero margins and a single A1 cell whose column/row is sized
    /// so that, at the sheet's explicit 100% configured scale, the printed content overflows the
    /// printable width by a factor of 2 (widthFitScale = 0.5) and the printable height by a factor of 4
    /// (heightFitScale = 0.25) -- two clearly DIFFERENT ratios so a product-vs-min discrepancy is
    /// unambiguous. Returns the expected (uniform-min-scale) and "buggy" (product-of-both-scales)
    /// rendered pixel width/height for the assertions above.
    /// </summary>
    private static (Workbook Workbook, Sheet Sheet, double ExpectedWidth, double ExpectedHeight, double BuggyWidth, double BuggyHeight)
        BuildOverflowingSheet()
    {
        const double dpi = 96.0;

        var workbook = new Workbook("Uniform residual scale");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PaperSize = WorksheetPaperSize.Letter;
        sheet.PageOrientation = WorksheetPageOrientation.Portrait;
        sheet.PageMargins = new WorksheetPageMargins(Left: 0, Right: 0, Top: 0, Bottom: 0);
        sheet.HeaderMargin = 0;
        sheet.FooterMargin = 0;
        sheet.PrintHeadings = false;
        sheet.PrintGridlines = false;
        sheet.CenterHorizontallyOnPage = false;
        sheet.CenterVerticallyOnPage = false;
        sheet.ScaleToFit = new WorksheetScaleToFit(ScalePercent: 100, FitToPagesWide: null, FitToPagesTall: null);

        var pageSize = WorksheetPageLayout.GetPageSizeInches(sheet.PaperSize, sheet.PageOrientation);
        var pageWidthPx = pageSize.Width * dpi;
        var pageHeightPx = pageSize.Height * dpi;

        // printableW == pageWidthPx, printableH == pageHeightPx (zero margins). Choose a column width
        // that is exactly 2x the printable width (widthFitScale = 0.5) and a row height exactly 4x the
        // printable height (heightFitScale = 0.25).
        var columnWidthPx = pageWidthPx * 2.0;
        var rowHeightPx = pageHeightPx * 4.0;
        sheet.ColumnWidths[1] = ColumnWidthPixelMapper.PixelsToColumnWidth(columnWidthPx);
        sheet.RowHeights[1] = rowHeightPx;

        var fillStyle = workbook.RegisterStyle(new CellStyle
        {
            FillColor = new CellColor(FillColor.R, FillColor.G, FillColor.B)
        });
        var address = new CellAddress(sheet.Id, 1, 1);
        var cell = Cell.FromValue(new NumberValue(1));
        cell.StyleId = fillStyle;
        sheet.SetCell(address, cell);
        sheet.PrintArea = new GridRange(address, address);

        var widthFitScale = 0.5;
        var heightFitScale = 0.25;
        var uniformScale = Math.Min(widthFitScale, heightFitScale); // = 0.25 (correct, R101-fixed)
        var buggyScale = widthFitScale * heightFitScale;             // = 0.125 (pre-fix, sequential-clamp)

        // ColumnWidthPixelMapper round-trips to whole pixels, so re-derive the actual printed
        // (unscaled) extent from the resolved column width rather than assuming the exact requested
        // value survived the char-width conversion.
        var actualColumnWidthPx = ColumnWidthPixelMapper.ColumnWidthToPixels(sheet.ColumnWidths[1]);

        var expectedWidth = actualColumnWidthPx * uniformScale;
        var expectedHeight = rowHeightPx * uniformScale;
        var buggyWidth = actualColumnWidthPx * buggyScale;
        var buggyHeight = rowHeightPx * buggyScale;

        return (workbook, sheet, expectedWidth, expectedHeight, buggyWidth, buggyHeight);
    }

    /// <summary>
    /// Renders the page to a bitmap and measures the bounding box (widest horizontal run, tallest
    /// vertical run) of pixels matching <paramref name="fillColor"/>, giving the actual on-page pixel
    /// size the cell's fill rectangle was drawn at after every scale/transform has been applied.
    /// </summary>
    private static (double Width, double Height) MeasureFillRectExtent(FrameworkElement page, Color fillColor)
    {
        var width = Math.Max(1, (int)Math.Ceiling(page.Width));
        var height = Math.Max(1, (int)Math.Ceiling(page.Height));
        var size = new Size(width, height);
        page.Measure(size);
        page.Arrange(new Rect(size));
        page.UpdateLayout();

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(page);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);

        int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                var blue = pixels[i];
                var green = pixels[i + 1];
                var red = pixels[i + 2];
                var isMatch = Math.Abs(red - fillColor.R) <= 3 &&
                    Math.Abs(green - fillColor.G) <= 3 &&
                    Math.Abs(blue - fillColor.B) <= 3;

                if (!isMatch)
                    continue;

                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }

        if (maxX < minX || maxY < minY)
            return (0, 0);

        return (maxX - minX + 1, maxY - minY + 1);
    }
}
