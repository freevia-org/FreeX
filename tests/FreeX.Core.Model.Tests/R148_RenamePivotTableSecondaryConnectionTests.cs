using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// slicer-timeline-wiring F1: <see cref="RenamePivotTableCommand"/> only ever matched a slicer/timeline's
/// PRIMARY connection (<see cref="SlicerModel.SourcePivotTableName"/>/<see cref="TimelineModel.SourcePivotTableName"/>)
/// against the old name. A slicer/timeline bound to SEVERAL pivot tables at once (Excel's "Report
/// Connections", modeled by <see cref="SlicerModel.ConnectedPivotTableNames"/>/
/// <see cref="TimelineModel.ConnectedPivotTableNames"/>) was never touched when the renamed pivot table was
/// only a SECONDARY connection -- the stale old name stayed in ConnectedPivotTableNames forever, and
/// <see cref="SetSlicerSelectionCommand"/>/<see cref="SetTimelineRangeCommand"/> then silently treat the
/// unresolved stale entry the same way they treat a genuinely deleted pivot table: skipped, no error, but
/// the renamed (still-live) pivot table just stops being filtered.
/// <para>
/// These tests drive the real product entry points end to end -- <see cref="RenamePivotTableCommand"/>
/// followed by <see cref="SetSlicerSelectionCommand"/>/<see cref="SetTimelineRangeCommand"/> -- and assert
/// the renamed SECONDARY pivot table's actually-rendered Grand Total narrows, not just that
/// ConnectedPivotTableNames was rewritten in memory.
/// </para>
/// </summary>
public sealed class R148_RenamePivotTableSecondaryConnectionTests
{
    private static CellAddress Addr(Sheet sheet, string a1) => CellAddress.Parse(a1, sheet.Id);

    private static GridRange Range(Sheet sheet, string start, string end) =>
        new(Addr(sheet, start), Addr(sheet, end));

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
        var workbook = new Workbook("R148RenameSecondaryConnection");
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

    /// <summary>THE FIX: renaming the SECONDARY connection (PT2) must keep the slicer filtering it.</summary>
    [Fact]
    public void RenameSecondaryConnectedPivotTable_SlicerStillFiltersItAfterRename()
    {
        var (workbook, sheet, pt1, pt2) = BuildTwoPivotsSharedSource();

        GrandTotal(sheet, pt1).Should().Be(new NumberValue(60));
        GrandTotal(sheet, pt2).Should().Be(new NumberValue(60));

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

        // Rename PT2 -- the SECONDARY connection, not the slicer's primary (PT1).
        var rename = new RenamePivotTableCommand(sheet.Id, "PT2", "PT2New");
        var renameOutcome = rename.Apply(ctx);
        renameOutcome.Success.Should().BeTrue(renameOutcome.ErrorMessage);
        pt2.Name.Should().Be("PT2New");

        // The primary connection must be untouched by a secondary-only rename.
        slicer.SourcePivotTableName.Should().Be("PT1");
        slicer.ConnectedPivotTableNames.Should().Equal(["PT1", "PT2New"],
            "the secondary connection's stale old name must be rewritten in place, leaving the primary connection alone");

        var selectionOutcome = new SetSlicerSelectionCommand("Region Slicer", ["East"]).Apply(ctx);
        selectionOutcome.Success.Should().BeTrue(selectionOutcome.ErrorMessage);

        GrandTotal(sheet, pt1).Should().Be(new NumberValue(10));
        // Before the fix: PT2New stayed at the unfiltered 60 because the stale "PT2" entry in
        // ConnectedPivotTableNames could never resolve to the renamed live pivot table and was silently
        // skipped as if it were a deleted connection.
        GrandTotal(sheet, pt2).Should().Be(new NumberValue(10),
            "the renamed (still-live) secondary connection must keep being filtered by the slicer, exactly like Excel");
    }

    /// <summary>Sibling coverage for the timeline control (same wiring, same bug class).</summary>
    [Fact]
    public void RenameSecondaryConnectedPivotTable_TimelineStillFiltersItAfterRename()
    {
        var workbook = new Workbook("R148RenameSecondaryConnectionTimeline");
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

        var rename = new RenamePivotTableCommand(sheet.Id, "PT2", "PT2New");
        rename.Apply(ctx).Success.Should().BeTrue();
        pt2.Name.Should().Be("PT2New");

        var timeline = workbook.Timelines.Single();
        timeline.SourcePivotTableName.Should().Be("PT1");
        timeline.ConnectedPivotTableNames.Should().Equal(["PT1", "PT2New"]);

        var outcome = new SetTimelineRangeCommand("Date Timeline", "2026-01-01", "2026-01-31").Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        GrandTotal(sheet, pt1).Should().Be(new NumberValue(100));
        GrandTotal(sheet, pt2).Should().Be(new NumberValue(100),
            "the timeline's renamed secondary connection must keep narrowing to the January row, not stay unfiltered");
    }

    /// <summary>
    /// No-regression sibling: renaming the PRIMARY connection must keep behaving exactly as before --
    /// primary name updated, unrelated secondary connection left completely alone.
    /// </summary>
    [Fact]
    public void RenamePrimaryConnectedPivotTable_UpdatesPrimaryAndLeavesSecondaryConnectionAlone()
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

        var rename = new RenamePivotTableCommand(sheet.Id, "PT1", "PT1New");
        rename.Apply(ctx).Success.Should().BeTrue();

        slicer.SourcePivotTableName.Should().Be("PT1New");
        slicer.ConnectedPivotTableNames.Should().Equal(["PT1New", "PT2"],
            "the renamed primary connection updates in place; the OTHER (untouched) connection must be left alone");

        var selectionOutcome = new SetSlicerSelectionCommand("Region Slicer", ["East"]).Apply(ctx);
        selectionOutcome.Success.Should().BeTrue(selectionOutcome.ErrorMessage);

        GrandTotal(sheet, pt1).Should().Be(new NumberValue(10));
        GrandTotal(sheet, pt2).Should().Be(new NumberValue(10));

        // Revert must restore the primary name (and undo the filter) exactly as before this fix.
        rename.Revert(ctx);
        slicer.SourcePivotTableName.Should().Be("PT1");
        slicer.ConnectedPivotTableNames.Should().Equal(["PT1", "PT2"]);
    }
}
