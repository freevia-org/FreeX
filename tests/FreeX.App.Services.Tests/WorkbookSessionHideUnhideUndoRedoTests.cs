using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Regression coverage for K9: Undo/Redo of Hide/Unhide Sheet must re-activate the sheet whose
/// visibility just changed (matching Excel), not silently leave the view on whatever sheet
/// happened to be active going into the Undo/Redo. See WorkbookSession.ApplySuccessfulHistoryResult
/// / FindSheetWithFlippedHiddenState.
/// </summary>
public sealed class WorkbookSessionHideUnhideUndoRedoTests
{
    [Fact]
    public void UndoHideActiveSheet_ReactivatesTheSheetThatWasUnhidden()
    {
        var workbook = CreateWorkbook();
        var details = workbook.AddSheet("Details");
        var charts = workbook.AddSheet("Charts");
        var session = CreateSession(workbook);
        session.SelectSheet(details.Id);

        var hide = session.HideActiveSheet();
        hide.Success.Should().BeTrue(hide.ErrorMessage);
        session.ActiveSheet.Should().BeSameAs(charts, "Excel selects a visible survivor when the active sheet is hidden");

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue(undo.ErrorMessage);
        details.IsHidden.Should().BeFalse();
        session.ActiveSheet.Should().BeSameAs(details,
            "undoing Hide must restore focus to the sheet whose visibility was just restored, as Excel does");
    }

    [Fact]
    public void RedoHideActiveSheet_SwitchesAwayFromTheReHiddenSheetAgain()
    {
        var workbook = CreateWorkbook();
        var details = workbook.AddSheet("Details");
        var charts = workbook.AddSheet("Charts");
        var session = CreateSession(workbook);
        session.SelectSheet(details.Id);

        var hide = session.HideActiveSheet();
        hide.Success.Should().BeTrue(hide.ErrorMessage);
        var undoBeforeRedo = session.UndoLastEdit();
        undoBeforeRedo.Success.Should().BeTrue(undoBeforeRedo.ErrorMessage);
        session.ActiveSheet.Should().BeSameAs(details);

        var redo = session.RedoLastEdit();

        redo.Success.Should().BeTrue(redo.ErrorMessage);
        details.IsHidden.Should().BeTrue();
        session.ActiveSheet.Should().BeSameAs(charts,
            "redoing Hide must switch the view away from the now-re-hidden sheet to a visible survivor");
    }

    [Fact]
    public void UndoUnhideSheet_SwitchesAwayFromTheReHiddenSheet()
    {
        var workbook = CreateWorkbook();
        var summary = workbook.Sheets.Single();
        var details = workbook.AddSheet("Details");
        details.IsHidden = true;
        var session = CreateSession(workbook);

        var unhide = session.UnhideSheet(details.Id);
        unhide.Success.Should().BeTrue(unhide.ErrorMessage);
        session.ActiveSheet.Should().BeSameAs(details);

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue(undo.ErrorMessage);
        details.IsHidden.Should().BeTrue();
        session.ActiveSheet.Should().BeSameAs(summary,
            "undoing Unhide re-hides the sheet, so the view must fall back to a visible survivor");
    }

    [Fact]
    public void RedoUnhideSheet_ReactivatesTheSheetThatWasUnhiddenAgain()
    {
        var workbook = CreateWorkbook();
        var details = workbook.AddSheet("Details");
        details.IsHidden = true;
        var session = CreateSession(workbook);

        var unhide = session.UnhideSheet(details.Id);
        unhide.Success.Should().BeTrue(unhide.ErrorMessage);
        var undoBeforeRedo = session.UndoLastEdit();
        undoBeforeRedo.Success.Should().BeTrue(undoBeforeRedo.ErrorMessage);
        details.IsHidden.Should().BeTrue();

        var redo = session.RedoLastEdit();

        redo.Success.Should().BeTrue(redo.ErrorMessage);
        details.IsHidden.Should().BeFalse();
        session.ActiveSheet.Should().BeSameAs(details,
            "redoing Unhide must restore focus to the sheet whose visibility was just restored, as Excel does");
    }

    private static WorkbookSession CreateSession(Workbook workbook) =>
        new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);

    private static Workbook CreateWorkbook(string name = "Book")
    {
        var workbook = new Workbook(name);
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        return workbook;
    }
}
