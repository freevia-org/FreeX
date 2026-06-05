using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

// End-to-end: an Implicit-mode formula that produces a range intersects to a scalar at recalc; a
// Dynamic-mode formula at the same position spills. Mirrors the fidelity finding =A7:J7*B15.
public class ImplicitIntersectionEvalTests
{
    private static (Workbook wb, Sheet sheet) Setup()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        for (uint c = 1; c <= 10; c++)                                        // A7:J7 = 1..10
            sheet.SetCell(new CellAddress(sheet.Id, 7, c), Cell.FromValue(new NumberValue(c)));
        sheet.SetCell(new CellAddress(sheet.Id, 15, 2), Cell.FromValue(new NumberValue(2))); // B15 = 2
        return (wb, sheet);
    }

    private static ScalarValue Recalc(Workbook wb, Sheet sheet, uint row, uint col, string formula, FormulaArrayMode mode)
    {
        var cell = Cell.FromFormula(formula);
        cell.ArrayMode = mode;
        sheet.SetCell(new CellAddress(sheet.Id, row, col), cell);
        new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()).RecalculateAllFormulas(wb);
        return sheet.GetCell(row, col)!.Value;
    }

    [Fact]
    public void Implicit_IntersectsToFormulaColumn()
    {
        var (wb, sheet) = Setup();
        // formula in column 10 (J): A7:J7 intersects to J7 (=10), * B15 (2) = 20
        Recalc(wb, sheet, 20, 10, "A7:J7*B15", FormulaArrayMode.Implicit).Should().Be(new NumberValue(20));
    }

    [Fact]
    public void Implicit_AtColumnA_IntersectsFirstCell()
    {
        var (wb, sheet) = Setup();
        Recalc(wb, sheet, 20, 1, "A7:J7*B15", FormulaArrayMode.Implicit).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Implicit_OffAxis_IsValueError()
    {
        var (wb, sheet) = Setup();
        // column 12 (L) is outside A7:J7's columns
        Recalc(wb, sheet, 20, 12, "A7:J7*B15", FormulaArrayMode.Implicit).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Dynamic_SpillsAnchorIsFirstElement_NotIntersected()
    {
        var (wb, sheet) = Setup();
        // Same column 10 position, but Dynamic spills -> anchor = A7*B15 = 2 (not the intersected 20).
        Recalc(wb, sheet, 20, 10, "A7:J7*B15", FormulaArrayMode.Dynamic).Should().Be(new NumberValue(2));
    }
}
