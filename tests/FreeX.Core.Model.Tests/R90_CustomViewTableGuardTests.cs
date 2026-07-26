using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R90-io-sheet-view-custom-views-5-2: real Excel disables the whole Custom Views feature
/// (View &gt; Custom Views is grayed out; the command raises "This command is not available in a
/// workbook that contains a table") the moment any sheet in the workbook has a structured Table.
/// Exercises the real product entry points -- SaveCustomViewCommand/ApplyCustomViewCommand/
/// DeleteCustomViewCommand, the same commands the Custom Views manager dialog dispatches.
/// </summary>
public sealed class R90_CustomViewTableGuardTests
{
    private static Sheet AddTable(Sheet sheet)
    {
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            HeaderRowCount = 1,
        });
        return sheet;
    }

    [Fact]
    public void SaveCustomViewCommand_RejectsWhenWorkbookContainsATable()
    {
        var workbook = new Workbook("test");
        var sheet = AddTable(workbook.AddSheet("Sheet1"));
        var ctx = new TestCommandContext(workbook);

        var outcome = new SaveCustomViewCommand("Audit View").Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Be("This command is not available in a workbook that contains a table.");
        workbook.CustomViews.Should().BeEmpty();
        sheet.StructuredTables.Should().ContainSingle();
    }

    [Fact]
    public void ApplyAndDeleteCustomViewCommand_RejectWhenWorkbookContainsATable()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        new SaveCustomViewCommand("Saved").Apply(ctx).Success.Should().BeTrue();

        // A table added AFTER the view was saved must still block Show/Delete -- Excel's gate is
        // evaluated at the time of the later command, not frozen at save time.
        AddTable(sheet);

        var applyOutcome = new ApplyCustomViewCommand("Saved").Apply(ctx);
        applyOutcome.Success.Should().BeFalse();
        applyOutcome.ErrorMessage.Should().Be("This command is not available in a workbook that contains a table.");

        var deleteOutcome = new DeleteCustomViewCommand("Saved").Apply(ctx);
        deleteOutcome.Success.Should().BeFalse();
        deleteOutcome.ErrorMessage.Should().Be("This command is not available in a workbook that contains a table.");
        workbook.CustomViews.Should().ContainSingle();
    }

    [Fact]
    public void SaveCustomViewCommand_SucceedsWhenWorkbookHasNoTable()
    {
        var workbook = new Workbook("test");
        workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        var outcome = new SaveCustomViewCommand("Audit View").Apply(ctx);

        outcome.Success.Should().BeTrue();
        workbook.CustomViews.Should().ContainSingle().Which.Name.Should().Be("Audit View");
    }
}
