using System.Linq;
using System.Windows.Media;
using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R79-render-cf-display-5-3: the WPF Print/Print-Preview path (PrintRenderer.GridCells.cs) reads
/// DisplayCell.Style for fill/border/font (which the viewport already merges CF highlight/colorscale
/// fills into) but never referenced DisplayCell.ConditionalDataBar or DisplayCell.ConditionalIcon --
/// Excel's own Data Bar and Icon Set conditional formats never appeared at all in printed output,
/// only the cell's plain base style. Fixed by drawing them via the interactive grid's own
/// GridView.DrawConditionalDataBar/DrawConditionalIcon helpers (made public for this cross-assembly
/// reuse) in the same per-cell pass that already draws text.
/// </summary>
public sealed class R79_PrintRendererConditionalDataBarIconTests
{
    // The data bar's own authored fill color -- distinct from every other color this print path ever
    // draws (white page background, black text/borders/gridlines) -- so finding a solid brush of
    // exactly this color in the rendered page proves the bar itself was drawn, not just the cell's
    // plain style.
    private static readonly Color DataBarFillColor = Color.FromRgb(99, 142, 198);

    // ConditionalIconGlyphRenderer's fixed traffic-light/icon palette (GridView.UI's
    // ConditionalIconGlyphRenderer): none of these six colors are ever otherwise produced by this
    // print path, so finding any of them proves an icon glyph was actually drawn.
    private static readonly Color[] IconGlyphColors =
    [
        Color.FromRgb(0xC0, 0x00, 0x00),
        Color.FromRgb(0xED, 0x7D, 0x31),
        Color.FromRgb(0xFF, 0xC0, 0x00),
        Color.FromRgb(0x92, 0xD0, 0x50),
        Color.FromRgb(0x00, 0xB0, 0x50),
        Color.FromRgb(0x66, 0x66, 0x66),
    ];

    [Fact]
    public void RenderWorksheet_DataBarCfRule_DrawsDataBarFillInPrintOutput()
    {
        StaTestRunner.Run(() =>
        {
            var (workbook, sheet) = BuildDataBarWorkbook();

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = document.Pages[0].GetPageRoot(forceReload: false)!;
            var geometryDrawings = ExtractGeometryDrawings(page);

            HasDataBarFill(geometryDrawings).Should().BeTrue(
                "Print/Print Preview must draw the cell's Data Bar conditional format, not just its base style");
        });
    }

    [Fact]
    public void RenderWorksheet_PlainCellWithoutDataBarRule_NeverDrawsDataBarFillColor()
    {
        // No-regression sibling: a workbook with the SAME cell values but no Data Bar rule at all
        // must never incidentally produce the data bar's distinctive fill color.
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("No data bar");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(0)));
            sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(50)));
            sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new NumberValue(100)));
            sheet.PrintArea = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 1));

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = document.Pages[0].GetPageRoot(forceReload: false)!;
            var geometryDrawings = ExtractGeometryDrawings(page);

            HasDataBarFill(geometryDrawings).Should().BeFalse();
        });
    }

    [Fact]
    public void RenderWorksheet_IconSetCfRule_DrawsIconGlyphInPrintOutput()
    {
        StaTestRunner.Run(() =>
        {
            var (workbook, sheet) = BuildIconSetWorkbook();

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = document.Pages[0].GetPageRoot(forceReload: false)!;
            var geometryDrawings = ExtractGeometryDrawings(page);

            HasIconGlyphColor(geometryDrawings).Should().BeTrue(
                "Print/Print Preview must draw the cell's Icon Set conditional format, not just its base style");
        });
    }

    [Fact]
    public void RenderWorksheet_PlainCellWithoutIconSetRule_NeverDrawsIconGlyphColors()
    {
        // No-regression sibling: the same cell values with no Icon Set rule must never incidentally
        // produce any of the fixed icon-glyph palette colors.
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("No icon set");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(10)));
            sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(50)));
            sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new NumberValue(90)));
            sheet.PrintArea = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 1));

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = document.Pages[0].GetPageRoot(forceReload: false)!;
            var geometryDrawings = ExtractGeometryDrawings(page);

            HasIconGlyphColor(geometryDrawings).Should().BeFalse();
        });
    }

    private static (Workbook Workbook, Sheet Sheet) BuildDataBarWorkbook()
    {
        var workbook = new Workbook("Data bar print");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(0)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(50)));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new NumberValue(100)));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            Priority = 1,
            RuleType = CfRuleType.DataBar,
            DataBarColor = new RgbColor(DataBarFillColor.R, DataBarFillColor.G, DataBarFillColor.B),
            // Solid (not gradient) fill so the drawn brush is an exact SolidColorBrush match instead
            // of a partially-transparent gradient stop.
            DataBarGradient = false,
        });

        sheet.PrintArea = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1));

        return (workbook, sheet);
    }

    private static (Workbook Workbook, Sheet Sheet) BuildIconSetWorkbook()
    {
        var workbook = new Workbook("Icon set print");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(50)));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new NumberValue(90)));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            Priority = 1,
            RuleType = CfRuleType.IconSet,
            IconSetStyle = "3TrafficLights1",
        });

        sheet.PrintArea = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1));

        return (workbook, sheet);
    }

    private static bool HasDataBarFill(IEnumerable<GeometryDrawing> geometryDrawings)
    {
        foreach (var gd in geometryDrawings)
        {
            if (gd.Brush is SolidColorBrush solid && solid.Color == DataBarFillColor)
                return true;
        }

        return false;
    }

    private static bool HasIconGlyphColor(IEnumerable<GeometryDrawing> geometryDrawings)
    {
        foreach (var gd in geometryDrawings)
        {
            if (gd.Brush is SolidColorBrush fillBrush && IconGlyphColors.Contains(fillBrush.Color))
                return true;

            if (gd.Pen?.Brush is SolidColorBrush penBrush && IconGlyphColors.Contains(penBrush.Color))
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
