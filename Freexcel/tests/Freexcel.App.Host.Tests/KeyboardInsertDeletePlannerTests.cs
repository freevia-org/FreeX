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

    [Theory]
    [InlineData("right", KeyboardInsertDeleteDialogChoice.ShiftRight)]
    [InlineData("r", KeyboardInsertDeleteDialogChoice.ShiftRight)]
    [InlineData("down", KeyboardInsertDeleteDialogChoice.ShiftDown)]
    [InlineData("d", KeyboardInsertDeleteDialogChoice.ShiftDown)]
    [InlineData("row", KeyboardInsertDeleteDialogChoice.EntireRow)]
    [InlineData("rows", KeyboardInsertDeleteDialogChoice.EntireRow)]
    [InlineData("entire row", KeyboardInsertDeleteDialogChoice.EntireRow)]
    [InlineData("column", KeyboardInsertDeleteDialogChoice.EntireColumn)]
    [InlineData("columns", KeyboardInsertDeleteDialogChoice.EntireColumn)]
    [InlineData("entire column", KeyboardInsertDeleteDialogChoice.EntireColumn)]
    public void TryParseInsertDialogChoice_RecognizesShiftAndEntireAxisChoices(
        string input,
        KeyboardInsertDeleteDialogChoice expected)
    {
        var parsed = KeyboardInsertDeletePlanner.TryParseInsertDialogChoice(input, out var choice);

        parsed.Should().BeTrue();
        choice.Should().Be(expected);
    }

    [Theory]
    [InlineData("left", KeyboardInsertDeleteDialogChoice.ShiftLeft)]
    [InlineData("l", KeyboardInsertDeleteDialogChoice.ShiftLeft)]
    [InlineData("up", KeyboardInsertDeleteDialogChoice.ShiftUp)]
    [InlineData("u", KeyboardInsertDeleteDialogChoice.ShiftUp)]
    [InlineData("row", KeyboardInsertDeleteDialogChoice.EntireRow)]
    [InlineData("rows", KeyboardInsertDeleteDialogChoice.EntireRow)]
    [InlineData("entire row", KeyboardInsertDeleteDialogChoice.EntireRow)]
    [InlineData("column", KeyboardInsertDeleteDialogChoice.EntireColumn)]
    [InlineData("columns", KeyboardInsertDeleteDialogChoice.EntireColumn)]
    [InlineData("entire column", KeyboardInsertDeleteDialogChoice.EntireColumn)]
    public void TryParseDeleteDialogChoice_RecognizesShiftAndEntireAxisChoices(
        string input,
        KeyboardInsertDeleteDialogChoice expected)
    {
        var parsed = KeyboardInsertDeletePlanner.TryParseDeleteDialogChoice(input, out var choice);

        parsed.Should().BeTrue();
        choice.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("sideways")]
    [InlineData("left")]
    [InlineData("up")]
    public void TryParseInsertDialogChoice_RejectsInvalidOrDeleteOnlyChoices(string input)
    {
        var parsed = KeyboardInsertDeletePlanner.TryParseInsertDialogChoice(input, out _);

        parsed.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("sideways")]
    [InlineData("right")]
    [InlineData("down")]
    public void TryParseDeleteDialogChoice_RejectsInvalidOrInsertOnlyChoices(string input)
    {
        var parsed = KeyboardInsertDeletePlanner.TryParseDeleteDialogChoice(input, out _);

        parsed.Should().BeFalse();
    }
}
