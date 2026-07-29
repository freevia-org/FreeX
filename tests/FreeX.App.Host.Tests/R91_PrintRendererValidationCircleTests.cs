using System.Linq;
using System.Windows.Media;
using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R91-meta-1 / R91-print-twin-two-tier-synthetic-sweep-1: R90 relocated the Circle Invalid Data
/// circled-cell set onto Sheet.ValidationCircleCells specifically so a print renderer could read it
/// (see Sheet.ValidationCircles.cs), but no PrintRenderer file was ever wired to actually draw the
/// circles -- Print, Print Preview, PDF, and XPS output silently omitted every circle while still
/// printing the cell's plain value/fill/borders/gridlines, identical to before the r90 "fix". Fixed
/// by having PrintRenderer.GridCells.cs draw the same red ellipse GridView.Overlays.cs's
/// RenderValidationCircles draws on screen (mirroring how R88 wired sparklines into this same print
/// path -- see R88_PrintRendererSparklineTests).
/// </summary>
public sealed class R91_PrintRendererValidationCircleTests
{
    // The validation-circle stroke is this exact red (226, 28, 33) -- distinct from every other
    // color this print path ever draws (white page background, black text/borders/gridlines) --
    // so finding a Pen of exactly this color stroking a GeometryDrawing in the rendered page proves
    // the circle itself was actually drawn as ink, not merely that the model state is readable.
    private static readonly Color ValidationCircleColor = Color.FromRgb(226, 28, 33);

    [Fact]
    public void RenderWorksheet_CellFlaggedByValidationCircleCells_DrawsRedCircleInPrintOutput()
    {
        StaTestRunner.Run(() =>
        {
            var (workbook, sheet) = BuildCircledCellWorkbook();

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = document.Pages[0].GetPageRoot(forceReload: false)!;
            var geometryDrawings = ExtractGeometryDrawings(page);

            HasValidationCircleStroke(geometryDrawings).Should().BeTrue(
                "Print/Print Preview/PDF/XPS must draw the same red validation circle Circle " +
                "Invalid Data shows on screen (Sheet.ValidationCircleCells), not just the cell's " +
                "plain style");
        });
    }

    [Fact]
    public void RenderWorksheet_SameDataWithoutValidationCircleCells_NeverDrawsCircleStrokeColor()
    {
        // No-regression sibling: the exact same underlying data, but with no
        // Sheet.ValidationCircleCells set, must never incidentally produce the circle's
        // distinctive red stroke color -- proving the color only ever appears because a circle was
        // actually drawn.
        StaTestRunner.Run(() =>
        {
            var (workbook, sheet) = BuildCircledCellWorkbook();
            sheet.ValidationCircleCells = null;

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = document.Pages[0].GetPageRoot(forceReload: false)!;
            var geometryDrawings = ExtractGeometryDrawings(page);

            HasValidationCircleStroke(geometryDrawings).Should().BeFalse();
        });
    }

    private static (Workbook Workbook, Sheet Sheet) BuildCircledCellWorkbook()
    {
        var workbook = new Workbook("Validation circle print");
        var sheet = workbook.AddSheet("Sheet1");
        var flaggedCell = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(flaggedCell, Cell.FromValue(new NumberValue(42)));

        sheet.ValidationCircleCells = new List<CellAddress> { flaggedCell };

        sheet.PrintArea = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 2));

        return (workbook, sheet);
    }

    private static bool HasValidationCircleStroke(IEnumerable<GeometryDrawing> geometryDrawings)
    {
        foreach (var gd in geometryDrawings)
        {
            if (gd.Pen?.Brush is SolidColorBrush solid && solid.Color == ValidationCircleColor)
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
