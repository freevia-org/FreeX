using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-20 regression tests for defined-name evaluation:
///   R20-defined-name-eval-deep-1: a named formula with RELATIVE (non-$) references must be
///     re-anchored per using cell (Excel-classic relative-name behaviour), not evaluated
///     literally from its definition text no matter where it's used.
///   R20-defined-name-eval-deep-2: INDIRECT("Name") must resolve a name whose RefersTo is a
///     formula/dynamic expression (e.g. OFFSET-based), not just a plain named range.
/// </summary>
public class R20_defined_name_eval_Tests
{
    private readonly FormulaEvaluator _evaluator = new();

    // ── R20-defined-name-eval-deep-1 ──────────────────────────────────────────

    [Fact]
    public void RelativeNamedFormula_UsedFromDifferentCells_ReAnchorsPerUsingCell()
    {
        // Foo = "=B2" (relative, no $ markers) — implicit anchor is A1 of the sheet.
        // Used from D4 (offset +3 rows/+3 cols from A1), Foo must resolve to B2 shifted by the
        // same (+3,+3) delta, i.e. E5 — NOT the literal, unshifted value at B2.
        // Used from A10 (offset +9 rows/+0 cols from A1), Foo must resolve to B2 shifted by
        // (+9,+0), i.e. B11 — a DIFFERENT result than the D4 case, proving per-cell re-anchoring
        // rather than a single fixed evaluation.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.NamedFormulas["Foo"] = "B2";

        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(999));   // B2 — must NOT be returned verbatim
        sheet.SetCell(new CellAddress(sheet.Id, 5, 5), new NumberValue(200));   // E5 — expected result when used from D4
        sheet.SetCell(new CellAddress(sheet.Id, 11, 2), new NumberValue(300));  // B11 — expected result when used from A10

        var fromD4 = _evaluator.Evaluate("=Foo", sheet, workbook, new CellAddress(sheet.Id, 4, 4));
        var fromA10 = _evaluator.Evaluate("=Foo", sheet, workbook, new CellAddress(sheet.Id, 10, 1));

        fromD4.Should().Be(new NumberValue(200));
        fromA10.Should().Be(new NumberValue(300));
        // The two using-cells must yield different, correctly-anchored results — not the same
        // unshifted B2 value (999) both times, which is what the pre-fix literal-AST evaluation
        // produced regardless of the using cell.
        fromD4.Should().NotBe(fromA10);
    }

    [Fact]
    public void AbsoluteNamedFormula_UsedFromDifferentCells_NeverShifts()
    {
        // Foo = "=$B$2" (absolute) must return the same value from every using cell — the
        // relative re-anchoring fix must not touch absolute references.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.NamedFormulas["Foo"] = "$B$2";
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(42));

        var fromD4 = _evaluator.Evaluate("=Foo", sheet, workbook, new CellAddress(sheet.Id, 4, 4));
        var fromA10 = _evaluator.Evaluate("=Foo", sheet, workbook, new CellAddress(sheet.Id, 10, 1));

        fromD4.Should().Be(new NumberValue(42));
        fromA10.Should().Be(new NumberValue(42));
    }

    // ── R20-defined-name-eval-deep-2 ──────────────────────────────────────────

    [Fact]
    public void Indirect_OfDynamicNamedFormula_ResolvesInsteadOfRef()
    {
        // Foo is a dynamic named range built from a formula expression (OFFSET+COUNTA), a
        // standard "growing range" authoring technique — not a plain static named range.
        // INDIRECT("Foo") must resolve it (matching direct use of "=Foo"), not return #REF!.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(30));
        workbook.NamedFormulas["Foo"] = "OFFSET($A$1,0,0,COUNTA($A:$A),1)";

        // Direct name use already worked pre-fix (sanity check it still does).
        _evaluator.Evaluate("=SUM(Foo)", sheet, workbook).Should().Be(new NumberValue(60));

        // INDIRECT of the same dynamic name must resolve to the same values, not #REF!.
        var result = _evaluator.Evaluate("=SUM(INDIRECT(\"Foo\"))", sheet, workbook);

        result.Should().Be(new NumberValue(60));
        result.Should().NotBe(ErrorValue.Ref);
    }

    [Fact]
    public void Indirect_OfPlainNamedRange_StillResolves()
    {
        // Regression guard: INDIRECT of an ordinary (non-formula) named range must keep working
        // exactly as before the fix.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(3));
        workbook.DefineNamedRange("MyData", new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1)));

        _evaluator.Evaluate("=SUM(INDIRECT(\"MyData\"))", sheet, workbook).Should().Be(new NumberValue(6));
    }

    [Fact]
    public void Indirect_OfUndefinedName_StillReturnsRef()
    {
        // Regression guard: a text string that resolves to neither a plain named range nor a
        // named formula must still fall through to #REF!, not silently succeed.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");

        var result = _evaluator.Evaluate("=INDIRECT(\"NotDefined\")", sheet, workbook);

        result.Should().Be(ErrorValue.Ref);
    }
}
