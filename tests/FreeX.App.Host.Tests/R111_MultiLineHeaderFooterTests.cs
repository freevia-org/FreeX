using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R111-app-host-multiline-header-footer-1: Excel's Header/Footer editor lets a user insert a
/// literal line break inside one section (Alt+Enter), producing a <see cref="WorksheetHeaderFooter"/>
/// string with an embedded '\n'. <see cref="Presentation.PageLayout.PagePrintTextPlanner.TokenizeSectionText"/>
/// treats '\n' as an ordinary character and never splits it into separate runs, so before this fix
/// <see cref="PrintRenderer"/>'s <c>DrawHeaderFooterFormattedRuns</c> handed the whole multi-line
/// string to a single WPF <c>FormattedText</c> with <c>MaxLineCount = 1</c> -- per WPF's documented
/// behavior, everything past the first line is simply not drawn and the fixed 18px band
/// (<c>CalculateHeaderFooterLineHeight</c>) never grew to make room for it either. These tests render
/// through the real <see cref="PrintRenderer.RenderWorksheet"/> entry point (not a hand-built visual)
/// and assert on the actual drawn text-overlay records, which are populated at the exact same
/// <c>textPoint</c> the corresponding <c>DrawingContext.DrawText</c> ink call uses.
/// </summary>
public sealed class R111_MultiLineHeaderFooterTests
{
    [Fact]
    public void RenderWorksheet_MultiLineFooterSection_BothLinesAreDrawnOnSeparateRows()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("MultiLineFooter.xlsx");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("DATA"));
            sheet.PageFooter = new WorksheetHeaderFooter("", "Confidential\nDo Not Distribute", "");

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = document.Pages[0].GetPageRoot(forceReload: false)!;
            var overlays = PdfTextOverlayExtractor.Extract(page);

            // Before the fix, the whole "Confidential\nDo Not Distribute" string was handed to a
            // single FormattedText with MaxLineCount = 1, so only the first line was ever drawn and
            // no overlay with the second line's own exact text ever existed.
            var line1 = overlays.Should().ContainSingle(o => o.Text == "Confidential").Subject;
            var line2 = overlays.Should().ContainSingle(o => o.Text == "Do Not Distribute").Subject;

            // The two lines must be drawn on genuinely different rows, not stacked/overlapping at the
            // same Y (which would just mean the text was measured but silently discarded elsewhere).
            line2.Y.Should().NotBe(line1.Y,
                "the second line of a multi-line footer section must be drawn on its own row, not " +
                "silently dropped or overlaid on top of the first line");
        });
    }

    [Fact]
    public void RenderWorksheet_MultiLineFooterSection_BandGrowsToFitBothLines()
    {
        StaTestRunner.Run(() =>
        {
            var workbookSingle = new Workbook("SingleLineFooter.xlsx");
            var sheetSingle = workbookSingle.AddSheet("Sheet1");
            sheetSingle.PageFooter = new WorksheetHeaderFooter("", "Confidential", "");
            var singleLineHeight = PrintRenderer.CalculateHeaderFooterLineHeight(
                sheetSingle.PageFooter, sheetSingle.PageFooterPictures, draftQuality: false);

            var workbookMulti = new Workbook("MultiLineFooter.xlsx");
            var sheetMulti = workbookMulti.AddSheet("Sheet1");
            sheetMulti.PageFooter = new WorksheetHeaderFooter("", "Confidential\nDo Not Distribute", "");
            var multiLineHeight = PrintRenderer.CalculateHeaderFooterLineHeight(
                sheetMulti.PageFooter, sheetMulti.PageFooterPictures, draftQuality: false);

            multiLineHeight.Should().BeGreaterThan(singleLineHeight,
                "a two-line footer section must reserve a taller band than a one-line section so the " +
                "second line has room to draw instead of overlapping the printed grid");
        });
    }

    /// <summary>
    /// No-regression sibling: a plain single-line footer section (no embedded newline) must keep
    /// rendering exactly as before -- a single overlay with the unsplit text, drawn once.
    /// </summary>
    [Fact]
    public void RenderWorksheet_SingleLineFooterSection_StillRendersOneOverlay()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("PlainFooter.xlsx");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("DATA"));
            sheet.PageFooter = new WorksheetHeaderFooter("", "Confidential", "");

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = document.Pages[0].GetPageRoot(forceReload: false)!;
            var overlays = PdfTextOverlayExtractor.Extract(page);

            overlays.Should().ContainSingle(o => o.Text == "Confidential",
                "a plain single-line footer section must be completely unaffected by the multi-line fix");
        });
    }
}
