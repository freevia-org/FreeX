using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// R84-calc-precision-display-5-1: Excel's "Precision as displayed" workbook option
/// (calcPr/@fullPrecision="0", <see cref="Workbook.FullPrecision"/> == false) permanently rounds
/// every calculated value to the decimal-place count its own number format actually displays, not
/// just to the ~15 significant-digit storage ceiling. A cell formatted "0.00" that computes
/// 1/3 == 0.333333333333333 must have its *stored* value become exactly 0.33 the moment it is
/// calculated, so a downstream formula referencing it (e.g. *3) sees 0.99, matching real Excel --
/// not 0.999999999999999 from the untouched full-precision double.
/// </summary>
public sealed class R84_PrecisionAsDisplayedTests
{
    private static RecalcEngine Engine() => new(new DependencyGraph(), new FormulaEvaluator());

    [Fact]
    public void Recalculate_WithPrecisionAsDisplayedOn_RoundsStoredValueToFormatDecimalsAndFeedsDownstreamFormula()
    {
        var workbook = new Workbook("Test") { FullPrecision = false };
        var sheet = workbook.AddSheet("Sheet1");
        var engine = Engine();

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 2, 1);

        sheet.SetFormula(a1, "1/3");
        var a1Cell = sheet.GetCell(a1)!;
        a1Cell.StyleId = workbook.RegisterStyle(new CellStyle { NumberFormat = "0.00" });

        sheet.SetFormula(b1, "A1*3");

        engine.RecalculateAllFormulas(workbook);

        // The stored value, not just the display, must have been rounded to 2 decimals.
        ((NumberValue)sheet.GetCell(a1)!.Value).Value.Should().Be(0.33);

        // B1 must see the rounded 0.33, not the untouched ~0.333333333333333 -- Excel's 0.99, not
        // ~0.999999999999999.
        ((NumberValue)sheet.GetCell(b1)!.Value).Value.Should().Be(0.99);
    }

    [Fact]
    public void Recalculate_WithPrecisionAsDisplayedOff_LeavesFullPrecisionValueUnchanged()
    {
        // No-regression sibling: FullPrecision defaults to true (Excel's option unchecked), so the
        // same workbook must keep A1's full double precision and B1's correspondingly unrounded
        // result -- the fix must not kick in unless the workbook option is actually enabled.
        var workbook = new Workbook("Test");
        workbook.FullPrecision.Should().BeTrue("Excel's Precision As Displayed is off by default");
        var sheet = workbook.AddSheet("Sheet1");
        var engine = Engine();

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 2, 1);

        sheet.SetFormula(a1, "1/3");
        var a1Cell = sheet.GetCell(a1)!;
        a1Cell.StyleId = workbook.RegisterStyle(new CellStyle { NumberFormat = "0.00" });

        sheet.SetFormula(b1, "A1*3");

        engine.RecalculateAllFormulas(workbook);

        // Formula evaluation already normalizes every arithmetic result to Excel's ~15
        // significant-digit storage ceiling regardless of this option (see
        // FormulaEvaluator.Operators.cs), so the untouched value is 0.333333333333333, not the raw
        // IEEE double 1.0/3.0 -- the point of this assertion is that it is NOT further rounded down
        // to the format's 2 displayed decimals (0.33) the way the "on" test above requires.
        ((NumberValue)sheet.GetCell(a1)!.Value).Value.Should().Be(0.333333333333333);
        ((NumberValue)sheet.GetCell(b1)!.Value).Value.Should().Be(0.999999999999999);
    }

    [Fact]
    public void Recalculate_GeneralPrecisionFallback_PreservesTinyFiniteValue()
    {
        var workbook = new Workbook("Test") { FullPrecision = false };
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetFormula(address, "5E-200");

        Engine().RecalculateAllFormulas(workbook);

        sheet.GetValue(address).Should().Be(new NumberValue(5e-200));
    }
}
