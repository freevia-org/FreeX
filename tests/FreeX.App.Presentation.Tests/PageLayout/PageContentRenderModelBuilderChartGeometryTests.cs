using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.Tests.Charts;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

/// <summary>
/// Regression coverage for H20 (K-print review group): printed/previewed charts anchored on a sheet
/// with non-default row heights/column widths must land at the same position the real (non-uniform)
/// sheet geometry implies — not at a position derived from a fixed 20px row / evenly divided column
/// grid. <see cref="ChartModel.Left"/>/<see cref="ChartModel.Top"/> are absolute pixel offsets computed
/// from the sheet's real, non-uniform column widths/row heights in <c>XlsxDrawingAnchorApplier</c>'s
/// <c>SumColumnPixels</c>/<c>SumRowPixels</c> convention: <c>width-in-chars * 8</c> per column, real
/// per-row height, both skipping hidden rows/columns — see <see cref="ChartAnchorGeometry"/>. That is a
/// DIFFERENT column pixel-per-character convention than the printed grid itself uses
/// (<c>ColumnWidthPixelMapper</c>'s <c>width*7+5</c>), so expected chart X positions below are computed
/// via <see cref="ChartAnchorGeometry.ConvertColumnOffsetToGridSpace"/>, matching what the builder now
/// does internally (see J1 regression coverage in <c>PageContentRenderModelBuilderChartPageTwoGeometryTests</c>
/// for the multi-page-axis unit-mixing bug this guards against). Row/height convention is identical
/// between the two spaces, so Y assertions use the raw anchor-space sum unchanged.
/// </summary>
public sealed class PageContentRenderModelBuilderChartGeometryTests
{
    private static readonly FakeTextMeasurer Measurer = new();

    [Fact]
    public void Build_ChartOnNonUniformSheetLandsAtRealGeometryPosition()
    {
        var (workbook, sheet) = CreateWorkbook();
        PopulateChartSource(sheet);
        sheet.PrintArea = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 20, 8));

        // Non-default geometry: column A is narrow (2 chars), row 1 is tall (40px). Column B and row 2
        // stay at the sheet defaults (8.43 chars / 20px).
        sheet.ColumnWidths[1] = 2.0;
        sheet.RowHeights[1] = 40.0;

        // Chart anchored at column C (3), row 3, offset 0 — matching how XlsxDrawingAnchorApplier
        // derives ChartModel.Left/Top from real (non-uniform) column/row pixel sums.
        var expectedLeft = ChartAnchorGeometry.SumColumnPixels(sheet, 1, 2); // columns 1-2
        var expectedTop = ChartAnchorGeometry.SumRowPixels(sheet, 1, 2);    // rows 1-2
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            Title = "Real geometry chart",
            Left = expectedLeft,
            Top = expectedTop,
            Width = 200,
            Height = 120
        };
        sheet.Charts.Add(chart);

        var layout = BuildFirstPage(workbook, sheet)!;

        // The print area starts at A1 with no repeat titles, so the page's body starts at column 1/row
        // 1 and the chart's real-sheet anchor origin coincides with the page's body-grid origin: the
        // chart must land at gridLeft/gridTop + chart.Left/chart.Top translated into the grid's own
        // pixel space (columns only — row height uses the same convention in both spaces).
        var expectedGridLeft = ChartAnchorGeometry.ConvertColumnOffsetToGridSpace(sheet, expectedLeft);
        var block = layout.Charts.Should().ContainSingle().Subject;
        block.Bounds.Left.Should().BeApproximately(layout.GridBounds.Left + expectedGridLeft, 0.01);
        block.Bounds.Top.Should().BeApproximately(layout.GridBounds.Top + expectedTop, 0.01);
    }

    [Fact]
    public void Build_ChartAnchoredPastNarrowColumnDoesNotDriftWithUniformColumnWidthAssumption()
    {
        // Regression guard for the specific bug: before the fix, page-grid-relative chart placement
        // assumed every column was the same (uniform, evenly-divided-page-width) size. A sheet with a
        // narrow first column exposes the drift because the uniform column width differs sharply from
        // the real (narrow) column-1 width used to compute chart.Left.
        var (workbook, sheet) = CreateWorkbook();
        PopulateChartSource(sheet);
        sheet.PrintArea = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 20, 8));
        sheet.ColumnWidths[1] = 2.0; // narrow first column

        var realLeft = ChartAnchorGeometry.SumColumnPixels(sheet, 1, 1); // just column 1
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            Title = "Narrow column chart",
            Left = realLeft,
            Top = 0,
            Width = 150,
            Height = 100
        };
        sheet.Charts.Add(chart);

        var layout = BuildFirstPage(workbook, sheet)!;

        var block = layout.Charts.Should().ContainSingle().Subject;
        // Chart anchored right after the narrow (2-char) column 1 must land at gridLeft + (realLeft
        // translated into the grid's own pixel space), not at gridLeft + (uniform full-width column)
        // which would place it much further right.
        var expectedGridLeft = ChartAnchorGeometry.ConvertColumnOffsetToGridSpace(sheet, realLeft);
        block.Bounds.Left.Should().BeApproximately(layout.GridBounds.Left + expectedGridLeft, 0.01);
        var uniformColumnWidth = (layout.GridBounds.Right - layout.GridBounds.Left) / 8;
        block.Bounds.Left.Should().BeLessThan(layout.GridBounds.Left + uniformColumnWidth,
            "the narrow real column-1 width must place the chart well left of where a uniform column grid would");
    }

    private static PageContentLayout? BuildFirstPage(Workbook workbook, Sheet sheet) =>
        PageContentRenderModelBuilder.Build(workbook, sheet, Paginate(sheet), 0, Measurer, new DateTime(2026, 1, 1));

    private static PagePaginationResult Paginate(Sheet sheet)
    {
        var printRange = sheet.PrintArea ?? sheet.GetUsedRange()
            ?? new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        return PagePaginationPlanner.Paginate(
            printRange,
            sheet.ScaleToFit,
            sheet.PrintTitleRows,
            sheet.PrintTitleColumns,
            sheet.PaperSize,
            sheet.PageOrientation,
            sheet.PageMargins,
            sheet.RowPageBreaks,
            sheet.ColumnPageBreaks);
    }

    private static (Workbook Workbook, Sheet Sheet) CreateWorkbook(string name = "Book1.xlsx")
    {
        var workbook = new Workbook { Name = name };
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet);
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
