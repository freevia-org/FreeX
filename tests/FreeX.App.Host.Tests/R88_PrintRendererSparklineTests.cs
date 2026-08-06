using System.Linq;
using System.Windows.Media;
using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R88-render-sparkline-5-1: sparklines are drawn as a screen-only overlay
/// (GridView.Overlays.Sparklines.cs RenderSparklines) above the interactive grid, never referenced
/// by PrintRenderer.GridCells.cs -- Print, Print Preview, PDF, and XPS output silently omitted every
/// sparkline while still printing the cell's plain value/fill/borders/gridlines. Fixed by drawing
/// them in the print path via GridView.DrawSparklineIntoCell/SparklineAxisScalePlanner
/// helpers (public for exactly this cross-assembly reuse, mirroring how
/// DrawConditionalDataBar/DrawConditionalIcon were already made public for the R79 fix --
/// see R79_PrintRendererConditionalDataBarIconTests).
/// </summary>
public sealed class R88_PrintRendererSparklineTests
{
    // A column sparkline's positive-bar fill uses exactly this SeriesColor -- distinct from every
    // other color this print path ever draws (white page background, black text/borders/gridlines)
    // -- so finding a solid brush of exactly this color in the rendered page proves the sparkline
    // bar itself was drawn, not just the cell's plain (empty) style.
    private static readonly Color SparklineSeriesColor = Color.FromRgb(12, 34, 56);

    [Fact]
    public void RenderWorksheet_ColumnSparklineCell_DrawsSparklineBarFillInPrintOutput()
    {
        StaTestRunner.Run(() =>
        {
            var (workbook, sheet) = BuildColumnSparklineWorkbook();

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = document.Pages[0].GetPageRoot(forceReload: false)!;
            var geometryDrawings = ExtractGeometryDrawings(page);

            HasSparklineFill(geometryDrawings).Should().BeTrue(
                "Print/Print Preview must draw the cell's sparkline, not just its base style");
        });
    }

    [Fact]
    public void RenderWorksheet_SameDataWithoutSparklineOnSheet_NeverDrawsSparklineFillColor()
    {
        // No-regression sibling: the exact same underlying data range, but with no SparklineModel
        // attached to the sheet, must never incidentally produce the sparkline's distinctive fill
        // color -- proving the color only ever appears because a sparkline was actually drawn.
        StaTestRunner.Run(() =>
        {
            var (workbook, sheet) = BuildColumnSparklineWorkbook();
            sheet.Sparklines.Clear();

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = document.Pages[0].GetPageRoot(forceReload: false)!;
            var geometryDrawings = ExtractGeometryDrawings(page);

            HasSparklineFill(geometryDrawings).Should().BeFalse();
        });
    }

    private static (Workbook Workbook, Sheet Sheet) BuildColumnSparklineWorkbook()
    {
        var workbook = new Workbook("Sparkline print");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(1)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromValue(new NumberValue(5)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), Cell.FromValue(new NumberValue(3)));

        var dataRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 3));
        var sparklineLocation = new CellAddress(sheet.Id, 2, 1);

        sheet.Sparklines.Add(new SparklineModel
        {
            DataRange = dataRange,
            Location = sparklineLocation,
            Kind = SparklineKind.Column,
            SeriesColor = new CellColor(SparklineSeriesColor.R, SparklineSeriesColor.G, SparklineSeriesColor.B),
        });

        sheet.PrintArea = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 3));

        return (workbook, sheet);
    }

    private static bool HasSparklineFill(IEnumerable<GeometryDrawing> geometryDrawings)
    {
        foreach (var gd in geometryDrawings)
        {
            if (gd.Brush is SolidColorBrush solid && solid.Color == SparklineSeriesColor)
                return true;
        }

        return false;
    }

    private static List<GeometryDrawing> ExtractGeometryDrawings(System.Windows.Documents.FixedPage page)
    {
        var results = new List<GeometryDrawing>();
        foreach (var host in page.Children.OfType<VisualHost>())
        {
            if (host.Visual is null)
                continue;

            CollectGeometryDrawings(VisualTreeHelper.GetDrawing(host.Visual), results);
        }

        return results;
    }

    private static void CollectGeometryDrawings(Drawing? drawing, List<GeometryDrawing> results)
    {
        switch (drawing)
        {
            case GeometryDrawing geometryDrawing:
                results.Add(geometryDrawing);
                break;
            case DrawingGroup group:
                foreach (var child in group.Children)
                    CollectGeometryDrawings(child, results);
                break;
        }
    }
}
