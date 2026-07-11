using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// Regression coverage for R27-meta-2: round-26 gave <see cref="DeleteSheetOp"/> an optional
/// DeletedTableNames list so a cross-sheet Table[...] reference to a table that lived on the
/// deleted sheet becomes "#REF!" (FormulaRewriter.cs), but <see cref="RemoveSheetCommand"/> --
/// the only production caller that actually deletes a sheet -- constructed every DeleteSheetOp as
/// <c>new DeleteSheetOp(deletedSheetName)</c>, leaving DeletedTableNames null. Per
/// FormulaRewriter's own guard (<c>if (op.DeletedTableNames is null) return false;</c>), the whole
/// new code path never fired in the real app: deleting a sheet that owned a structured table left
/// cross-sheet Table[...] formulas as stale text that falls through to #NAME? at recalc instead of
/// the correct #REF!. Fixed by threading <c>sheet.StructuredTables.Select(t => t.Name)</c> through
/// every DeleteSheetOp construction in RemoveSheetCommand (including the two named-formula rewrite
/// helpers, which needed a new parameter).
/// </summary>
public sealed class R27_RemoveSheetStructuredTableFormulaRefTests
{
    [Fact]
    public void RemoveSheetCommand_RewritesCrossSheetStructuredReferenceToDeletedTable_ToRef_AndUndoRestores()
    {
        // Bug case: "Data" hosts TABLE1; "Report" has a formula referencing TABLE1[Amount] across
        // sheets. Deleting "Data" must turn that formula into a #REF! error, not leave it as
        // dangling text that resolves to #NAME? at the next recalc.
        var workbook = new Workbook("RemoveSheetStructuredTableTest");
        var data = workbook.AddSheet("Data");
        var report = workbook.AddSheet("Report");

        data.SetCell(new CellAddress(data.Id, 1, 1), new TextValue("Amount"));
        data.SetCell(new CellAddress(data.Id, 2, 1), new NumberValue(10));
        data.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "TABLE1",
            DisplayName = "TABLE1",
            Range = new GridRange(new CellAddress(data.Id, 1, 1), new CellAddress(data.Id, 2, 1)),
        });

        var formulaCell = new CellAddress(report.Id, 1, 1);
        report.SetFormula(formulaCell, "SUM(TABLE1[Amount])");

        var ctx = new TestCommandContext(workbook);
        var command = new RemoveSheetCommand(data.Id);

        command.Apply(ctx).Success.Should().BeTrue();

        report.GetCell(formulaCell)!.FormulaText.Should().Be("SUM(#REF!)",
            because: "TABLE1 no longer exists anywhere in the workbook once its host sheet is " +
                     "deleted, so a cross-sheet structured reference to it must go stale like any " +
                     "other reference to a deleted sheet, not silently resolve to #NAME? later");

        command.Revert(ctx);

        report.GetCell(formulaCell)!.FormulaText.Should().Be("SUM(TABLE1[Amount])");
    }

    [Fact]
    public void RemoveSheetCommand_LeavesStructuredReferenceToSurvivingTableUntouched()
    {
        // Sibling already-working case (no over-correction): deleting a sheet that owns no
        // structured tables must not disturb a structured reference to a table that lives
        // elsewhere in the workbook.
        var workbook = new Workbook("RemoveSheetStructuredTableUnrelatedTest");
        var data = workbook.AddSheet("Data");
        var report = workbook.AddSheet("Report");
        var scratch = workbook.AddSheet("Scratch");

        data.SetCell(new CellAddress(data.Id, 1, 1), new TextValue("Amount"));
        data.SetCell(new CellAddress(data.Id, 2, 1), new NumberValue(10));
        data.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "TABLE1",
            DisplayName = "TABLE1",
            Range = new GridRange(new CellAddress(data.Id, 1, 1), new CellAddress(data.Id, 2, 1)),
        });

        var formulaCell = new CellAddress(report.Id, 1, 1);
        report.SetFormula(formulaCell, "SUM(TABLE1[Amount])");

        var ctx = new TestCommandContext(workbook);
        var command = new RemoveSheetCommand(scratch.Id);

        command.Apply(ctx).Success.Should().BeTrue();

        report.GetCell(formulaCell)!.FormulaText.Should().Be("SUM(TABLE1[Amount])",
            because: "deleting an unrelated, table-less sheet must not touch a structured " +
                     "reference to a table that still exists in the workbook");
    }
}
