using FluentAssertions;
using FreeX.App.Presentation;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class DataTablePlannerTests
{
    [Fact]
    public void CreatePlan_FromDialogResultOwnsCommandProjectionAndSupportsRangeRebasing()
    {
        var sheetId = SheetId.New();
        var originalRange = new GridRange(
            new CellAddress(sheetId, 2, 2),
            new CellAddress(sheetId, 8, 5));
        var repeatedRange = new GridRange(
            new CellAddress(sheetId, 10, 2),
            new CellAddress(sheetId, 16, 5));
        var dialogResult = new DataTableDialogResult(
            DataTableMode.TwoVariable,
            DataTableInputOrientation.Column,
            new CellAddress(sheetId, 2, 2),
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 1, 3));

        var plan = DataTablePlanner.CreatePlan(originalRange, dialogResult);
        var command = plan.CreateCommand(repeatedRange);

        plan.Mode.Should().Be(DataTablePlanMode.TwoVariable);
        plan.TableRange.Should().Be(originalRange);
        command.Should().BeOfType<TwoVariableDataTableCommand>().Which.Label.Should().Be("Data Table");
    }

    [Fact]
    public void CreatePlan_BuildsOneVariableColumnPlanAndCommand()
    {
        var workbook = new Workbook("Data Table");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetFormula(new CellAddress(sheet.Id, 2, 3), "G1*2");

        var result = DataTablePlanner.CreatePlan(
            workbook,
            sheet.Id,
            tableRangeText: "B2:E5",
            rowInputCellText: "",
            columnInputCellText: "$G$1");

        result.IsReady.Should().BeTrue();
        result.Status.Should().Be(DataTablePlanStatus.Ready);
        result.InvalidText.Should().BeEmpty();

        var plan = result.Plan!;
        plan.Mode.Should().Be(DataTablePlanMode.OneVariable);
        plan.Orientation.Should().Be(DataTableInputOrientation.Column);
        plan.TableRange.Should().Be(Range(sheet.Id, 2, 2, 5, 5));
        plan.FormulaCell.Should().Be(new CellAddress(sheet.Id, 2, 3));
        plan.RowInputCell.Should().BeNull();
        plan.ColumnInputCell.Should().Be(new CellAddress(sheet.Id, 1, 7));
        plan.OutputRange.Should().Be(Range(sheet.Id, 3, 3, 5, 5));
        plan.OutputCellCount.Should().Be(9);
        plan.CreateCommand().Should().BeOfType<OneVariableDataTableCommand>().Which.Label.Should().Be("Data Table");
    }

    [Fact]
    public void CreatePlan_BuildsTwoVariablePlanAndCommand()
    {
        var workbook = new Workbook("Data Table");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetFormula(new CellAddress(sheet.Id, 2, 2), "G1+H1");

        var result = DataTablePlanner.CreatePlan(
            workbook,
            sheet.Id,
            tableRangeText: "B2:D4",
            rowInputCellText: "G1",
            columnInputCellText: "R1C8");

        result.IsReady.Should().BeTrue();

        var plan = result.Plan!;
        plan.Mode.Should().Be(DataTablePlanMode.TwoVariable);
        plan.IsTwoVariable.Should().BeTrue();
        plan.Orientation.Should().Be(DataTableInputOrientation.Column);
        plan.TableRange.Should().Be(Range(sheet.Id, 2, 2, 4, 4));
        plan.FormulaCell.Should().Be(new CellAddress(sheet.Id, 2, 2));
        plan.RowInputCell.Should().Be(new CellAddress(sheet.Id, 1, 7));
        plan.ColumnInputCell.Should().Be(new CellAddress(sheet.Id, 1, 8));
        plan.OutputRange.Should().Be(Range(sheet.Id, 3, 3, 4, 4));
        plan.OutputCellCount.Should().Be(4);
        plan.CreateCommand().Should().BeOfType<TwoVariableDataTableCommand>().Which.Label.Should().Be("Data Table");
    }

    [Theory]
    [InlineData("", DataTablePlanStatus.InvalidTableRange, "")]
    [InlineData("   ", DataTablePlanStatus.InvalidTableRange, "")]
    [InlineData("not-a-range", DataTablePlanStatus.InvalidTableRange, "not-a-range")]
    [InlineData("B2", DataTablePlanStatus.TableRangeTooSmall, "B2:B2")]
    [InlineData("B2:B5", DataTablePlanStatus.TableRangeTooSmall, "B2:B5")]
    public void CreatePlan_RejectsInvalidOrTooSmallTableRanges(
        string tableRangeText,
        DataTablePlanStatus expectedStatus,
        string expectedInvalidText)
    {
        var workbook = new Workbook("Data Table");
        var sheet = workbook.AddSheet("Sheet1");

        var result = DataTablePlanner.CreatePlan(
            workbook,
            sheet.Id,
            tableRangeText,
            rowInputCellText: "",
            columnInputCellText: "G1");

        result.IsReady.Should().BeFalse();
        result.Plan.Should().BeNull();
        result.Status.Should().Be(expectedStatus);
        result.InvalidText.Should().Be(expectedInvalidText);
    }

    [Theory]
    [InlineData("", "", DataTablePlanStatus.MissingInputCell, "")]
    [InlineData("bad", "", DataTablePlanStatus.InvalidRowInputCell, "bad")]
    [InlineData("", "bad", DataTablePlanStatus.InvalidColumnInputCell, "bad")]
    [InlineData("B2", "", DataTablePlanStatus.RowInputCellInsideTableRange, "B2")]
    [InlineData("", "C3", DataTablePlanStatus.ColumnInputCellInsideTableRange, "C3")]
    [InlineData("G1", "G1", DataTablePlanStatus.InputCellsMustBeDifferent, "G1")]
    public void CreatePlan_RejectsInvalidInputCells(
        string rowInputCellText,
        string columnInputCellText,
        DataTablePlanStatus expectedStatus,
        string expectedInvalidText)
    {
        var workbook = new Workbook("Data Table");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetFormula(new CellAddress(sheet.Id, 2, 3), "G1*2");

        var result = DataTablePlanner.CreatePlan(
            workbook,
            sheet.Id,
            tableRangeText: "B2:D4",
            rowInputCellText,
            columnInputCellText);

        result.IsReady.Should().BeFalse();
        result.Status.Should().Be(expectedStatus);
        result.InvalidText.Should().Be(expectedInvalidText);
    }

    [Fact]
    public void CreatePlan_RejectsInputCellsOnDifferentSheet()
    {
        var workbook = new Workbook("Data Table");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.AddSheet("Other");
        sheet.SetFormula(new CellAddress(sheet.Id, 2, 3), "G1*2");

        var result = DataTablePlanner.CreatePlan(
            workbook,
            sheet.Id,
            tableRangeText: "B2:D4",
            rowInputCellText: "",
            columnInputCellText: "Other!A1");

        result.IsReady.Should().BeFalse();
        result.Status.Should().Be(DataTablePlanStatus.InputCellSheetMismatch);
        result.InvalidText.Should().Be("Other!A1");
    }

    [Fact]
    public void CreatePlan_RequiresFormulaAtDefaultFormulaCell()
    {
        var workbook = new Workbook("Data Table");
        var sheet = workbook.AddSheet("Sheet1");

        var result = DataTablePlanner.CreatePlan(
            workbook,
            sheet.Id,
            tableRangeText: "B2:D4",
            rowInputCellText: "",
            columnInputCellText: "G1");

        result.IsReady.Should().BeFalse();
        result.Status.Should().Be(DataTablePlanStatus.FormulaCellMustContainFormula);
        result.InvalidText.Should().Be("C2");
    }

    private static GridRange Range(SheetId sheetId, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(new CellAddress(sheetId, startRow, startCol), new CellAddress(sheetId, endRow, endCol));
}
