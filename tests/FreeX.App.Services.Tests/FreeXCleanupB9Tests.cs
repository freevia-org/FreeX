using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Regression coverage for cleanup batch B9 finding P22 in WorkbookSession.cs: a Paste Special &gt;
/// Linked Picture never updated when a source-range cell value was edited, because the only
/// existing content refresh (RowColumnShiftHelpers.RefreshLinkedPictureSnapshot) is gated on a
/// structural row/column shift changing LinkedSourceRange's coordinates. A plain value edit inside
/// the linked range left the picture's cached Cells frozen at whatever was true at paste time.
/// </summary>
public sealed class FreeXCleanupB9Tests
{
    [Fact]
    public void CommitCellText_EditingLinkedPictureSourceCell_RefreshesPictureSnapshot()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        var d5 = new CellAddress(sheet.Id, 5, 4);
        sheet.SetCell(a1, new NumberValue(10));
        sheet.SetCell(b1, new NumberValue(20));
        sheet.SetCell(a2, new NumberValue(30));
        sheet.SetCell(b2, new NumberValue(40));

        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(new GridRange(a1, b2));
        var clipboardText = session.CopySelectedRangeText();
        session.SelectCell(d5);
        var pasteResult = session.PastePictureFromClipboardAtActiveCell(clipboardText, linkedPicture: true);
        pasteResult.Success.Should().BeTrue();

        var picture = sheet.Pictures.Should().ContainSingle().Subject;
        picture.IsLinkedToSourceRange.Should().BeTrue();
        picture.Cells.Should().Contain(cell => cell.RowOffset == 0 && cell.ColumnOffset == 0 && cell.Text == "10");

        // Type 99 into A1 -- a plain value edit inside the linked source range, with no row/column
        // shift involved. Excel's camera/linked-picture refreshes its content immediately.
        session.SelectCell(a1);
        var editResult = session.CommitCellText("99");

        editResult.Success.Should().BeTrue();
        sheet.GetValue(a1).Should().Be(new NumberValue(99));
        var refreshedPicture = sheet.Pictures.Should().ContainSingle().Subject;
        refreshedPicture.Cells.Should().Contain(cell => cell.RowOffset == 0 && cell.ColumnOffset == 0 && cell.Text == "99");
        refreshedPicture.Cells.Should().NotContain(cell => cell.RowOffset == 0 && cell.ColumnOffset == 0 && cell.Text == "10");
        // The rest of the linked range must be untouched.
        refreshedPicture.Cells.Should().Contain(cell => cell.RowOffset == 0 && cell.ColumnOffset == 1 && cell.Text == "20");
        refreshedPicture.Cells.Should().Contain(cell => cell.RowOffset == 1 && cell.ColumnOffset == 0 && cell.Text == "30");
        refreshedPicture.Cells.Should().Contain(cell => cell.RowOffset == 1 && cell.ColumnOffset == 1 && cell.Text == "40");
    }

    [Fact]
    public void FillSelectedRange_EditingLinkedPictureSourceCellsViaRangeEdit_RefreshesPictureSnapshot()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        var d5 = new CellAddress(sheet.Id, 5, 4);
        sheet.SetCell(a1, new NumberValue(10));
        sheet.SetCell(b1, new NumberValue(20));
        sheet.SetCell(a2, new NumberValue(30));
        sheet.SetCell(b2, new NumberValue(40));

        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(new GridRange(a1, b2));
        var clipboardText = session.CopySelectedRangeText();
        session.SelectCell(d5);
        var pasteResult = session.PastePictureFromClipboardAtActiveCell(clipboardText, linkedPicture: true);
        pasteResult.Success.Should().BeTrue();

        var picture = sheet.Pictures.Should().ContainSingle().Subject;
        picture.Cells.Should().Contain(cell => cell.RowOffset == 1 && cell.ColumnOffset == 0 && cell.Text == "30");

        // Fill Down A1:A2 -- a range-edit (not a single-cell CommitCellText) that overwrites A2
        // (inside the linked source range) with A1's value. This goes through
        // ApplySuccessfulRangeEditResult rather than ApplySuccessfulEditResult, so it must be
        // refreshed too, exactly like a plain single-cell edit is.
        session.SelectRange(new GridRange(a1, a2));
        var fillResult = session.FillSelectedRange(FillCellsDirection.Down);

        fillResult.Success.Should().BeTrue();
        sheet.GetValue(a2).Should().Be(new NumberValue(10));
        var refreshedPicture = sheet.Pictures.Should().ContainSingle().Subject;
        refreshedPicture.Cells.Should().Contain(cell => cell.RowOffset == 1 && cell.ColumnOffset == 0 && cell.Text == "10");
        refreshedPicture.Cells.Should().NotContain(cell => cell.RowOffset == 1 && cell.ColumnOffset == 0 && cell.Text == "30");
    }

    [Fact]
    public void CommitCellText_EditingUnlinkedPictureSourceCell_DoesNotRefreshSnapshot()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var d5 = new CellAddress(sheet.Id, 5, 4);
        sheet.SetCell(a1, new NumberValue(10));

        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(new GridRange(a1, a1));
        var clipboardText = session.CopySelectedRangeText();
        session.SelectCell(d5);
        // Ordinary (non-linked) Paste Picture: must remain a frozen snapshot even though we still
        // exercise the same edit path that now refreshes linked pictures.
        var pasteResult = session.PastePictureFromClipboardAtActiveCell(clipboardText, linkedPicture: false);
        pasteResult.Success.Should().BeTrue();

        session.SelectCell(a1);
        session.CommitCellText("99").Success.Should().BeTrue();

        var picture = sheet.Pictures.Should().ContainSingle().Subject;
        picture.IsLinkedToSourceRange.Should().BeFalse();
        picture.Cells.Should().Contain(cell => cell.RowOffset == 0 && cell.ColumnOffset == 0 && cell.Text == "10");
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
