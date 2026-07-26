using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

// R90-io-table-style-banding-5-3: real Excel's table banding is purely positional and continuously
// re-flows after a row insert/delete or a sort — StructuredTableStyleService's load-time bake
// previously never ran again, so a Cell's baked StyleId (including its stripe fill) simply
// travelled with the cell's data through an insert/sort instead of being recomputed from the
// cell's new row position. Exercised through the real command entry points: InsertRowsCommand and
// SortCommand, both driving StructuredTableStyleService.RebandTable via TestCommandContext.
public sealed class R90_StructuredTableBandingReflowTests
{
    [Fact]
    public void R90_InsertRows_ReflowsBandingAcrossNewAndShiftedRows()
    {
        var workbook = new Workbook("BandingReflowInsert");
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

        // Bake the initial (pre-insert) banding, exactly like the workbook-open pipeline does:
        // row2=even, row3=odd, row4=even, row5=odd.
        StructuredTableStyleService.ApplyLoadedTableStyles(workbook).Should().BeTrue();
        var banding = StructuredTableStyleBandingResolver.Resolve("TableStyleMedium2", workbook.Theme);
        StyleAt(workbook, sheet, 4, 1).FillColor.Should().Be(banding.EvenRowFill, "sanity: pre-insert row4 is even");
        StyleAt(workbook, sheet, 5, 1).FillColor.Should().Be(banding.OddRowFill, "sanity: pre-insert row5 is odd");

        var ctx = new TestCommandContext(workbook);
        // Insert one row above physical row 4 — lands strictly inside the table body.
        new InsertRowsCommand(sheet.Id, beforeRow: 4, count: 1).Apply(ctx).Success.Should().BeTrue();

        var resized = sheet.StructuredTables.Single(t => t.Id == 1);
        resized.Range.End.Row.Should().Be(6, "the table body must have grown by the inserted row");

        // The brand-new row must be striped for its OWN position, not left blank.
        StyleAt(workbook, sheet, 4, 1).FillColor.Should().Be(banding.EvenRowFill,
            "the newly-inserted row is the 3rd data row (offset 2) — even parity");
        // The row that shifted down from old row4 must be recomputed for ITS new position,
        // not keep carrying its old (now-stale) fill down with it.
        StyleAt(workbook, sheet, 5, 1).FillColor.Should().Be(banding.OddRowFill,
            "old row4's content shifted to row5 (offset 3) — odd parity, even though it carried an even fill down");
        StyleAt(workbook, sheet, 6, 1).FillColor.Should().Be(banding.EvenRowFill,
            "old row5's content shifted to row6 (offset 4) — even parity, even though it carried an odd fill down");
    }

    // No-regression sibling: an insert that lands OUTSIDE any table's body must not disturb an
    // already-correct banding pattern at all.
    [Fact]
    public void R90_InsertRows_OutsideTable_LeavesExistingBandingUntouched()
    {
        var workbook = new Workbook("BandingReflowInsertOutside");
        var sheet = workbook.AddSheet("Data");
        SeedTable(sheet, rowCount: 4);

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
        // Insert well below the table — must not touch its banding or its range.
        new InsertRowsCommand(sheet.Id, beforeRow: 20, count: 1).Apply(ctx).Success.Should().BeTrue();

        var unchanged = sheet.StructuredTables.Single(t => t.Id == 1);
        unchanged.Range.End.Row.Should().Be(5);
        StyleAt(workbook, sheet, 2, 1).FillColor.Should().Be(banding.EvenRowFill);
        StyleAt(workbook, sheet, 3, 1).FillColor.Should().Be(banding.OddRowFill);
        StyleAt(workbook, sheet, 4, 1).FillColor.Should().Be(banding.EvenRowFill);
        StyleAt(workbook, sheet, 5, 1).FillColor.Should().Be(banding.OddRowFill);
    }

    [Fact]
    public void R90_Sort_ReflowsBandingToNewPhysicalRowOrder()
    {
        var workbook = new Workbook("BandingReflowSort");
        var sheet = workbook.AddSheet("Data");
        // Header row1; data rows 2-5, column 1 holds a descending sort key (4,3,2,1) so an
        // ascending sort fully reverses row order.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Key"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        for (var r = 0; r < 4; r++)
        {
            var row = (uint)(2 + r);
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(4 - r));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue((4 - r) * 10));
        }

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
        var sortRange = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 5, 2));
        new SortCommand(sheet.Id, sortRange, sortByColOffset: 0, ascending: true).Apply(ctx).Success.Should().BeTrue();

        // Sanity: the sort fully reversed the rows (key 1 now sits at row2).
        (sheet.GetCell(2, 1)!.Value as NumberValue)!.Value.Should().Be(1);
        (sheet.GetCell(5, 1)!.Value as NumberValue)!.Value.Should().Be(4);

        // Real Excel's banding stays purely positional after a sort — the physical row's parity
        // never changes, regardless of which record's data landed there.
        StyleAt(workbook, sheet, 2, 1).FillColor.Should().Be(banding.EvenRowFill);
        StyleAt(workbook, sheet, 3, 1).FillColor.Should().Be(banding.OddRowFill);
        StyleAt(workbook, sheet, 4, 1).FillColor.Should().Be(banding.EvenRowFill);
        StyleAt(workbook, sheet, 5, 1).FillColor.Should().Be(banding.OddRowFill);
    }

    // No-regression sibling: sorting a plain (non-table) range must not throw or attempt to
    // reband anything — there is no owning StructuredTable to look up.
    [Fact]
    public void R90_Sort_PlainRangeWithNoOwningTable_DoesNotThrow()
    {
        var workbook = new Workbook("BandingReflowSortPlain");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(1));

        var ctx = new TestCommandContext(workbook);
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1));
        var outcome = new SortCommand(sheet.Id, range, sortByColOffset: 0, ascending: true).Apply(ctx);

        outcome.Success.Should().BeTrue();
        (sheet.GetCell(1, 1)!.Value as NumberValue)!.Value.Should().Be(1);
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
