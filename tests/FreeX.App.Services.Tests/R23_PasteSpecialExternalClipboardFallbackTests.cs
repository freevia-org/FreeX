using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Regression coverage for review R23-clipboard-formats-deep-2: when a FreeX-internal clipboard
/// exists but the live OS clipboard text no longer matches it (the user copied FreeX cells, then
/// copied plain text from another app), PasteSpecialClipboardAtActiveCell used to hard-reject with
/// "Paste Special requires copied FreeX cells." instead of falling back to an external-text Paste
/// Special -- an asymmetry with the WPF host's ExecutePaste, which treats this exact situation as
/// "clipboard changed externally" and pastes the external text honoring the selected options
/// (Transpose / Skip Blanks / Operation), per the review-P46 fix.
/// </summary>
public sealed class R23_PasteSpecialExternalClipboardFallbackTests
{
    [Fact]
    public void PasteSpecialClipboardAtActiveCell_StaleInternalClipboard_FallsBackToExternalTextWithOptions()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        var c2 = new CellAddress(sheet.Id, 2, 3);
        sheet.SetCell(a1, new NumberValue(99));

        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        // Step 1: copy a FreeX cell so an internal clipboard is captured.
        session.SelectCell(a1);
        session.CopySelectedRangeText();

        // Step 2: the OS clipboard is now "changed externally" (e.g. the user switched to another
        // app and copied plain text there) -- the live text no longer matches the internal clip.
        session.SelectCell(c1);
        var result = session.PasteSpecialClipboardAtActiveCell(
            "10\t20",
            PasteCellsMode.All,
            new PasteSpecialOptions(Transpose: true));

        // Must succeed by falling back to an external-text paste, not reject.
        result.Success.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();

        // Transpose must be honored: the single source row "10\t20" becomes a column.
        sheet.GetValue(c1).Should().Be(new NumberValue(10));
        sheet.GetValue(c2).Should().Be(new NumberValue(20));
    }

    [Fact]
    public void PasteSpecialClipboardAtActiveCell_StaleInternalClipboardWithEmptyText_StillRejects()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(a1, new TextValue("source"));

        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);
        session.CopySelectedRangeText();
        session.SelectCell(c1);

        // The live OS clipboard text is empty/unreadable -- there is no external text to fall back
        // to, so Paste Special must still reject rather than paste nothing.
        var result = session.PasteSpecialClipboardAtActiveCell(string.Empty, PasteCellsMode.Values, default);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Paste Special requires copied FreeX cells.");
        sheet.GetCell(c1).Should().BeNull();
    }

    private static WorkbookSession CreateSession(StartupWorkbookLoadResult source) =>
        new WorkbookSessionFactory().Create(source, viewportHeight: 240, viewportWidth: 320);

    private static Workbook CreateWorkbook(string name = "Book")
    {
        var workbook = new Workbook(name);
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        return workbook;
    }
}
