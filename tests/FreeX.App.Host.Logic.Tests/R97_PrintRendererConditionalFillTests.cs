using System.Linq;
using System.Windows.Media;
using FluentAssertions;
using FreeX.App.Host;
using FreeX.App.Host.Tests;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Host.Logic.Tests;

/// <summary>
/// R97 investigation: the round-96 handoff flagged a suspected gap where the WPF print renderer
/// (PrintRenderer.GridCells.cs) supposedly paints no conditional formatting at all. Investigation
/// found that gap was already closed well before round 96 -- R79 wired DisplayCell.ConditionalDataBar
/// /ConditionalIcon into GridView.DrawConditionalDataBar/DrawConditionalIcon (which themselves call
/// the shared portable ConditionalDataBarLayoutPlanner/ConditionalIconCellLayoutPlanner/
/// ConditionalIconGlyphGeometry/ConditionalIconGlyphResolver in FreeX.App.Presentation), and plain CF
/// fill has printed since the print path started reading DisplayCell.Style, because ViewportService
/// (FreeX.Core.Calc, the SAME service instance PrintRenderer.RenderWorksheet calls for its cellLookup)
/// already merges CF fill into that Style via ViewportConditionalFormatEvaluator.MergeStyles before
/// print ever sees the cell.
///
/// These tests exist to pin down that finding with ink-level evidence (not just model round-trips):
/// a plain CellValue/highlight-rule CF fill actually lands as a drawn rectangle brush in the printed
/// page, and when a CF fill rule and an explicit user-authored fill both apply to the same cell, the
/// CF fill wins on the printed page -- exactly matching MergeStyles' documented precedence (CF fully
/// replaces the base fill) and the Avalonia preview / PDF export tiers, which merge through the same
/// portable ConditionalFormatEvaluator rule (see PageContentRenderModelBuilder.EvaluateConditionalFormat's
/// doc comment: "CF rule ... fully replaces the base cell's background").
/// </summary>
public sealed class R97_PrintRendererConditionalFillTests
{
    // Distinct from white page background, black text/gridlines/borders, and the R79 data-bar/icon
    // palette, so finding this exact color proves the CF fill rule was drawn.
    private static readonly Color CfFillColor = Color.FromRgb(255, 235, 156);

    // The cell's own explicit (non-CF) authored fill -- must NOT appear on the printed page for the
    // rule-matching cell, since CF fill replaces it entirely (MergeStyles semantics).
    private static readonly Color ExplicitFillColor = Color.FromRgb(0, 176, 80);

    [Fact]
    public void RenderWorksheet_CellValueCfRule_DrawsCfFillInPrintOutput()
    {
        StaTestRunner.Run(() =>
        {
            var (workbook, sheet) = BuildHighlightRuleWorkbook(withExplicitFill: false);

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = document.Pages[0].GetPageRoot(forceReload: false)!;
            var rectFills = ExtractRectangleFillColors(page);

            rectFills.Should().Contain(CfFillColor,
                "Print/Print Preview must draw the cell's CellValue conditional-format fill, not just its base style");
        });
    }

