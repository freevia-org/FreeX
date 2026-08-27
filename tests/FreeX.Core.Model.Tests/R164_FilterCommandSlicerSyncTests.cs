using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// freex-autofilter F1: a Table Slicer widget (<see cref="SlicerModel.SourceTableId"/>/
/// <see cref="SlicerModel.SourceTableColumnId"/>) filters the very same structured-table column its
/// bound table's own header-cell AutoFilter dropdown can edit directly through <see cref="FilterCommand"/>.
/// <see cref="FreeX.Core.Commands.SetSlicerSelectionCommand"/> is the only other writer of
/// <see cref="SlicerModel.SelectedItems"/>, and the slicer renderer
/// (<c>FreeX.App.Presentation.SlicerTimeline.SlicerLayoutModel.BuildFull</c>/<c>Toggle</c>) reads ONLY
/// <see cref="SlicerModel.SelectedItems"/> to decide which tiles are highlighted. Before this fix,
/// <see cref="FilterCommand"/> mutated <c>table.FilterColumns</c>/<c>sheet.FilterHiddenRows</c> directly
/// and never touched a bound slicer at all, so editing the header dropdown left the slicer widget
/// showing its old (now wrong) selection even though the sheet's actually-visible rows had changed.
/// </summary>
public sealed class R164_FilterCommandSlicerSyncTests
{
    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    private static StructuredTableModel AddTable(Sheet sheet, GridRange range)
    {
        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            Range = range,
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Region"));
        sheet.StructuredTables.Add(table);
        return table;
    }

