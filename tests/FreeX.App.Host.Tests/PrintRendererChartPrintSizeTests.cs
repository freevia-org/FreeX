using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for K2 (review5, group A-chart-print-size): <c>PrintRenderer.DrawPrintedCharts</c>
/// (the live WPF print/print-preview/PDF drawing path) converts chart.Left/Top into the print grid's own
/// pixel space via <c>ChartAnchorGeometry.ConvertColumnOffsetToGridSpace</c>/<c>ConvertRowOffsetToGridSpace</c>,
/// but left chart.Width/chart.Height in <c>XlsxDrawingAnchorApplier</c>'s <c>width-in-chars * 8</c>
/// anchor-space convention, so the drawn <c>Rect</c> mixed a grid-space origin with an anchor-space
/// extent. These tests derive Width from a real multi-column anchor span (matching how
/// <c>XlsxDrawingAnchorApplier.ApplyToChart</c> computes it for a twoCellAnchor) and measure the actual
/// rendered pixel run of the chart's solid fill color on the printed page bitmap, verifying it now spans
/// the grid-space width rather than the wider anchor-space width.
/// </summary>
public sealed class PrintRendererChartPrintSizeTests
{
    [Fact]
    public void RenderWorksheet_PrintedChartWidthMatchesGridSpaceNotAnchorSpaceExtent()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Chart print size");
            var sheet = workbook.AddSheet("Sheet1");
            PopulateChartSource(sheet);
            sheet.PrintArea = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 20, 12));

            // 8 uniform default-width (8.43 char) columns: anchor-space per-column = 8.43*8 = 67.44px;
            // grid-space per-column = round(8.43*7+5) = 64px. A chart spanning all 8 columns gets
            // Width = 539.52 (anchor-space) but must render at 512px (grid-space) wide.
            for (uint col = 1; col <= 8; col++)
                sheet.ColumnWidths[col] = 8.43;

            var anchorWidth = ChartAnchorGeometry.SumColumnPixels(sheet, 1, 8);
            var fillColor = new CellColor(23, 180, 90);
            var chart = new ChartModel
            {
                Type = ChartType.Column,
                DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
                Title = "Anchor-sized print chart",
                Left = 0,
                Top = 0,
                Width = anchorWidth,
                Height = 100,
                // Use the chart-area (whole-image) background fill rather than a series bar color, so
                // the rendered color reliably spans the chart's full width edge-to-edge, making the
                // widest-horizontal-run measurement below a direct read of the drawn rect's width.
                ChartAreaFillColor = fillColor
            };
            sheet.Charts.Add(chart);

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = document.Pages[0].GetPageRoot(forceReload: false)!;

            var runWidth = MeasureWidestHorizontalRun(page, fillColor.R, fillColor.G, fillColor.B);

            var expectedGridWidth = ChartAnchorGeometry.ConvertColumnExtentToGridSpace(sheet, 0, anchorWidth);

            // The rendered fill run must be close to the grid-space width, not the (wider) anchor-space
            // width the pre-fix code would have stretched the chart image to.
            runWidth.Should().BeInRange(expectedGridWidth - 6, expectedGridWidth + 6,
                "the printed chart width must be converted into the grid's pixel space");
            runWidth.Should().BeLessThan(anchorWidth - 6,
                "the pre-fix bug would render the chart at the wider, unconverted anchor-space width");
        });
    }

    /// <summary>
    /// Scans the rendered page for the widest contiguous horizontal run of the given fill color across
    /// any row, returning its pixel width. Used to measure how wide a solid-fill chart was actually drawn,
    /// independent of exact page-margin pixel math.
    /// </summary>
    private static double MeasureWidestHorizontalRun(FrameworkElement page, byte expectedRed, byte expectedGreen, byte expectedBlue)
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

        var widestRun = 0;
        for (var y = 0; y < height; y++)
        {
            var currentRun = 0;
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                var blue = pixels[i];
                var green = pixels[i + 1];
                var red = pixels[i + 2];
                var isMatch = Math.Abs(red - expectedRed) <= 3 &&
                    Math.Abs(green - expectedGreen) <= 3 &&
                    Math.Abs(blue - expectedBlue) <= 3;

                if (isMatch)
                {
                    currentRun++;
                    widestRun = Math.Max(widestRun, currentRun);
                }
                else
                {
                    currentRun = 0;
                }
            }
        }

        return widestRun;
    }

    private static void PopulateChartSource(Sheet sheet)
    {
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Month"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Jan"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(8));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Feb"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(14));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Mar"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(11));
    }
}
