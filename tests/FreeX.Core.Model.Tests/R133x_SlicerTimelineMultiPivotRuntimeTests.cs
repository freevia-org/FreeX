using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R133x-commands-slicer-timeline-multipivot-runtime: the r133 fix (XlsxSlicerTimelineStateRewriter)
/// only repaired PERSISTENCE of a slicer/timeline's multiple pivot table connections
/// (<see cref="SlicerModel.ConnectedPivotTableNames"/>/<see cref="TimelineModel.ConnectedPivotTableNames"/>)
/// -- the file now records every connection on save -- but <see cref="SetSlicerSelectionCommand"/> and
/// <see cref="SetTimelineRangeCommand"/> (the actual runtime entry points a slicer tile click / timeline
/// drag invokes) still only ever looked up and mutated the SINGLE primary
/// <c>SourcePivotTableName</c> connection. A slicer connected to two pivot tables (Excel's "Report
/// Connections") would filter the first one live but leave the second showing completely unfiltered
/// data, even though both connections round-tripped correctly on disk.
/// <para>
/// These tests drive the real product entry points end to end: build two live <see cref="PivotTableModel"/>
/// instances sharing one slicer/timeline connection list, apply a selection, and assert BOTH pivots'
/// actually-rendered sheet output changed -- not just their in-memory field metadata. Grand-total cells
/// are located dynamically off each pivot's <see cref="PivotTableModel.LastRenderedRange"/> (set by
/// <see cref="PivotTableRefreshService.Refresh"/>) rather than hardcoded row offsets, since a filter
/// changes how many item rows a pivot renders.
/// </para>
/// </summary>
public sealed class R133x_SlicerTimelineMultiPivotRuntimeTests
{
    private static CellAddress Addr(Sheet sheet, string a1) => CellAddress.Parse(a1, sheet.Id);

    private static GridRange Range(Sheet sheet, string start, string end) =>
        new(Addr(sheet, start), Addr(sheet, end));

    /// <summary>The pivot's Grand Total row is always its rendered footprint's last row; the data value
    /// sits one column right of the row-label column.</summary>
    private static NumberValue GrandTotal(Sheet sheet, PivotTableModel pivot)
    {
        var range = pivot.LastRenderedRange ?? pivot.TargetRange;
        var labelCell = sheet.GetCell(new CellAddress(range.Start.Sheet, range.End.Row, range.Start.Col));
        labelCell.Should().NotBeNull("the pivot must have rendered a Grand Total row");
        labelCell!.Value.Should().Be(new TextValue("Grand Total"));

        var valueCell = sheet.GetCell(new CellAddress(range.Start.Sheet, range.End.Row, range.Start.Col + 1));
        valueCell.Should().NotBeNull();
        return (NumberValue)valueCell!.Value!;
    }

