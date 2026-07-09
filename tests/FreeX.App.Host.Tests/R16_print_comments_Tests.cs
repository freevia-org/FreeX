using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Round 16 fix verification (R16-meta-3): the WPF direct-print/FixedDocument path
/// (<c>PrintRenderer.DrawDisplayedComments</c>, invoked from <c>PrintRenderer.HeaderFooter.cs</c>
/// when <see cref="WorksheetPrintComments.AsDisplayed"/> is set) must thread
/// <see cref="Sheet.ShownComments"/> into <see cref="FreeX.App.Presentation.PageLayout.WorksheetPageLayout.GetDisplayedCommentOverlays(System.Collections.Generic.IReadOnlyDictionary{CellAddress,string},System.Collections.Generic.IReadOnlyList{uint},System.Collections.Generic.IReadOnlyList{uint},System.Collections.Generic.IReadOnlySet{CellAddress})"/>
/// so it only draws the notes the user actually pinned "shown" -- matching Excel's "Indicators
/// only" display state and the same contract the portable/Skia PDF path
/// (<c>PortablePdfExportPlanner</c>) already honors. Pre-fix, the WPF path called the 4-arg
/// overload with an implicit <c>shownComments: null</c>, so it drew a box for EVERY note on the
/// sheet regardless of pin state.
/// </summary>
public sealed class R16_print_comments_Tests
{
    [Fact]
    public void RenderWorksheet_AsDisplayed_OnlyDrawsPinnedNoteAmongThreeNotes()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Three notes, one pinned");
            var sheet = workbook.AddSheet("Sheet1");
            var a1 = new CellAddress(sheet.Id, 1, 1);
            var b2 = new CellAddress(sheet.Id, 2, 2);
            var c3 = new CellAddress(sheet.Id, 3, 3);
            sheet.SetCell(a1, new TextValue("Row1"));
            sheet.SetCell(b2, new TextValue("Row2"));
            sheet.SetCell(c3, new TextValue("Row3"));
            sheet.Comments[a1] = "Unpinned note one";
            sheet.Comments[b2] = "Unpinned note two";
            sheet.Comments[c3] = "Pinned note three";
            sheet.PrintComments = WorksheetPrintComments.AsDisplayed;

            // Only c3's note is pinned "shown" (Sheet.ShownComments) -- Excel's "As displayed on
            // sheet" print mode must draw a box only for this one, not the other two.
            sheet.ShownComments.Add(c3);

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = document.Pages[0].GetPageRoot(forceReload: false)!;
            var overlayTexts = PdfTextOverlayExtractor.Extract(page)
                .Select(overlay => overlay.Text)
                .ToList();

            overlayTexts.Should().Contain("Pinned note three");
            overlayTexts.Should().NotContain("Unpinned note one");
            overlayTexts.Should().NotContain("Unpinned note two");
        });
    }
}
