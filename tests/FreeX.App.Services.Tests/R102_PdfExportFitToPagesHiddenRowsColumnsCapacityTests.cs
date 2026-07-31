using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// PDF-export twin of
/// tests/FreeX.App.Presentation.Tests/PageLayout/R102_FitToPagesHiddenRowsColumnsCapacityTests.cs:
/// <see cref="SheetPdfPageSetupResolver"/>'s own private <c>CountBodyItems</c>/<c>CountRepeatItems</c>
/// had the identical shape as (and the identical hidden-item counting bug as)
/// <c>PagePaginationPlanner.ApplyScaleToFitCapacity</c>'s helpers of the same name, so a sheet with
/// many hidden rows/columns and an explicit "Fit to N pages" request got the PDF export's row/column
/// capacity resolved against the raw row/column span instead of the real VISIBLE content -- collapsing
/// far more visible content onto a page than Excel (and the WPF print-preview path) would.
///
/// R102: both bugs are fixed and the counting rule is now consolidated into
/// <c>PageGeometryRules.CountRepeatItems</c>/<c>PageGeometryRules.CountBodyItems</c>
/// (src/FreeX.App.Presentation/PageLayout/PageGeometryRules.cs), called by both
/// <see cref="SheetPdfPageSetupResolver"/> (PDF export) and <c>PagePaginationPlanner</c> (WPF print).
/// These tests go through <see cref="SheetPdfPageSetupResolver.ResolveCapacity"/> -- the real PDF-export
/// entry point -- and assert the exact rows/columns-per-page a user would observe.
/// </summary>
public sealed class R102_PdfExportFitToPagesHiddenRowsColumnsCapacityTests
{
    // Letter portrait (11.0in tall) with 2.375in top/bottom margins gives an exact 600px printable
    // body height at 96 dpi ((11.0 - 4.75) * 96 = 600), and the default 20px row height gives an
    // exact 30 baseline rows/page (600 / 20 = 30) -- matching the presentation-tier twin test's setup.
    private static readonly WorksheetPageMargins TallBodyMargins = new(Left: 0.75, Right: 0.75, Top: 2.375, Bottom: 2.375);

    // Letter portrait (8.5in wide) with 2.25in left/right margins gives an exact 384px printable body
    // width at 96 dpi ((8.5 - 4.5) * 96 = 384), and a 40px column width gives an exact 9 baseline
    // columns/page (384 / 40 = 9.6 -> floor 9).
    private static readonly WorksheetPageMargins WideBodyMargins = new(Left: 2.25, Right: 2.25, Top: 0.75, Bottom: 0.75);

    /// <summary>
    /// Reproduces the defect evidence: print range rows 1-300 with only rows 1-90 visible
    /// (91-300 hidden), "Fit to 3 pages tall". 90 visible rows over a 30-rows/page baseline need
    /// EXACTLY 3 pages at 100% scale (Excel's ground truth) -- no shrink required at all. The bug
    /// resolved the fit-to-pages target against the raw 300-row span instead of the 90 visible rows,
    /// deriving a hugely inflated per-page budget that swallowed all 90 visible rows onto one page
    /// (i.e. RowsPerPage far exceeding 30).
    /// </summary>
    [Fact]
    public void ResolveCapacity_FitToPagesTall_WithHiddenRows_ExcludesHiddenRowsFromCapacityTarget()
    {
        var workbook = new Workbook("W");
        var sheet    = workbook.AddSheet("S");
        sheet.PaperSize       = WorksheetPaperSize.Letter;
        sheet.PageOrientation = WorksheetPageOrientation.Portrait;
        sheet.PageMargins     = TallBodyMargins;
        sheet.HeaderMargin    = 0.0;
        sheet.FooterMargin    = 0.0;
        sheet.ScaleToFit      = new WorksheetScaleToFit(ScalePercent: null, FitToPagesWide: null, FitToPagesTall: 3);

        for (var row = 91u; row <= 300u; row++)
            sheet.HiddenRows.Add(row);

        var range = GridRange.Parse("A1:A300", sheet.Id);
        var capacity = SheetPdfPageSetupResolver.ResolveCapacity(sheet, range);

        capacity.RowsPerPage.Should().Be(30,
            "90 visible rows over a 30-rows/page baseline need exactly 3 pages at 100% scale, matching " +
            "Excel's fit-to-3-pages-tall result -- counting the 210 hidden rows (91-300) in the capacity " +
            "target inflates the per-page budget so all 90 visible rows would wrongly collapse onto one page");
    }

