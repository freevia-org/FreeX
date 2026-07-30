using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.Tests.Charts;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

/// <summary>
/// Regression coverage for K1 (review5, group A-chart-print-size): the J1 fix converted chart
/// Left/Top into the print grid's own pixel space (see <see cref="ChartAnchorGeometry.ConvertColumnOffsetToGridSpace"/>/
/// <see cref="ChartAnchorGeometry.ConvertRowOffsetToGridSpace"/>) but left Width/Height in
/// <c>XlsxDrawingAnchorApplier</c>'s <c>width-in-chars * 8</c> anchor-space convention, mixing units
/// within a single <see cref="LayoutRect"/>. These tests derive Width/Height exactly the way
/// <c>XlsxDrawingAnchorApplier.ApplyToChart</c> does for a real two-cell-anchor chart (no explicit
/// anchor ext override): a sum of real column/row pixels in the *8/height convention, spanning
/// several columns/rows — the same value a chart loaded from a real xlsx file would carry — rather
/// than the flat literal widths used by the older J1 geometry tests (which mask this bug entirely).
/// </summary>
public sealed class PageContentRenderModelBuilderChartPrintSizeTests
{
    private static readonly FakeTextMeasurer Measurer = new();

    [Fact]
    public void Build_ChartWidthAndHeightAreConvertedToGridSpaceLikeLeftAndTop()
    {
        var (workbook, sheet) = CreateWorkbook();
        PopulateChartSource(sheet);
        // R98: the print area spans exactly the 10 explicitly-widened columns below (10 * 64px
        // grid-space = 640px), comfortably inside the default A4 page's printable width (~659.52px)
        // so this unit-conversion fixture doesn't also trip PageContentRenderModelBuilder's
        // Scale%/Fit-to-pages defensive residual-overflow shrink -- that shrink is exercised by its
        // own dedicated PageContentRenderModelBuilderScalePercentTests instead of incidentally here.
        sheet.PrintArea = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 20, 10));

        // Wide, uniform default-width columns so the anchor-space (*8) vs grid-space (*7+5) ratio is
        // stable and large: default width 8.43 chars -> 67.44px/col anchor-space vs 64px/col grid-space.
        for (uint col = 1; col <= 10; col++)
            sheet.ColumnWidths[col] = 8.43;
        for (uint row = 1; row <= 10; row++)
            sheet.RowHeights[row] = 20.0;

        // Chart anchored at the sheet origin, spanning exactly 4 columns and 3 rows — mirroring how
        // XlsxDrawingAnchorApplier.ApplyToChart derives Width/Height for a twoCellAnchor with no
        // explicit ext override: SumColumnPixels/SumRowPixels over the anchor's column/row span.
        var anchorWidth = ChartAnchorGeometry.SumColumnPixels(sheet, 1, 4);
        var anchorHeight = ChartAnchorGeometry.SumRowPixels(sheet, 1, 3);
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            Title = "Anchor-sized chart",
            Left = 0,
            Top = 0,
            Width = anchorWidth,
            Height = anchorHeight
        };
        sheet.Charts.Add(chart);

        var layout = BuildFirstPage(workbook, sheet)!;
        var block = layout.Charts.Should().ContainSingle().Subject;

        var expectedGridWidth = ChartAnchorGeometry.ConvertColumnExtentToGridSpace(sheet, 0, anchorWidth);
        var expectedGridHeight = ChartAnchorGeometry.ConvertRowExtentToGridSpace(sheet, 0, anchorHeight);

        block.Bounds.Width.Should().BeApproximately(expectedGridWidth, 0.01);
        block.Bounds.Height.Should().BeApproximately(expectedGridHeight, 0.01);

        // The bug: printing chart.Width/chart.Height unconverted would keep the anchor-space (*8) value
        // straight through. Confirm the fixed width/height is measurably smaller than that (grid-space
        // per-column is narrower than anchor-space for these default-width columns), i.e. the conversion
        // actually ran rather than being a no-op.
        block.Bounds.Width.Should().BeLessThan(anchorWidth,
            "grid-space per-column pixels (width*7+5) are narrower than anchor-space (width*8) for default-width columns");
        block.Bounds.Height.Should().BeApproximately(anchorHeight, 0.01,
            "row height uses the same convention in both anchor-space and grid-space, so height should be unchanged");
    }

    [Fact]
    public void Build_ChartWidthGridSpaceRatioMatchesFindingsConcreteNumericExample()
    {
        var (workbook, sheet) = CreateWorkbook();
        PopulateChartSource(sheet);
        // R98: the print area spans exactly the 8 explicitly-widened columns below (8 * 64px
        // grid-space = 512px), comfortably inside the default A4 page's printable width (~659.52px)
        // so this unit-conversion fixture doesn't also trip PageContentRenderModelBuilder's
        // Scale%/Fit-to-pages defensive residual-overflow shrink -- that shrink is exercised by its
        // own dedicated PageContentRenderModelBuilderScalePercentTests instead of incidentally here.
        sheet.PrintArea = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 20, 8));

        for (uint col = 1; col <= 8; col++)
            sheet.ColumnWidths[col] = 8.43;

        // Chart spans exactly the first 8 default-width columns.
        var anchorWidth = ChartAnchorGeometry.SumColumnPixels(sheet, 1, 8);
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            Title = "8-column chart",
            Left = 0,
            Top = 0,
            Width = anchorWidth,
            Height = 100
        };
        sheet.Charts.Add(chart);

        var layout = BuildFirstPage(workbook, sheet)!;
        var block = layout.Charts.Should().ContainSingle().Subject;

        // Finding's concrete numbers: 8 default-width (8.43 char) columns give 539.52px in anchor-space
        // vs 512px (8 * round(8.43*7+5) = 8*64) in real grid space.
        anchorWidth.Should().BeApproximately(539.52, 0.01);
        block.Bounds.Width.Should().BeApproximately(512.0, 0.01);
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
