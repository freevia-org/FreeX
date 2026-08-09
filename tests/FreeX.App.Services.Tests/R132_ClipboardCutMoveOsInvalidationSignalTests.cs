using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R132-clipboard-cut-move-os-invalidation (src/FreeX.App.Services/WorkbookSession.cs,
/// PasteInternalClipboardAtActiveCell).
///
/// Before the fix: a successful Cut+Paste MOVE never told the caller anything beyond "the paste
/// succeeded" -- <see cref="WorkbookCellEditResult"/> carried no signal distinguishing a completed
/// MOVE from an ordinary paste. The Avalonia shell (src/FreeX.App.Avalonia/MainWindow.cs) has no
/// other way to learn that a Cut just got consumed, so it never invalidated the real OS clipboard
/// the way the WPF host's InvalidateOsClipboardAfterCutMove does from this exact same <c>IsCut</c>
/// branch of its own paste flow -- a later Ctrl+V (finding the FreeX-internal clipboard already
/// null) would fall through to the external-clipboard path and re-paste the OS clipboard's still-
/// stale cut payload a second time.
///
/// Exercised at the WorkbookSession/service layer -- driving the real Cut/Paste entry points, never
/// the OS clipboard -- per the round-132 note that the R49/R57/R82/R91 real-clipboard integration
/// tests are known STA-flaky.
/// </summary>
public sealed class R132_ClipboardCutMoveOsInvalidationSignalTests
{
    [Fact]
    public void PasteClipboardTextAtActiveCell_AfterCut_SignalsClipboardCutMoveCompleted()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var d1 = new CellAddress(sheet.Id, 1, 4);
        sheet.SetCell(a1, new NumberValue(42));

        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook, "Book.fxl", "Opened .fxl.", IsFallback: false));

        session.SelectCell(a1);
        var cutText = session.CutSelectedRangeText();

        session.SelectCell(d1);
        var result = session.PasteClipboardTextAtActiveCell(cutText);

        result.Success.Should().BeTrue();
        result.ClipboardCutMoveCompleted.Should().BeTrue(
            "a completed Cut+Paste MOVE must signal the host shell to invalidate the real OS " +
            "clipboard, or a later Ctrl+V can re-paste the already-moved content a second time");
        sheet.GetValue(d1).Should().Be(new NumberValue(42));
        sheet.GetCell(a1).Should().BeNull("the source cell must be empty after a Cut+Paste move");
    }

    // Sibling no-regression: an ordinary Copy+Paste is NOT a move and must not raise the same
    // signal, or the host shell would wrongly clear the OS clipboard right after a plain paste.
    [Fact]
    public void PasteClipboardTextAtActiveCell_AfterCopy_DoesNotSignalClipboardCutMoveCompleted()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var d1 = new CellAddress(sheet.Id, 1, 4);
        sheet.SetCell(a1, new NumberValue(42));

        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook, "Book.fxl", "Opened .fxl.", IsFallback: false));

        session.SelectCell(a1);
        var copyText = session.CopySelectedRangeText();

        session.SelectCell(d1);
        var result = session.PasteClipboardTextAtActiveCell(copyText);

        result.Success.Should().BeTrue();
        result.ClipboardCutMoveCompleted.Should().BeFalse(
            "an ordinary Copy+Paste must not be mistaken for a Cut+Paste move");
        sheet.GetValue(d1).Should().Be(new NumberValue(42));
        sheet.GetValue(a1).Should().Be(new NumberValue(42), "Copy must leave the source cell untouched");
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
