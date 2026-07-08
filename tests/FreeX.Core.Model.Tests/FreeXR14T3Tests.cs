using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression tests for round-14 bucket T3 (Data Table what-if findings):
///   R14-data-tables-whatif-2 — the formula rewriter must never rewrite cell-like text that sits
///     INSIDE a string literal; only the real (unquoted) cell reference is substituted.
///   R14-data-tables-whatif-3 — a one-variable data table body column/row whose header cell holds
///     a constant (or is blank) must repeat that constant (0 for blank), never reuse an unrelated
///     column/row's formula.
/// </summary>
public sealed class FreeXR14T3Tests
{
    private static readonly FormulaEvaluator Evaluator = new();

    [Fact]
    public void OneVariableDataTableCommand_ColumnOriented_PreservesCellLikeTextInsideStringLiterals()
    {
        // Input cell B3; result formula references B3 both as a REAL reference (IF(B3>100,...))
        // and as plain text inside two string literals ("B3 over" / "B3 under"). Excel substitutes
        // VALUES, not formula text, so the literal "B3" text must stay untouched — only the bare,
        // unquoted reference is rewritten to each row's trial-input cell.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        var inputCell = new CellAddress(sheet.Id, 3, 2);   // B3
        var formulaCell = new CellAddress(sheet.Id, 1, 4); // D1
        sheet.SetCell(inputCell, new NumberValue(50));
        sheet.SetFormula(formulaCell, "IF(B3>100,\"B3 over\",\"B3 under\")");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(150)); // C2 trial = 150
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(20));  // C3 trial = 20

        var command = new OneVariableDataTableCommand(
            new GridRange(new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 3, 4)),
            formulaCell,
            inputCell);

        command.Apply(ctx).Success.Should().BeTrue();

        var d2Formula = sheet.GetCell(2, 4)!.FormulaText!;
        var d3Formula = sheet.GetCell(3, 4)!.FormulaText!;

        d2Formula.Should().Be("IF(C2>100,\"B3 over\",\"B3 under\")",
            because: "only the bare B3 reference is substituted; the quoted labels must stay exactly \"B3 over\"/\"B3 under\"");
        d3Formula.Should().Be("IF(C3>100,\"B3 over\",\"B3 under\")");

        Evaluator.Evaluate(d2Formula, sheet, workbook).Should().Be(new TextValue("B3 over"));
        Evaluator.Evaluate(d3Formula, sheet, workbook).Should().Be(new TextValue("B3 under"));
    }

    [Fact]
    public void OneVariableDataTableCommand_ColumnOriented_NonFormulaHeaderRepeatsItsOwnConstantNotTheFirstFormula()
    {
        // Column B carries the real result formula (referencing the external rate cell E1).
        // Column C's header (C1) is a plain constant label with no formula. Column D's header
        // (D1) is entirely blank. Excel evaluates each column against its own top-row cell: a
        // constant header repeats that constant down the column, a blank header repeats 0 —
        // neither column may be filled with column B's formula/results.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        var rateCell = new CellAddress(sheet.Id, 1, 5); // E1
        sheet.SetCell(rateCell, new NumberValue(7));

        var b1 = new CellAddress(sheet.Id, 1, 2); // B1: the real result formula
        sheet.SetFormula(b1, "E1*10");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Total")); // C1: constant, no formula
        // D1 (row 1, col 4) is left entirely unset — a blank header.

        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(1)); // A2 trial
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(2)); // A3 trial

        var command = new OneVariableDataTableCommand(
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 4)),
            b1,
            rateCell);

        command.Apply(ctx).Success.Should().BeTrue();

        // Column B still computes normally from its own formula.
        var b2Formula = sheet.GetCell(2, 2)!.FormulaText!;
        Evaluator.Evaluate(b2Formula, sheet, workbook).Should().Be(new NumberValue(10)); // A2*10

        // Column C (constant header "Total"): every body cell repeats that same constant, never
        // column B's formula/result.
        var c2 = sheet.GetCell(2, 3)!;
        c2.FormulaText.Should().BeNull();
        c2.Value.Should().Be(new TextValue("Total"));
        var c3 = sheet.GetCell(3, 3)!;
        c3.FormulaText.Should().BeNull();
        c3.Value.Should().Be(new TextValue("Total"));

        // Column D (blank header): every body cell is the constant 0, never column B's formula.
        var d2 = sheet.GetCell(2, 4)!;
        d2.FormulaText.Should().BeNull();
        d2.Value.Should().Be(new NumberValue(0));
        var d3 = sheet.GetCell(3, 4)!;
        d3.FormulaText.Should().BeNull();
        d3.Value.Should().Be(new NumberValue(0));
    }
}
