using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class CellStateSnapshotTests
{
    [Fact]
    public void CellStateSnapshot_CaptureAndToCell_MatchesCanonicalCloneForEveryField()
    {
        var sheetId = SheetId.New();
        var address = new CellAddress(sheetId, 7, 9);
        var cachedAst = new object();
        var original = new Cell
        {
            Value = new TextValue("00123"),
            IgnoreFormulaError = true,
            StyleId = new StyleId(17),
            QuotePrefix = true
        };
        original.FormulaText = "A1+B2";
        original.CachedAst = cachedAst;
        original.ArrayMode = FormulaArrayMode.Implicit;
        original.LegacyArrayRows = 3;
        original.LegacyArrayCols = 4;

        var canonicalClone = original.Clone();
        var snapshot = CellStateSnapshot.Capture(address, original);
        var restored = snapshot.ToCell();

        snapshot.Row.Should().Be(address.Row);
        snapshot.Col.Should().Be(address.Col);
        snapshot.FormulaText.Should().Be(original.FormulaText);
        snapshot.ToAddress(sheetId).Should().Be(address);
        restored.Should().NotBeSameAs(original);
        restored.Should().BeEquivalentTo(canonicalClone);
        restored.CachedAst.Should().BeSameAs(cachedAst);
        restored.QuotePrefix.Should().BeTrue();
    }

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

    [Theory]
    [InlineData(StructuralMutation.InsertRows)]
    [InlineData(StructuralMutation.DeleteRows)]
    [InlineData(StructuralMutation.InsertColumns)]
    [InlineData(StructuralMutation.DeleteColumns)]
    public void StructuralCommand_Undo_PreservesQuotePrefixOnShiftedCell(StructuralMutation mutation)
    {
        var workbook = new Workbook("QuotePrefixStructuralUndo");
        var sheet = workbook.AddSheet("Sheet1");
        var originalAddress = new CellAddress(sheet.Id, 5, 5);
        sheet.SetCell(originalAddress, new Cell
        {
            Value = new TextValue("00123"),
            QuotePrefix = true
        });

        var command = CreateCommand(mutation, sheet.Id);
        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        var shiftedAddress = mutation switch
        {
            StructuralMutation.InsertRows => new CellAddress(sheet.Id, 6, 5),
            StructuralMutation.DeleteRows => new CellAddress(sheet.Id, 4, 5),
            StructuralMutation.InsertColumns => new CellAddress(sheet.Id, 5, 6),
            StructuralMutation.DeleteColumns => new CellAddress(sheet.Id, 5, 4),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null)
        };
        sheet.GetCell(shiftedAddress)!.QuotePrefix.Should().BeTrue();

        command.Revert(new TestCommandContext(workbook));

        sheet.GetCell(originalAddress)!.QuotePrefix.Should().BeTrue(
            "undo reconstructs shifted cells through CellStateSnapshot");
    }

    [Theory]
    [InlineData(StructuralMutation.DeleteRows)]
    [InlineData(StructuralMutation.DeleteColumns)]
    public void DeleteCommand_Undo_RestoresQuotePrefixOnDeletedCell(StructuralMutation mutation)
    {
        var workbook = new Workbook("QuotePrefixDeletedCellUndo");
        var sheet = workbook.AddSheet("Sheet1");
        var deletedAddress = mutation == StructuralMutation.DeleteRows
            ? new CellAddress(sheet.Id, 2, 5)
            : new CellAddress(sheet.Id, 5, 2);
        sheet.SetCell(deletedAddress, new Cell
        {
            Value = new TextValue("00456"),
            QuotePrefix = true
        });

        var command = CreateCommand(mutation, sheet.Id);
        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        command.Revert(new TestCommandContext(workbook));

        sheet.GetCell(deletedAddress)!.QuotePrefix.Should().BeTrue(
            "undo reconstructs cells removed with the deleted row or column through CellStateSnapshot");
    }

    private static IWorkbookCommand CreateCommand(StructuralMutation mutation, SheetId sheetId) =>
        mutation switch
        {
            StructuralMutation.InsertRows => new InsertRowsCommand(sheetId, beforeRow: 2, count: 1),
            StructuralMutation.DeleteRows => new DeleteRowsCommand(sheetId, startRow: 2, count: 1),
            StructuralMutation.InsertColumns => new InsertColumnsCommand(sheetId, beforeCol: 2, count: 1),
            StructuralMutation.DeleteColumns => new DeleteColumnsCommand(sheetId, startCol: 2, count: 1),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null)
        };

    public enum StructuralMutation
    {
        InsertRows,
        DeleteRows,
        InsertColumns,
        DeleteColumns
    }
}
