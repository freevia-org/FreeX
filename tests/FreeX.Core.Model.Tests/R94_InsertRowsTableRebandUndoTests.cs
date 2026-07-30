using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

// R94-commands-undo-structural-format-reband-1: InsertRowsCommand.FillGrownCalculatedColumnsForInsertedRows
// calls StructuredTableStyleService.RebandTable after a row insert inside a structured table, and
// RebandTable (via ApplyTableStyle's forceFill:true) always repaints the table's ENTIRE data body --
// not just the newly-inserted row window -- unconditionally overwriting any explicit FillColor on
// every body cell (MergeStyleOntoCell's keepExistingFill is forced false under forceFill). The
// pre-reband snapshot captured before that call was scoped ONLY to the narrow inserted-row window,
// so an explicit fill on any OTHER row of the table (e.g. a user-highlighted cell above the insertion
// point) had no undo coverage at all -- Ctrl+Z never restored it. Exercised through the real command
// entry points: ApplyStyleCommand (the real "highlight a cell" path) and InsertRowsCommand.
public sealed class R94_InsertRowsTableRebandUndoTests
{
    private static readonly CellColor UserHighlight = new(255, 0, 0);

    [Fact]
    public void InsertRowInsideTable_UndoRestoresExplicitFillOnRowAboveInsertionPoint()
    {
        var workbook = new Workbook("RebandUndoAbove");
        var sheet = workbook.AddSheet("Data");
        // Header row1; data rows 2-6 (5 data rows) so the table body spans well past the insertion
        // point used below, giving room for an untouched row above it.
        SeedTable(sheet, rowCount: 5);

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 2)),
            HeaderRowCount = 1,
            StyleName = "TableStyleMedium2",
            ShowRowStripes = true
        };
        sheet.StructuredTables.Add(table);
        StructuredTableStyleService.ApplyLoadedTableStyles(workbook).Should().BeTrue();

        var ctx = new TestCommandContext(workbook);

        // The real "user manually highlights a specific data cell" path: row2, well ABOVE the
        // insertion point used below, so _movedSnapshot (which only captures rows >= beforeRow) never
        // sees it either.
        var highlightedCell = new CellAddress(sheet.Id, 2, 1);
        var highlightRange = new GridRange(highlightedCell, highlightedCell);
        new ApplyStyleCommand(sheet.Id, highlightRange, new StyleDiff(FillColor: UserHighlight))
            .Apply(ctx).Success.Should().BeTrue();
        StyleAt(workbook, sheet, 2, 1).FillColor.Should().Be(UserHighlight, "sanity: the explicit highlight was applied");

        // Insert a row strictly inside the table body, well below row2.
        var insertCommand = new InsertRowsCommand(sheet.Id, beforeRow: 5, count: 1);
        insertCommand.Apply(ctx).Success.Should().BeTrue();

        // Sanity: RebandTable's blast radius really does reach row2 -- it overwrote the explicit
        // highlight with the recomputed banding fill (this part mirrors DeleteRowsCommand's already-
        // accepted "banding is purely positional and always repaints" behavior; it is not itself the
        // defect under test).
        StyleAt(workbook, sheet, 2, 1).FillColor.Should().NotBe(UserHighlight,
            "sanity: reband's forceFill overwrote the user's explicit fill (expected, mirrors DeleteRowsCommand)");

        // The defect: undoing the insert must restore row2's explicit fill, exactly as real Excel's
        // Ctrl+Z would -- an edit far from row2 (and its own undo) must not permanently destroy row2's
        // unrelated formatting.
        insertCommand.Revert(ctx);
        StyleAt(workbook, sheet, 2, 1).FillColor.Should().Be(UserHighlight,
            "undoing the insert must restore the explicit fill on a table row above the insertion point");
    }

    // No-regression sibling: the pre-existing narrow-window undo coverage (calculated-column cells
    // and other newly-inserted-row cells inside [_beforeRow, _beforeRow+_count-1]) must still work
    // exactly as before -- this exercises the same list-ordering guard the fix's wider capture must
    // not break (a calculated-column cell inside the window must undo back to blank, not to some
    // stale "already filled" snapshot entry).
    [Fact]
    public void InsertRowInsideTable_UndoStillClearsCalculatedColumnFillInsideInsertedWindow()
    {
        var workbook = new Workbook("RebandUndoWindow");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Double"));
        for (uint r = 2; r <= 4; r++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, r, 1), new NumberValue(r));
            sheet.SetCell(new CellAddress(sheet.Id, r, 2), Cell.FromFormula($"A{r}*2"));
        }

        // The table's declared bounds extend two rows past its populated data (rows 5-6 are blank
        // but still part of the table body) so the insert below lands in a window with nothing
        // pre-existing anywhere below it to shift back up on Revert -- isolating the window's
        // null-baseline undo path from the row-shift-driven restore that a densely-populated table
        // would otherwise always supply for a "row above" address after a full undo.
        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 2)),
            Columns =
            {
                new StructuredTableColumnModel(1, "Category"),
                new StructuredTableColumnModel(2, "Double", CalculatedColumnFormula: "A2*2")
            }
        };
        sheet.StructuredTables.Add(table);

        var ctx = new TestCommandContext(workbook);
        var insertCommand = new InsertRowsCommand(sheet.Id, beforeRow: 5, count: 1);
        insertCommand.Apply(ctx).Success.Should().BeTrue();

        var newCalcCell = new CellAddress(sheet.Id, 5, 2);
        sheet.GetCell(newCalcCell)!.FormulaText.Should().Be("A5*2", "sanity: the calculated column auto-filled the new row");

        insertCommand.Revert(ctx);
        sheet.GetCell(newCalcCell).Should().BeNull("undo must clear the auto-filled formula cell back to its true pre-insert state (blank), not a stale reband snapshot");
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
