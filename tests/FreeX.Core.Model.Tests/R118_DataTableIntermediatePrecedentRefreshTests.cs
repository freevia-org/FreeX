using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R118-data-table-intermediate-precedent-refresh: R115 fixed the case where an edit lands directly
/// on a Data Table's master/header formula cell. But when that driver formula does not reference the
/// input cell directly and instead reaches it only through another (intermediate) formula cell --
/// e.g. driver cell C1 = "=E1" where E1 = "=B1*2" and B1 is the table's input cell --
/// DataTableFormulaRewriter.InlineAndSubstitute recursively inlines E1's formula TEXT verbatim into
/// every body cell at substitution time, then discards any live reference to E1 itself. R115's
/// IsDriverCellAmongEdits only ever matched the registration's FormulaCell or (for one-variable
/// tables) another header row/column cell -- never a cell reached only indirectly -- so editing E1
/// left the whole Data Table body permanently stale even though C1 itself (via ordinary formula
/// recalculation) picked up the change immediately. These tests exercise the fix through the real
/// product entry point for retyping a cell's formula -- EditCellsCommand -- exactly like R115's own
/// tests, rather than asserting on the rewriter helper directly.
/// </summary>
public sealed class R118_DataTableIntermediatePrecedentRefreshTests
{
    [Fact]
    public void EditCellsCommand_EditingIntermediatePrecedent_RefreshesOneVariableColumnOrientedBody()
    {
        // Column-oriented one-variable table: trial values run down column C (the table's own header
        // column), the master/driver formula lives in D1 (the body column's header row), body cells
        // are D2/D3. Here D1 = "=E1" (an indirect reference) and the helper cell E1 = "=B1*2" is what
        // actually reaches the input cell B1 -- mirroring the finding's D1="=C1"/C1="=PMT(...)" shape
        // one column over so it doesn't collide with the table's own trial-value column C.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var inputCell = new CellAddress(sheet.Id, 1, 2); // B1
        var formulaCell = new CellAddress(sheet.Id, 1, 4); // D1
        var helperCell = new CellAddress(sheet.Id, 1, 5); // E1 -- NOT part of the table range at all
        sheet.SetCell(inputCell, new NumberValue(10));
        sheet.SetFormula(helperCell, "B1*2");
        sheet.SetFormula(formulaCell, "E1");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(1)); // C2 trial value
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(2)); // C3 trial value

        var creation = new OneVariableDataTableCommand(
            new GridRange(new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 3, 4)), // C1:D3
            formulaCell,
            inputCell);
        creation.Apply(ctx).Success.Should().BeTrue();

        // Table-creation time: InlineAndSubstitute could not match B1 directly in "E1", so it
        // recursively inlined E1's formula text in parentheses before substituting the trial cell.
        sheet.GetCell(2, 4)!.FormulaText.Should().Be("(C2*2)");
        sheet.GetCell(3, 4)!.FormulaText.Should().Be("(C3*2)");

        // Retype the INTERMEDIATE precedent (E1), never the driver cell (D1) itself -- exactly the
        // scenario the finding describes (e.g. changing a PMT helper's term).
        var edit = EditCellsCommand.ForFormula(sheet.Id, helperCell, "B1*3");
        var outcome = edit.Apply(ctx);
        outcome.Success.Should().BeTrue();

        // The driver cell D1 itself always recalculates via the ordinary dependency graph regardless
        // of this fix (its own formula "=E1" hasn't changed) -- what must change is the Data Table
        // BODY, which must re-derive from E1's NEW formula text.
        sheet.GetCell(2, 4)!.FormulaText.Should().Be("(C2*3)",
            because: "editing an intermediate precedent reached only indirectly must still refresh the data table body");
        sheet.GetCell(3, 4)!.FormulaText.Should().Be("(C3*3)",
            because: "every body cell must re-derive from the edited intermediate precedent's new formula text");

        // The refresh must also be reported so the recalc engine actually re-evaluates the body.
        outcome.AffectedCells.Should().Contain(new CellAddress(sheet.Id, 2, 4));
        outcome.AffectedCells.Should().Contain(new CellAddress(sheet.Id, 3, 4));

        // Undo must restore the body alongside the helper-cell edit in the same transaction.
        edit.Revert(ctx);
        sheet.GetCell(helperCell)!.FormulaText.Should().Be("B1*2");
        sheet.GetCell(2, 4)!.FormulaText.Should().Be("(C2*2)");
        sheet.GetCell(3, 4)!.FormulaText.Should().Be("(C3*2)");
    }

    [Fact]
    public void EditCellsCommand_EditingIntermediatePrecedent_RefreshesOneVariableRowOrientedBody()
    {
        // Row-oriented sibling of the test above, mirroring R115's own row-oriented layout: header
        // column A hosts the master formula in A2, trial values run across row 1 (B1, C1).
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var inputCell = new CellAddress(sheet.Id, 5, 1); // A5
        var formulaCell = new CellAddress(sheet.Id, 2, 1); // A2
        var helperCell = new CellAddress(sheet.Id, 6, 1); // A6 -- outside the table range
        sheet.SetCell(inputCell, new NumberValue(10));
        sheet.SetFormula(helperCell, "A5*2");
        sheet.SetFormula(formulaCell, "A6");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(1)); // B1 trial value
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(2)); // C1 trial value

        var creation = new OneVariableDataTableCommand(
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 3)),
            formulaCell,
            inputCell,
            DataTableInputOrientation.Row);
        creation.Apply(ctx).Success.Should().BeTrue();

        sheet.GetCell(2, 2)!.FormulaText.Should().Be("(B1*2)");
        sheet.GetCell(2, 3)!.FormulaText.Should().Be("(C1*2)");

        var edit = EditCellsCommand.ForFormula(sheet.Id, helperCell, "A5*3");
        var outcome = edit.Apply(ctx);
        outcome.Success.Should().BeTrue();

        sheet.GetCell(2, 2)!.FormulaText.Should().Be("(B1*3)",
            because: "editing an intermediate precedent must refresh a row-oriented body too");
        sheet.GetCell(2, 3)!.FormulaText.Should().Be("(C1*3)");

        outcome.AffectedCells.Should().Contain(new CellAddress(sheet.Id, 2, 2));
        outcome.AffectedCells.Should().Contain(new CellAddress(sheet.Id, 2, 3));
    }

    [Fact]
    public void EditCellsCommand_EditingIntermediatePrecedent_RefreshesTwoVariableBody()
    {
        // Two-variable sibling: single corner formula cell D1 = "=E1", helper E1 = "=B1+C1" reaches
        // both input cells indirectly.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var rowInputCell = new CellAddress(sheet.Id, 1, 2); // B1
        var columnInputCell = new CellAddress(sheet.Id, 1, 3); // C1
        var formulaCell = new CellAddress(sheet.Id, 1, 4); // D1
        var helperCell = new CellAddress(sheet.Id, 1, 6); // F1 -- outside the table range
        sheet.SetCell(rowInputCell, new NumberValue(1));
        sheet.SetCell(columnInputCell, new NumberValue(2));
        sheet.SetFormula(helperCell, "B1+C1");
        sheet.SetFormula(formulaCell, "F1");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 4), new NumberValue(10)); // row trial value
        sheet.SetCell(new CellAddress(sheet.Id, 1, 5), new NumberValue(20)); // column trial value

        var creation = new TwoVariableDataTableCommand(
            new GridRange(formulaCell, new CellAddress(sheet.Id, 2, 5)),
            formulaCell,
            rowInputCell,
            columnInputCell);
        creation.Apply(ctx).Success.Should().BeTrue();

        // Trial substitution replaces the input-cell reference with the trial CELL's address (a live
        // reference), not its literal value -- E1 hosts the row-trial value (20), D2 the
        // column-trial value (10) -- mirroring ComputeAndApplyTwoVariableBody's own substitution
        // order (column input first, then row input).
        var bodyCellBefore = sheet.GetCell(2, 5)!.FormulaText;
        bodyCellBefore.Should().Be("(E1+D2)");

        var edit = EditCellsCommand.ForFormula(sheet.Id, helperCell, "B1*C1");
        var outcome = edit.Apply(ctx);
        outcome.Success.Should().BeTrue();

        var bodyCellAfter = sheet.GetCell(2, 5)!.FormulaText;
        bodyCellAfter.Should().Be("(E1*D2)",
            because: "editing the two-variable table's intermediate precedent must refresh the body too");
        outcome.AffectedCells.Should().Contain(new CellAddress(sheet.Id, 2, 5));
    }

    [Fact]
    public void EditCellsCommand_EditingUnrelatedCell_DoesNotRefreshDataTableBody()
    {
        // No-regression guard: a cell that is NOT the driver formula and NOT reachable from it via
        // any same-sheet formula reference must never trigger a Data Table body refresh -- otherwise
        // the fix would be over-broad and every unrelated edit on the sheet would rewrite (and
        // needlessly churn the undo stack for) the table body.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var inputCell = new CellAddress(sheet.Id, 1, 2); // B1
        var formulaCell = new CellAddress(sheet.Id, 1, 4); // D1
        var unrelatedCell = new CellAddress(sheet.Id, 10, 10); // J10 -- shares no reference chain
        sheet.SetCell(inputCell, new NumberValue(10));
        sheet.SetFormula(formulaCell, "B1*2");
        sheet.SetCell(unrelatedCell, new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(1)); // C2 trial value
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(2)); // C3 trial value

        var creation = new OneVariableDataTableCommand(
            new GridRange(new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 3, 4)), // C1:D3
            formulaCell,
            inputCell);
        creation.Apply(ctx).Success.Should().BeTrue();

        var bodyBefore2 = sheet.GetCell(2, 4)!.FormulaText;
        var bodyBefore3 = sheet.GetCell(3, 4)!.FormulaText;
        bodyBefore2.Should().NotBeNull();
        bodyBefore3.Should().NotBeNull();

        var edit = EditCellsCommand.ForFormula(sheet.Id, unrelatedCell, "99");
        var outcome = edit.Apply(ctx);
        outcome.Success.Should().BeTrue();

        sheet.GetCell(2, 4)!.FormulaText.Should().Be(bodyBefore2);
        sheet.GetCell(3, 4)!.FormulaText.Should().Be(bodyBefore3);
        outcome.AffectedCells.Should().NotContain(new CellAddress(sheet.Id, 2, 4));
        outcome.AffectedCells.Should().NotContain(new CellAddress(sheet.Id, 3, 4));
    }
}
