using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// PDF-export twin of PagePaginationPlanner's R107-presentation-pagination-fit-to-pages-hidden-average-exclusion
/// fix (src/FreeX.App.Presentation/PageLayout/PagePaginationPlanner.cs, AverageRowHeightPixels /
/// AverageColumnWidthPixels): <see cref="SheetPdfPageSetupResolver"/>'s private AverageRowHeightPx /
/// AverageColumnWidthPx summed EVERY row/column height in the print range -- including hidden ones --
/// and divided by the full count, unlike every OTHER computation in that same file (CountBodyItems /
/// CountRepeatItems / ComputeAccumulationBreakPoints), which already thread
/// Sheet.IsRowEffectivelyHidden / IsColEffectivelyHidden through. A hidden row/column with a wildly
/// different recorded size (e.g. a very tall hidden row) skewed the average used to derive the
/// "natural" (unscaled) rows/columns-per-page capacity, producing a far smaller page capacity than the
/// visible content alone would need -- unlike Excel and unlike FreeX's own WPF/Avalonia print-preview
/// path (R107).
///
/// R108: AverageRowHeightPx / AverageColumnWidthPx now delegate to the shared, already hidden-aware
/// <c>PagePaginationPlanner.AverageRowHeightPixels</c> / <c>AverageColumnWidthPixels</c> instead of
/// re-summing locally, so the PDF export path and the WPF/Avalonia print-preview path share one
/// implementation.
///
/// These tests go through <see cref="SheetPdfPageSetupResolver.ResolveCapacity"/> -- the real PDF-export
/// entry point reached from <c>WorkbookExportPrintPlanner</c> -- with NO Fit-to-N-pages scaling active,
/// so the resolved capacity is the raw average-derived ("natural") value the defect targets directly
/// (unlike R102, which only covered the separate CountBodyItems/CountRepeatItems fit-to-pages bug).
/// </summary>
public sealed class R108_PdfExportHiddenRowColumnAverageExclusionTests
{
    // Letter portrait (11.0in tall) with 2.375in top/bottom margins gives an exact 600px printable
    // body height at 96 dpi ((11.0 - 4.75) * 96 = 600) -- same fixture as
    // R102_PdfExportFitToPagesHiddenRowsColumnsCapacityTests.TallBodyMargins.
    private static readonly WorksheetPageMargins TallBodyMargins = new(Left: 0.75, Right: 0.75, Top: 2.375, Bottom: 2.375);

    // Letter portrait (8.5in wide) with 2.25in left/right margins gives an exact 384px printable body
    // width at 96 dpi ((8.5 - 4.5) * 96 = 384) -- same fixture as
    // R102_PdfExportFitToPagesHiddenRowsColumnsCapacityTests.WideBodyMargins.
    private static readonly WorksheetPageMargins WideBodyMargins = new(Left: 2.25, Right: 2.25, Top: 0.75, Bottom: 0.75);

    /// <summary>
    /// Reproduces the defect: print range rows 1-300, only rows 1-30 visible (31-300 hidden with a
    /// deliberately huge 1000px recorded height -- e.g. a collapsed but not-yet-cleared tall row). No
    /// Fit-to-N-pages scaling is active, so the resolved capacity is the raw average-derived value.
    /// Excel's ground truth (and R107's WPF path) excludes the hidden rows: average = 20px (the 30
    /// visible rows' real height), giving 600/20 = 30 rows/page. Folding the hidden rows' 1000px height
    /// into the average (the pre-fix bug) inflates the average to ~902px, collapsing the capacity to a
    /// single row/page (600/902 floors to 0, clamped to the 1-row minimum) -- a page that fits only ONE
    /// visible row when Excel would fit all 30.
    /// </summary>
    [Fact]
    public void ResolveCapacity_NoScaling_WithHiddenTallRows_ExcludesHiddenRowsFromAverage()
    {
        var workbook = new Workbook("W");
        var sheet    = workbook.AddSheet("S");
        sheet.PaperSize       = WorksheetPaperSize.Letter;
        sheet.PageOrientation = WorksheetPageOrientation.Portrait;
        sheet.PageMargins     = TallBodyMargins;
        sheet.HeaderMargin    = 0.0;
        sheet.FooterMargin    = 0.0;
        // sheet.ScaleToFit left at its default (no explicit scale / fit-to-pages) so the resolved
        // capacity is the raw, unscaled average-derived value the defect targets.

        for (var row = 31u; row <= 300u; row++)
        {
            sheet.HiddenRows.Add(row);
            sheet.RowHeights[row] = 1000.0;
        }

        var range = GridRange.Parse("A1:A300", sheet.Id);
        var capacity = SheetPdfPageSetupResolver.ResolveCapacity(sheet, range);

        capacity.RowsPerPage.Should().Be(30,
            "the 270 hidden rows' 1000px recorded height must not be folded into the average -- only " +
            "the 30 visible rows (20px default height) should count, giving 600px / 20px = 30 rows/page, " +
            "matching Excel and the WPF print-preview path (R107)");
    }

