using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R111-app-host-headerfooter-scale-with-document-1: Sheet.HeaderFooterScaleWithDocument (Excel's Page
/// Setup &gt; Header/Footer &gt; "Scale with document" checkbox, default checked) is round-tripped and
/// user-editable via HeaderFooterDialog/PageSetupDialog, but before this fix neither
/// <see cref="PrintRenderer.RenderPageVisual"/> nor any header/footer drawing helper ever consulted it --
/// header/footer text was always drawn at each run's authored/default font size regardless of the
/// page's own print scale (explicit Scale% or the ratio implied by Fit-to-N-pages), even though the flag
/// defaults to true (meaning Excel's real default behavior -- shrinking header/footer text right along
/// with the grid -- was silently never reproduced). These tests render through the real
/// <see cref="PrintRenderer.RenderWorksheet"/> entry point (not a hand-built visual) and assert on the
/// actual drawn text-overlay records' <c>FontSize</c>, which mirrors the exact font size passed to the
/// <c>DrawingContext.DrawText</c> ink call for that run.
/// </summary>
public sealed class R111_HeaderFooterScaleWithDocumentTests
{
    private const double DefaultHeaderFooterFontSize = 9.0;

    [Fact]
    public void RenderWorksheet_ScaleWithDocumentTrueAtHalfPageScale_ShrinksFooterFontSizeToMatch()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("ScaledFooter.xlsx");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("DATA"));
            sheet.PageFooter = new WorksheetHeaderFooter("", "Confidential", "");
            sheet.ScaleToFit = new WorksheetScaleToFit(50, null, null);
            // Default is already true, but set explicitly so the intent of this test is unambiguous
            // and doesn't silently pass/fail based on Sheet's own default changing later.
            sheet.HeaderFooterScaleWithDocument = true;

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = document.Pages[0].GetPageRoot(forceReload: false)!;
            var overlay = PdfTextOverlayExtractor.Extract(page)
                .Should().ContainSingle(o => o.Text == "Confidential").Subject;

            // Before the fix this was always 9.0 (the unscaled default), completely ignoring the 50%
            // page scale -- Excel's real "Scale with document" (default checked) behavior halves the
            // header/footer font size right along with the grid.
            overlay.FontSize.Should().BeApproximately(DefaultHeaderFooterFontSize * 0.5, 0.01,
                "Sheet.HeaderFooterScaleWithDocument defaults to true, so a 50% page scale must shrink " +
                "the footer text's font size by the same ratio as the grid, matching Excel");
        });
    }

    /// <summary>
    /// No-regression sibling: when the user unchecks "Scale with document", Excel keeps header/footer
    /// text at its authored/default size no matter how the page content itself is scaled. This must
    /// keep working exactly as before this fix -- the fix must be gated on the flag, not unconditional.
    /// </summary>
    [Fact]
    public void RenderWorksheet_ScaleWithDocumentFalseAtHalfPageScale_KeepsFooterFontSizeConstant()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("UnscaledFooter.xlsx");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("DATA"));
            sheet.PageFooter = new WorksheetHeaderFooter("", "Confidential", "");
            sheet.ScaleToFit = new WorksheetScaleToFit(50, null, null);
            sheet.HeaderFooterScaleWithDocument = false;

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = document.Pages[0].GetPageRoot(forceReload: false)!;
            var overlay = PdfTextOverlayExtractor.Extract(page)
                .Should().ContainSingle(o => o.Text == "Confidential").Subject;

            overlay.FontSize.Should().BeApproximately(DefaultHeaderFooterFontSize, 0.01,
                "unchecking Scale with document must keep the footer text at its authored/default size " +
                "regardless of the page's own print scale");
        });
    }

    /// <summary>
    /// The grid's own content scale (which this fix must never disturb) is unaffected by
    /// HeaderFooterScaleWithDocument in either direction -- a 50% page scale still halves ordinary cell
    /// text overlays whether the header/footer flag is on or off. Compares against a 100%-scale render
    /// of the same sheet rather than a hard-coded font size, since the grid's default cell font size is
    /// resolved independently of the header/footer default (PrintRenderer.CellText's
    /// DefaultPrintedCellFontSizePoints, not PrintFontSize).
    /// </summary>
    [Fact]
    public void RenderWorksheet_HeaderFooterScaleWithDocumentFalse_StillShrinksGridCellText()
    {
        StaTestRunner.Run(() =>
        {
            double RenderCellFontSize(int? scalePercent, bool headerFooterScaleWithDocument)
            {
                var workbook = new Workbook("GridScaleGrid.xlsx");
                var sheet = workbook.AddSheet("Sheet1");
                sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("DATA"));
                sheet.HeaderFooterScaleWithDocument = headerFooterScaleWithDocument;
                if (scalePercent is { } percent)
                    sheet.ScaleToFit = new WorksheetScaleToFit(percent, null, null);

                var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
                var page = document.Pages[0].GetPageRoot(forceReload: false)!;
                return PdfTextOverlayExtractor.Extract(page)
                    .Should().ContainSingle(o => o.Text == "DATA").Subject.FontSize;
            }

            var baselineFontSize = RenderCellFontSize(scalePercent: null, headerFooterScaleWithDocument: false);
            var scaledFontSize = RenderCellFontSize(scalePercent: 50, headerFooterScaleWithDocument: false);

            scaledFontSize.Should().BeApproximately(baselineFontSize * 0.5, 0.01,
                "HeaderFooterScaleWithDocument only governs header/footer text -- the grid's own cell " +
                "text must still scale with the page's print scale regardless of this flag's value");
        });
    }
}
