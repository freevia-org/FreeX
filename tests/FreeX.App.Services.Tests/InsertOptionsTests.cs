using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Excel's Insert Options button: after inserting rows the new band can take the format of the row
/// above (the default), the row below, or no format; each choice is one undo step.
/// </summary>
public sealed class InsertOptionsTests
{
    [Fact]
    public void InsertedRows_ShowTheButton_AndCanTakeTheFormatBelowOrNone()
    {
        var (session, sheet) = CreateSession();
        var above = session.Workbook.RegisterStyle(new CellStyle { Bold = true });
        var below = session.Workbook.RegisterStyle(new CellStyle { Italic = true });
        sheet.SetStyleOnly(1, 1, above);
        sheet.SetStyleOnly(2, 1, below);
        var tracker = new InsertOptionsTracker();
        tracker.Update(session).Should().BeNull();

        session.ExecuteCommandPreservingSelection(new InsertRowsCommand(sheet.Id, 2, 1)).Success.Should().BeTrue();
        var band = tracker.Update(session);

        band.Should().Be(new InsertedBand(sheet.Id, true, 2, 1));
        sheet.GetStyleOnly(2, 1).Should().Be(above, "Excel's default copies the row above");

        session.FormatInsertedBand(band!, InsertedBandFormat.SameAsAfter).Success.Should().BeTrue();
        sheet.GetStyleOnly(2, 1).Should().Be(below, "the row below moved down to row 3");
        tracker.Update(session).Should().Be(band, "the button stays to switch again");

        session.FormatInsertedBand(band!, InsertedBandFormat.Clear).Success.Should().BeTrue();
        sheet.GetStyleOnly(2, 1).Should().BeNull();

        session.UndoLastEdit();
        sheet.GetStyleOnly(2, 1).Should().Be(below, "undo restores the band's previous format");
    }

    [Fact]
    public void OtherEdits_HideTheButton()
    {
        var (session, sheet) = CreateSession();
        var tracker = new InsertOptionsTracker();
        tracker.Update(session);
        session.ExecuteCommandPreservingSelection(new InsertColumnsCommand(sheet.Id, 2, 2)).Success.Should().BeTrue();
        tracker.Update(session).Should().Be(new InsertedBand(sheet.Id, false, 2, 2));

        session.SelectCell(new CellAddress(sheet.Id, 5, 5));
        session.CommitCellText("x").Success.Should().BeTrue();

        tracker.Update(session).Should().BeNull();
    }

    [Fact]
    public void Menu_FollowsExcel()
    {
        InsertOptionsPlanner.Items(rows: true).Select(item => item.Header)
            .Should().Equal("Format Same As Above", "Format Same As Below", "Clear Formatting");
        InsertOptionsPlanner.Items(rows: false).Select(item => item.Header)
            .Should().Equal("Format Same As Left", "Format Same As Right", "Clear Formatting");
    }

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
