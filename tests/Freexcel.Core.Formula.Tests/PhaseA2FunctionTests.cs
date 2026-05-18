using Freexcel.Core.Formula;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.Core.Formula.Tests;

/// <summary>
/// Tests for Phase A2 functions:
///   ISREF, ISFORMULA, FORMULATEXT, OFFSET, CELL, INFO, AGGREGATE, CONVERT.
/// </summary>
public class PhaseA2FunctionTests
{
    private readonly FormulaEvaluator _eval = new();

    private static (Workbook wb, Sheet sheet) MakeWb(params (int row, int col, ScalarValue val)[] cells)
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("S");
        foreach (var (r, c, v) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)r, (uint)c), v);
        return (wb, sheet);
    }

    // ── ISREF ────────────────────────────────────────────────────────────────

    [Fact]
    public void IsRef_CellRef_ReturnsTrue()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=ISREF(A1)", sheet, wb).Should().Be(new BoolValue(true));
    }

    [Fact]
    public void IsRef_RangeRef_ReturnsTrue()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=ISREF(A1:B3)", sheet, wb).Should().Be(new BoolValue(true));
    }

    [Fact]
    public void IsRef_NumberLiteral_ReturnsFalse()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=ISREF(1)", sheet, wb).Should().Be(new BoolValue(false));
    }

    [Fact]
    public void IsRef_UndefinedName_ReturnsFalse()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=ISREF(SomeUndefinedName)", sheet, wb).Should().Be(new BoolValue(false));
    }

    [Fact]
    public void IsRef_DefinedName_ReturnsTrue()
    {
        var (wb, sheet) = MakeWb();
        wb.DefineNamedRange("MyData", new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3)));
        _eval.Evaluate("=ISREF(MyData)", sheet, wb).Should().Be(new BoolValue(true));
    }

    // ── ISFORMULA ────────────────────────────────────────────────────────────

    [Fact]
    public void IsFormula_FormulaCell_ReturnsTrue()
    {
        var (wb, sheet) = MakeWb();
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 1), "1+2");
        _eval.Evaluate("=ISFORMULA(A1)", sheet, wb).Should().Be(new BoolValue(true));
    }

    [Fact]
    public void IsFormula_ValueCell_ReturnsFalse()
    {
        var (wb, sheet) = MakeWb((1, 1, new NumberValue(42)));
        _eval.Evaluate("=ISFORMULA(A1)", sheet, wb).Should().Be(new BoolValue(false));
    }

    [Fact]
    public void IsFormula_EmptyCell_ReturnsFalse()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=ISFORMULA(A1)", sheet, wb).Should().Be(new BoolValue(false));
    }

    [Fact]
    public void IsFormula_Number_ReturnsValueError()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=ISFORMULA(1)", sheet, wb).Should().Be(ErrorValue.Value);
    }

    // ── FORMULATEXT ──────────────────────────────────────────────────────────

    [Fact]
    public void FormulaText_FormulaCell_ReturnsFormulaWithoutEquals()
    {
        var (wb, sheet) = MakeWb();
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 1), "SUM(B1:B3)");
        _eval.Evaluate("=FORMULATEXT(A1)", sheet, wb).Should().Be(new TextValue("SUM(B1:B3)"));
    }

    [Fact]
    public void FormulaText_ValueCell_ReturnsNA()
    {
        var (wb, sheet) = MakeWb((1, 1, new NumberValue(42)));
        _eval.Evaluate("=FORMULATEXT(A1)", sheet, wb).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void FormulaText_NonRef_ReturnsNA()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=FORMULATEXT(1)", sheet, wb).Should().Be(ErrorValue.NA);
    }

    // ── OFFSET ───────────────────────────────────────────────────────────────

    [Fact]
    public void Offset_ZeroOffset_ReturnsBaseCellValue()
    {
        var (wb, sheet) = MakeWb((1, 1, new NumberValue(42)));
        _eval.Evaluate("=OFFSET(A1,0,0)", sheet, wb).Should().Be(new NumberValue(42));
    }

    [Fact]
    public void Offset_RowOffset_ReturnsTargetCell()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(10)),
            (3, 1, new NumberValue(30)));
        _eval.Evaluate("=OFFSET(A1,2,0)", sheet, wb).Should().Be(new NumberValue(30));
    }

    [Fact]
    public void Offset_ColOffset_ReturnsTargetCell()
    {
        var (wb, sheet) = MakeWb((1, 3, new NumberValue(99)));
        _eval.Evaluate("=OFFSET(A1,0,2)", sheet, wb).Should().Be(new NumberValue(99));
    }

    [Fact]
    public void Offset_OutOfBoundsRowNegative_ReturnsRef()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=OFFSET(A1,-1,0)", sheet, wb).Should().Be(ErrorValue.Ref);
    }

    [Fact]
    public void Offset_OutOfBoundsColNegative_ReturnsRef()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=OFFSET(A1,0,-1)", sheet, wb).Should().Be(ErrorValue.Ref);
    }

    [Fact]
    public void Offset_FeedsSumproduct_ReturnsRangeSum()
    {
        var (wb, sheet) = MakeWb(
            (2, 2, new NumberValue(1)), (2, 3, new NumberValue(2)),
            (3, 2, new NumberValue(3)), (3, 3, new NumberValue(4)));
        // SUMPRODUCT consumes the 2x2 RangeValue produced by OFFSET.
        _eval.Evaluate("=SUMPRODUCT(OFFSET(A1,1,1,2,2))", sheet, wb).Should().Be(new NumberValue(10));
    }

    [Fact]
    public void Offset_IsVolatile()
    {
        BuiltInFunctions.IsVolatile("OFFSET").Should().BeTrue();
    }

    // ── CELL ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Cell_Address_ReturnsAbsoluteAddress()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=CELL(\"address\",B3)", sheet, wb).Should().Be(new TextValue("$B$3"));
    }

    [Fact]
    public void Cell_Row_ReturnsRowNumber()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=CELL(\"row\",B5)", sheet, wb).Should().Be(new NumberValue(5));
    }

    [Fact]
    public void Cell_Col_ReturnsColumnNumber()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=CELL(\"col\",C1)", sheet, wb).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Cell_Contents_ReturnsCellValue()
    {
        var (wb, sheet) = MakeWb((1, 1, new NumberValue(123)));
        _eval.Evaluate("=CELL(\"contents\",A1)", sheet, wb).Should().Be(new NumberValue(123));
    }

    [Fact]
    public void Cell_TypeText_ReturnsL()
    {
        var (wb, sheet) = MakeWb((1, 1, new TextValue("hi")));
        _eval.Evaluate("=CELL(\"type\",A1)", sheet, wb).Should().Be(new TextValue("l"));
    }

    [Fact]
    public void Cell_TypeNumber_ReturnsV()
    {
        var (wb, sheet) = MakeWb((1, 1, new NumberValue(1)));
        _eval.Evaluate("=CELL(\"type\",A1)", sheet, wb).Should().Be(new TextValue("v"));
    }

    [Fact]
    public void Cell_TypeBlank_ReturnsB()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=CELL(\"type\",A1)", sheet, wb).Should().Be(new TextValue("b"));
    }

    [Fact]
    public void Cell_UnknownInfo_ReturnsValueError()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=CELL(\"bogus\",A1)", sheet, wb).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Cell_Width_ReturnsColumnWidth()
    {
        var (wb, sheet) = MakeWb();
        sheet.ColumnWidths[1] = 12.5;
        _eval.Evaluate("=CELL(\"width\",A1)", sheet, wb).Should().Be(new NumberValue(12.5));
    }

    [Fact]
    public void Cell_Protect_UnprotectedSheet_ReturnsZero()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=CELL(\"protect\",A1)", sheet, wb).Should().Be(new NumberValue(0));
    }

    // ── INFO ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Info_NumFile_ReturnsSheetCount()
    {
        var (wb, sheet) = MakeWb();
        wb.AddSheet("S2");
        _eval.Evaluate("=INFO(\"numfile\")", sheet, wb).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Info_Release_ReturnsSixteenZero()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=INFO(\"release\")", sheet, wb).Should().Be(new TextValue("16.0"));
    }

    [Fact]
    public void Info_System_ReturnsPcDos()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=INFO(\"system\")", sheet, wb).Should().Be(new TextValue("pcdos"));
    }

    [Fact]
    public void Info_Recalc_AutomaticByDefault()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=INFO(\"recalc\")", sheet, wb).Should().Be(new TextValue("Automatic"));
    }

    [Fact]
    public void Info_Recalc_ManualWhenSet()
    {
        var (wb, sheet) = MakeWb();
        wb.CalculationMode = WorkbookCalculationMode.Manual;
        _eval.Evaluate("=INFO(\"recalc\")", sheet, wb).Should().Be(new TextValue("Manual"));
    }

    [Fact]
    public void Info_Unknown_ReturnsValueError()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=INFO(\"bogus\")", sheet, wb).Should().Be(ErrorValue.Value);
    }

    // ── AGGREGATE ────────────────────────────────────────────────────────────

    [Fact]
    public void Aggregate_Sum_BasicRange()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(2)),
            (3, 1, new NumberValue(3)));
        // function 9 = SUM, options 4 = ignore nothing
        _eval.Evaluate("=AGGREGATE(9,4,A1:A3)", sheet, wb).Should().Be(new NumberValue(6));
    }

    [Fact]
    public void Aggregate_Sum_IgnoresErrorsWhenOption6()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(1)),
            (2, 1, ErrorValue.DivByZero),
            (3, 1, new NumberValue(3)));
        _eval.Evaluate("=AGGREGATE(9,6,A1:A3)", sheet, wb).Should().Be(new NumberValue(4));
    }

    [Fact]
    public void Aggregate_Sum_PropagatesErrorsWhenOption4()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(1)),
            (2, 1, ErrorValue.DivByZero));
        _eval.Evaluate("=AGGREGATE(9,4,A1:A2)", sheet, wb).Should().Be(ErrorValue.DivByZero);
    }

    [Fact]
    public void Aggregate_Average_BasicRange()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(10)),
            (2, 1, new NumberValue(20)));
        _eval.Evaluate("=AGGREGATE(1,4,A1:A2)", sheet, wb).Should().Be(new NumberValue(15));
    }

    [Fact]
    public void Aggregate_Large_RequiresK()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(2)),
            (3, 1, new NumberValue(3)));
        _eval.Evaluate("=AGGREGATE(14,4,A1:A3,1)", sheet, wb).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Aggregate_Small_WithK()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(5)),
            (2, 1, new NumberValue(1)),
            (3, 1, new NumberValue(3)));
        _eval.Evaluate("=AGGREGATE(15,4,A1:A3,2)", sheet, wb).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Aggregate_InvalidFuncNum_ReturnsValueError()
    {
        var (wb, sheet) = MakeWb((1, 1, new NumberValue(1)));
        _eval.Evaluate("=AGGREGATE(20,4,A1)", sheet, wb).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Aggregate_Count()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(1)),
            (2, 1, new TextValue("x")),
            (3, 1, new NumberValue(3)));
        _eval.Evaluate("=AGGREGATE(2,4,A1:A3)", sheet, wb).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Aggregate_Max()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(5)),
            (2, 1, new NumberValue(11)));
        _eval.Evaluate("=AGGREGATE(4,4,A1:A2)", sheet, wb).Should().Be(new NumberValue(11));
    }

    // ── CONVERT ──────────────────────────────────────────────────────────────

    [Fact]
    public void Convert_KgToG_Multiplies()
    {
        var (wb, sheet) = MakeWb();
        var result = _eval.Evaluate("=CONVERT(1,\"kg\",\"g\")", sheet, wb);
        result.Should().Be(new NumberValue(1000));
    }

    [Fact]
    public void Convert_MeterToCentimeter()
    {
        var (wb, sheet) = MakeWb();
        var result = _eval.Evaluate("=CONVERT(1,\"m\",\"cm\")", sheet, wb);
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(100, 1e-9);
    }

    [Fact]
    public void Convert_HoursToSeconds()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=CONVERT(1,\"hr\",\"sec\")", sheet, wb).Should().Be(new NumberValue(3600));
    }

    [Fact]
    public void Convert_CelsiusToFahrenheit()
    {
        var (wb, sheet) = MakeWb();
        var result = _eval.Evaluate("=CONVERT(100,\"C\",\"F\")", sheet, wb);
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(212, 1e-9);
    }

    [Fact]
    public void Convert_FahrenheitToCelsius()
    {
        var (wb, sheet) = MakeWb();
        var result = _eval.Evaluate("=CONVERT(32,\"F\",\"C\")", sheet, wb);
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(0, 1e-9);
    }

    [Fact]
    public void Convert_CelsiusToKelvin()
    {
        var (wb, sheet) = MakeWb();
        var result = _eval.Evaluate("=CONVERT(0,\"C\",\"K\")", sheet, wb);
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(273.15, 1e-9);
    }

    [Fact]
    public void Convert_IncompatibleCategories_ReturnsNA()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=CONVERT(1,\"kg\",\"m\")", sheet, wb).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Convert_UnknownUnit_ReturnsNA()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=CONVERT(1,\"foo\",\"g\")", sheet, wb).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Convert_BytesToBits()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=CONVERT(1,\"byte\",\"bit\")", sheet, wb).Should().Be(new NumberValue(8));
    }
}
