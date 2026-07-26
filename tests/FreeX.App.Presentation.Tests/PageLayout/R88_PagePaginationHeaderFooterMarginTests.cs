using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

/// <summary>
/// R88-services-page-setup-margins-5-1: the header/footer margin is the distance from the page edge
/// to the header/footer band, which sits WITHIN the top/bottom margin band (Excel's guide-line model)
/// as long as it doesn't exceed it. The body height must only shrink further when a header/footer
/// margin exceeds its corresponding top/bottom margin -- treating it as an unconditional additional
/// reservation (the old formula) silently lost real body height even for Excel's own defaults (0.3in
/// header/footer margin under a 0.75in top/bottom margin), where nothing extra should be reserved.
/// </summary>
public sealed class R88_PagePaginationHeaderFooterMarginTests
{
    private static readonly Dictionary<uint, double> EmptyDict = new();

    private static GridRange Range(uint startRow, uint startCol, uint endRow, uint endCol)
    {
        var sheetId = SheetId.New();
        return new GridRange(
            new CellAddress(sheetId, startRow, startCol),
            new CellAddress(sheetId, endRow, endCol));
    }

    /// <summary>
    /// Primary regression test (fails before the fix, passes after): Letter/Portrait with Excel's own
    /// Normal margins (Top=Bottom=0.75in) and the default header/footer margins (0.3in each, smaller
    /// than the top/bottom margins) must yield the same printable-body row capacity as if there were no
    /// header/footer at all -- floor(9.5in * 96dpi / 20px) = floor(912 / 20) = 45 rows per page. The old
    /// formula subtracted the header+footer margins (57.6px) on top of the margins, giving only 42.
    /// </summary>
    [Fact]
    public void CalculatePageCapacity_LetterNormalMarginsWithDefaultHeaderFooter_MatchesExcelRowsPerPage()
    {
        var range = Range(1, 1, 500, 5);
        var margins = WorksheetPageMargins.Normal; // Top = Bottom = 0.75in
        var noScale = WorksheetScaleToFit.Default;

        var capacity = PagePaginationPlanner.CalculatePageCapacity(
            range,
            noScale,
            printTitleRows: null,
            printTitleColumns: null,
            WorksheetPaperSize.Letter,
            WorksheetPageOrientation.Portrait,
            margins,
            rowHeights: EmptyDict,
            defaultRowHeight: 20.0,
            columnWidths: EmptyDict,
            defaultColumnWidth: 8.43,
            headerMarginInches: 0.3,
            footerMarginInches: 0.3);

        capacity.RowsPerPage.Should().Be(45,
            because: "Letter portrait is 11in tall; minus Normal 0.75in top/bottom margins that's a 9.5in " +
                      "(912px) printable body -- the 0.3in default header/footer margins fit entirely within " +
                      "that margin band and must not shrink it further, matching real Excel's row count");
    }

    /// <summary>
    /// No-regression sibling: when the header/footer margin genuinely exceeds the corresponding
    /// top/bottom margin, the body must still shrink (Excel's guide-line model reacts in that case too),
    /// so the PR5 shrink behavior the original fix intended to add is preserved by the corrected formula.
    /// </summary>
    [Fact]
    public void CalculatePageCapacity_OversizedHeaderFooterMargin_StillReducesRowsPerPage()
    {
        var range = Range(1, 1, 500, 5);
        var margins = WorksheetPageMargins.Normal; // Top = Bottom = 0.75in
        var noScale = WorksheetScaleToFit.Default;

        var baseline = PagePaginationPlanner.CalculatePageCapacity(
            range,
            noScale,
            printTitleRows: null,
            printTitleColumns: null,
            WorksheetPaperSize.Letter,
            WorksheetPageOrientation.Portrait,
            margins,
            rowHeights: EmptyDict,
            defaultRowHeight: 20.0,
            columnWidths: EmptyDict,
            defaultColumnWidth: 8.43,
            headerMarginInches: 0.0,
            footerMarginInches: 0.0);

        var withOversizedHeaderFooter = PagePaginationPlanner.CalculatePageCapacity(
            range,
            noScale,
            printTitleRows: null,
            printTitleColumns: null,
            WorksheetPaperSize.Letter,
            WorksheetPageOrientation.Portrait,
            margins,
            rowHeights: EmptyDict,
            defaultRowHeight: 20.0,
            columnWidths: EmptyDict,
            defaultColumnWidth: 8.43,
            headerMarginInches: 1.0,
            footerMarginInches: 1.0);

        withOversizedHeaderFooter.RowsPerPage.Should().BeLessThan(baseline.RowsPerPage,
            because: "a 1in header/footer margin exceeds the 0.75in top/bottom margin, so the body must " +
                      "shrink to accommodate it, unlike the default 0.3in margins which fit within the band");
    }
}