    /// <summary>Column-axis counterpart: 300-column range, only columns 1-27 visible (28-300 hidden with a huge recorded width).</summary>
    [Fact]
    public void ResolveCapacity_NoScaling_WithHiddenWideColumns_ExcludesHiddenColumnsFromAverage()
    {
        var workbook = new Workbook("W");
        var sheet    = workbook.AddSheet("S");
        sheet.PaperSize          = WorksheetPaperSize.Letter;
        sheet.PageOrientation    = WorksheetPageOrientation.Portrait;
        sheet.PageMargins        = WideBodyMargins;
        sheet.HeaderMargin       = 0.0;
        sheet.FooterMargin       = 0.0;
        sheet.DefaultColumnWidth = 5.0; // -> ColumnWidthToPixels(5.0) = 40px, matching the 384px body's 9-col baseline.

        for (var col = 28u; col <= 300u; col++)
        {
            sheet.HiddenCols.Add(col);
            sheet.ColumnWidths[col] = 200.0; // -> ColumnWidthToPixels(200.0) = 1405px, a deliberately huge outlier.
        }

        var range = GridRange.Parse("A1:KN1", sheet.Id); // column 300 = KN
        var capacity = SheetPdfPageSetupResolver.ResolveCapacity(sheet, range);

        capacity.ColumnsPerPage.Should().Be(9,
            "the 273 hidden columns' 1405px recorded width must not be folded into the average -- only " +
            "the 27 visible columns (40px default width) should count, giving 384px / 40px = 9 columns/page");
    }

    /// <summary>
    /// No-regression sibling: the identical hidden-tall-rows setup but with NO rows actually hidden
    /// (all 300 rows visible, still 1000px each past row 30) must average ALL 300 rows -- proving the
    /// exclusion only kicks in for rows Sheet.IsRowEffectivelyHidden reports as hidden, not merely
    /// because they carry an oversized recorded height.
    /// </summary>
    [Fact]
    public void ResolveCapacity_NoScaling_TallRowsNotHidden_StillAveragesAcrossFullRange()
    {
        var workbook = new Workbook("W");
        var sheet    = workbook.AddSheet("S");
        sheet.PaperSize       = WorksheetPaperSize.Letter;
        sheet.PageOrientation = WorksheetPageOrientation.Portrait;
        sheet.PageMargins     = TallBodyMargins;
        sheet.HeaderMargin    = 0.0;
        sheet.FooterMargin    = 0.0;

        for (var row = 31u; row <= 300u; row++)
            sheet.RowHeights[row] = 1000.0; // NOT hidden this time.

        var range = GridRange.Parse("A1:A300", sheet.Id);
        var capacity = SheetPdfPageSetupResolver.ResolveCapacity(sheet, range);

        capacity.RowsPerPage.Should().Be(1,
            "with none of the tall rows hidden, all 300 rows are real visible body rows: the average " +
            "(~902px) legitimately collapses the capacity to the 1-row minimum -- this must not regress " +
            "once hidden-row exclusion is introduced (only actually-hidden rows should be excluded)");
    }
}
