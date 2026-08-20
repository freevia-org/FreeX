using FluentAssertions;
using FreeX.App.Presentation.Editing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Editing;

/// <summary>
/// R154/meta-F1: InsertCopiedCellsPlanner's CutMoveFollowUpCommand backs "Insert Cut Cells"
/// (right-click &gt; Insert Cut Cells / Shift-drag). Its own-formula fixup (Apply, overwrites the
/// blanket paste's rewrite of the MOVED cell's own formula with a move-semantics rewrite) and its
/// external-reference repoint (Apply, follows any OTHER cell's formula that referenced a cut cell to
/// the destination) both cloned/re-fetched the target cell (which correctly preserves ArrayMode/
/// LegacyArrayRows/LegacyArrayCols, see Cell.Clone) and then immediately reassigned
/// <c>cell.FormulaText</c> to the corrected reference text -- silently undoing that preservation,
/// because the FormulaText setter (Cell.cs) unconditionally resets those three properties on every
/// assignment. The identical shape exists in Revert for both passes too (restoring the pre-move
/// text). Fixed by routing all four reassignments through a local
/// SetFormulaTextPreservingArrayIdentity helper mirroring
/// RowColumnShiftHelpers.SetFormulaTextPreservingArrayIdentity in FreeX.Core.Commands (which this
/// project cannot call directly since that helper is internal to that assembly).
/// </summary>
public sealed class R154_M1_InsertCopiedCellsLegacyArrayIdentityTests
{
    [Fact]
    public void InsertCutCells_MovedCellIsLegacyArrayAnchor_PreservesArrayIdentity_ApplyAndRevert()
    {
        // A10:A14 = 1..5; B1 (2x1 legacy CSE array, {=SUM(A10:A14)}) is cut and Insert-Cut-Cells'd
        // to D1 via ShiftRight -- own-formula fixup path (own reference to A10:A14 never moved, so
        // the blanket paste offset and the true move-semantics rewrite agree here; what matters is
        // that Clone-then-reassign in the fixup does not strip the array identity).
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        for (uint r = 10; r <= 14; r++)
            sheet.SetCell(new CellAddress(sheet.Id, r, 1), new NumberValue(r - 9));

        var b1 = new CellAddress(sheet.Id, 1, 2);
        var legacyCell = Cell.FromFormula("SUM(A10:A14)");
        legacyCell.LegacyArrayRows = 2;
        legacyCell.LegacyArrayCols = 1;
        sheet.SetCell(b1, legacyCell);

        var source = new GridRange(b1, b1);
        var cells = new[] { (b1, sheet.GetCell(b1)!.Clone()) };

        var d1 = new CellAddress(sheet.Id, 1, 4);
        var destination = new GridRange(d1, d1);

        var command = InsertCopiedCellsPlanner.CreateCommand(
            workbook, sheet.Id, source, cells, destination,
            KeyboardInsertDeleteDialogChoice.ShiftRight, isCut: true);

        command.Apply(ctx).Success.Should().BeTrue();

        var moved = sheet.GetCell(d1)!;
        moved.FormulaText.Should().Be("SUM(A10:A14)");
        moved.LegacyArrayRows.Should().Be(2u,
            "Insert Cut Cells' own-formula fixup must not strip a legacy CSE array formula's " +
            "fixed-extent identity");
        moved.LegacyArrayCols.Should().Be(1u);

        command.Revert(ctx);

        var reverted = sheet.GetCell(b1)!;
        reverted.FormulaText.Should().Be("SUM(A10:A14)");
        reverted.LegacyArrayRows.Should().Be(2u,
            "Revert's own-formula-fixup restore must not strip the array identity either");
        reverted.LegacyArrayCols.Should().Be(1u);
    }

    // Isolates the own-formula fixup's OWN Revert (the "_destinationFormulaSnapshot" restore, which
    // writes back to the DESTINATION address D1) from the rest of the composite's Revert -- the
    // outer command.Revert() above also runs pasteCommand.Revert() and ClearContentsCommand.Revert(),
    // both of which independently touch D1/B1 through their own (already-correct) snapshot machinery
    // and so cannot tell this specific fixup-restore's own bug apart from the rest. Calling just the
    // last child command (CutMoveFollowUpCommand, always added last -- see CreateCommand) in isolation
    // pins the destination-address state exactly as CutMoveFollowUpCommand.Revert left it.
    [Fact]
    public void InsertCutCells_OwnFormulaFixupRevertInIsolation_PreservesArrayIdentityAtDestination()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        for (uint r = 10; r <= 14; r++)
            sheet.SetCell(new CellAddress(sheet.Id, r, 1), new NumberValue(r - 9));

        var b1 = new CellAddress(sheet.Id, 1, 2);
        var legacyCell = Cell.FromFormula("SUM(A10:A14)");
        legacyCell.LegacyArrayRows = 2;
        legacyCell.LegacyArrayCols = 1;
        sheet.SetCell(b1, legacyCell);

        var source = new GridRange(b1, b1);
        var cells = new[] { (b1, sheet.GetCell(b1)!.Clone()) };

