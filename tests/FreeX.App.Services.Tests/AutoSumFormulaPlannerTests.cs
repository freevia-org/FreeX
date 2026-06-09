using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class AutoSumFormulaPlannerTests
{
    public static TheoryData<string> AggregateFunctions => new()
    {
        "SUM",
        "AVERAGE",
        "COUNT",
        "COUNTA",
        "MAX",
        "MIN"
    };

    [Theory]
    [MemberData(nameof(AggregateFunctions))]
    public void TryCreatePlan_PlacesAggregateBelowVerticalSelection(string functionName)
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var selection = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1));

        AutoSumFormulaPlanner.TryCreatePlan(sheet, functionName, selection, out var plan)
            .Should()
            .BeTrue();

        plan.Target.Should().Be(new CellAddress(sheet.Id, 4, 1));
        plan.Formula.Should().Be($"{functionName}(A1:A3)");
    }

    [Fact]
    public void TryCreatePlan_PlacesAggregateToRightOfHorizontalSelection()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var selection = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 3));

        AutoSumFormulaPlanner.TryCreatePlan(sheet, "SUM", selection, out var plan)
            .Should()
            .BeTrue();

        plan.Target.Should().Be(new CellAddress(sheet.Id, 1, 4));
        plan.Formula.Should().Be("SUM(A1:C1)");
    }

    [Fact]
    public void TryCreatePlan_SingleCellInfersContiguousNumbersAbove()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(20));
        var selection = new GridRange(new CellAddress(sheet.Id, 3, 1), new CellAddress(sheet.Id, 3, 1));

        AutoSumFormulaPlanner.TryCreatePlan(sheet, "MAX", selection, out var plan)
            .Should()
            .BeTrue();

        plan.Target.Should().Be(new CellAddress(sheet.Id, 3, 1));
        plan.Formula.Should().Be("MAX(A1:A2)");
    }

    [Fact]
    public void TryCreatePlan_ReturnsFalseWhenSelectionTargetWouldExceedWorksheet()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var selection = new GridRange(
            new CellAddress(sheet.Id, CellAddress.MaxRow - 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 1));

        AutoSumFormulaPlanner.TryCreatePlan(sheet, "SUM", selection, out _)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void BuildFormula_UsesContiguousNumbersAboveTheTargetCell()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(20));

        AutoSumFormulaPlanner.BuildFormula(sheet, "SUM", new CellAddress(sheet.Id, 4, 3))
            .Should()
            .Be("SUM(C2:C3)");
    }

    [Fact]
    public void BuildFormula_FallsBackToContiguousNumbersOnTheLeft()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(20));

        AutoSumFormulaPlanner.BuildFormula(sheet, "AVERAGE", new CellAddress(sheet.Id, 5, 3))
            .Should()
            .Be("AVERAGE(A5:B5)");
    }

    [Fact]
    public void BuildFormula_UsesExcelFallbackRangeWhenNoAdjacentNumbersExist()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");

        AutoSumFormulaPlanner.BuildFormula(sheet, "COUNT", new CellAddress(sheet.Id, 1, 2))
            .Should()
            .Be("COUNT(B1:B1)");
    }
}
