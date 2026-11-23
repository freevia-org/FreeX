using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Calc.Tests;

// An array never holds empty cells: Excel (measured through COM) spills a blank element of =A1:A5, FILTER, SORT, UNIQUE,
// TAKE, VSTACK ... as 0.
public class SpillBlankElementTests
{
    private static (Workbook wb, Sheet sheet) Setup()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(1)));
        // A2 stays empty
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new NumberValue(3)));
        return (wb, sheet);
    }

    private static void Recalc(Workbook wb, Sheet sheet, string formula)
    {
        var cell = Cell.FromFormula(formula);
        cell.ArrayMode = FormulaArrayMode.Dynamic;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), cell);
        new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()).RecalculateAllFormulas(wb);
    }

    [Theory]
    [InlineData("A1:A3")]
    [InlineData("TAKE(A1:A3,3)")]
    [InlineData("VSTACK(A1:A3)")]
    public void BlankElementsSpillAsZero(string formula)
    {
        var (wb, sheet) = Setup();

        Recalc(wb, sheet, formula);

        sheet.GetValue(new CellAddress(sheet.Id, 1, 3)).Should().Be(new NumberValue(1));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 3)).Should().Be(new NumberValue(0));
        sheet.GetValue(new CellAddress(sheet.Id, 3, 3)).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void FilterOfBlankCellsSpillsZero()
    {
        var (wb, sheet) = Setup();

        Recalc(wb, sheet, "FILTER(A1:A3,A1:A3=\"\")");

        sheet.GetValue(new CellAddress(sheet.Id, 1, 3)).Should().Be(new NumberValue(0));
    }
}
