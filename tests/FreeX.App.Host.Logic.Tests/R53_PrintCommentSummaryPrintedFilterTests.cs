using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R53-io-comment-print-position-3-1: PrintRenderer.RenderWorksheet's "Comments: At end of sheet"
/// appendix used to pass the sheet's FULL, unfiltered Comments/ThreadedComments dictionaries into
/// PrintCommentSummaryPlanner.BuildPages with no restriction to the rows/columns the render plan
/// actually decided to print -- so a note on a hidden row (or a cell outside the configured print
/// area) still showed up in the trailing comments page, even though the printed grid pages
/// themselves correctly exclude that row/cell. Real Excel only lists notes on cells that are
/// actually printed. The fix restricts the appendix to comments whose cell is present on at least
/// one page of the render plan, matching the "As displayed" overlay path's existing
/// rows/columns-actually-printed restriction.
/// </summary>
public sealed class R53_PrintCommentSummaryPrintedFilterTests
{
    [Fact]
    public void RenderWorksheet_AtEnd_ExcludesCommentOnHiddenRow()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Hidden row comment");
            var sheet = workbook.AddSheet("Sheet1");
            var a1 = new CellAddress(sheet.Id, 1, 1);
            var z50 = new CellAddress(sheet.Id, 50, 26); // Z50, row 50 will be hidden
            sheet.SetCell(a1, new TextValue("Total"));
            sheet.SetCell(z50, new TextValue("Helper"));
            sheet.Comments[a1] = "Total is final";
            sheet.Comments[z50] = "scratch calc, ignore";
            sheet.HiddenRows.Add(50);
            sheet.PrintComments = WorksheetPrintComments.AtEnd;

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var summaryPage = document.Pages[document.Pages.Count - 1].GetPageRoot(forceReload: false)!;
            var overlayTexts = PdfTextOverlayExtractor.Extract(summaryPage)
                .Select(overlay => overlay.Text)
                .ToList();

            // Pre-fix, both notes were listed unconditionally (row 50's note has no
            // row/print-area filtering applied at all), so this would fail: the hidden row's note
            // would still appear. Post-fix, only the printed A1 note is listed.
            overlayTexts.Should().Contain("A1: Total is final");
            overlayTexts.Should().NotContain(text => text.Contains("scratch calc, ignore", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void RenderWorksheet_AtEnd_StillIncludesCommentOnOrdinaryPrintedCell()
    {
        // Sibling/no-regression case: an ordinary comment on a normally-visible, in-print-area cell
        // (the overwhelming majority of real notes) must still appear in the "At end" appendix
        // after adding the printed-cell filter.
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Ordinary comment");
            var sheet = workbook.AddSheet("Sheet1");
            var a1 = new CellAddress(sheet.Id, 1, 1);
            sheet.SetCell(a1, new TextValue("Total"));
            sheet.Comments[a1] = "Visible note";
            sheet.PrintComments = WorksheetPrintComments.AtEnd;

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var summaryPage = document.Pages[document.Pages.Count - 1].GetPageRoot(forceReload: false)!;
            var overlayTexts = PdfTextOverlayExtractor.Extract(summaryPage)
                .Select(overlay => overlay.Text)
                .ToList();

            overlayTexts.Should().Contain("A1: Visible note");
        });
    }
}
