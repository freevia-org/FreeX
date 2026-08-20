using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class PivotTableCommandTests
{
    // ── R3: AddPivotTableCommand.Revert must clear LastRenderedRange ───────────

    [Fact]
    public void AddPivotTableCommand_Revert_ClearsOrphanCellsWhenRenderedRangeExceedsTargetRange()
    {
        // Pivot whose rendered output (with grand total row) exceeds the minimal TargetRange.
        var workbook = new Workbook("AddPivotUndoClearRenderedTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet); // A1:B3 — Category / Amount
        var ctx = new TestCommandContext(workbook);

        // Use a narrow target; after refresh the pivot may render below it.
        var source = Range(sheet, "A1", "B3");
        var target = Range(sheet, "D3", "E3"); // intentionally small

        var command = new AddPivotTableCommand(
            sheet.Id,
            source,
            target,
            "PivotTable1",
            rowFieldIndexes: [0],
            dataFieldIndexes: [1]);

        command.Apply(ctx).Success.Should().BeTrue();

        var pivot = sheet.PivotTables.Should().ContainSingle().Subject;
        var renderedRange = pivot.LastRenderedRange;
        // Rendered range should extend beyond the initial target.
        renderedRange.Should().NotBeNull();

        command.Revert(ctx);

        sheet.PivotTables.Should().BeEmpty();
        workbook.PivotCaches.Should().BeEmpty();

        // All cells within the rendered range must be cleared.
        if (renderedRange is not null)
        {
            for (var row = renderedRange.Value.Start.Row; row <= renderedRange.Value.End.Row; row++)
            for (var col = renderedRange.Value.Start.Col; col <= renderedRange.Value.End.Col; col++)
                sheet.GetCell(row, col)
                    .Should().BeNull($"cell ({row},{col}) must be cleared after undo");
        }
    }

    // ── R2: MovePivotTableCommand — no orphan cells when rendered > TargetRange ─

    [Fact]
    public void MovePivotTableCommand_Apply_ClearsOldRenderedRangeNotJustTargetRange()
    {
        // Build a pivot that renders beyond its TargetRange.
        var workbook = new Workbook("MovePivotClearRenderedApplyTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        var ctx = new TestCommandContext(workbook);

        // Let AddPivotTableCommand create the pivot so LastRenderedRange is set.
        var source = Range(sheet, "A1", "B3");
        var target = Range(sheet, "D3", "E3");
        var addCmd = new AddPivotTableCommand(
            sheet.Id, source, target, "PivotTable1",
            rowFieldIndexes: [0], dataFieldIndexes: [1]);
        addCmd.Apply(ctx).Success.Should().BeTrue();

        var pivot = sheet.PivotTables.Should().ContainSingle().Subject;
        var oldRendered = pivot.LastRenderedRange;
        oldRendered.Should().NotBeNull("pivot must have a rendered range after refresh");

        // Now move it somewhere else.
        var moveCommand = new MovePivotTableCommand(sheet.Id, "PivotTable1", Addr(sheet, "H10"));
        moveCommand.Apply(ctx).Success.Should().BeTrue();

        // Old rendered range cells must all be cleared.
        if (oldRendered is not null)
        {
            for (var row = oldRendered.Value.Start.Row; row <= oldRendered.Value.End.Row; row++)
            for (var col = oldRendered.Value.Start.Col; col <= oldRendered.Value.End.Col; col++)
                sheet.GetCell(row, col)
                    .Should().BeNull($"orphan cell ({row},{col}) at old location after move");
        }
    }

    [Fact]
    public void MovePivotTableCommand_Revert_ClearsNewLocationRenderedRange()
    {
        var workbook = new Workbook("MovePivotClearRenderedRevertTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        var ctx = new TestCommandContext(workbook);

        var source = Range(sheet, "A1", "B3");
        var target = Range(sheet, "D3", "E3");
        var addCmd = new AddPivotTableCommand(
            sheet.Id, source, target, "PivotTable1",
            rowFieldIndexes: [0], dataFieldIndexes: [1]);
        addCmd.Apply(ctx).Success.Should().BeTrue();

        var pivot = sheet.PivotTables.Should().ContainSingle().Subject;

        var moveCommand = new MovePivotTableCommand(sheet.Id, "PivotTable1", Addr(sheet, "H10"));
        moveCommand.Apply(ctx).Success.Should().BeTrue();

        var newRendered = pivot.LastRenderedRange;
        newRendered.Should().NotBeNull("pivot must have a rendered range after move");

        moveCommand.Revert(ctx);

        // New location cells must be cleared after undo.
        if (newRendered is not null)
        {
            for (var row = newRendered.Value.Start.Row; row <= newRendered.Value.End.Row; row++)
            for (var col = newRendered.Value.Start.Col; col <= newRendered.Value.End.Col; col++)
                sheet.GetCell(row, col)
                    .Should().BeNull($"orphan cell ({row},{col}) at new location after undo");
        }

        // Old location must be restored.
        pivot.TargetRange.Start.ToA1().Should().Be("D3");
    }

    // ── sweep92-F1: MovePivotTableCommand.Revert must restore merged row labels ─

    [Fact]
    public void MovePivotTableCommand_Revert_RestoresMergedRowLabelsAtOldLocation()
    {
        // Pivot with >=2 row fields and Merge and Center Cells With Labels turned on renders
        // merged label cells (PivotTableRefreshService.MergedLabels.cs). Apply's clear step
        // (ClearRange) strips every merged region overlapping the old footprint; Revert must put
        // them back, not just the cell text/values.
        var workbook = new Workbook("MovePivotMergedLabelsUndoTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Region"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Product"));
        sheet.SetCell(Addr(sheet, "C1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B2"), new TextValue("Widgets"));
        sheet.SetCell(Addr(sheet, "C2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B3"), new TextValue("Gadgets"));
        sheet.SetCell(Addr(sheet, "C3"), new NumberValue(20));
        sheet.SetCell(Addr(sheet, "A4"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "B4"), new TextValue("Widgets"));
        sheet.SetCell(Addr(sheet, "C4"), new NumberValue(5));
        var ctx = new TestCommandContext(workbook);

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C4"),
            TargetRange = Range(sheet, "E3", "G10"),
            ReportLayout = PivotReportLayout.Tabular,
            MergeAndCenterLabels = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var mergedBefore = sheet.MergedRegions.ToList();
        mergedBefore.Should().NotBeEmpty("the repeated 'East' region label should merge across its two rows");

        var moveCommand = new MovePivotTableCommand(sheet.Id, "PivotTable1", Addr(sheet, "K10"));
        moveCommand.Apply(ctx).Success.Should().BeTrue();

        // Apply's own re-render at the new location must reproduce the same merge shape.
        sheet.MergedRegions.Should().NotBeEmpty("the moved pivot must re-render its merged labels at the new location");

        moveCommand.Revert(ctx);

        pivot.TargetRange.Start.ToA1().Should().Be("E3");
        sheet.MergedRegions.Should().BeEquivalentTo(mergedBefore,
            "undo must restore the merged row-label cells the move's clear step removed, not just the cell text");
    }

    [Fact]
    public void MovePivotTableCommand_Revert_LeavesUnrelatedMergedRegionsUntouched()
    {
        // Sibling/no-regression case: a merged region that sits entirely outside the pivot's old
        // and new footprints (a manual merge the user made elsewhere on the sheet) must survive a
        // Move + Undo cycle unchanged -- the fix must not blindly re-add or reshuffle every merge
        // on the sheet, only the ones the move's own clear step actually removed.
        var workbook = new Workbook("MovePivotUnrelatedMergeUntouchedTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        sheet.SetCell(Addr(sheet, "Z1"), new TextValue("Unrelated"));
        var unrelatedMerge = Range(sheet, "Z1", "AA1");
        sheet.AddMergedRegion(unrelatedMerge);
        var ctx = new TestCommandContext(workbook);

        var source = Range(sheet, "A1", "B3");
        var target = Range(sheet, "D3", "E3");
        var addCmd = new AddPivotTableCommand(
            sheet.Id, source, target, "PivotTable1",
            rowFieldIndexes: [0], dataFieldIndexes: [1]);
        addCmd.Apply(ctx).Success.Should().BeTrue();

        var moveCommand = new MovePivotTableCommand(sheet.Id, "PivotTable1", Addr(sheet, "H10"));
        moveCommand.Apply(ctx).Success.Should().BeTrue();
        sheet.MergedRegions.Should().Contain(unrelatedMerge, "the move must never touch a merge outside its own footprint");

        moveCommand.Revert(ctx);
        sheet.MergedRegions.Should().Contain(unrelatedMerge, "undo must never touch a merge outside its own footprint either");
        sheet.MergedRegions.Count(region => region == unrelatedMerge).Should().Be(1, "the unrelated merge must not be duplicated by the restore step");
    }

    // ── R4: ConfigurePivotTableLayoutCommand.Revert must update chart bindings ─

    [Fact]
    public void ConfigurePivotTableLayoutCommand_Revert_UpdatesBoundPivotChartDataRange()
    {
        var workbook = new Workbook("PivotLayoutRevertChartSyncTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Category"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Quarter"));
        sheet.SetCell(Addr(sheet, "C1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("A"));
        sheet.SetCell(Addr(sheet, "B2"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("B"));
        sheet.SetCell(Addr(sheet, "B3"), new TextValue("Q2"));
        sheet.SetCell(Addr(sheet, "C3"), new NumberValue(20));
        var ctx = new TestCommandContext(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C3"),
            TargetRange = Range(sheet, "E3", "H8")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var originalDataRange = PivotTableRefreshService.GetMaterializedOutputRange(sheet, pivot);
        sheet.Charts.Add(new ChartModel
        {
            IsPivotChart = true,
            PivotTableName = "PivotTable1",
            PivotCacheId = 1,
            DataRange = originalDataRange
        });

        // Change layout: add a second row field, which expands the output.
        var command = new ConfigurePivotTableLayoutCommand(
            sheet.Id,
            "PivotTable1",
            rowFields: [new PivotFieldModel(0), new PivotFieldModel(1)],
            columnFields: [],
            pageFields: [],
            dataFields: [new PivotDataFieldModel(2, "Sum of Amount", "sum")]);

        command.Apply(ctx).Success.Should().BeTrue();

        var postApplyDataRange = sheet.Charts[0].DataRange;
        postApplyDataRange.Should().NotBe(originalDataRange,
            "Apply must update chart data range to new output range");

        command.Revert(ctx);

        // After undo the chart data range must revert to the original output range.
        sheet.Charts[0].DataRange.Should().Be(originalDataRange,
            "Revert must restore chart data range to the pre-Apply output range");
    }
}
