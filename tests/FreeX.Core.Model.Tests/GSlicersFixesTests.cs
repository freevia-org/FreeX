using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression tests for the G-slicers review-3 fixes:
/// H10 (slicer/timeline selection on a field absent from Row/Column/PageFields must still filter),
/// H11 (table-connected slicer must filter its structured table instead of erroring),
/// H59 (undoing a timeline granularity change must restore a null Level as null).
/// </summary>
public sealed class GSlicersFixesTests
{
    private static CellAddress Addr(Sheet sheet, string a1) => CellAddress.Parse(a1, sheet.Id);

    private static GridRange Range(Sheet sheet, string start, string end) =>
        new(Addr(sheet, start), Addr(sheet, end));

    private static void SeedProductRegionSalesData(Sheet sheet)
    {
        // A single Product ("Widget") so the pivot's sole row group is unambiguous regardless of
        // Region sort order — the test only cares about the Region filter's effect on that one group.
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Product"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Region"));
        sheet.SetCell(Addr(sheet, "C1"), new TextValue("Sales"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("Widget"));
        sheet.SetCell(Addr(sheet, "B2"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "C2"), new NumberValue(100));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("Widget"));
        sheet.SetCell(Addr(sheet, "B3"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "C3"), new NumberValue(200));
    }

    // ── H10: slicer/timeline field absent from Row/Column/PageFields must still filter ──────────

    [Fact]
    public void SetSlicerSelectionCommand_FieldNotInPivotLayout_StillFiltersAndUndoRestores()
    {
        var workbook = new Workbook("H10SlicerUnplacedFieldTest");
        var sheet = workbook.AddSheet("Data");
        SeedProductRegionSalesData(sheet);
        var ctx = new TestCommandContext(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C3"),
            TargetRange = Range(sheet, "E3", "G6")
        };
        // Row = Product only; Region ("B") is NOT in Row/Column/PageFields.
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Sales", "sum"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        // Sanity: unfiltered, Widget's total is 100 (West) + 200 (East) = 300.
        sheet.GetCell(Addr(sheet, "E4"))!.Value.Should().Be(new TextValue("Widget"));
        sheet.GetCell(Addr(sheet, "F4"))!.Value.Should().Be(new NumberValue(300));

        workbook.Slicers.Add(new SlicerModel
        {
            Name = "Region Slicer",
            CacheName = "Slicer_Region",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Region"
        });

        var command = new SetSlicerSelectionCommand("Region Slicer", ["West"]);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        // The field must now be present somewhere in the pivot's field layout (as a filter), since
        // it was absent from Row/Column/PageFields before the command ran.
        var allFields = pivot.RowFields.Concat(pivot.ColumnFields).Concat(pivot.PageFields).ToList();
        allFields.Should().Contain(field => field.SourceFieldIndex == 1 && field.SelectedItems!.Contains("West"));

        // Widget must now show only the West row's sales (100), not 300 (both regions).
        sheet.GetCell(Addr(sheet, "F4"))!.Value.Should().Be(new NumberValue(100));

        command.Revert(ctx);

        pivot.PageFields.Should().BeEmpty();
        pivot.ColumnFields.Should().BeEmpty();
        sheet.GetCell(Addr(sheet, "F4"))!.Value.Should().Be(new NumberValue(300));
    }

    [Fact]
    public void SetTimelineRangeCommand_FieldNotInPivotLayout_StillFiltersAndUndoRestores()
    {
        var workbook = new Workbook("H10TimelineUnplacedFieldTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Product"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Date"));
        sheet.SetCell(Addr(sheet, "C1"), new TextValue("Sales"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("Widget"));
        sheet.SetCell(Addr(sheet, "B2"), DateTimeValue.FromDateTime(new DateTime(2026, 1, 5)));
        sheet.SetCell(Addr(sheet, "C2"), new NumberValue(100));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("Widget"));
        sheet.SetCell(Addr(sheet, "B3"), DateTimeValue.FromDateTime(new DateTime(2026, 2, 2)));
        sheet.SetCell(Addr(sheet, "C3"), new NumberValue(200));

        var ctx = new TestCommandContext(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C3"),
            TargetRange = Range(sheet, "E3", "G6")
        };
        // Row = Product only; Date ("B") is NOT in Row/Column/PageFields.
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Sales", "sum"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        sheet.GetCell(Addr(sheet, "F4"))!.Value.Should().Be(new NumberValue(300));

        workbook.Timelines.Add(new TimelineModel
        {
            Name = "Date Timeline",
            CacheName = "Timeline_Date",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Date"
        });

        var command = new SetTimelineRangeCommand("Date Timeline", "2026-01-01", "2026-01-31");
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        var allFields = pivot.RowFields.Concat(pivot.ColumnFields).Concat(pivot.PageFields).ToList();
        allFields.Should().Contain(field => field.SourceFieldIndex == 1);
        sheet.GetCell(Addr(sheet, "F4"))!.Value.Should().Be(new NumberValue(100));

        command.Revert(ctx);

        pivot.PageFields.Should().BeEmpty();
        sheet.GetCell(Addr(sheet, "F4"))!.Value.Should().Be(new NumberValue(300));
    }

    // ── H11: table-connected slicer must filter its structured table ────────────────────────────

    [Fact]
    public void SetSlicerSelectionCommand_TableSlicer_FiltersTableRowsAndUndoRestores()
    {
        var workbook = new Workbook("H11TableSlicerTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Region"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("North"));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("South"));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(20));
        sheet.SetCell(Addr(sheet, "A4"), new TextValue("North"));
        sheet.SetCell(Addr(sheet, "B4"), new NumberValue(30));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = Range(sheet, "A1", "B4"),
            HasAutoFilter = true
        };
        table.Columns.Add(new StructuredTableColumnModel(0, "Region"));
        table.Columns.Add(new StructuredTableColumnModel(1, "Amount"));
        sheet.StructuredTables.Add(table);

        workbook.Slicers.Add(new SlicerModel
        {
            Name = "Region Table Slicer",
            CacheName = "Slicer_RegionTable",
            SourceTableId = 1,
            SourceTableColumnId = 0
        });

        var ctx = new TestCommandContext(workbook);
        var command = new SetSlicerSelectionCommand("Region Table Slicer", ["North"]);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        workbook.Slicers[0].SelectedItems.Should().Equal("North");
        // Row 3 (South) must be hidden; rows 2 and 4 (North) remain visible.
        sheet.FilterHiddenRows.Should().Contain(3u);
        sheet.FilterHiddenRows.Should().NotContain(2u);
        sheet.FilterHiddenRows.Should().NotContain(4u);
        table.FilterColumns.Should().ContainSingle(f => f.ColumnId == 0 && f.Values.Contains("North"));

        command.Revert(ctx);

        workbook.Slicers[0].SelectedItems.Should().BeEmpty();
        sheet.FilterHiddenRows.Should().BeEmpty();
        table.FilterColumns.Should().BeEmpty();
    }

    [Fact]
    public void SetSlicerSelectionCommand_TableSlicer_ClearingSelectionRemovesFilterColumn()
    {
        var workbook = new Workbook("H11TableSlicerClearTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Region"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("North"));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("South"));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(20));

        var table = new StructuredTableModel
        {
            Id = 7,
            Name = "Table7",
            DisplayName = "Table7",
            Range = Range(sheet, "A1", "B3"),
            HasAutoFilter = true
        };
        table.Columns.Add(new StructuredTableColumnModel(0, "Region"));
        table.Columns.Add(new StructuredTableColumnModel(1, "Amount"));
        sheet.StructuredTables.Add(table);

        workbook.Slicers.Add(new SlicerModel
        {
            Name = "Region Table Slicer",
            CacheName = "Slicer_RegionTable",
            SourceTableId = 7,
            SourceTableColumnId = 0
        });

        var ctx = new TestCommandContext(workbook);
        new SetSlicerSelectionCommand("Region Table Slicer", ["North"]).Apply(ctx).Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().Contain(3u);

        var clearCommand = new SetSlicerSelectionCommand("Region Table Slicer", []);
        var outcome = clearCommand.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.FilterHiddenRows.Should().BeEmpty();
        table.FilterColumns.Should().BeEmpty();
    }

    [Fact]
    public void SetSlicerSelectionCommand_TableSlicer_RejectsProtectedSheetWithoutUseAutoFilterPermission()
    {
        var workbook = new Workbook("H11TableSlicerProtectionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Region"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("North"));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = Range(sheet, "A1", "A2"),
            HasAutoFilter = true
        };
        table.Columns.Add(new StructuredTableColumnModel(0, "Region"));
        sheet.StructuredTables.Add(table);
        sheet.IsProtected = true;

        workbook.Slicers.Add(new SlicerModel
        {
            Name = "Region Table Slicer",
            CacheName = "Slicer_RegionTable",
            SourceTableId = 1,
            SourceTableColumnId = 0
        });

        var ctx = new TestCommandContext(workbook);
        var outcome = new SetSlicerSelectionCommand("Region Table Slicer", ["North"]).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
    }

    // ── H59: undo of timeline granularity change must restore a null Level as null ────────────────

    [Fact]
    public void SetTimelineGranularityCommand_UndoRestoresAbsentLevelAsNull()
    {
        var workbook = new Workbook("H59TimelineGranularityUndoTest");
        // AddTimelineCommand never sets Level, so a freshly-inserted timeline starts with Level == null.
        var timeline = new TimelineModel
        {
            Name = "Date Timeline",
            CacheName = "Timeline_Date",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Date"
        };
        workbook.Timelines.Add(timeline);
        timeline.Level.Should().BeNull();
        var ctx = new TestCommandContext(workbook);

        var command = new SetTimelineGranularityCommand("Date Timeline", 3);
        command.Apply(ctx).Success.Should().BeTrue();

        timeline.Level.Should().Be(3);

        command.Revert(ctx);

        timeline.Level.Should().BeNull("undo must restore the pre-change state, including an absent Level, not fall back to the Month default");
    }

    [Fact]
    public void SetTimelineGranularityCommand_UndoRestoresExplicitPriorLevel()
    {
        var workbook = new Workbook("H59TimelineGranularityExplicitUndoTest");
        var timeline = new TimelineModel
        {
            Name = "Date Timeline",
            CacheName = "Timeline_Date",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Date",
            Level = 1
        };
        workbook.Timelines.Add(timeline);
        var ctx = new TestCommandContext(workbook);

        var command = new SetTimelineGranularityCommand("Date Timeline", 3);
        command.Apply(ctx).Success.Should().BeTrue();
        timeline.Level.Should().Be(3);

        command.Revert(ctx);

        timeline.Level.Should().Be(1);
    }
}
