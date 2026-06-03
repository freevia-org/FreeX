using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class PhaseA2FunctionTests
{
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

    [Fact]
    public void IsRef_OffsetReference_ReturnsTrue()
    {
        var (wb, sheet) = MakeWb((1, 1, new NumberValue(10)));

        _eval.Evaluate("=ISREF(OFFSET(A1,0,0))", sheet, wb).Should().Be(new BoolValue(true));
    }

    [Fact]
    public void IsRef_IndirectReference_ReturnsTrue()
    {
        var (wb, sheet) = MakeWb((1, 1, new NumberValue(10)));

        _eval.Evaluate("=ISREF(INDIRECT(\"A1\"))", sheet, wb).Should().Be(new BoolValue(true));
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

    [Fact]
    public void IsFormula_OffsetReference_InspectsTargetCell()
    {
        var (wb, sheet) = MakeWb();
        sheet.SetFormula(new CellAddress(sheet.Id, 2, 2), "1+2");

        _eval.Evaluate("=ISFORMULA(OFFSET(A1,1,1))", sheet, wb).Should().Be(new BoolValue(true));
    }

    [Fact]
    public void IsFormula_IndirectReference_InspectsTargetCell()
    {
        var (wb, sheet) = MakeWb();
        sheet.SetFormula(new CellAddress(sheet.Id, 2, 2), "1+2");

        _eval.Evaluate("=ISFORMULA(INDIRECT(\"B2\"))", sheet, wb).Should().Be(new BoolValue(true));
    }

    // ── FORMULATEXT ──────────────────────────────────────────────────────────

    [Fact]
    public void FormulaText_FormulaCell_ReturnsFormulaWithEquals()
    {
        var (wb, sheet) = MakeWb();
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 1), "SUM(B1:B3)");
        _eval.Evaluate("=FORMULATEXT(A1)", sheet, wb).Should().Be(new TextValue("=SUM(B1:B3)"));
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

    [Fact]
    public void FormulaText_OffsetReference_ReturnsTargetFormulaWithEquals()
    {
        var (wb, sheet) = MakeWb();
        sheet.SetFormula(new CellAddress(sheet.Id, 2, 2), "SUM(C1:C3)");

        _eval.Evaluate("=FORMULATEXT(OFFSET(A1,1,1))", sheet, wb)
            .Should().Be(new TextValue("=SUM(C1:C3)"));
    }

    [Fact]
    public void FormulaText_IndirectReference_ReturnsTargetFormulaWithEquals()
    {
        var (wb, sheet) = MakeWb();
        sheet.SetFormula(new CellAddress(sheet.Id, 2, 2), "SUM(C1:C3)");

        _eval.Evaluate("=FORMULATEXT(INDIRECT(\"B2\"))", sheet, wb)
            .Should().Be(new TextValue("=SUM(C1:C3)"));
    }
}
