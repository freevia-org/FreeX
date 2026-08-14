using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookSessionGroupedSheetStructureTests
{
    [Fact]
    public void MoveOrCopySelectedSheets_CopiesGroupInWorkbookOrderAndUndoIsAtomic()
    {
        var (session, workbook, selectedIds, remainingIds) = CreateGroupedSession();
        var originalOrder = workbook.Sheets.Select(sheet => sheet.Id).ToArray();

        var result = session.MoveOrCopySelectedSheets(workbook.Sheets.Count, createCopy: true);

        result.Success.Should().BeTrue();
        var copyIds = workbook.Sheets.Select(sheet => sheet.Id).Except(originalOrder).ToArray();
        copyIds.Should().HaveCount(2);
        workbook.Sheets.Select(sheet => sheet.Id)
            .Should().Equal([.. selectedIds, .. remainingIds, .. copyIds]);
        session.ActiveSheet.Id.Should().Be(copyIds[^1]);
        session.IsWorkbookGrouped.Should().BeFalse();

        session.UndoLastEdit().Success.Should().BeTrue();
        workbook.Sheets.Select(sheet => sheet.Id).Should().Equal(originalOrder);

        session.RedoLastEdit().Success.Should().BeTrue();
        workbook.Sheets.Select(sheet => sheet.Id)
            .Should().Equal([.. selectedIds, .. remainingIds, .. copyIds]);
    }

    [Fact]
    public void MoveOrCopySelectedSheets_MovesWholeGroupAndUndoRestoresOrder()
    {
        var (session, workbook, selectedIds, remainingIds) = CreateGroupedSession();
        var originalOrder = workbook.Sheets.Select(sheet => sheet.Id).ToArray();

        var result = session.MoveOrCopySelectedSheets(workbook.Sheets.Count, createCopy: false);

        result.Success.Should().BeTrue();
        workbook.Sheets.Select(sheet => sheet.Id).Should().Equal([.. remainingIds, .. selectedIds]);
        session.IsWorkbookGrouped.Should().BeFalse();

        session.UndoLastEdit().Success.Should().BeTrue();
        workbook.Sheets.Select(sheet => sheet.Id).Should().Equal(originalOrder);
    }

    [Fact]
    public void MoveOrCopySelectedSheets_ResolvesInsertBeforeAgainstTheOriginalWorkbookOrder()
    {
        var (session, workbook, selectedIds, remainingIds) = CreateGroupedSession();
        var originalOrder = workbook.Sheets.Select(sheet => sheet.Id).ToArray();
        var stableTargetId = originalOrder[3];

        session.MoveOrCopySelectedSheets(insertBeforeIndex: 3, createCopy: true).Success.Should().BeTrue();

        var copyIds = workbook.Sheets.Select(sheet => sheet.Id).Except(originalOrder).ToArray();
        workbook.Sheets.Select(sheet => sheet.Id).Should().Equal(
            selectedIds[0], selectedIds[1], remainingIds[0], copyIds[0], copyIds[1], stableTargetId);
    }

    [Fact]
    public void DeleteSelectedSheets_DeletesWholeGroupAndOneUndoRestoresIt()
    {
        var (session, workbook, selectedIds, remainingIds) = CreateGroupedSession();
        var originalOrder = workbook.Sheets.Select(sheet => sheet.Id).ToArray();

        var result = session.DeleteSelectedSheets();

        result.Success.Should().BeTrue();
        workbook.Sheets.Select(sheet => sheet.Id).Should().Equal(remainingIds);
        session.IsWorkbookGrouped.Should().BeFalse();

        session.UndoLastEdit().Success.Should().BeTrue();
        workbook.Sheets.Select(sheet => sheet.Id).Should().Equal(originalOrder);
        workbook.Sheets.Select(sheet => sheet.Id).Should().Contain(selectedIds);
    }

    [Fact]
    public void DeleteAndHideSelectedSheets_RejectRemovingEveryVisibleSheetAtomically()
    {
        var workbook = CreateWorkbook();
        var session = CreateSession(workbook);
        session.SelectAllVisibleSheets();
        var originalIds = workbook.Sheets.Select(sheet => sheet.Id).ToArray();

        session.DeleteSelectedSheets().Success.Should().BeFalse();
        session.HideSelectedSheets().Success.Should().BeFalse();

        workbook.Sheets.Select(sheet => sheet.Id).Should().Equal(originalIds);
        workbook.Sheets.Should().OnlyContain(sheet => !sheet.IsHidden);
        session.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void TabColorAndHideApplyToTheWholeSelectedGroup()
    {
        var (session, workbook, selectedIds, remainingIds) = CreateGroupedSession();
        var color = new CellColor(12, 34, 56);

        session.SetSelectedSheetTabColor(color).Success.Should().BeTrue();
        selectedIds.Select(workbook.GetSheet).Should().OnlyContain(sheet => sheet!.TabColor == color);
        remainingIds.Select(workbook.GetSheet).Should().OnlyContain(sheet => sheet!.TabColor == null);
        session.IsWorkbookGrouped.Should().BeTrue();

        session.HideSelectedSheets().Success.Should().BeTrue();
        selectedIds.Select(workbook.GetSheet).Should().OnlyContain(sheet => sheet!.IsHidden);
        remainingIds.Select(workbook.GetSheet).Should().OnlyContain(sheet => !sheet!.IsHidden);
        session.IsWorkbookGrouped.Should().BeFalse();

        session.UndoLastEdit().Success.Should().BeTrue();
        selectedIds.Select(workbook.GetSheet).Should().OnlyContain(sheet => !sheet!.IsHidden);

        session.UndoLastEdit().Success.Should().BeTrue();
        selectedIds.Select(workbook.GetSheet).Should().OnlyContain(sheet => sheet!.TabColor == null);
    }

    [Fact]
    public void GroupedStructureOperations_RejectProtectedWorkbookWithoutPartialMutation()
    {
        var (session, workbook, _, _) = CreateGroupedSession();
        var originalOrder = workbook.Sheets.Select(sheet => sheet.Id).ToArray();
        workbook.IsStructureProtected = true;

        var results = new[]
        {
            session.MoveOrCopySelectedSheets(workbook.Sheets.Count, createCopy: false),
            session.MoveOrCopySelectedSheets(workbook.Sheets.Count, createCopy: true),
            session.DeleteSelectedSheets(),
            session.HideSelectedSheets(),
            session.SetSelectedSheetTabColor(new CellColor(12, 34, 56)),
        };

        results.Should().OnlyContain(result => !result.Success && result.ErrorMessage!.Contains("protected"));
        workbook.Sheets.Select(sheet => sheet.Id).Should().Equal(originalOrder);
        workbook.Sheets.Should().OnlyContain(sheet => !sheet.IsHidden && sheet.TabColor == null);
        session.CanUndo.Should().BeFalse();
    }

    private static (WorkbookSession Session, Workbook Workbook, SheetId[] SelectedIds, SheetId[] RemainingIds)
        CreateGroupedSession()
    {
        var workbook = CreateWorkbook();
        var session = CreateSession(workbook);
        var selectedIds = workbook.Sheets.Take(2).Select(sheet => sheet.Id).ToArray();
        var remainingIds = workbook.Sheets.Skip(2).Select(sheet => sheet.Id).ToArray();

        session.SelectSheetFromTab(selectedIds[1], selectRange: false, toggle: true);
        session.IsWorkbookGrouped.Should().BeTrue();
        session.GetCurrentGroupedStructureSheetIds().Should().Equal(selectedIds);
        return (session, workbook, selectedIds, remainingIds);
    }

    private static Workbook CreateWorkbook()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");
        workbook.AddSheet("Details");
        workbook.AddSheet("Charts");
        workbook.AddSheet("Archive");
        workbook.ActiveSheetIndex = 0;
        return workbook;
    }

    private static WorkbookSession CreateSession(Workbook workbook) =>
        new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(
                workbook,
                "Book.fxl",
                "Opened .fxl.",
                IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);
}
