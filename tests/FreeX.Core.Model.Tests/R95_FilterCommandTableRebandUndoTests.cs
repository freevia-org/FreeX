using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

// R95-commands-filter-table-reband-undo-1: FilterCommand.Apply/Revert call
// StructuredTableBandingReflow.ReflowIfMatched whenever a filter hides/shows rows in a structured
// table (R91-meta-3), which calls StructuredTableStyleService.RebandTable with forceReband:true.
// RebandTable's body-row pass always repaints the table's ENTIRE data body with forceFill:true
// (MergeStyleOntoCell's keepExistingFill is unconditionally false under forceFill), unconditionally
// overwriting any explicit FillColor a user set on a body cell. FilterCommand's only undo state
// (FilterUndoSnapshot) captures exclusively row-visibility bookkeeping, never cell content or style
// -- unlike InsertRowsCommand/DeleteRowsCommand/SortCommand, which each snapshot the table's full
// data body before calling RebandTable specifically so Ctrl+Z can restore a clobbered user fill.
// Exercised through the real command entry points: ApplyStyleCommand (the real "highlight a cell"
// path) and FilterCommand itself (mirrors R94_SortCommandTableRebandUndoTests).
public sealed class R95_FilterCommandTableRebandUndoTests
{
    private static readonly CellColor UserHighlight = new(255, 0, 0);

    [Fact]
    public void ApplyValueFilter_UndoRestoresExplicitFillOnVisibleBodyRow()
    {
        var workbook = new Workbook("FilterRebandUndo");
        var sheet = workbook.AddSheet("Data");
        // Header row1; data rows 2-5. Column1 alternates Keep/Drop so filtering to "Keep" hides
        // rows 3 and 5, leaving rows 2 and 4 visible.
        SeedTable(sheet);

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2));
        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = range,
            HeaderRowCount = 1,
            StyleName = "TableStyleMedium2",
            ShowRowStripes = true
        };
        sheet.StructuredTables.Add(table);
        StructuredTableStyleService.ApplyLoadedTableStyles(workbook).Should().BeTrue();

        var ctx = new TestCommandContext(workbook);

        // The real "user manually highlights a specific data cell" path: row4, a row that stays
        // VISIBLE after the filter below (so it is squarely inside RebandTable's repaint blast
        // radius, not merely a row that got hidden).
        var highlightedCell = new CellAddress(sheet.Id, 4, 1);
        var highlightRange = new GridRange(highlightedCell, highlightedCell);
        new ApplyStyleCommand(sheet.Id, highlightRange, new StyleDiff(FillColor: UserHighlight))
            .Apply(ctx).Success.Should().BeTrue();
        StyleAt(workbook, sheet, 4, 1).FillColor.Should().Be(UserHighlight, "sanity: the explicit highlight was applied");

        var filterCommand = new FilterCommand(sheet.Id, range, filterColOffset: 0, allowedValues: ["Keep"]);
        filterCommand.Apply(ctx).Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().Contain([3u, 5u], "sanity: the filter hid rows 3 and 5");

        // Sanity: RebandTable's forceFill repaint really does reach row4 and overwrite the user's
        // explicit fill (this is the defect, not itself under test here).
        StyleAt(workbook, sheet, 4, 1).FillColor.Should().NotBe(UserHighlight,
            "sanity: the filter's table reband overwrote the user's explicit fill (expected -- this is the bug)");

        // The defect: undoing the filter must restore row4's explicit fill, exactly as real Excel's
        // Ctrl+Z would -- applying (or clearing) a filter must never permanently destroy formatting.
        filterCommand.Revert(ctx);
        StyleAt(workbook, sheet, 4, 1).FillColor.Should().Be(UserHighlight,
            "undoing the filter must restore the explicit fill the reband clobbered");
        sheet.FilterHiddenRows.Should().BeEmpty("undo must also restore row visibility");
    }

    // No-regression sibling: the pre-existing row-visibility undo coverage (the actual point of
    // FilterCommand's undo snapshot) must still work exactly as before the fix.
    [Fact]
    public void ApplyValueFilter_UndoStillRestoresRowVisibilityAndFilterColumns()
    {
        var workbook = new Workbook("FilterRebandUndoVisibility");
        var sheet = workbook.AddSheet("Data");
        SeedTable(sheet);

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2));
        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = range,
            HeaderRowCount = 1,
            StyleName = "TableStyleMedium2",
            ShowRowStripes = true
        };
        sheet.StructuredTables.Add(table);
        StructuredTableStyleService.ApplyLoadedTableStyles(workbook).Should().BeTrue();

        var ctx = new TestCommandContext(workbook);
        var filterCommand = new FilterCommand(sheet.Id, range, filterColOffset: 0, allowedValues: ["Keep"]);
        filterCommand.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().Contain([3u, 5u]);
        sheet.StructuredTables[0].FilterColumns.Should().ContainSingle(fc => fc.ColumnId == 0);

        filterCommand.Revert(ctx);

        sheet.FilterHiddenRows.Should().BeEmpty("undo must restore all rows as visible");
        sheet.ActiveValueFilterColumns.Should().BeEmpty("undo must clear the active value-filter column bookkeeping");
        sheet.StructuredTables[0].FilterColumns.Should().BeEmpty("undo must restore the table's pre-filter FilterColumns model");

        // Banding must also re-flow correctly back to the purely-positional pattern once every row
        // is visible again -- confirms the fix's snapshot-restore (instead of a second RebandTable
        // call) still leaves banding internally consistent, not just "whatever it happened to be".
        var banding = StructuredTableStyleBandingResolver.Resolve("TableStyleMedium2", workbook.Theme);
        StyleAt(workbook, sheet, 2, 1).FillColor.Should().Be(banding.EvenRowFill);
        StyleAt(workbook, sheet, 3, 1).FillColor.Should().Be(banding.OddRowFill);
        StyleAt(workbook, sheet, 4, 1).FillColor.Should().Be(banding.EvenRowFill);
        StyleAt(workbook, sheet, 5, 1).FillColor.Should().Be(banding.OddRowFill);
    }

    private static void SeedTable(Sheet sheet)
    {
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Keep"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Drop"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Keep"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("Drop"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(40));
    }

    private static CellStyle StyleAt(Workbook workbook, Sheet sheet, uint row, uint col) =>
        workbook.GetStyle(sheet.GetCell(new CellAddress(sheet.Id, row, col))!.StyleId);
}