    [Fact]
    public void RenderWorksheet_PlainCellWithoutCfRule_NeverDrawsCfFillColor()
    {
        // No-regression sibling: same values, no CF rule at all -- the CF fill color must never
        // appear incidentally.
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("No CF fill");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(2000)));
            sheet.PrintArea = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 1));

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = document.Pages[0].GetPageRoot(forceReload: false)!;
            var rectFills = ExtractRectangleFillColors(page);

            rectFills.Should().NotContain(CfFillColor);
        });
    }

    [Fact]
    public void RenderWorksheet_CfFillAndExplicitFillBothApply_CfFillWinsInPrintOutput()
    {
        // Tier-agreement case: a cell with BOTH an explicit user fill and a matching CF fill rule
        // must print with only the CF color visible -- matching MergeStyles (CF fully replaces the
        // base fill) and the Avalonia/PDF tiers' identical precedence.
        StaTestRunner.Run(() =>
        {
            var (workbook, sheet) = BuildHighlightRuleWorkbook(withExplicitFill: true);

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = document.Pages[0].GetPageRoot(forceReload: false)!;
            var rectFills = ExtractRectangleFillColors(page);

            rectFills.Should().Contain(CfFillColor, "the CF rule matched and must win");
            rectFills.Should().NotContain(ExplicitFillColor, "CF fill fully replaces the explicit base fill when the rule matches");
        });
    }

    [Fact]
    public void RenderWorksheet_ArrowIconSetCfRule_DrawsPolygonIconGlyphInPrintOutput()
    {
        // Icon-set shape coverage: R79 already proved the ellipse-based traffic-light shape
        // (ConditionalIconGlyphKind.TrafficLight) prints; this covers the OTHER glyph primitive kind
        // the shared ConditionalIconGlyphGeometry.Build emits -- Arrows resolve to
        // ConditionalIconGlyphKind.Arrow, drawn as a CfGlyphPrimitiveKind.Polygon (a StreamGeometry,
        // not an EllipseGeometry) via ConditionalIconGlyphRenderer.DrawOp -> dc.DrawGeometry.
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Arrow icon set print");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(10)));
            sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(50)));
            sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new NumberValue(90)));

            sheet.ConditionalFormats.Add(new ConditionalFormat
            {
                AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
                Priority = 1,
                RuleType = CfRuleType.IconSet,
                IconSetStyle = "3Arrows",
            });

            sheet.PrintArea = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 1));

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = document.Pages[0].GetPageRoot(forceReload: false)!;
            var geometryDrawings = ExtractGeometryDrawings(page);

            var hasNonEllipseIconGlyph = geometryDrawings.Any(gd =>
                gd.Geometry is not EllipseGeometry &&
                ((gd.Brush is SolidColorBrush fillBrush && IconGlyphColors.Contains(fillBrush.Color)) ||
                 (gd.Pen?.Brush is SolidColorBrush penBrush && IconGlyphColors.Contains(penBrush.Color))));

            hasNonEllipseIconGlyph.Should().BeTrue(
                "the Arrow icon set glyph must draw as a non-ellipse (polygon) shape, distinct from the traffic-light ellipse glyph");
        });
    }

    // Same fixed icon-glyph palette R79 uses to prove an icon actually drew (as opposed to just the
    // cell's plain style/fill).
    private static readonly Color[] IconGlyphColors =
    [
        Color.FromRgb(0xC0, 0x00, 0x00),
        Color.FromRgb(0xED, 0x7D, 0x31),
        Color.FromRgb(0xFF, 0xC0, 0x00),
        Color.FromRgb(0x92, 0xD0, 0x50),
        Color.FromRgb(0x00, 0xB0, 0x50),
        Color.FromRgb(0x66, 0x66, 0x66),
    ];

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

    private static (Workbook Workbook, Sheet Sheet) BuildHighlightRuleWorkbook(bool withExplicitFill)
    {
        var workbook = new Workbook("CF fill print");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        var cell = Cell.FromValue(new NumberValue(2000));
        if (withExplicitFill)
        {
            var explicitStyle = workbook.RegisterStyle(new CellStyle
            {
                FillColor = new CellColor(ExplicitFillColor.R, ExplicitFillColor.G, ExplicitFillColor.B)
            });
            cell.StyleId = explicitStyle;
        }
        sheet.SetCell(address, cell);

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(address, address),
            Priority = 1,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThanOrEqual,
            Value1 = "1600",
            FormatIfTrue = new CellStyle
            {
                FillColor = new CellColor(CfFillColor.R, CfFillColor.G, CfFillColor.B)
            }
        });

        sheet.PrintArea = new GridRange(address, address);

        return (workbook, sheet);
    }

    private static List<Color> ExtractRectangleFillColors(System.Windows.Documents.FixedPage page)
    {
        var results = new List<Color>();
        foreach (var host in page.Children.OfType<VisualHost>())
        {
            if (host.Visual is null)
                continue;

            CollectRectangleFills(VisualTreeHelper.GetDrawing(host.Visual), results);
        }

        return results;
    }

    private static void CollectRectangleFills(Drawing? drawing, List<Color> results)
    {
        switch (drawing)
        {
            case GeometryDrawing { Geometry: RectangleGeometry, Brush: SolidColorBrush solid } geometryDrawing:
                results.Add(solid.Color);
                break;
            case GeometryDrawing:
                break;
            case DrawingGroup group:
                foreach (var child in group.Children)
                    CollectRectangleFills(child, results);
                break;
        }
    }
}
