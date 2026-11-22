using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Regressions found by the FreeX-vs-Excel corpus sweep (SheetFidelity recalc vs Excel cached values).
/// Every expectation was confirmed against real Excel via COM.
/// </summary>
public class CorpusSweepParityTests
{
    private static (Workbook wb, Sheet sheet) Setup()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new TextValue("CATIO")));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new TextValue("CATBUD")));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromValue(new NumberValue(5)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), Cell.FromValue(new NumberValue(7)));
        return (wb, sheet);
    }

    private static ScalarValue Recalc(Workbook wb, Sheet sheet, uint row, uint col, string formula, FormulaArrayMode mode = FormulaArrayMode.Implicit)
    {
        var cell = Cell.FromFormula(formula);
        cell.ArrayMode = mode;
        sheet.SetCell(new CellAddress(sheet.Id, row, col), cell);
        new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()).RecalculateAllFormulas(wb);
        return sheet.GetCell(row, col)!.Value;
    }

    [Theory]
    [InlineData(10, 4)] // column D: the array's positional frame must not matter
    [InlineData(10, 1)]
    [InlineData(10, 9)]
    public void LegacyCell_SumifWithArrayConstantCriteria_ShowsFirstElement(uint row, uint col)
    {
        var (wb, sheet) = Setup();
        // Excel: =SUMIF(A1:A2,{"CATBUD","CATIO"},B1:B2) in a legacy (non-array) cell shows 7, the first element.
        Recalc(wb, sheet, row, col, "SUMIF($A1:$A2,{\"CATBUD\",\"CATIO\"},B1:B2)").Should().Be(new NumberValue(7));
    }

    [Fact]
    public void SumOverSumifWithArrayConstantCriteria_AddsAllElements()
    {
        var (wb, sheet) = Setup();
        Recalc(wb, sheet, 10, 4, "SUM(SUMIF(A1:A2,{\"CATBUD\",\"CATIO\"},B1:B2))").Should().Be(new NumberValue(12));
    }

    [Fact]
    public void OffsetWhoseBaseIsTheFormulaCell_IsNotCircular()
    {
        var (wb, sheet) = Setup();
        // Excel: OFFSET only uses the base reference's address, so =OFFSET(A12,-1,0,1,1) in A12 is not circular.
        Recalc(wb, sheet, 12, 1,
            "IF(ISERROR(VALUE(SUBSTITUTE(OFFSET(A12,-1,0,1,1),\".\",\"\"))),\"1\",\"x\")").Should().Be(new TextValue("1"));
    }

    [Fact]
    public void SumifSumRangeContainingOwnCell_IsNotCircular()
    {
        var (wb, sheet) = Setup();
        // Excel: =SUMIF(A1:A3,"CATIO",B1:B3) in B3 (inside its own sum_range) is 5, not a circular reference.
        Recalc(wb, sheet, 3, 2, "SUMIF(A1:A3,\"CATIO\",B1:B3)").Should().Be(new NumberValue(5));
    }

    [Fact]
    public void ExactMatchWhoseLookupRangeContainsOwnCell_IsNotCircular()
    {
        var (wb, sheet) = Setup();
        // Excel: ="C"&MATCH("CATBUD",A:A,0) in A3 finds A2 without a circular reference.
        Recalc(wb, sheet, 3, 1, "\"C\"&MATCH(\"CATBUD\",A1:A5,0)").Should().Be(new TextValue("C2"));
    }

    [Fact]
    public void CountifRangeContainingOwnCell_StaysCircular()
    {
        var (wb, sheet) = Setup();
        // Excel: COUNTIF reads its whole range, so =COUNTIF(A1:A3,"CATIO") in A3 is circular (0).
        Recalc(wb, sheet, 3, 1, "COUNTIF(A1:A3,\"CATIO\")").Should().Be(new NumberValue(0));
    }

    [Fact]
    public void ChainedRangeOperator_FormsTheBoundingRange()
    {
        var (wb, sheet) = Setup();
        // Excel: B1:B2:B3 is B1:B3 (the range operator is left-associative). B1=5, B2=7, B3=blank.
        Recalc(wb, sheet, 10, 4, "SUM(B1:B2:B3)").Should().Be(new NumberValue(12));
    }

    [Fact]
    public void WholeColumnExternalLookup_PreservesLoadedValueInsteadOfRef()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("Sheet1");
        var link = new ExternalLinkModel { PackagePart = "xl/externalLinks/externalLink1.xml", TargetUri = "Data File.xlsx", TargetMode = "External" };
        link.SheetNames.Add("Vendors");
        wb.ExternalLinks.Add(link);

        var key = new CellAddress(sheet.Id, 1, 1);
        var lookup = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(key, new TextValue("k"));
        sheet.SetFormula(lookup, "VLOOKUP(A1,[1]Vendors!E:F,2,0)");
        sheet.GetCell(lookup)!.Value = new TextValue("cached vendor name");

        new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()).RecalculateAllFormulas(wb);

        // The external cache holds only the cells Excel referenced; a whole-column external range can
        // never be recomputed locally, so the last-known cached value must survive (not become #REF!).
        sheet.GetValue(lookup).Should().Be(new TextValue("cached vendor name"));
    }

    [Fact]
    public void RowAndColumnOfOwnCell_AreNotCircular()
    {
        var (wb, sheet) = Setup();
        Recalc(wb, sheet, 8, 4, "ROW(D8)").Should().Be(new NumberValue(8));
        Recalc(wb, sheet, 10, 4, "COLUMN(D10)").Should().Be(new NumberValue(4));
        Recalc(wb, sheet, 11, 4, "ROWS(D11:D13)").Should().Be(new NumberValue(3));
    }
}
