using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

// R94-commands-sort-partial-reband-1: SortCommand.RebandOwningTableIfAny calls
// StructuredTableStyleService.RebandTable on the owning table's CURRENT model after a table-scoped
// sort, and RebandTable (via ApplyTableStyle's forceFill:true) always repaints the table's ENTIRE
// data body -- not just the sorted sub-range -- unconditionally overwriting any explicit FillColor
// on every body cell (MergeStyleOntoCell's keepExistingFill is forced false under forceFill).
// FindOwningStructuredTableIndex only requires the sort range's row span to be CONTAINED WITHIN the
// table (table.Range.Contains(range)), not equal to its full data body, so the quick ribbon Sort
// Ascending/Descending buttons (which pass an arbitrary user row selection straight through) can
// reach a table-scoped sort whose range is a genuine proper subset of the table. SortCommand's own
// _snapshot is scoped only to the sort range, so a row outside that range had no undo coverage at
// all -- Ctrl+Z never restored its explicit fill. Exercised through the real command entry points:
// ApplyStyleCommand (the real "highlight a cell" path) and SortCommand itself.
public sealed class R94_SortCommandTableRebandUndoTests
{
    private static readonly CellColor UserHighlight = new(255, 0, 0);

    [Fact]
    public void SortPartialRowRangeInsideTable_UndoRestoresExplicitFillOnRowOutsideSortRange()
    {
        var workbook = new Workbook("SortRebandUndo");
        var sheet = workbook.AddSheet("Data");
        // Header row1; data rows 2-7 (6 data rows) so there's room for untouched rows both above
        // and below the narrow sort range used below.
        SeedTable(sheet, dataRowCount: 6);

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 7, 2)),
            HeaderRowCount = 1,
            StyleName = "TableStyleMedium2",
            ShowRowStripes = true
        };
        sheet.StructuredTables.Add(table);
        StructuredTableStyleService.ApplyLoadedTableStyles(workbook).Should().BeTrue();

        var ctx = new TestCommandContext(workbook);

        // The real "user manually highlights a specific data cell" path: row2, well OUTSIDE the
        // sort range used below (rows 4-6), so the sort's own snapshot never sees it.
        var highlightedCell = new CellAddress(sheet.Id, 2, 1);
        var highlightRange = new GridRange(highlightedCell, highlightedCell);
        new ApplyStyleCommand(sheet.Id, highlightRange, new StyleDiff(FillColor: UserHighlight))
            .Apply(ctx).Success.Should().BeTrue();
        StyleAt(workbook, sheet, 2, 1).FillColor.Should().Be(UserHighlight, "sanity: the explicit highlight was applied");

        // Quick ribbon Sort Ascending/Descending: an arbitrary user row selection (rows 4-6) whose
        // COLUMN span exactly matches the table's columns and whose row span is strictly CONTAINED
        // WITHIN the table's row extent (1-7) but is NOT the table's whole data body (2-7) --
        // exactly the shape FindOwningStructuredTableIndex still recognizes as table-owned.
        var sortRange = new GridRange(new CellAddress(sheet.Id, 4, 1), new CellAddress(sheet.Id, 6, 2));
        var sortCommand = new SortCommand(sheet.Id, sortRange, sortByColOffset: 1, ascending: false);
        sortCommand.Apply(ctx).Success.Should().BeTrue();

        // Sanity: RebandTable's blast radius really does reach row2, well outside the sort range
        // (mirrors the already-accepted InsertRowsCommand/DeleteRowsCommand banding behavior; it is
        // not itself the defect under test).
        StyleAt(workbook, sheet, 2, 1).FillColor.Should().NotBe(UserHighlight,
            "sanity: reband's forceFill overwrote the user's explicit fill on a row outside the sort range (expected)");

        // The defect: undoing the sort must restore row2's explicit fill, exactly as real Excel's
        // Ctrl+Z would -- a sort of an unrelated sub-range (and its own undo) must not permanently
        // destroy formatting on a row it never touched directly.
        sortCommand.Revert(ctx);
        StyleAt(workbook, sheet, 2, 1).FillColor.Should().Be(UserHighlight,
            "undoing the sort must restore the explicit fill on a table row outside the sorted sub-range");
    }

    // No-regression sibling: the pre-existing sort-range undo coverage (the rows actually
    // permuted by the sort) must still work exactly as before -- this exercises the same
    // restore-ordering guard the fix's wider capture must not break (a row inside the sort range
    // must undo back to its true pre-sort value, not some stale "post-reband" snapshot entry).
    [Fact]
    public void SortPartialRowRangeInsideTable_UndoStillRestoresRowsInsideSortRangeToPreSortOrder()
    {
        var workbook = new Workbook("SortRebandUndoWindow");
        var sheet = workbook.AddSheet("Data");
        SeedTable(sheet, dataRowCount: 6);

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 7, 2)),
            HeaderRowCount = 1
        };
        sheet.StructuredTables.Add(table);

        var ctx = new TestCommandContext(workbook);

        // Pre-sort values in the sort window (rows 4-6, column B): 40, 20, 60 (row4, row5, row6).
        sheet.GetValue(4, 2).Should().Be(new NumberValue(40));
        sheet.GetValue(5, 2).Should().Be(new NumberValue(20));
        sheet.GetValue(6, 2).Should().Be(new NumberValue(60));

        var sortRange = new GridRange(new CellAddress(sheet.Id, 4, 1), new CellAddress(sheet.Id, 6, 2));
        var sortCommand = new SortCommand(sheet.Id, sortRange, sortByColOffset: 1, ascending: true);
        sortCommand.Apply(ctx).Success.Should().BeTrue();

        // Sanity: ascending sort reordered the window.
        sheet.GetValue(4, 2).Should().Be(new NumberValue(20));
        sheet.GetValue(5, 2).Should().Be(new NumberValue(40));
        sheet.GetValue(6, 2).Should().Be(new NumberValue(60));

        sortCommand.Revert(ctx);

        // Undo must restore the exact pre-sort order inside the sorted window.
        sheet.GetValue(4, 2).Should().Be(new NumberValue(40));
        sheet.GetValue(5, 2).Should().Be(new NumberValue(20));
        sheet.GetValue(6, 2).Should().Be(new NumberValue(60));
    }

    private static void SeedTable(Sheet sheet, int dataRowCount)
    {
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        double[] amounts = [10, 30, 40, 20, 60, 50];
        for (var i = 0; i < dataRowCount; i++)
        {
            var row = (uint)(2 + i);
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"Row{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(amounts[i]));
        }
    }

    private static CellStyle StyleAt(Workbook workbook, Sheet sheet, uint row, uint col) =>
        workbook.GetStyle(sheet.GetCell(new CellAddress(sheet.Id, row, col))!.StyleId);
}
