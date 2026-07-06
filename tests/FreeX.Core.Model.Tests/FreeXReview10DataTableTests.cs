using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression tests for round-10 review findings P16 and P18 (Data Table):
///   P16 — a Data Table must produce VARYING results when the result formula references the
///         input cell only INDIRECTLY (through an intermediate formula cell, or through an
///         explicit same-sheet-qualified reference like Sheet1!A1) instead of silently
///         collapsing to one repeated constant.
///   P18 — a one-variable Data Table with MULTIPLE result formulas across the header
///         row/column (e.g. B1 and C1 for a column-oriented table) must compute each body
///         column/row from ITS OWN header formula, not always from the single default formula
///         cell.
/// </summary>
public sealed class FreeXReview10DataTableTests
{
    private static readonly FormulaEvaluator Evaluator = new();

    // ── P16: indirect reference through an intermediate formula cell ───────────

    [Fact]
    public void OneVariableDataTableCommand_IndirectInputReferenceProducesVaryingResults()
    {
        // A1 = input (overridden per trial), B1 = intermediate "=A1*2", C1 = data-table
        // formula "=B1+1" (referencing the input cell only THROUGH B1, never directly).
        // Table range C1:D3 (column-oriented): trial values in C2/C3, results written to D2/D3.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        var inputCell = new CellAddress(sheet.Id, 1, 1);     // A1
        var intermediateCell = new CellAddress(sheet.Id, 1, 2); // B1
        var formulaCell = new CellAddress(sheet.Id, 1, 3);   // C1

        sheet.SetCell(inputCell, new NumberValue(5));
        sheet.SetFormula(intermediateCell, "A1*2");
        sheet.SetFormula(formulaCell, "B1+1");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(3)); // C2 trial = 3
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(7)); // C3 trial = 7

        var command = new OneVariableDataTableCommand(
            new GridRange(new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 3, 4)),
            formulaCell,
            inputCell);

        command.Apply(ctx).Success.Should().BeTrue();

        var d2Formula = sheet.GetCell(2, 4)!.FormulaText!;
        var d3Formula = sheet.GetCell(3, 4)!.FormulaText!;

        // The pre-fix rewriter only replaced DIRECT references to A1; since "B1+1" never
        // mentions A1 directly, both body cells degenerated to the identical formula "B1+1"
        // (a constant, since B1's cached value never changes). The fix must make the two body
        // formulas differ from each other.
        d2Formula.Should().NotBe(d3Formula,
            because: "each row must substitute its own trial value through the B1 -> A1 chain, not collapse to one constant");
        d2Formula.Should().NotBe("B1+1",
            because: "the formula must be rewritten to reach through the intermediate cell, not left unchanged");

        // Evaluate the rewritten body formulas directly: since the fix inlines B1's formula
        // text into the body cell (rather than relying on B1's stale cached value), each body
        // cell must be self-consistent and correct on its own.
        Evaluator.Evaluate(d2Formula, sheet, workbook).Should().Be(new NumberValue(7));  // (3*2)+1
        Evaluator.Evaluate(d3Formula, sheet, workbook).Should().Be(new NumberValue(15)); // (7*2)+1
    }

    [Fact]
    public void OneVariableDataTableCommand_SameSheetQualifiedInputReferenceIsSubstituted()
    {
        // The data-table formula spells its own sheet out explicitly (Sheet1!A1) instead of
        // using the bare form (A1). The pre-fix regex's lookbehind deliberately excluded any
        // '!'-prefixed match (to protect genuine cross-sheet references), which also blocked
        // this same-sheet-qualified spelling, degenerating the table to a constant.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        var inputCell = new CellAddress(sheet.Id, 1, 1);   // A1
        var formulaCell = new CellAddress(sheet.Id, 1, 2); // B1

        sheet.SetCell(inputCell, new NumberValue(5));
        sheet.SetFormula(formulaCell, "Sheet1!A1*2");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(3)); // A2 trial = 3
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(7)); // A3 trial = 7

        var command = new OneVariableDataTableCommand(
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            formulaCell,
            inputCell);

        command.Apply(ctx).Success.Should().BeTrue();

        var b2Formula = sheet.GetCell(2, 2)!.FormulaText!;
        var b3Formula = sheet.GetCell(3, 2)!.FormulaText!;

        b2Formula.Should().NotBe("Sheet1!A1*2",
            because: "the same-sheet-qualified input reference must be substituted, not left unchanged");
        b2Formula.Should().NotBe(b3Formula,
            because: "each row must get its own trial value substituted");

        Evaluator.Evaluate(b2Formula, sheet, workbook).Should().Be(new NumberValue(6));  // 3*2
        Evaluator.Evaluate(b3Formula, sheet, workbook).Should().Be(new NumberValue(14)); // 7*2
    }

    [Fact]
    public void OneVariableDataTableCommand_CrossSheetReferenceStillNeverSubstituted()
    {
        // Regression guard alongside the new same-sheet-qualified handling: a reference to the
        // SAME coordinates on a genuinely DIFFERENT sheet must still never be rewritten.
        var workbook = new Workbook("test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        var ctx = new TestCommandContext(workbook);

        var inputCell = new CellAddress(sheet1.Id, 1, 1);   // Sheet1!A1
        var formulaCell = new CellAddress(sheet1.Id, 1, 2); // Sheet1!B1
        sheet1.SetCell(inputCell, new NumberValue(5));
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(100));
        sheet1.SetFormula(formulaCell, "A1+Sheet2!A1");
        sheet1.SetCell(new CellAddress(sheet1.Id, 2, 1), new NumberValue(3));

        var command = new OneVariableDataTableCommand(
            new GridRange(new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 2, 2)),
            formulaCell,
            inputCell);

        command.Apply(ctx).Success.Should().BeTrue();

        var rewritten = sheet1.GetCell(2, 2)!.FormulaText!;
        rewritten.Should().Contain("Sheet2!A1");
        Evaluator.Evaluate(rewritten, sheet1, workbook).Should().Be(new NumberValue(103)); // 3+100
    }

    // ── P18: multiple result formulas across the header row ────────────────────

    [Fact]
    public void OneVariableDataTableCommand_ColumnOriented_EachBodyColumnUsesItsOwnHeaderFormula()
    {
        // External "rate" cell E1 is the actual data-table input. Header row 1 carries TWO
        // different result formulas: B1 = "E1*10", C1 = "E1*100". Trial values run down column
        // A. Table range A1:C3. Column B must compute from B1's formula, column C from C1's —
        // not both columns from the single default formula cell (B1).
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        var rateCell = new CellAddress(sheet.Id, 1, 5); // E1
        sheet.SetCell(rateCell, new NumberValue(0));

        var b1 = new CellAddress(sheet.Id, 1, 2);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetFormula(b1, "E1*10");
        sheet.SetFormula(c1, "E1*100");

        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(1)); // A2 trial = 1
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(2)); // A3 trial = 2

        var command = new OneVariableDataTableCommand(
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3)),
            b1, // default formula cell, as DataTablePlanner.GetDefaultFormulaCell would supply
            rateCell);

        command.Apply(ctx).Success.Should().BeTrue();

        var b2Formula = sheet.GetCell(2, 2)!.FormulaText!;
        var c2Formula = sheet.GetCell(2, 3)!.FormulaText!;

        c2Formula.Should().NotBe(b2Formula,
            because: "column C must use its own C1 formula, not silently reuse column B's B1 formula");

        Evaluator.Evaluate(b2Formula, sheet, workbook).Should().Be(new NumberValue(10));  // A2*10
        Evaluator.Evaluate(c2Formula, sheet, workbook).Should().Be(new NumberValue(100)); // A2*100

        var b3Formula = sheet.GetCell(3, 2)!.FormulaText!;
        var c3Formula = sheet.GetCell(3, 3)!.FormulaText!;
        Evaluator.Evaluate(b3Formula, sheet, workbook).Should().Be(new NumberValue(20));  // A3*10
        Evaluator.Evaluate(c3Formula, sheet, workbook).Should().Be(new NumberValue(200)); // A3*100
    }

    [Fact]
    public void OneVariableDataTableCommand_RowOriented_EachBodyRowUsesItsOwnHeaderFormula()
    {
        // Mirror of the column-oriented case: trial values run across the header row, and
        // multiple result formulas run down the header column (A2 and A3), each body row must
        // use its own row's formula.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        var rateCell = new CellAddress(sheet.Id, 5, 1); // A5
        sheet.SetCell(rateCell, new NumberValue(0));

        var a2 = new CellAddress(sheet.Id, 2, 1);
        var a3 = new CellAddress(sheet.Id, 3, 1);
        sheet.SetFormula(a2, "A5*10");
        sheet.SetFormula(a3, "A5*100");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(1)); // B1 trial = 1
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(2)); // C1 trial = 2

        var command = new OneVariableDataTableCommand(
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3)),
            a2, // default formula cell for Row orientation, per DataTablePlanner.GetDefaultFormulaCell
            rateCell,
            DataTableInputOrientation.Row);

        command.Apply(ctx).Success.Should().BeTrue();

        var b2Formula = sheet.GetCell(2, 2)!.FormulaText!;
        var b3Formula = sheet.GetCell(3, 2)!.FormulaText!;

        b3Formula.Should().NotBe(b2Formula,
            because: "row 3 must use its own A3 formula, not silently reuse row 2's A2 formula");

        Evaluator.Evaluate(b2Formula, sheet, workbook).Should().Be(new NumberValue(10));  // B1*10
        Evaluator.Evaluate(b3Formula, sheet, workbook).Should().Be(new NumberValue(100)); // B1*100

        var c2Formula = sheet.GetCell(2, 3)!.FormulaText!;
        var c3Formula = sheet.GetCell(3, 3)!.FormulaText!;
        Evaluator.Evaluate(c2Formula, sheet, workbook).Should().Be(new NumberValue(20));  // C1*10
        Evaluator.Evaluate(c3Formula, sheet, workbook).Should().Be(new NumberValue(200)); // C1*100
    }
}
