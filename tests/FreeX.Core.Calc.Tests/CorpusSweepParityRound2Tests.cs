using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Round 2 of the corpus sweep (SheetFidelity recalc vs Excel cached values). Every expectation was
/// confirmed against real Excel via COM.
/// </summary>
public class CorpusSweepParityRound2Tests
{
    private static (Workbook wb, Sheet sheet) Setup()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        return (wb, sheet);
    }

    private static void Put(Sheet sheet, uint row, uint col, ScalarValue value) =>
        sheet.SetCell(new CellAddress(sheet.Id, row, col), Cell.FromValue(value));

    private static ScalarValue Recalc(Workbook wb, Sheet sheet, uint row, uint col, string formula)
    {
        sheet.SetCell(new CellAddress(sheet.Id, row, col), Cell.FromFormula(formula));
        new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()).RecalculateAllFormulas(wb);
        return sheet.GetCell(row, col)!.Value;
    }

    [Theory]
    [InlineData("VLOOKUP(B1,A1:A4,1,0)")]
    [InlineData("VLOOKUP(B1,A:A,1,0)")]
    [InlineData("MATCH(B1,A1:A4,0)")]
    [InlineData("HLOOKUP(B1,A1:D1,1,0)")]
    public void LegacyExactLookupOfBlankValue_DoesNotMatchBlankCells(string formula)
    {
        var (wb, sheet) = Setup();
        Put(sheet, 1, 1, new TextValue("x"));
        Put(sheet, 2, 1, new TextValue("y"));
        Put(sheet, 4, 1, new TextValue("z"));
        // A3 / B1 are blank: Excel treats the blank lookup value as 0, so it never matches a blank cell.
        Recalc(wb, sheet, 6, 6, formula).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void XlookupOfBlankValue_StillMatchesBlankCell()
    {
        var (wb, sheet) = Setup();
        Put(sheet, 1, 1, new TextValue("x"));
        Put(sheet, 2, 1, new TextValue("y"));
        Put(sheet, 4, 1, new TextValue("z"));
        Recalc(wb, sheet, 6, 6, "XLOOKUP(B1,A1:A4,A1:A4)").Should().Be(new NumberValue(0));
    }

    [Fact]
    public void RootSubtraction_SnapsRoundingResidueToZero()
    {
        var (wb, sheet) = Setup();
        Put(sheet, 1, 1, new NumberValue(5825895.909999999));
        Put(sheet, 2, 1, new NumberValue(4263342.63));
        Put(sheet, 3, 1, new NumberValue(10089238.539999995));
        // Excel shows 0 for a formula whose outermost operation is a subtraction that cancels to ~1 ulp.
        Recalc(wb, sheet, 5, 1, "A3-(A1+A2)").Should().Be(new NumberValue(0));
    }

    [Fact]
    public void NestedSubtraction_KeepsRoundingResidue()
    {
        var (wb, sheet) = Setup();
        Put(sheet, 1, 1, new NumberValue(5825895.909999999));
        Put(sheet, 2, 1, new NumberValue(4263342.63));
        Put(sheet, 3, 1, new NumberValue(10089238.539999995));
        // Excel: =ABS(A3-(A1+A2)) is 3.7252902984619E-09, not 0 (the snap only applies to the final operation).
        var value = Recalc(wb, sheet, 5, 1, "ABS(A3-(A1+A2))");
        value.Should().BeOfType<NumberValue>().Which.Value.Should().BeApproximately(3.7252902984619e-9, 1e-15);
    }

    [Fact]
    public void RootSubtraction_OfGenuinelyDifferentValues_IsNotSnapped()
    {
        var (wb, sheet) = Setup();
        Put(sheet, 1, 1, new NumberValue(1));
        Put(sheet, 2, 1, new NumberValue(1 + 8 * Math.Pow(2, -52)));
        // Excel: 8 ulps of difference survives (1.77635683940025E-15).
        var value = Recalc(wb, sheet, 4, 1, "A2-A1");
        value.Should().BeOfType<NumberValue>().Which.Value.Should().BeApproximately(1.77635683940025e-15, 1e-20);
    }

    [Fact]
    public void CountifsWithErrorCriteria_CountsMatchingErrorCells_InsteadOfPropagating()
    {
        var (wb, sheet) = Setup();
        Put(sheet, 1, 2, ErrorValue.DivByZero);
        Put(sheet, 2, 2, ErrorValue.NA);
        Put(sheet, 3, 2, ErrorValue.DivByZero);
        Put(sheet, 4, 2, new NumberValue(5));
        Put(sheet, 1, 3, new NumberValue(10));
        Put(sheet, 2, 3, new NumberValue(20));
        Put(sheet, 3, 3, new NumberValue(30));
        Put(sheet, 4, 3, new NumberValue(40));
        Recalc(wb, sheet, 10, 5, "COUNTIFS(B1:B4,\">=\"&(1/0))").Should().Be(new NumberValue(2));
        Recalc(wb, sheet, 11, 5, "COUNTIF(B1:B4,NA())").Should().Be(new NumberValue(1));
        Recalc(wb, sheet, 12, 5, "SUMIFS(C1:C4,B1:B4,1/0)").Should().Be(new NumberValue(40));
        Recalc(wb, sheet, 13, 5, "SUMIF(B1:B4,1/0,C1:C4)").Should().Be(new NumberValue(40));
    }

    [Fact]
    public void CountifsWithDateOfTextMonth_ReturnsZeroLikeExcel()
    {
        var (wb, sheet) = Setup();
        Put(sheet, 1, 1, new TextValue("Apr"));
        Put(sheet, 1, 2, new NumberValue(44562));
        Recalc(wb, sheet, 5, 1, "COUNTIFS(B1:B3,\">=\"&DATE(2022,A1,1))").Should().Be(new NumberValue(0));
    }
}
