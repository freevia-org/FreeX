using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Excel's Paste Options button shows right after a paste of cells and goes away as soon as
/// anything else is done; selecting other cells leaves it in place.
/// </summary>
public sealed class PasteOptionsTrackerTests
{
    [Fact]
    public void ShowsAfterAPaste_StaysOnSelectionChange_AndGoesAwayOnTheNextEdit()
    {
        var (session, sheet) = CreateSession();
        var tracker = new PasteOptionsTracker();
        var c3 = new CellAddress(sheet.Id, 3, 3);
        tracker.Update(session, session.SelectedRange).Should().BeNull("nothing has been pasted yet");

        session.SelectCell(c3);
        session.PasteExternalTextAtActiveCell("1\t2").Success.Should().BeTrue();
        var pasted = tracker.Update(session, session.SelectedRange);
        pasted.Should().NotBeNull("a paste of cells shows the button");

        session.SelectCell(new CellAddress(sheet.Id, 10, 10));
        tracker.Update(session, session.SelectedRange).Should().Be(pasted, "selecting elsewhere keeps the button on the pasted range");

        session.CommitCellText("x").Success.Should().BeTrue();
        tracker.Update(session, session.SelectedRange).Should().BeNull("the next edit dismisses it");
    }

    [Fact]
    public void UndoingBackOntoAPaste_DoesNotShowTheButtonAgain()
    {
        var (session, sheet) = CreateSession();
        var tracker = new PasteOptionsTracker();
        tracker.Update(session, session.SelectedRange);
        session.SelectCell(new CellAddress(sheet.Id, 3, 3));
        session.PasteExternalTextAtActiveCell("1").Success.Should().BeTrue();
        tracker.Update(session, session.SelectedRange);
        session.CommitCellText("x").Success.Should().BeTrue();
        tracker.Update(session, session.SelectedRange).Should().BeNull();

        session.UndoLastEdit();

        tracker.Update(session, session.SelectedRange).Should().BeNull();
    }

    [Fact]
    public void Dismiss_HidesTheButton()
    {
        var (session, sheet) = CreateSession();
        var tracker = new PasteOptionsTracker();
        tracker.Update(session, session.SelectedRange);
        session.SelectCell(new CellAddress(sheet.Id, 2, 2));
        session.PasteExternalTextAtActiveCell("1").Success.Should().BeTrue();
        tracker.Update(session, session.SelectedRange).Should().NotBeNull();

        tracker.Dismiss();

        tracker.Range.Should().BeNull();
        tracker.Update(session, session.SelectedRange).Should().BeNull("dismissing lasts until the next paste");
    }

    [Theory]
    [InlineData("Paste 4 Cells", true)]
    [InlineData("Paste Cell", true)]
    [InlineData("Paste Special", true)]
    [InlineData("Paste Pictures", false)]
    [InlineData("Paste Charts", false)]
    [InlineData("Edit Cell", false)]
    [InlineData(null, false)]
    public void OnlyCellPastesQualify(string? label, bool expected) =>
        PasteOptionsPlanner.IsCellPasteLabel(label).Should().Be(expected);

    private static (WorkbookSession Session, Sheet Sheet) CreateSession()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        var sheet = workbook.Sheets.Single();
        var session = new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);
        session.SelectCell(new CellAddress(sheet.Id, 1, 1));
        return (session, sheet);
    }
}
