using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R66-services-clipboard-formats-6-2: committing an ordinary edit (or Clear Contents) did NOT cancel
/// a pending Cut in the shared <c>WorkbookSession</c> internal-clipboard model, so on the Avalonia
/// shell a later Paste still MOVED (and deleted) the cut source, even though the user had since typed
/// over or cleared unrelated cells. The WPF host already cancels the cut on any edit (R54,
/// <c>MainWindow.CommandExecution.TryExecuteEditCells</c>); this mirrors that into
/// <c>WorkbookSession.CommitCellText</c>/<c>ClearSelectedRangeContents</c> so the Avalonia shell (which
/// shares this session, not the WPF host's editing entry points) gets the same cancellation.
/// </summary>
public sealed class R66_CutCancelledByMutatingEditTests
{
    [Fact]
    public void CommitCellText_AfterCut_CancelsTheCut_SoALaterPasteDoesNotMoveTheSource()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var b5 = new CellAddress(sheet.Id, 5, 2);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(a1, new NumberValue(1));
        sheet.SetCell(a2, new NumberValue(2));

        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook, "Book.fxl", "Opened .fxl.", IsFallback: false));
        session.SelectRange(new GridRange(a1, a2));
        var clipboardText = session.CutSelectedRangeText();

        // An unrelated edit elsewhere on the sheet -- Excel cancels the Cut's marching-ants/move
        // semantics the instant this commits.
        session.SelectCell(b5);
        var editResult = session.CommitCellText("hello");
        editResult.Success.Should().BeTrue();

        session.SelectCell(c1);
        var paste = session.PasteClipboardTextAtActiveCell(clipboardText);

        paste.Success.Should().BeTrue();
        // The cut must have been cancelled: the source range is untouched, and the paste behaved as an
        // ordinary (non-moving) paste of the clipboard text instead of relocating A1:A2.
        sheet.GetCell(a1)!.Value.Should().Be(new NumberValue(1), "the cut source must survive once the cut was cancelled by the intervening edit");
        sheet.GetCell(a2)!.Value.Should().Be(new NumberValue(2));
        sheet.GetCell(c1)!.Value.Should().Be(new NumberValue(1));
    }

    [Fact]
    public void ClearSelectedRangeContents_AfterCut_CancelsTheCut_SoALaterPasteDoesNotMoveTheSource()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var b5 = new CellAddress(sheet.Id, 5, 2);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(a1, new NumberValue(1));
        sheet.SetCell(a2, new NumberValue(2));
        sheet.SetCell(b5, new NumberValue(99));

        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook, "Book.fxl", "Opened .fxl.", IsFallback: false));
        session.SelectRange(new GridRange(a1, a2));
        var clipboardText = session.CutSelectedRangeText();

        session.SelectCell(b5);
        var clearResult = session.ClearSelectedRangeContents();
        clearResult.Success.Should().BeTrue();

        session.SelectCell(c1);
        var paste = session.PasteClipboardTextAtActiveCell(clipboardText);

        paste.Success.Should().BeTrue();
        sheet.GetCell(a1)!.Value.Should().Be(new NumberValue(1), "Clear Contents elsewhere must also cancel the pending cut");
        sheet.GetCell(a2)!.Value.Should().Be(new NumberValue(2));
        sheet.GetCell(c1)!.Value.Should().Be(new NumberValue(1));
    }

    [Fact]
    public void CutThenImmediatePaste_WithNoInterveningEdit_StillMovesTheSource()
    {
        // Sibling no-regression check: the ordinary cut+paste MOVE behavior (WorkbookSessionCutMoveActiveCellTests)
        // must be untouched when nothing else edits the sheet in between.
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(a1, new NumberValue(7));

        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook, "Book.fxl", "Opened .fxl.", IsFallback: false));
        session.SelectCell(a1);
        var clipboardText = session.CutSelectedRangeText();
        session.SelectCell(c1);

        var paste = session.PasteClipboardTextAtActiveCell(clipboardText);

        paste.Success.Should().BeTrue();
        sheet.GetCell(a1).Should().BeNull("a cut immediately followed by paste (no intervening edit) must still MOVE the source");
        sheet.GetCell(c1)!.Value.Should().Be(new NumberValue(7));
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