        var d1 = new CellAddress(sheet.Id, 1, 4);
        var destination = new GridRange(d1, d1);

        var command = (CompositeWorkbookCommand)InsertCopiedCellsPlanner.CreateCommand(
            workbook, sheet.Id, source, cells, destination,
            KeyboardInsertDeleteDialogChoice.ShiftRight, isCut: true);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.GetCell(d1)!.FormulaText.Should().Be("SUM(A10:A14)", "sanity: the fixup ran");

        // The follow-up command is always the LAST child of the composite (see CreateCommand).
        var followUp = command.Commands[^1];
        followUp.Revert(ctx);

        var afterFixupRevert = sheet.GetCell(d1)!;
        afterFixupRevert.FormulaText.Should().Be("SUM(C10:C14)",
            "sanity: the fixup-revert restored the PRE-fixup (blanket-paste) text at the destination");
        afterFixupRevert.LegacyArrayRows.Should().Be(2u,
            "the own-formula fixup's destination-address Revert must not strip the legacy array's " +
            "fixed-extent identity");
        afterFixupRevert.LegacyArrayCols.Should().Be(1u);
    }

    [Fact]
    public void InsertCutCells_OtherLegacyArrayFormulaElsewhereReferencesCutCell_RepointsAndPreservesIdentity()
    {
        // A1=5 (the cell being cut, moved to D1). H2 (2x1 legacy CSE array, {=SUM(A1:A1)}) lives on a
        // DIFFERENT row (outside the ShiftRight insert's row-1 band, so it never itself relocates) and
        // references the cut cell -- external-reference repoint path.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, Cell.FromValue(new NumberValue(5)));

        var h1 = new CellAddress(sheet.Id, 2, 8);
        var legacyCell = Cell.FromFormula("SUM(A1:A1)");
        legacyCell.LegacyArrayRows = 2;
        legacyCell.LegacyArrayCols = 1;
        sheet.SetCell(h1, legacyCell);

        var source = new GridRange(a1, a1);
        var cells = new[] { (a1, sheet.GetCell(a1)!.Clone()) };

        var d1 = new CellAddress(sheet.Id, 1, 4);
        var destination = new GridRange(d1, d1);

        var command = InsertCopiedCellsPlanner.CreateCommand(
            workbook, sheet.Id, source, cells, destination,
            KeyboardInsertDeleteDialogChoice.ShiftRight, isCut: true);

        command.Apply(ctx).Success.Should().BeTrue();

        var repointed = sheet.GetCell(h1)!;
        repointed.FormulaText.Should().Be("SUM(D1:D1)",
            "the external reference must follow the cut cell to its destination");
        repointed.LegacyArrayRows.Should().Be(2u,
            "the external-reference repoint must not strip the OTHER (unrelated) legacy array " +
            "formula's fixed-extent identity");
        repointed.LegacyArrayCols.Should().Be(1u);

        command.Revert(ctx);

        var revertedExternal = sheet.GetCell(h1)!;
        revertedExternal.FormulaText.Should().Be("SUM(A1:A1)");
        revertedExternal.LegacyArrayRows.Should().Be(2u,
            "Revert's external-reference restore must not strip the array identity either");
        revertedExternal.LegacyArrayCols.Should().Be(1u);
    }

    [Fact]
    public void InsertCutCells_OrdinaryFormulas_StillRewriteAndStayDynamic_NoRegression()
    {
        // Mirrors the two scenarios above but with plain (non-array) formulas, confirming the
        // preservation helper doesn't change ordinary rewrite/revert behavior.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(a1, Cell.FromValue(new NumberValue(5)));
        var b1Formula = Cell.FromFormula("A1+1");
        b1Formula.Value = new NumberValue(6);
        sheet.SetCell(b1, b1Formula);
        var c1Formula = Cell.FromFormula("B1*10");
        c1Formula.Value = new NumberValue(60);
        sheet.SetCell(c1, c1Formula);

        var source = new GridRange(b1, b1);
        var cells = new[] { (b1, sheet.GetCell(b1)!.Clone()) };

        var d1 = new CellAddress(sheet.Id, 1, 4);
        var destination = new GridRange(d1, d1);

        var command = InsertCopiedCellsPlanner.CreateCommand(
            workbook, sheet.Id, source, cells, destination,
            KeyboardInsertDeleteDialogChoice.ShiftRight, isCut: true);

        command.Apply(ctx).Success.Should().BeTrue();

        var moved = sheet.GetCell(d1)!;
        moved.FormulaText.Should().Be("A1+1");
        moved.ArrayMode.Should().Be(FormulaArrayMode.Dynamic);
        moved.LegacyArrayRows.Should().Be(0u);

        var repointed = sheet.GetCell(c1)!;
        repointed.FormulaText.Should().Be("D1*10");
        repointed.ArrayMode.Should().Be(FormulaArrayMode.Dynamic);
        repointed.LegacyArrayRows.Should().Be(0u);

        command.Revert(ctx);

        sheet.GetCell(b1)!.FormulaText.Should().Be("A1+1");
        sheet.GetCell(c1)!.FormulaText.Should().Be("B1*10");
    }

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }
}
