using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.Tests.Charts;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

/// <summary>
/// Regression coverage for J1 (review4, group A-print-units): on any printed page after the first
/// column-page (bodyColumns[0] > 1), the chart anchor position must be translated into the print grid's
/// own pixel space before being combined with <c>bodyGridLeft</c>/<c>measurement.ColumnOffset</c>.
///
/// <see cref="ChartModel.Left"/> is an absolute pixel offset from column 1 in
/// <c>XlsxDrawingAnchorApplier</c>'s <c>width-in-chars * 8</c> convention (see
/// <see cref="ChartAnchorGeometry.SumColumnPixels"/>), which is a DIFFERENT column pixel-per-character
/// convention than the print grid itself uses (<c>ColumnWidthPixelMapper</c>'s <c>width*7+5</c>, which
/// <see cref="PrintGridMeasurement.ColumnOffset"/> is built from). On page 1, bodyColumns[0]==1 makes the
/// *8-space "pageGridLeft" term collapse to exactly 0 regardless of unit system, masking the mismatch —
/// see <see cref="PageContentRenderModelBuilderChartGeometryTests"/>, which only covers pageIndex 0. This
/// file forces a second column-page (bodyColumns[0] > 1) via a manual column page break so the mismatch
/// would manifest as a measurable X-position error if the two pixel spaces were ever summed directly.
/// </summary>
public sealed class PageContentRenderModelBuilderChartPageTwoGeometryTests
{
    private static readonly FakeTextMeasurer Measurer = new();

    [Fact]
    public void Build_ChartOnSecondColumnPageLandsAtGridSpacePosition()
    {
        var (workbook, sheet) = CreateWorkbook();
        PopulateChartSource(sheet);
        sheet.PrintArea = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 20, 6));

        // Two explicit-width columns (1-2) will land on page 1; a manual column break before column 3
        // forces columns 3+ onto page 2, so bodyColumns[0] == 3 there (> 1, exercising the bug).
        sheet.ColumnWidths[1] = 2.0;
        sheet.ColumnWidths[2] = 12.0;

        // Chart anchored 1 column + a half-column offset into page 2 (i.e. starting partway through
        // column 4), expressed in XlsxDrawingAnchorApplier's real anchor-space convention: sum of
        // columns 1-3 (*8 space) plus half of column 4's *8-space width.
        var column4AnchorSpaceWidth = sheet.DefaultColumnWidth * 8;
        var anchorLeft = ChartAnchorGeometry.SumColumnPixels(sheet, 1, 3) + column4AnchorSpaceWidth / 2;
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            Title = "Page 2 chart",
            Left = anchorLeft,
            Top = 0,
            Width = 150,
            Height = 100
        };
        sheet.Charts.Add(chart);

        var pagePlan = Paginate(sheet, columnPageBreaks: [3]);
        pagePlan.ColumnPageCount.Should().BeGreaterThan(1, "the manual column break must produce a second column-page");

        var layout = PageContentRenderModelBuilder.Build(workbook, sheet, pagePlan, 1, Measurer, new DateTime(2026, 1, 1));
        layout.Should().NotBeNull("page index 1 (the second column-page) must produce content");

        // Expected X position: translate the chart's *8-space anchor offset, and the page's *8-space
        // body-column-1 offset, into the SAME grid-space unit system before subtracting — mirroring
        // exactly what the fixed production code does (never summing *8-space with 7x+5-space values).
        var pageFirstBodyColumnAnchorSpaceOffset = ChartAnchorGeometry.SumColumnPixels(sheet, 1, 2); // columns 1-2 (page 1's body)
        var chartGridLeft = ChartAnchorGeometry.ConvertColumnOffsetToGridSpace(sheet, anchorLeft);
        var pageGridLeftInGridSpace = ChartAnchorGeometry.ConvertColumnOffsetToGridSpace(sheet, pageFirstBodyColumnAnchorSpaceOffset);
        var expectedRelativeLeft = chartGridLeft - pageGridLeftInGridSpace;

        var block = layout!.Charts.Should().ContainSingle().Subject;
        block.Bounds.Left.Should().BeApproximately(layout.GridBounds.Left + expectedRelativeLeft, 0.01);

        // Sanity: the buggy (*8-space-mixed) computation would have produced a measurably different,
        // larger X position — assert the fixed position is NOT what the old *8-space-only math gives.
        var buggyPageGridLeftAnchorSpace = pageFirstBodyColumnAnchorSpaceOffset; // never converted, old bug
        var buggyRelativeLeft = anchorLeft - buggyPageGridLeftAnchorSpace;
        buggyRelativeLeft.Should().NotBe(expectedRelativeLeft,
            "the *8-space and 7x+5-space deltas must differ here (this scenario is specifically chosen to not coincidentally cancel)");
    }

    [Fact]
    public void Build_ChartOnSecondColumnPageDoesNotDriftWithGrowingColumnCount()
    {
        // A wider first page (more columns before the chart's page) must not make the X-position error
        // grow — verifying the fix (rather than a coincidental cancellation for a specific column count).
        var (workbook, sheet) = CreateWorkbook();
        PopulateChartSource(sheet);
        sheet.PrintArea = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 20, 12));

        // 8 explicit-width default-ish columns on page 1 (matches the finding's concrete numeric
        // example: 8 columns of default width 8.43 chars each).
        for (uint col = 1; col <= 8; col++)
            sheet.ColumnWidths[col] = 8.43;

        var anchorLeft = ChartAnchorGeometry.SumColumnPixels(sheet, 1, 8) + ChartAnchorGeometry.SumColumnPixels(sheet, 9, 2);
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            Title = "Wide page 2 chart",
            Left = anchorLeft,
            Top = 0,
            Width = 150,
            Height = 100
        };
        sheet.Charts.Add(chart);

        var pagePlan = Paginate(sheet, columnPageBreaks: [9]);
        pagePlan.ColumnPageCount.Should().BeGreaterThan(1);

        var layout = PageContentRenderModelBuilder.Build(workbook, sheet, pagePlan, 1, Measurer, new DateTime(2026, 1, 1))!;

        var pageFirstBodyColumnAnchorSpaceOffset = ChartAnchorGeometry.SumColumnPixels(sheet, 1, 8);
        var chartGridLeft = ChartAnchorGeometry.ConvertColumnOffsetToGridSpace(sheet, anchorLeft);
        var pageGridLeftInGridSpace = ChartAnchorGeometry.ConvertColumnOffsetToGridSpace(sheet, pageFirstBodyColumnAnchorSpaceOffset);
        var expectedRelativeLeft = chartGridLeft - pageGridLeftInGridSpace;

        var block = layout.Charts.Should().ContainSingle().Subject;
        block.Bounds.Left.Should().BeApproximately(layout.GridBounds.Left + expectedRelativeLeft, 0.01);

        // The finding's concrete numbers: 8 default-width (8.43 char) columns give 539.52px in *8-space
        // vs 512px (8 * round(8.43*7+5) = 8*64) in real grid space — a 27.52px whole-page-1-width
        // discrepancy. Confirm our expected relative-left is computed from the grid-space (512-based)
        // total, not the *8-space (539.52-based) total.
        pageGridLeftInGridSpace.Should().BeApproximately(512.0, 0.01);
        pageFirstBodyColumnAnchorSpaceOffset.Should().BeApproximately(539.52, 0.01);
    }

    private static PagePaginationResult Paginate(Sheet sheet, IReadOnlyCollection<uint>? columnPageBreaks = null)
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
            columnPageBreaks ?? sheet.ColumnPageBreaks);
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
