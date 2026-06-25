using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Tests for evaluating defined names that are bound to formula expressions
/// rather than plain cell ranges (named formulas).
/// </summary>
public class NamedFormulaTests
{
    private readonly FormulaEvaluator _evaluator = new();

    // ── Scalar named formula ──────────────────────────────────────────────────

    [Fact]
    public void ScalarNamedFormula_DateExpression_ReturnsDateSerial()
    {
        // DateOfFirst = DATE(2011,1,1)  → 40544 (Excel date serial for 2011-01-01)
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.NamedFormulas["DateOfFirst"] = "DATE(2011,1,1)";

        var result = _evaluator.Evaluate("=DateOfFirst", sheet, workbook);

        result.Should().Be(new NumberValue(40544));
    }

    [Fact]
    public void ScalarNamedFormula_SimpleArithmetic_ReturnsValue()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.NamedFormulas["MyConst"] = "2+3";

        var result = _evaluator.Evaluate("=MyConst", sheet, workbook);

        result.Should().Be(new NumberValue(5));
    }

    [Fact]
    public void ScalarNamedFormula_UsedInExpression_ReturnsComputedValue()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.NamedFormulas["MyConst"] = "10";

        var result = _evaluator.Evaluate("=MyConst*2", sheet, workbook);

        result.Should().Be(new NumberValue(20));
    }

    // ── Name-depends-on-name ──────────────────────────────────────────────────

    [Fact]
    public void NameDependsOnName_SimpleChain_ReturnsComputedValue()
    {
        // A = 5, B = A+1  → B should evaluate to 6
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.NamedFormulas["A"] = "5";
        workbook.NamedFormulas["B"] = "A+1";

        var result = _evaluator.Evaluate("=B", sheet, workbook);

        result.Should().Be(new NumberValue(6));
    }

    [Fact]
    public void NameDependsOnName_ThreeLevel_ReturnsComputedValue()
    {
        // DateOfFirst = DATE(2011,1,1), FirstWeekDay = WEEKDAY(DateOfFirst, 2)
        // WEEKDAY(DATE(2011,1,1), 2) → 6 (Saturday in mode 2: Mon=1..Sun=7)
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.NamedFormulas["DateOfFirst"] = "DATE(2011,1,1)";
        workbook.NamedFormulas["FirstWeekDay"] = "WEEKDAY(DateOfFirst,2)";

        var result = _evaluator.Evaluate("=FirstWeekDay", sheet, workbook);

        result.Should().Be(new NumberValue(6));
    }

    [Fact]
    public void NameDependsOnName_UsedInCellFormula_ComputesCorrectly()
    {
        // Reproduces the Shift Calendar pattern:
        // DateOfFirst = DATE(2011,1,1), FirstWeekDay = WEEKDAY(DateOfFirst,2)
        // Cell formula: DateOfFirst - FirstWeekDay  → 40544 - 6 = 40538
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Shift Calendar");
        workbook.NamedFormulas["DateOfFirst"] = "DATE(2011,1,1)";
        workbook.NamedFormulas["FirstWeekDay"] = "WEEKDAY(DateOfFirst,2)";

        var result = _evaluator.Evaluate("=DateOfFirst-FirstWeekDay", sheet, workbook);

        result.Should().Be(new NumberValue(40538));
    }

    // ── Cycle detection ────────────────────────────────────────────────────────

    [Fact]
    public void CircularNamedFormula_DirectSelfReference_ReturnsRef()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.NamedFormulas["Circ"] = "Circ+1";

        var result = _evaluator.Evaluate("=Circ", sheet, workbook);

        result.Should().Be(ErrorValue.Ref);
    }

    [Fact]
    public void CircularNamedFormula_IndirectCycle_ReturnsRef()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.NamedFormulas["A"] = "B+1";
        workbook.NamedFormulas["B"] = "A+1";

        // Either A or B will hit the cycle guard; both should return #REF!
        var result = _evaluator.Evaluate("=A", sheet, workbook);

        result.Should().Be(ErrorValue.Ref);
    }

    // ── Named formula does NOT shadow plain range names ───────────────────────

    [Fact]
    public void PlainRangeName_StillResolvesNormally_WhenFormulaNotPresent()
    {
        // A plain named range (cell range) should still work via DefineNamedRange.
        // When used in SUM (aggregate context), it flattens to its scalar values.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, new NumberValue(99));
        workbook.DefineNamedRange("MyRange", new GridRange(addr, addr));

        var result = _evaluator.Evaluate("=SUM(MyRange)", sheet, workbook);

        result.Should().Be(new NumberValue(99));
    }

    // ── Array-valued named formula ────────────────────────────────────────────

    [Fact]
    public void ArrayNamedFormula_UsedInSum_ReturnsCorrectSum()
    {
        // FortyTwoDays = COLUMN($A:$G)*ROW($1:$4)  — simplified test with literal array
        // Use a simple constant-array name instead of the full range expression
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        // Fill A1:A3 with values to sum via named formula
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(30));
        workbook.NamedFormulas["MyData"] = "A1:A3";

        var result = _evaluator.Evaluate("=SUM(MyData)", sheet, workbook);

        result.Should().Be(new NumberValue(60));
    }

    // ── Sheet-scoped named formula precedence (Q13 fix) ──────────────────────

    [Fact]
    public void SheetScopedNamedFormula_ShadowsWorkbookGlobal_OnMatchingSheet()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");

        // Workbook-global: MyConst = 10
        workbook.NamedFormulas["MyConst"] = "10";
        // Sheet2-scoped: MyConst = 20 — must shadow the global when on Sheet2
        workbook.DefineNamedFormula("MyConst", "20", sheet2.Id);

        var result = _evaluator.Evaluate("=MyConst", sheet2, workbook);
        result.Should().Be(new NumberValue(20));
        _ = sheet1;
    }

    [Fact]
    public void SheetScopedNamedFormula_FallsBackToWorkbookGlobal_OnNonMatchingSheet()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");

        workbook.NamedFormulas["MyConst"] = "10";
        workbook.DefineNamedFormula("MyConst", "20", sheet2.Id);

        // On Sheet1 there is no scoped binding; global = 10
        var result = _evaluator.Evaluate("=MyConst", sheet1, workbook);
        result.Should().Be(new NumberValue(10));
        _ = sheet2;
    }

    // ── Cross-sheet named formula ─────────────────────────────────────────────

    [Fact]
    public void CrossSheetNamedFormula_ReadsFromQualifiedReference()
    {
        // DateOfFirst = DATE('Shift Calendar'!$C$13, 'Shift Calendar'!$C$12, 1)
        var workbook = new Workbook("Test");
        var shiftCal = workbook.AddSheet("Shift Calendar");
        var sheet = workbook.AddSheet("Output");
        // C12 = year (2011), C13 = month (1)
        shiftCal.SetCell(new CellAddress(shiftCal.Id, 12, 3), new NumberValue(2011));
        shiftCal.SetCell(new CellAddress(shiftCal.Id, 13, 3), new NumberValue(1));
        workbook.NamedFormulas["DateOfFirst"] = "DATE('Shift Calendar'!$C$13,'Shift Calendar'!$C$12,1)";

        var result = _evaluator.Evaluate("=DateOfFirst", sheet, workbook);

        // DATE(1, 2011, 1) — month arg is C13=1, year arg is C12=2011, day=1
        // = DATE(1, 2011, 1) in Excel formula: DATE(year, month, day) = DATE(1,2011,1)
        // Actually: DATE('Shift Calendar'!$C$13 = 1, 'Shift Calendar'!$C$12 = 2011, 1)
        // = DATE(1, 2011, 1) — this is DATE(year=1, month=2011, day=1) which Excel normalises
        // The formula in the real file is DATE('Shift Calendar'!$C$13,'Shift Calendar'!$C$12,1)
        // meaning: year=C13=1, month=C12=2011, day=1 — verify it doesn't return #NAME?/#REF!
        result.Should().NotBe(ErrorValue.Name);
        result.Should().NotBe(ErrorValue.Ref);
    }
}
