using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// HIGH-severity finding: a Table AutoFilter's value-list filter that keeps "(Blanks)" checked (the
/// checklist entry whose normalized <see cref="FilterValueFormatter.ToText"/> value is the literal
/// sentinel <c>""</c>, per <see cref="FilterCommand.ApplyToStructuredTableIfMatched"/>, which never
/// converts that entry into <see cref="StructuredTableFilterColumnModel.IncludeBlank"/>) used to be
/// silently dropped on save+reopen. <see cref="XlsxStructuredTableWriter"/> serializes the "" entry as
/// a literal <c>&lt;filter val=""/&gt;</c> element (no <c>blank="1"</c>), but
/// <see cref="XlsxStructuredTableMetadataReader"/>'s <c>ReadFilterColumns</c> used to discard any
/// empty/whitespace-only <c>val</c> on read (<c>!string.IsNullOrWhiteSpace</c>), losing the criterion
/// entirely -- unlike the sibling worksheet-level AutoFilter reader
/// (<see cref="XlsxWorksheetAutoFilterXmlMapper"/>), which was already patched to keep any non-null
/// <c>val</c> for exactly this reason. The fix aligns the structured-table reader's <c>val</c> filter
/// with the worksheet-level reader's <c>value is not null</c> check.
/// </summary>
public sealed class R99_StructuredTableBlankFilterRoundTripTests
{
    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    private static Workbook SaveAndReload(Workbook workbook)
    {
        var adapter = new XlsxFileAdapter();
        using var ms = new MemoryStream();
        adapter.Save(workbook, ms);
        ms.Position = 0;
        return adapter.Load(ms);
    }

    /// <summary>
    /// Builds a table (header row 1, data rows 2-6, column A = Category text, column B = Amount
    /// number) with rows 3 and 6 left as genuinely blank Category cells.
    /// </summary>
    private static (Workbook Workbook, Sheet Sheet, StructuredTableModel Table, GridRange Range) BuildWorkbookWithTable()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Fruit"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        // Row 3: Category left genuinely blank (no cell set at all).
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Veg"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("Fruit"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(40));
        // Row 6: Category also genuinely blank.
        sheet.SetCell(new CellAddress(sheet.Id, 6, 2), new NumberValue(50));

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 2));
        var table = new StructuredTableModel
        {
            Id = 7,
            Name = "T",
            DisplayName = "T",
            Range = range,
            HasAutoFilter = true,
            Columns =
            {
                new StructuredTableColumnModel(1, "Category"),
                new StructuredTableColumnModel(2, "Amount"),
            },
        };
        sheet.StructuredTables.Add(table);
        return (wb, sheet, table, range);
    }

    /// <summary>
    /// Full round trip through the REAL product entry point: applying the user's interactive filter via
    /// <see cref="FilterCommand"/> (leaves "Fruit" and "(Blanks)" checked, unchecks "Veg" -- the exact
    /// _allowedValues shape AutoFilterChecklistPlanner hands FilterCommand), then
    /// <see cref="XlsxFileAdapter"/>.Save/Load (what actually runs on Save/Close/Reopen).
    /// </summary>
    [Fact]
    public void R99_TableFilter_KeepingBlanksChecked_SurvivesSaveAndReload()
    {
        var (wb, sheet, table, range) = BuildWorkbookWithTable();
        var ctx = new TestCommandContext(wb);

        // User leaves "Fruit" and "(Blanks)" checked, unchecks "Veg".
        var filter = new FilterCommand(sheet.Id, range, filterColOffset: 0, allowedValues: ["Fruit", ""]);
        filter.Apply(ctx).Success.Should().BeTrue();

        // In-session: only the Veg row (4) is hidden; both blank rows (3, 6) stay visible.
        sheet.FilterHiddenRows.Should().BeEquivalentTo([4u]);

        var reloaded = SaveAndReload(wb);
        var reloadedSheet = reloaded.Sheets[0];
        var reloadedTable = reloadedSheet.StructuredTables.Single(t => t.Id == table.Id);

        // The persisted filter criterion for column 0 must still include the blank entry in some form
        // (literal "" in Values and/or IncludeBlank) -- not silently dropped.
        var reloadedFilterColumn = reloadedTable.FilterColumns.Single(fc => fc.ColumnId == 0);
        var keepsBlank = reloadedFilterColumn.IncludeBlank || reloadedFilterColumn.Values.Contains("");
        keepsBlank.Should().BeTrue("the '(Blanks)' criterion must round-trip, not vanish on save+reopen");

        // Real Excel ground truth: Save/Close/Reopen must not change which rows are visible. Row 4
        // (Veg) stays hidden; rows 3 and 6 (blank Category, previously kept visible by the user) must
        // NOT be silently re-hidden.
        reloadedSheet.FilterHiddenRows.Should().BeEquivalentTo([4u]);
        reloadedSheet.IsRowEffectivelyHidden(3).Should().BeFalse();
        reloadedSheet.IsRowEffectivelyHidden(6).Should().BeFalse();
        reloadedSheet.IsRowEffectivelyHidden(4).Should().BeTrue();
    }

    /// <summary>
    /// No-regression sibling: an ordinary (no-blanks-involved) value-list filter on a structured table
    /// must keep round-tripping exactly as before -- the fix must not change behavior for filters that
    /// never touch a blank/whitespace entry.
    /// </summary>
    [Fact]
    public void R99_TableFilter_WithoutBlanksChecked_StillRoundTripsExcludedRows()
    {
        var (wb, sheet, table, range) = BuildWorkbookWithTable();
        var ctx = new TestCommandContext(wb);

        // User keeps only "Fruit" checked -- "Veg" and "(Blanks)" both excluded.
        var filter = new FilterCommand(sheet.Id, range, filterColOffset: 0, allowedValues: ["Fruit"]);
        filter.Apply(ctx).Success.Should().BeTrue();

        // Veg (row 4) and both blank rows (3, 6) are hidden; only the two Fruit rows (2, 5) stay visible.
        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u, 4u, 6u]);

        var reloaded = SaveAndReload(wb);
        var reloadedSheet = reloaded.Sheets[0];
        var reloadedTable = reloadedSheet.StructuredTables.Single(t => t.Id == table.Id);

        var reloadedFilterColumn = reloadedTable.FilterColumns.Single(fc => fc.ColumnId == 0);
        reloadedFilterColumn.Values.Should().BeEquivalentTo(["Fruit"]);
        reloadedFilterColumn.IncludeBlank.Should().BeFalse();

        reloadedSheet.FilterHiddenRows.Should().BeEquivalentTo([3u, 4u, 6u]);
        reloadedSheet.IsRowEffectivelyHidden(2).Should().BeFalse();
        reloadedSheet.IsRowEffectivelyHidden(5).Should().BeFalse();
    }
}