    private static (Workbook Workbook, Sheet Sheet, PivotTableModel Pt1, PivotTableModel Pt2) BuildTwoPivotsSharedSource()
    {
        var workbook = new Workbook("R133xMultiPivotRuntime");
        var sheet = workbook.AddSheet("Data");

        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Region"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(20));
        sheet.SetCell(Addr(sheet, "A4"), new TextValue("North"));
        sheet.SetCell(Addr(sheet, "B4"), new NumberValue(30));

        var cache = new PivotCacheModel { CacheId = 1, SourceType = PivotCacheSourceType.WorksheetRange, SourceSheetName = "Data", SourceReference = "A1:B4" };
        cache.Fields.Add(new PivotCacheFieldModel("Region", ContainsString: true, SharedItems: ["East", "West", "North"]));
        cache.Fields.Add(new PivotCacheFieldModel("Amount", ContainsNumber: true));
        workbook.PivotCaches.Add(cache);

        var pt1 = new PivotTableModel
        {
            Name = "PT1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B4"),
            TargetRange = Range(sheet, "D3", "F10"),
        };
        pt1.RowFields.Add(new PivotFieldModel(0));
        pt1.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pt1);

        var pt2 = new PivotTableModel
        {
            Name = "PT2",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B4"),
            TargetRange = Range(sheet, "D30", "F37"),
        };
        pt2.RowFields.Add(new PivotFieldModel(0));
        pt2.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pt2);

        PivotTableRefreshService.Refresh(workbook, sheet, pt1);
        PivotTableRefreshService.Refresh(workbook, sheet, pt2);

        return (workbook, sheet, pt1, pt2);
    }

    [Fact]
    public void SetSlicerSelectionCommand_SlicerConnectedToTwoPivots_FiltersBothPivotsAtRuntime()
    {
        var (workbook, sheet, pt1, pt2) = BuildTwoPivotsSharedSource();

        // Sanity: unfiltered, both pivots' grand totals sum all three regions (10 + 20 + 30 = 60).
        GrandTotal(sheet, pt1).Should().Be(new NumberValue(60));
        GrandTotal(sheet, pt2).Should().Be(new NumberValue(60));

        var slicer = new SlicerModel
        {
            Name = "Region Slicer",
            CacheName = "Slicer_Region",
            SourcePivotTableName = "PT1",
            SourceFieldName = "Region",
            // Mirrors what a load from a package with two <pivotTable> connections populates (see
            // XlsxSlicerTimelineMetadataReader / FreeXR133SlicerTimelineMultiPivotTests) -- PT1 is the
            // primary connection, PT2 is a second "Report Connection" the same slicer also drives.
            ConnectedPivotTableNames = ["PT1", "PT2"],
        };
        workbook.Slicers.Add(slicer);

        var ctx = new TestCommandContext(workbook);
        var outcome = new SetSlicerSelectionCommand("Region Slicer", ["East"]).Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        // PT1 (the primary connection) must be filtered to East only: grand total narrows to 10.
        GrandTotal(sheet, pt1).Should().Be(new NumberValue(10));

        // PT2 (the SECOND connection) must ALSO be filtered to East only -- this is the runtime gap:
        // before the fix, PT2 kept showing all three regions (grand total 60) because
        // SetSlicerSelectionCommand only ever looked up SourcePivotTableName ("PT1").
        GrandTotal(sheet, pt2).Should().Be(new NumberValue(10),
            "the slicer's second connected pivot table must be filtered by the same selection, not stay at the unfiltered 60");

        // Both connected pivot tables' RowFields must carry the selection, not just PT1's.
        pt1.RowFields.Single().SelectedItem.Should().Be("East");
        pt2.RowFields.Single().SelectedItem.Should().Be("East");
    }

    [Fact]
    public void SetSlicerSelectionCommand_Revert_RestoresBothConnectedPivots()
    {
        var (workbook, sheet, pt1, pt2) = BuildTwoPivotsSharedSource();

        var slicer = new SlicerModel
        {
            Name = "Region Slicer",
            CacheName = "Slicer_Region",
            SourcePivotTableName = "PT1",
            SourceFieldName = "Region",
            ConnectedPivotTableNames = ["PT1", "PT2"],
        };
        workbook.Slicers.Add(slicer);

        var ctx = new TestCommandContext(workbook);
        var command = new SetSlicerSelectionCommand("Region Slicer", ["East"]);
        command.Apply(ctx).Success.Should().BeTrue();

        // Both filtered before revert.
        GrandTotal(sheet, pt1).Should().Be(new NumberValue(10));
        GrandTotal(sheet, pt2).Should().Be(new NumberValue(10));

        command.Revert(ctx);

        // Both pivots restored to their unfiltered Grand Total (60) after undo.
        GrandTotal(sheet, pt1).Should().Be(new NumberValue(60), "undo must restore PT1's unfiltered grand total");
        GrandTotal(sheet, pt2).Should().Be(new NumberValue(60),
            "undo must ALSO restore PT2's unfiltered grand total, not just PT1's");
        slicer.SelectedItems.Should().BeEmpty();
    }

    [Fact]
    public void SetTimelineRangeCommand_TimelineConnectedToTwoPivots_FiltersBothPivotsAtRuntime()
    {
        var workbook = new Workbook("R133xMultiPivotTimelineRuntime");
        var sheet = workbook.AddSheet("Data");

        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Date"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Sales"));
        sheet.SetCell(Addr(sheet, "A2"), DateTimeValue.FromDateTime(new DateTime(2026, 1, 5)));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(100));
        sheet.SetCell(Addr(sheet, "A3"), DateTimeValue.FromDateTime(new DateTime(2026, 2, 2)));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(200));

        var pt1 = new PivotTableModel
        {
            Name = "PT1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B3"),
            TargetRange = Range(sheet, "D3", "F9"),
        };
        pt1.RowFields.Add(new PivotFieldModel(0));
        pt1.DataFields.Add(new PivotDataFieldModel(1, "Sum of Sales", "sum"));
        sheet.PivotTables.Add(pt1);

        var pt2 = new PivotTableModel
        {
            Name = "PT2",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B3"),
            TargetRange = Range(sheet, "D30", "F36"),
        };
        pt2.RowFields.Add(new PivotFieldModel(0));
        pt2.DataFields.Add(new PivotDataFieldModel(1, "Sum of Sales", "sum"));
        sheet.PivotTables.Add(pt2);

        PivotTableRefreshService.Refresh(workbook, sheet, pt1);
        PivotTableRefreshService.Refresh(workbook, sheet, pt2);

        GrandTotal(sheet, pt1).Should().Be(new NumberValue(300));
        GrandTotal(sheet, pt2).Should().Be(new NumberValue(300));

        workbook.Timelines.Add(new TimelineModel
        {
            Name = "Date Timeline",
            CacheName = "Timeline_Date",
            SourcePivotTableName = "PT1",
            SourceFieldName = "Date",
            ConnectedPivotTableNames = ["PT1", "PT2"],
        });

        var ctx = new TestCommandContext(workbook);
        var outcome = new SetTimelineRangeCommand("Date Timeline", "2026-01-01", "2026-01-31").Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        GrandTotal(sheet, pt1).Should().Be(new NumberValue(100));
        // The second connected pivot table must ALSO narrow to the January row (100), not stay at the
        // unfiltered 300.
        GrandTotal(sheet, pt2).Should().Be(new NumberValue(100),
            "the timeline's second connected pivot table must be filtered by the same date range");
    }
}
