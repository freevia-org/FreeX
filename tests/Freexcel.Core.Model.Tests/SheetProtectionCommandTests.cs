using Freexcel.Core.Commands;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.Core.Model.Tests;

public class SheetProtectionCommandTests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new SimpleCtx(wb));
    }

    [Fact]
    public void ProtectSheetCommand_ProtectsSheetAndUndoRestoresPreviousState()
    {
        var (_, sheet, ctx) = Setup();

        var cmd = new ProtectSheetCommand(sheet.Id, "secret");
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.IsProtected.Should().BeTrue();
        sheet.ProtectionPassword.Should().Be("secret");

        cmd.Revert(ctx);

        sheet.IsProtected.Should().BeFalse();
        sheet.ProtectionPassword.Should().BeNull();
    }

    [Fact]
    public void ProtectSheetCommand_StoresSelectedPermissionsAndUndoRestoresPreviousPermissions()
    {
        var (_, sheet, ctx) = Setup();
        sheet.ProtectionPermissions.Clear();
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.SelectUnlockedCells);

        var cmd = new ProtectSheetCommand(
            sheet.Id,
            "secret",
            [SheetProtectionPermission.SelectLockedCells, SheetProtectionPermission.FormatCells]);

        cmd.Apply(ctx).Success.Should().BeTrue();

        sheet.ProtectionPermissions.Should().Equal(
            SheetProtectionPermission.SelectLockedCells,
            SheetProtectionPermission.FormatCells);

        cmd.Revert(ctx);

        sheet.ProtectionPermissions.Should().Equal(SheetProtectionPermission.SelectUnlockedCells);
    }

    [Fact]
    public void ProtectSheetCommand_UndoRestoresExistingProtectionState()
    {
        var (_, sheet, ctx) = Setup();
        sheet.IsProtected = true;
        sheet.ProtectionPassword = "old";

        var cmd = new ProtectSheetCommand(sheet.Id, "new");
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        sheet.IsProtected.Should().BeTrue();
        sheet.ProtectionPassword.Should().Be("old");
    }

    [Fact]
    public void UnprotectSheetCommand_UnprotectsSheetAndUndoRestoresProtection()
    {
        var (_, sheet, ctx) = Setup();
        sheet.IsProtected = true;
        sheet.ProtectionPassword = "secret";

        var cmd = new UnprotectSheetCommand(sheet.Id);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.IsProtected.Should().BeFalse();
        sheet.ProtectionPassword.Should().BeNull();

        cmd.Revert(ctx);

        sheet.IsProtected.Should().BeTrue();
        sheet.ProtectionPassword.Should().Be("secret");
    }

    [Fact]
    public void EditCellsCommand_RejectsEditsWhenSheetIsProtected()
    {
        var (_, sheet, ctx) = Setup();
        sheet.IsProtected = true;
        var address = new CellAddress(sheet.Id, 1, 1);

        var outcome = EditCellsCommand.ForValue(sheet.Id, address, new TextValue("blocked")).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.GetCell(address).Should().BeNull();
    }

    [Fact]
    public void EditCellsCommand_AllowsEditsToUnlockedCellsWhenSheetIsProtected()
    {
        var (wb, sheet, ctx) = Setup();
        var address = new CellAddress(sheet.Id, 1, 1);
        var unlockedStyleId = wb.RegisterStyle(new CellStyle { Locked = false });
        var cell = Cell.FromValue(new TextValue("old"));
        cell.StyleId = unlockedStyleId;
        sheet.SetCell(address, cell);
        sheet.IsProtected = true;

        var outcome = EditCellsCommand.ForValue(sheet.Id, address, new TextValue("new")).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GetValue(address).Should().Be(new TextValue("new"));
        sheet.GetCell(address)!.StyleId.Should().Be(unlockedStyleId);
    }

    [Fact]
    public void AllowEditRangeCommand_AddsAllowedRangeAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        var range = new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 3, 3));

        var command = new AllowEditRangeCommand(sheet.Id, range);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.AllowEditRanges.Should().ContainSingle().Which.Should().Be(range);

        command.Revert(ctx);

        sheet.AllowEditRanges.Should().BeEmpty();
    }

    [Fact]
    public void RemoveAllowEditRangeCommand_RemovesExistingRangeAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        var first = new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 3, 3));
        var second = new GridRange(
            new CellAddress(sheet.Id, 5, 1),
            new CellAddress(sheet.Id, 5, 2));
        sheet.AllowEditRanges.Add(first);
        sheet.AllowEditRanges.Add(second);

        var command = new RemoveAllowEditRangeCommand(sheet.Id, first);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.AllowEditRanges.Should().Equal(second);

        command.Revert(ctx);

        sheet.AllowEditRanges.Should().Equal(first, second);
    }

    [Fact]
    public void RemoveAllowEditRangeCommand_ReportsMissingRangeWithoutChangingList()
    {
        var (_, sheet, ctx) = Setup();
        var existing = new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 3, 3));
        var missing = new GridRange(
            new CellAddress(sheet.Id, 4, 4),
            new CellAddress(sheet.Id, 4, 4));
        sheet.AllowEditRanges.Add(existing);

        var outcome = new RemoveAllowEditRangeCommand(sheet.Id, missing).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("not found");
        sheet.AllowEditRanges.Should().Equal(existing);
    }

    [Fact]
    public void ClearAllowEditRangesCommand_ClearsAllRangesAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        var first = new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 3, 3));
        var second = new GridRange(
            new CellAddress(sheet.Id, 5, 1),
            new CellAddress(sheet.Id, 5, 2));
        sheet.AllowEditRanges.Add(first);
        sheet.AllowEditRanges.Add(second);

        var command = new ClearAllowEditRangesCommand(sheet.Id);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.AllowEditRanges.Should().BeEmpty();

        command.Revert(ctx);

        sheet.AllowEditRanges.Should().Equal(first, second);
    }

    [Fact]
    public void EditCellsCommand_AllowsLockedCellsInAllowedEditRangeWhenSheetIsProtected()
    {
        var (_, sheet, ctx) = Setup();
        var allowed = new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 3, 3));
        var address = new CellAddress(sheet.Id, 2, 2);
        sheet.AllowEditRanges.Add(allowed);
        sheet.IsProtected = true;

        var outcome = EditCellsCommand.ForValue(sheet.Id, address, new TextValue("allowed")).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GetValue(address).Should().Be(new TextValue("allowed"));
    }

    [Fact]
    public void EditCellsCommand_StillRejectsLockedCellsOutsideAllowedEditRange()
    {
        var (_, sheet, ctx) = Setup();
        sheet.AllowEditRanges.Add(new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 3, 3)));
        sheet.IsProtected = true;
        var address = new CellAddress(sheet.Id, 1, 1);

        var outcome = EditCellsCommand.ForValue(sheet.Id, address, new TextValue("blocked")).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
    }

    [Fact]
    public void ApplyStyleCommand_RejectsStyleChangesWhenSheetIsProtected()
    {
        var (_, sheet, ctx) = Setup();
        sheet.IsProtected = true;
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 1));

        var outcome = new ApplyStyleCommand(sheet.Id, range, new StyleDiff(Bold: true)).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.GetCell(1, 1).Should().BeNull();
    }

    [Fact]
    public void ApplyStyleCommand_AllowsStyleChangesWhenProtectedSheetAllowsFormatCells()
    {
        var (_, sheet, ctx) = Setup();
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.FormatCells);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 1));

        var outcome = new ApplyStyleCommand(sheet.Id, range, new StyleDiff(Bold: true)).Apply(ctx);

        outcome.Success.Should().BeTrue();
        var styleOnly = sheet.GetStyleOnly(1, 1);
        styleOnly.Should().NotBeNull();
        ctx.Workbook.GetStyle(styleOnly!.Value).Bold.Should().BeTrue();
    }

    [Fact]
    public void MergeCellsCommand_RejectsMergeWhenSheetIsProtected()
    {
        var (_, sheet, ctx) = Setup();
        sheet.IsProtected = true;
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 2));

        var outcome = new MergeCellsCommand(sheet.Id, range).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.MergedRegions.Should().BeEmpty();
    }

    [Fact]
    public void InsertRowsCommand_RejectsRowInsertionWhenSheetIsProtectedWithoutPermission()
    {
        var (_, sheet, ctx) = Setup();
        sheet.IsProtected = true;

        var outcome = new InsertRowsCommand(sheet.Id, beforeRow: 1).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
    }

    [Fact]
    public void InsertRowsCommand_AllowsProtectedSheetWithInsertRowsPermission()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("before"));
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.InsertRows);

        var outcome = new InsertRowsCommand(sheet.Id, beforeRow: 1).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GetCell(1, 1).Should().BeNull();
        sheet.GetValue(2, 1).Should().Be(new TextValue("before"));
    }

    [Fact]
    public void InsertColumnsCommand_AllowsProtectedSheetWithInsertColumnsPermission()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("before"));
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.InsertColumns);

        var outcome = new InsertColumnsCommand(sheet.Id, beforeCol: 1).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GetCell(1, 1).Should().BeNull();
        sheet.GetValue(1, 2).Should().Be(new TextValue("before"));
    }

    [Fact]
    public void DeleteRowsCommand_AllowsProtectedSheetWithDeleteRowsPermission()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("delete"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("keep"));
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.DeleteRows);

        var outcome = new DeleteRowsCommand(sheet.Id, startRow: 1).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GetValue(1, 1).Should().Be(new TextValue("keep"));
        sheet.GetCell(2, 1).Should().BeNull();
    }

    [Fact]
    public void DeleteColumnsCommand_AllowsProtectedSheetWithDeleteColumnsPermission()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("delete"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("keep"));
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.DeleteColumns);

        var outcome = new DeleteColumnsCommand(sheet.Id, startCol: 1).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GetValue(1, 1).Should().Be(new TextValue("keep"));
        sheet.GetCell(1, 2).Should().BeNull();
    }

    [Fact]
    public void SetColumnWidthCommand_RejectsLayoutChangesWhenSheetIsProtectedWithoutPermission()
    {
        var (_, sheet, ctx) = Setup();
        sheet.IsProtected = true;

        var outcome = new SetColumnWidthCommand(sheet.Id, 1, 1, 20).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.ColumnWidths.Should().BeEmpty();
    }

    [Fact]
    public void SetColumnWidthCommand_AllowsProtectedSheetWithFormatColumnsPermission()
    {
        var (_, sheet, ctx) = Setup();
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.FormatColumns);

        var outcome = new SetColumnWidthCommand(sheet.Id, 1, 1, 20).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.ColumnWidths[1].Should().Be(20);
    }

    [Fact]
    public void SetRowHeightCommand_AllowsProtectedSheetWithFormatRowsPermission()
    {
        var (_, sheet, ctx) = Setup();
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.FormatRows);

        var outcome = new SetRowHeightCommand(sheet.Id, 1, 1, 30).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.RowHeights[1].Should().Be(30);
    }

    [Fact]
    public void SetColumnsHiddenCommand_AllowsProtectedSheetWithFormatColumnsPermission()
    {
        var (_, sheet, ctx) = Setup();
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.FormatColumns);

        var outcome = new SetColumnsHiddenCommand(sheet.Id, 1, 1, hidden: true).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.HiddenCols.Should().Contain(1);
    }

    [Fact]
    public void SetRowsHiddenCommand_AllowsProtectedSheetWithFormatRowsPermission()
    {
        var (_, sheet, ctx) = Setup();
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.FormatRows);

        var outcome = new SetRowsHiddenCommand(sheet.Id, 1, 1, hidden: true).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.HiddenRows.Should().Contain(1);
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
