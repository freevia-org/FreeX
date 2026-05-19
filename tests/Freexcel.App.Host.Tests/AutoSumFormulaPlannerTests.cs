using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class AutoSumFormulaPlannerTests
{
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
