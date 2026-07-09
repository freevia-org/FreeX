using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

/// <summary>
/// R20-print-area-page-setup-3: when BOTH <see cref="WorksheetScaleToFit.FitToPagesWide"/> and
/// <see cref="WorksheetScaleToFit.FitToPagesTall"/> are explicitly set, Excel derives ONE uniform
/// scale -- <c>min(widthScale, heightScale)</c> -- and applies that SAME scale to both axes.
/// Pre-fix, <see cref="PagePaginationPlanner"/> resolved each axis independently to its own exact
/// requested page count (a possibly-different implicit scale per axis), which over-paginates
/// whichever axis needed less shrink and produces a non-uniform scale Excel's rendering model can
/// never actually apply.
/// </summary>
public sealed class R20_print_fit_both_axes_Tests
{
    private static readonly Dictionary<uint, double> EmptyDict = new();

    private static readonly double DefaultColumnWidthChars =
        ColumnWidthPixelMapper.PixelsToColumnWidth(PagePaginationPlanner.MinimumPrintColumnWidth);

    private static GridRange Range(uint startRow, uint startCol, uint endRow, uint endCol)
    {
        var sheetId = SheetId.New();
        return new GridRange(
            new CellAddress(sheetId, startRow, startCol),
            new CellAddress(sheetId, endRow, endCol));
    }

    private static PagePaginationPlan Build(GridRange range, WorksheetScaleToFit scaleToFit) =>
        PagePaginationPlanner.BuildPlan(
            range,
            scaleToFit,
            printTitleRows: null,
            printTitleColumns: null,
            WorksheetPaperSize.A4,
            WorksheetPageOrientation.Portrait,
            WorksheetPageMargins.Narrow,
            rowHeights: EmptyDict,
            defaultRowHeight: PagePaginationPlanner.NominalRowHeight,
            columnWidths: EmptyDict,
            defaultColumnWidth: DefaultColumnWidthChars,
            headerMarginInches: 0.0,
            footerMarginInches: 0.0);

    /// <summary>
    /// "Fit to 2 pages wide by 5 pages tall" (both axes explicitly set) over a range sized so 100%-scale
    /// pagination is exactly 6 column-pages x 5 row-pages -- i.e. the row axis is ALREADY exactly at its
    /// FitToPagesTall=5 target unscaled, while the column axis needs real shrink to hit FitToPagesWide=2.
    /// Excel derives ONE uniform scale = min(2/6, 5/5) = min(33%, 100%) = 33% and applies it to BOTH
    /// axes, so the row axis shrinks too (yielding roughly 2x2 = 4 total pages). Pre-fix, FreeX resolved
    /// each axis independently: columns to exactly 2 pages (33% shrink) AND rows to exactly 5 pages (no
    /// shrink at all, since 5 already matched the natural count) -- giving 2x5 = 10 pages, 2.5x more than
    /// Excel, with a non-uniform per-axis scale Excel's model can never produce.
    /// </summary>
    [Fact]
    public void BuildPlan_BothAxesFitTo_UsesUniformMinScaleNotIndependentPerAxisScales()
    {
        // Discover the natural (unscaled) per-page capacity for this paper size/margins/defaults so the
        // test doesn't hardcode brittle pixel-derived row/column counts.
        var baseline = Build(Range(1, 1, 2000, 2000), WorksheetScaleToFit.Default);
        var baseCols = baseline.Capacity.ColumnsPerPage;
        var baseRows = baseline.Capacity.RowsPerPage;

        var totalCols = baseCols * 6;
        var totalRows = baseRows * 5;

        var naturalPlan = Build(Range(1, 1, totalRows, totalCols), WorksheetScaleToFit.Default);
        naturalPlan.ColumnPageCount.Should().Be(6, "sanity check: range sized to 6 natural column-pages");
        naturalPlan.RowPageCount.Should().Be(5,
            "sanity check: range sized to 5 natural row-pages -- already exactly at the FitToPagesTall target");

        var plan = Build(
            Range(1, 1, totalRows, totalCols),
            new WorksheetScaleToFit(ScalePercent: null, FitToPagesWide: 2, FitToPagesTall: 5));

        plan.ColumnPageCount.Should().Be(2, "the column axis must hit its explicit Fit-to-2-wide target");
        plan.RowPageCount.Should().BeLessThan(5,
            "Excel applies the SAME uniform scale (min(2/6, 5/5) = 33%) to the row axis too, so it " +
            "shrinks along with the column axis instead of staying at its already-met 5-page target");
        plan.PageCount.Should().BeLessThan(10,
            "the pre-fix independent-per-axis resolution produces 2x5=10 pages; Excel's single uniform " +
            "scale produces roughly 2x2=4");
    }
}
