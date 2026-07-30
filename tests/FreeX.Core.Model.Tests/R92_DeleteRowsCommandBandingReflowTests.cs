using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

// R92-commands-undo-structural-format-5-1: deleting rows inside a row-banded structured table left
// the surviving rows' stripe fills at their PRE-delete parity -- MoveCellsForDelete relocates each
// cell's existing StyleId along with it but never repaints banding, so every row below the deleted
// band ended up with inverted banding relative to its new position. Mirrors
// R90_StructuredTableBandingReflowTests (InsertRowsCommand/SortCommand) but exercised through the
// real DeleteRowsCommand entry point, including an undo-fidelity assertion (Revert must restore the
// true pre-delete banding, not just the pre-delete cell content).
public sealed class R92_DeleteRowsCommandBandingReflowTests
{
    [Fact]
    public void R92_DeleteRows_ReflowsBandingAcrossShiftedRows()
    {
        var workbook = new Workbook("BandingReflowDelete");
        var sheet = workbook.AddSheet("Data");
        SeedTable(sheet, rowCount: 4); // header row1, data rows 2-5

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2)),
            HeaderRowCount = 1,
            StyleName = "TableStyleMedium2",
            ShowRowStripes = true
        };
        sheet.StructuredTables.Add(table);

        // Bake the initial (pre-delete) banding: row2=even, row3=odd, row4=even, row5=odd.
        StructuredTableStyleService.ApplyLoadedTableStyles(workbook).Should().BeTrue();
        var banding = StructuredTableStyleBandingResolver.Resolve("TableStyleMedium2", workbook.Theme);
        StyleAt(workbook, sheet, 4, 1).FillColor.Should().Be(banding.EvenRowFill, "sanity: pre-delete row4 is even");
        StyleAt(workbook, sheet, 5, 1).FillColor.Should().Be(banding.OddRowFill, "sanity: pre-delete row5 is odd");

        var ctx = new TestCommandContext(workbook);
        var command = new DeleteRowsCommand(sheet.Id, startRow: 3, count: 1);
        command.Apply(ctx).Success.Should().BeTrue();

        var resized = sheet.StructuredTables.Single(t => t.Id == 1);
        resized.Range.End.Row.Should().Be(4, "the table body must have shrunk by the deleted row");

        // Old row4's content ("Row4") shifted up to row3, which is now the 2nd data row (odd
        // parity) -- it must be repainted, not keep the "even" fill it carried down from row4.
        sheet.GetCell(3, 1)!.Value.Should().BeOfType<TextValue>()
            .Which.Value.Should().Be("Row4");
        StyleAt(workbook, sheet, 3, 1).FillColor.Should().Be(banding.OddRowFill,
            "old row4's content shifted to row3 (2nd data row) -- odd parity, even though it carried an even fill down");

        // Old row5's content ("Row5") shifted up to row4, which is now the 3rd data row (even
        // parity) -- it must be repainted, not keep the "odd" fill it carried down from row5.
        sheet.GetCell(4, 1)!.Value.Should().BeOfType<TextValue>()
            .Which.Value.Should().Be("Row5");
        StyleAt(workbook, sheet, 4, 1).FillColor.Should().Be(banding.EvenRowFill,
            "old row5's content shifted to row4 (3rd data row) -- even parity, even though it carried an odd fill down");

        // Undo-fidelity: Revert must restore the TRUE pre-delete state -- both content and banding.
        command.Revert(ctx);

        var restored = sheet.StructuredTables.Single(t => t.Id == 1);
        restored.Range.End.Row.Should().Be(5, "undo must restore the table's original row extent");

        sheet.GetCell(3, 1)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("Row3");
        sheet.GetCell(4, 1)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("Row4");
        sheet.GetCell(5, 1)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("Row5");

        StyleAt(workbook, sheet, 2, 1).FillColor.Should().Be(banding.EvenRowFill, "undo must restore row2's original banding");
        StyleAt(workbook, sheet, 3, 1).FillColor.Should().Be(banding.OddRowFill, "undo must restore row3's original banding");
        StyleAt(workbook, sheet, 4, 1).FillColor.Should().Be(banding.EvenRowFill, "undo must restore row4's original banding");
        StyleAt(workbook, sheet, 5, 1).FillColor.Should().Be(banding.OddRowFill, "undo must restore row5's original banding");
    }

    // No-regression sibling: a delete entirely OUTSIDE any table's body (whole table shifts up as a
    // unit, or the table is untouched) must not disturb an already-correct banding pattern at all.
    [Fact]
    public void R92_DeleteRows_OutsideTable_LeavesExistingBandingUntouched()
    {
        var workbook = new Workbook("BandingReflowDeleteOutside");
        var sheet = workbook.AddSheet("Data");
        SeedTable(sheet, rowCount: 4);
        // A row below the table that is unrelated to it.
        sheet.SetCell(new CellAddress(sheet.Id, 20, 1), new TextValue("Unrelated"));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2)),
            HeaderRowCount = 1,
            StyleName = "TableStyleMedium2",
            ShowRowStripes = true
        };
        sheet.StructuredTables.Add(table);
        StructuredTableStyleService.ApplyLoadedTableStyles(workbook).Should().BeTrue();
        var banding = StructuredTableStyleBandingResolver.Resolve("TableStyleMedium2", workbook.Theme);

        var ctx = new TestCommandContext(workbook);
        // Delete well below the table -- must not touch its banding or its range.
        new DeleteRowsCommand(sheet.Id, startRow: 20, count: 1).Apply(ctx).Success.Should().BeTrue();

        var unchanged = sheet.StructuredTables.Single(t => t.Id == 1);
        unchanged.Range.End.Row.Should().Be(5);
        StyleAt(workbook, sheet, 2, 1).FillColor.Should().Be(banding.EvenRowFill);
        StyleAt(workbook, sheet, 3, 1).FillColor.Should().Be(banding.OddRowFill);
        StyleAt(workbook, sheet, 4, 1).FillColor.Should().Be(banding.EvenRowFill);
        StyleAt(workbook, sheet, 5, 1).FillColor.Should().Be(banding.OddRowFill);
    }

    private static void SeedTable(Sheet sheet, int rowCount)
    {
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        for (var r = 2; r <= rowCount + 1; r++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, (uint)r, 1), new TextValue($"Row{r}"));
            sheet.SetCell(new CellAddress(sheet.Id, (uint)r, 2), new NumberValue(r * 10));
        }
    }

    private static CellStyle StyleAt(Workbook workbook, Sheet sheet, uint row, uint col) =>
        workbook.GetStyle(sheet.GetCell(new CellAddress(sheet.Id, row, col))!.StyleId);
}
