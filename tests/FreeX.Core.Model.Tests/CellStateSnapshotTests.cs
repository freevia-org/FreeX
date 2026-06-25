using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class CellStateSnapshotTests
{
    [Fact]
    public void CellStateSnapshot_CaptureAndToCell_PreservesImplicitArrayMode()
    {
        var workbook = new Workbook("CellSnapshotTest");
        var sheet = workbook.AddSheet("Sheet1");
        var addr = new CellAddress(sheet.Id, 1, 1);

        // Build a cell with ArrayMode=Implicit (as the loader would set it).
        var original = new Cell
        {
            Value = new NumberValue(42),
            IgnoreFormulaError = false,
            StyleId = StyleId.Default
        };
        original.FormulaText = "A2+A3"; // setter resets to Dynamic
        original.ArrayMode = FormulaArrayMode.Implicit; // override to Implicit

        original.ArrayMode.Should().Be(FormulaArrayMode.Implicit, "precondition: cell starts Implicit");

        var snapshot = CellStateSnapshot.Capture(addr, original);
        var restored = snapshot.ToCell();

        restored.FormulaText.Should().Be("A2+A3");
        restored.ArrayMode.Should().Be(FormulaArrayMode.Implicit,
            "ToCell must restore Implicit after the FormulaText setter resets it to Dynamic");
    }

    [Fact]
    public void CellStateSnapshot_CaptureAndToCell_DefaultDynamicModeIsPreserved()
    {
        var workbook = new Workbook("CellSnapshotDynamicTest");
        var sheet = workbook.AddSheet("Sheet1");
        var addr = new CellAddress(sheet.Id, 2, 1);

        var original = new Cell();
        original.FormulaText = "SUM(A1:A5)";
        // ArrayMode stays Dynamic (the default after FormulaText setter)

        var snapshot = CellStateSnapshot.Capture(addr, original);
        var restored = snapshot.ToCell();

        restored.ArrayMode.Should().Be(FormulaArrayMode.Dynamic);
    }

    [Fact]
    public void InsertRowCommand_Undo_PreservesImplicitArrayModeOnCellBelow()
    {
        // Regression: undo of row insert restores cells via CellStateSnapshot.
        // A cell with ArrayMode=Implicit must survive the round-trip.
        var workbook = new Workbook("InsertRowUndoArrayModeTest");
        var sheet = workbook.AddSheet("Sheet1");
        // Place the cell at row 5 so it is shifted by inserting a row at row 2.
        var addr = new CellAddress(sheet.Id, 5, 1);

        var cell = new Cell { Value = new NumberValue(1) };
        cell.FormulaText = "C1+C2"; // references rows 1 and 2 — won't shift when we insert at row 2... actually
        // Use a literal value cell with formula pointing to same-row to keep things simple.
        // Actually simplest: just a value cell (no formula rewrite concern), with ArrayMode=Implicit.
        var cellImplicit = new Cell { Value = new NumberValue(99) };
        cellImplicit.FormulaText = "1+1";
        cellImplicit.ArrayMode = FormulaArrayMode.Implicit;
        sheet.SetCell(addr, cellImplicit);

        var ctx = new TestCommandContext(workbook);
        // Insert 1 row before row 2; cell at row 5 moves to row 6.
        var command = new InsertRowsCommand(sheet.Id, beforeRow: 2, count: 1);
        command.Apply(ctx).Success.Should().BeTrue();

        command.Revert(ctx);

        // The cell must be back at row 5 with Implicit mode preserved.
        var restoredCell = sheet.GetCell(addr);
        restoredCell.Should().NotBeNull();
        restoredCell!.ArrayMode.Should().Be(FormulaArrayMode.Implicit,
            "undo of row insert must preserve ArrayMode=Implicit via CellStateSnapshot");
    }
}