    /// <summary>Column-axis counterpart: 300-column range, only columns 1-27 visible, "Fit to 3 pages wide".</summary>
    [Fact]
    public void ResolveCapacity_FitToPagesWide_WithHiddenColumns_ExcludesHiddenColumnsFromCapacityTarget()
    {
        var workbook = new Workbook("W");
        var sheet    = workbook.AddSheet("S");
        sheet.PaperSize        = WorksheetPaperSize.Letter;
        sheet.PageOrientation  = WorksheetPageOrientation.Portrait;
        sheet.PageMargins      = WideBodyMargins;
        sheet.HeaderMargin     = 0.0;
        sheet.FooterMargin     = 0.0;
        sheet.DefaultColumnWidth = ColumnWidthPixelMapper.PixelsToColumnWidth(40.0);
        sheet.ScaleToFit       = new WorksheetScaleToFit(ScalePercent: null, FitToPagesWide: 3, FitToPagesTall: null);

        for (var col = 28u; col <= 300u; col++)
            sheet.HiddenCols.Add(col);

        var range = GridRange.Parse("A1:KN1", sheet.Id); // column 300 = KN
        var capacity = SheetPdfPageSetupResolver.ResolveCapacity(sheet, range);

        capacity.ColumnsPerPage.Should().Be(9,
            "27 visible columns over a 9-columns/page baseline need exactly 3 pages at 100% scale -- " +
            "counting the 273 hidden columns (28-300) in the capacity target inflates the per-page budget " +
            "so all 27 visible columns would wrongly collapse onto one page");
    }

    /// <summary>
    /// No-regression sibling: the identical "Fit to 3 pages tall" request over the SAME 300-row range
    /// with NO hidden rows must still resolve against the full 300-row body (100 rows/page, 3 pages of
    /// 100 rows) -- proving the hidden-row exclusion only kicks in when rows are actually hidden.
    /// </summary>
    [Fact]
    public void ResolveCapacity_FitToPagesTall_NoHiddenRows_StillResolvesAgainstFullRange()
    {
        var workbook = new Workbook("W");
        var sheet    = workbook.AddSheet("S");
        sheet.PaperSize       = WorksheetPaperSize.Letter;
        sheet.PageOrientation = WorksheetPageOrientation.Portrait;
        sheet.PageMargins     = TallBodyMargins;
        sheet.HeaderMargin    = 0.0;
        sheet.FooterMargin    = 0.0;
        sheet.ScaleToFit      = new WorksheetScaleToFit(ScalePercent: null, FitToPagesWide: null, FitToPagesTall: 3);

        var range = GridRange.Parse("A1:A300", sheet.Id);
        var capacity = SheetPdfPageSetupResolver.ResolveCapacity(sheet, range);

        capacity.RowsPerPage.Should().Be(100,
            "with no hidden rows, all 300 rows are body rows: fit-to-3-pages-tall resolves to 100 " +
            "rows/page (300/3) -- must not regress once hidden-row exclusion is introduced");
    }

    /// <summary>
    /// PDF export and WPF print/print-preview must now agree exactly on the resolved capacity for the
    /// identical hidden-rows + fit-to-pages page setup, proving the two pagination paths are no longer
    /// split-brained on this rule.
    /// </summary>
    [Fact]
    public void ResolveCapacity_FitToPagesTall_WithHiddenRows_MatchesWpfPagePaginationPlanner()
    {
        var workbook = new Workbook("W");
        var sheet    = workbook.AddSheet("S");
        sheet.PaperSize       = WorksheetPaperSize.Letter;
        sheet.PageOrientation = WorksheetPageOrientation.Portrait;
        sheet.PageMargins     = TallBodyMargins;
        sheet.HeaderMargin    = 0.0;
        sheet.FooterMargin    = 0.0;
        sheet.ScaleToFit      = new WorksheetScaleToFit(ScalePercent: null, FitToPagesWide: null, FitToPagesTall: 3);

        for (var row = 91u; row <= 300u; row++)
            sheet.HiddenRows.Add(row);

        var range = GridRange.Parse("A1:A300", sheet.Id);

        var pdfCapacity = SheetPdfPageSetupResolver.ResolveCapacity(sheet, range);

        // BuildPlan is the WPF print-preview path's real product entry point (PagePaginationPlanner's
        // plain CalculatePageCapacity overloads do not accept isRowHidden/isColumnHidden at all, so
        // they cannot express this hidden-rows scenario -- only BuildPlan, which every desktop print/
        // print-preview surface actually calls, does).
        var wpfPlan = PagePaginationPlanner.BuildPlan(
            range,
            sheet.ScaleToFit,
            sheet.PrintTitleRows,
            sheet.PrintTitleColumns,
            sheet.PaperSize,
            sheet.PageOrientation,
            sheet.PageMargins,
            sheet.RowHeights,
            sheet.DefaultRowHeight,
            sheet.ColumnWidths,
            sheet.DefaultColumnWidth,
            sheet.HeaderMargin,
            sheet.FooterMargin,
            isRowHidden: sheet.IsRowEffectivelyHidden,
            isColumnHidden: sheet.IsColEffectivelyHidden);

        pdfCapacity.RowsPerPage.Should().Be((uint)wpfPlan.RowPlans[0].BodyRows.Count,
            "the PDF-export page capacity must match the WPF print-preview page's actual row count for " +
            "the same hidden-rows + fit-to-pages page setup");
        wpfPlan.RowPageCount.Should().Be(3, "90 visible rows over 30 rows/page need exactly 3 pages");
        pdfCapacity.RowsPerPage.Should().Be(30, "both paths must resolve to Excel's ground truth of 30 rows/page");
    }
}
