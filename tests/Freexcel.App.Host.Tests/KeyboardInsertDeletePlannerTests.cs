using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class KeyboardInsertDeletePlannerTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Plan_ReturnsRowOperationForWholeRowSelection(bool insert)
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 3, 1),
            new CellAddress(sheetId, 4, CellAddress.MaxCol));

        var plan = insert
            ? KeyboardInsertDeletePlanner.PlanInsert(range)
            : KeyboardInsertDeletePlanner.PlanDelete(range);

        plan.Should().Be(KeyboardInsertDeletePlan.Rows);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Plan_ReturnsColumnOperationForWholeColumnSelection(bool insert)
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 1, 2),
            new CellAddress(sheetId, CellAddress.MaxRow, 3));

        var plan = insert
            ? KeyboardInsertDeletePlanner.PlanInsert(range)
            : KeyboardInsertDeletePlanner.PlanDelete(range);

        plan.Should().Be(KeyboardInsertDeletePlan.Columns);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Plan_RequiresCellShiftDialogForNormalCellSelection(bool insert)
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 2, 2),
            new CellAddress(sheetId, 3, 3));

        var plan = insert
            ? KeyboardInsertDeletePlanner.PlanInsert(range)
            : KeyboardInsertDeletePlanner.PlanDelete(range);

        plan.Should().Be(KeyboardInsertDeletePlan.CellShiftDialog);
    }
}