    private static Workbook BuildWorkbookWithTableAndSlicer(out Sheet sheet, out GridRange range, out StructuredTableModel table)
    {
        var workbook = new Workbook("SlicerFilterSync");
        sheet = workbook.AddSheet("Sheet1");
        // Table A1:A5 -- header row 1, data rows 2-5 (Region: A/B/C/D).
        range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1));
        table = AddTable(sheet, range);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("C"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("D"));

        workbook.Slicers.Add(new SlicerModel
        {
            Name = "Region Slicer",
            SourceTableId = table.Id,
            SourceTableColumnId = 1,
        });

        return workbook;
    }

    // ── Primary finding: the reproduction gesture from freex-autofilter F1. ──
    // Fail-before/pass-after: step 2 (slicer click) leaves SelectedItems=["A"] (seeded directly here
    // exactly as SetSlicerSelectionCommand.ApplyTableSlicer would leave it after a real tile click,
    // isolating the assertion from that command's own row-hiding mechanism, which is a separate concern
    // from this finding); step 3 (header dropdown, i.e. this FilterCommand) must then update the SAME
    // bound slicer's SelectedItems to ["B", "C"] instead of leaving it stuck on ["A"].
    [Fact]
    public void FilterCommand_ValueListEdit_UpdatesBoundTableSlicerSelection()
    {
        var workbook = BuildWorkbookWithTableAndSlicer(out var sheet, out var range, out _);
        var ctx = new TestCommandContext(workbook);
        var slicer = workbook.Slicers.Single();

        // Step 2: click tile "A" in the slicer.
        slicer.SelectedItems.Add("A");
        slicer.SelectionCaptured = true;

        // Step 3: use the table's own header-cell AutoFilter dropdown to check B and C only.
        var filterCommand = new FilterCommand(sheet.Id, range, filterColOffset: 0, allowedValues: ["B", "C"]);
        filterCommand.Apply(ctx).Success.Should().BeTrue();

        // The sheet is now actually filtered to B/C...
        sheet.FilterHiddenRows.Should().Contain(2u); // row for "A" now hidden
        sheet.FilterHiddenRows.Should().NotContain(3u);
        sheet.FilterHiddenRows.Should().NotContain(4u);

        // ...and the slicer widget bound to the same column must reflect that, not stay on ["A"].
        slicer.SelectedItems.Should().BeEquivalentTo(["B", "C"],
            "the slicer renders purely from SlicerModel.SelectedItems, so it must be kept in sync with " +
            "a filter applied via the table's own header-cell AutoFilter dropdown");
        slicer.SelectionCaptured.Should().BeTrue();
    }

    [Fact]
    public void FilterCommand_ClearFilter_ClearsBoundTableSlicerSelectionToAll()
    {
        var workbook = BuildWorkbookWithTableAndSlicer(out var sheet, out var range, out _);
        var ctx = new TestCommandContext(workbook);
        var slicer = workbook.Slicers.Single();

        slicer.SelectedItems.Add("A");
        slicer.SelectionCaptured = true;

        // "Clear Filter From Region" runs FilterCommand with an empty allowed-values list.
        var clearCommand = new FilterCommand(sheet.Id, range, filterColOffset: 0, allowedValues: []);
        clearCommand.Apply(ctx).Success.Should().BeTrue();

        slicer.SelectedItems.Should().BeEmpty(
            "an empty SelectedItems is the slicer's unfiltered/all-selected state (SlicerLayoutModel " +
            "treats Count == 0 as \"everything selected\"), matching the now-unfiltered sheet");
        slicer.SelectionCaptured.Should().BeTrue();
    }

    [Fact]
    public void FilterCommand_ValueListEdit_ThenUndo_RestoresBoundSlicerSelection()
    {
        var workbook = BuildWorkbookWithTableAndSlicer(out var sheet, out var range, out _);
        var ctx = new TestCommandContext(workbook);
        var slicer = workbook.Slicers.Single();

        slicer.SelectedItems.Add("A");
        slicer.SelectionCaptured = true;

        var filterCommand = new FilterCommand(sheet.Id, range, filterColOffset: 0, allowedValues: ["B", "C"]);
        filterCommand.Apply(ctx).Success.Should().BeTrue();
        slicer.SelectedItems.Should().BeEquivalentTo(["B", "C"]);

        filterCommand.Revert(ctx);

        slicer.SelectedItems.Should().Equal(["A"],
            "undoing the header-dropdown filter edit must put the bound slicer's selection back to " +
            "its exact pre-edit state, not leave it at the edited (now-undone) value");
    }

    // ---- No-regression sibling: a plain worksheet AutoFilter range with no owning table/slicer must
    // behave exactly as before -- no slicer lookup should throw or otherwise misbehave when nothing is
    // bound. ----
    [Fact]
    public void FilterCommand_PlainWorksheetRange_NoStructuredTable_NoSlicerToSync_StillFilters()
    {
        var workbook = new Workbook("PlainRange");
        var sheet = workbook.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Status"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Keep"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Keep"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Drop"));

        var ctx = new TestCommandContext(workbook);
        var command = new FilterCommand(sheet.Id, range, filterColOffset: 0, allowedValues: ["Keep"]);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().NotContain(2u);
        sheet.FilterHiddenRows.Should().NotContain(3u);
        sheet.FilterHiddenRows.Should().Contain(4u);
        workbook.Slicers.Should().BeEmpty();
    }

    // ---- No-regression sibling: a slicer bound to a DIFFERENT column of the same table must be left
    // completely untouched by a filter edit on another column. ----
    [Fact]
    public void FilterCommand_ValueListEdit_DoesNotDisturbSlicerBoundToDifferentColumn()
    {
        var workbook = new Workbook("SlicerFilterSyncOtherColumn");
        var sheet = workbook.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2));
        var table = new StructuredTableModel { Id = 1, Name = "Table1", Range = range };
        table.Columns.Add(new StructuredTableColumnModel(1, "Region"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Status"));
        sheet.StructuredTables.Add(table);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Status"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Open"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("Closed"));

        // Slicer bound to the "Status" column (column id 2), not the "Region" column being edited.
        workbook.Slicers.Add(new SlicerModel
        {
            Name = "Status Slicer",
            SourceTableId = table.Id,
            SourceTableColumnId = 2,
        });
        var statusSlicer = workbook.Slicers.Single();
        statusSlicer.SelectedItems.Add("Open");
        statusSlicer.SelectionCaptured = true;

        var ctx = new TestCommandContext(workbook);
        // Edit the "Region" column (offset 0) via the header dropdown.
        var filterCommand = new FilterCommand(sheet.Id, range, filterColOffset: 0, allowedValues: ["A"]);
        filterCommand.Apply(ctx).Success.Should().BeTrue();

        statusSlicer.SelectedItems.Should().Equal(["Open"],
            "a filter edit on one table column must not touch a slicer bound to a DIFFERENT column");
    }
}
