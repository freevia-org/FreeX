using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

// R92-commands-undo-structural-format-5-2: inserting or deleting a column inside a column-banded
// structured table never reflowed the column-stripe fills -- MoveCellsForInsert/MoveCellsForDelete
// relocate each cell's existing StyleId along with it, but ShiftStructuredTables only reconciles the
// table's Range/Columns list, never repaints per-cell fills. Mirrors
// R92_DeleteRowsCommandBandingReflowTests (row axis) but exercises the COLUMN axis through the real
// InsertColumnsCommand/DeleteColumnsCommand entry points, including undo-fidelity assertions.
public sealed class R92_ColumnBandingReflowTests
{
    [Fact]
    public void R92_InsertColumns_ReflowsColumnBandingAndUndoesCleanly()
    {
        var workbook = new Workbook("ColumnBandingReflowInsert");
        var sheet = workbook.AddSheet("Data");
        SeedTable(sheet); // header row1 A-D; data row2: A0,B0,C0,D0

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 4)),
            HeaderRowCount = 1,
            StyleName = "TableStyleMedium2",
            ShowRowStripes = false,
            ShowColumnStripes = true
        };
        sheet.StructuredTables.Add(table);

        // Bake the initial (pre-insert) column banding: colA=even, colB=odd, colC=even, colD=odd.
        StructuredTableStyleService.ApplyLoadedTableStyles(workbook).Should().BeTrue();
        var banding = StructuredTableStyleBandingResolver.Resolve("TableStyleMedium2", workbook.Theme);
        StyleAt(workbook, sheet, 2, 3).FillColor.Should().Be(banding.EvenRowFill, "sanity: pre-insert colC is even");
        StyleAt(workbook, sheet, 2, 4).FillColor.Should().Be(banding.OddRowFill, "sanity: pre-insert colD is odd");

        var ctx = new TestCommandContext(workbook);
        var command = new InsertColumnsCommand(sheet.Id, beforeCol: 2, count: 1);
        command.Apply(ctx).Success.Should().BeTrue();

        var resized = sheet.StructuredTables.Single(t => t.Id == 1);
        resized.Range.End.Col.Should().Be(5, "the table body must have grown by the inserted column");

        // The brand-new column (col2) is now the table's 2nd column (offset 1, odd parity) --
        // it must be materialized with its OWN correct stripe, not left unstriped.
        var newColCell = sheet.GetCell(2, 2);
        newColCell.Should().NotBeNull("the inserted column's stripe fill must be painted even onto a previously-nonexistent cell");
        workbook.GetStyle(newColCell!.StyleId).FillColor.Should().Be(banding.OddRowFill);

        // Old colB's content ("B0") shifted right to col3, which is now the table's 3rd column
        // (offset 2, even parity) -- it must be recomputed, not keep the "odd" fill it carried
        // over from its old position.
        sheet.GetCell(2, 3)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("B0");
        StyleAt(workbook, sheet, 2, 3).FillColor.Should().Be(banding.EvenRowFill,
            "old colB's content shifted to col3 (3rd column) -- even parity, even though it carried an odd fill over");

        // Old colC's content ("C0") shifted right to col4 (4th column, odd parity).
        sheet.GetCell(2, 4)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("C0");
        StyleAt(workbook, sheet, 2, 4).FillColor.Should().Be(banding.OddRowFill,
            "old colC's content shifted to col4 (4th column) -- odd parity, even though it carried an even fill over");

        // Old colD's content ("D0") shifted right to col5 (5th column, even parity).
        sheet.GetCell(2, 5)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("D0");
        StyleAt(workbook, sheet, 2, 5).FillColor.Should().Be(banding.EvenRowFill,
            "old colD's content shifted to col5 (5th column) -- even parity, even though it carried an odd fill over");

        // Undo-fidelity: Revert must restore the TRUE pre-insert state -- both content and banding
        // -- and the phantom cell materialized purely to hold the inserted column's stripe must
        // vanish completely, not survive as a permanent (if invisible) leftover.
        command.Revert(ctx);

        var restored = sheet.StructuredTables.Single(t => t.Id == 1);
        restored.Range.End.Col.Should().Be(4, "undo must restore the table's original column extent");

        sheet.GetCell(2, 2).Should().NotBeNull();
        sheet.GetCell(2, 2)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("B0");
        sheet.GetCell(2, 3)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("C0");
        sheet.GetCell(2, 4)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("D0");
        sheet.GetCell(2, 5).Should().BeNull("the phantom stripe-only cell from the inserted column must not survive undo");

        StyleAt(workbook, sheet, 2, 1).FillColor.Should().Be(banding.EvenRowFill, "undo must restore colA's original banding");
        StyleAt(workbook, sheet, 2, 2).FillColor.Should().Be(banding.OddRowFill, "undo must restore colB's original banding");
        StyleAt(workbook, sheet, 2, 3).FillColor.Should().Be(banding.EvenRowFill, "undo must restore colC's original banding");
        StyleAt(workbook, sheet, 2, 4).FillColor.Should().Be(banding.OddRowFill, "undo must restore colD's original banding");
    }

    [Fact]
    public void R92_DeleteColumns_ReflowsColumnBandingAndUndoesCleanly()
    {
        var workbook = new Workbook("ColumnBandingReflowDelete");
        var sheet = workbook.AddSheet("Data");
        SeedTable(sheet); // header row1 A-D; data row2: A0,B0,C0,D0

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 4)),
            HeaderRowCount = 1,
            StyleName = "TableStyleMedium2",
            ShowRowStripes = false,
            ShowColumnStripes = true
        };
        sheet.StructuredTables.Add(table);

        StructuredTableStyleService.ApplyLoadedTableStyles(workbook).Should().BeTrue();
        var banding = StructuredTableStyleBandingResolver.Resolve("TableStyleMedium2", workbook.Theme);
        StyleAt(workbook, sheet, 2, 3).FillColor.Should().Be(banding.EvenRowFill, "sanity: pre-delete colC is even");
        StyleAt(workbook, sheet, 2, 4).FillColor.Should().Be(banding.OddRowFill, "sanity: pre-delete colD is odd");

        var ctx = new TestCommandContext(workbook);
        var command = new DeleteColumnsCommand(sheet.Id, startCol: 2, count: 1);
        command.Apply(ctx).Success.Should().BeTrue();

        var resized = sheet.StructuredTables.Single(t => t.Id == 1);
        resized.Range.End.Col.Should().Be(3, "the table body must have shrunk by the deleted column");

        // Old colC's content ("C0") shifted left to col2, which is now the table's 2nd column
        // (offset 1, odd parity) -- it must be recomputed, not keep the "even" fill it carried over.
        sheet.GetCell(2, 2)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("C0");
        StyleAt(workbook, sheet, 2, 2).FillColor.Should().Be(banding.OddRowFill,
            "old colC's content shifted to col2 (2nd column) -- odd parity, even though it carried an even fill over");

        // Old colD's content ("D0") shifted left to col3, which is now the table's 3rd column
        // (offset 2, even parity) -- it must be recomputed, not keep the "odd" fill it carried over.
        sheet.GetCell(2, 3)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("D0");
        StyleAt(workbook, sheet, 2, 3).FillColor.Should().Be(banding.EvenRowFill,
            "old colD's content shifted to col3 (3rd column) -- even parity, even though it carried an odd fill over");

        // Undo-fidelity: Revert must restore the TRUE pre-delete state -- both content and banding.
        command.Revert(ctx);

        var restored = sheet.StructuredTables.Single(t => t.Id == 1);
        restored.Range.End.Col.Should().Be(4, "undo must restore the table's original column extent");

        sheet.GetCell(2, 1)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("A0");
        sheet.GetCell(2, 2)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("B0");
        sheet.GetCell(2, 3)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("C0");
        sheet.GetCell(2, 4)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("D0");

        StyleAt(workbook, sheet, 2, 1).FillColor.Should().Be(banding.EvenRowFill, "undo must restore colA's original banding");
        StyleAt(workbook, sheet, 2, 2).FillColor.Should().Be(banding.OddRowFill, "undo must restore colB's original banding");
        StyleAt(workbook, sheet, 2, 3).FillColor.Should().Be(banding.EvenRowFill, "undo must restore colC's original banding");
        StyleAt(workbook, sheet, 2, 4).FillColor.Should().Be(banding.OddRowFill, "undo must restore colD's original banding");
    }

    // No-regression sibling: an insert/delete entirely OUTSIDE any table's body must not disturb an
    // already-correct column-banding pattern at all.
    [Fact]
    public void R92_InsertAndDeleteColumns_OutsideTable_LeavesExistingBandingUntouched()
    {
        var workbook = new Workbook("ColumnBandingReflowOutside");
        var sheet = workbook.AddSheet("Data");
        SeedTable(sheet);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 20), new TextValue("Unrelated"));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 4)),
            HeaderRowCount = 1,
            StyleName = "TableStyleMedium2",
            ShowRowStripes = false,
            ShowColumnStripes = true
        };
        sheet.StructuredTables.Add(table);
        StructuredTableStyleService.ApplyLoadedTableStyles(workbook).Should().BeTrue();
        var banding = StructuredTableStyleBandingResolver.Resolve("TableStyleMedium2", workbook.Theme);

        var ctx = new TestCommandContext(workbook);
        new InsertColumnsCommand(sheet.Id, beforeCol: 20, count: 1).Apply(ctx).Success.Should().BeTrue();
        new DeleteColumnsCommand(sheet.Id, startCol: 21, count: 1).Apply(ctx).Success.Should().BeTrue();

        var unchanged = sheet.StructuredTables.Single(t => t.Id == 1);
        unchanged.Range.End.Col.Should().Be(4);
        StyleAt(workbook, sheet, 2, 1).FillColor.Should().Be(banding.EvenRowFill);
        StyleAt(workbook, sheet, 2, 2).FillColor.Should().Be(banding.OddRowFill);
        StyleAt(workbook, sheet, 2, 3).FillColor.Should().Be(banding.EvenRowFill);
        StyleAt(workbook, sheet, 2, 4).FillColor.Should().Be(banding.OddRowFill);
    }

    private static void SeedTable(Sheet sheet)
    {
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("ColA"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("ColB"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("ColC"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new TextValue("ColD"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A0"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("B0"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("C0"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 4), new TextValue("D0"));
    }

    private static CellStyle StyleAt(Workbook workbook, Sheet sheet, uint row, uint col) =>
        workbook.GetStyle(sheet.GetCell(new CellAddress(sheet.Id, row, col))!.StyleId);
}
