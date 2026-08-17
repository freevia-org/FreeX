using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R140-remediation-pivot-refresh-growth-guard-completeness: the R140 fix wave added the
/// growth-conflict guard (R140_PivotRefreshGrowthOverwriteGuardTests) to ONLY
/// <see cref="RefreshPivotTableCommand"/>. This class drives EVERY OTHER call site of
/// <see cref="PivotTableRefreshService.Refresh"/> through its real public entry point -- the same
/// command a user/ribbon/dialog reaches -- and proves each one now refuses a refresh whose growth
/// would land on unrelated pre-existing content, leaving the sheet and the pivot's own state exactly
/// as they were, instead of silently overwriting it. One test per call site, covering:
/// AddPivotTableCommand, ConfigurePivotTableLayoutCommand, ConfigurePivotTableFieldFiltersCommand,
/// ConfigurePivotTableViewCommand, ConfigurePivotTableOptionsCommand,
/// ConfigurePivotTableCalculatedItemsCommand, ChangePivotTableSourceCommand,
/// ClearPivotTableViewCommand, MovePivotTableCommand, SetSlicerSelectionCommand,
/// SetTimelineRangeCommand, AddPivotChartCommand.
/// </summary>
public sealed class R140_RemediationPivotRefreshGrowthGuardAllCallSitesTests
{
    // ── shared helpers ───────────────────────────────────────────────────────────────────────────

    private static CellAddress Addr(Sheet sheet, string a1) => CellAddress.Parse(a1, sheet.Id);

    private static GridRange Range(Sheet sheet, string start, string end) =>
        new(Addr(sheet, start), Addr(sheet, end));

    /// <summary>Category/Amount source with 3 distinct categories (A, B, C) -- a 3rd category renders one extra row.</summary>
    private static void SeedThreeCategoryData(Sheet sheet)
    {
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Category"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("A"));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("B"));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(20));
        sheet.SetCell(Addr(sheet, "A4"), new TextValue("C"));
        sheet.SetCell(Addr(sheet, "B4"), new NumberValue(30));
    }

    private static PivotTableModel CreateTwoCategoryPivot(Sheet sheet, GridRange targetRange, IReadOnlyList<string>? selectedRowItems = null)
    {
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B4"),
            TargetRange = targetRange,
            ReportLayout = PivotReportLayout.Tabular
        };
        pivot.RowFields.Add(new PivotFieldModel(0, SelectedItems: selectedRowItems));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        return pivot;
    }

    // ── AddPivotTableCommand ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void AddPivotTableCommand_TargetRangeTooSmallForRender_RefusesAndLeavesNeighbourUntouched()
    {
        var workbook = new Workbook("AddPivotGrowthGuardTest");
        var sheet = workbook.AddSheet("Data");
        SeedThreeCategoryData(sheet);
        var ctx = new TestCommandContext(workbook);

        // 2 categories in the source range picked (A1:B3 -- excludes the seeded "C" row), so the
        // natural render needs 4 rows (header, A, B, Grand Total) = D3:E6, but the user only drew a
        // 2-row-tall target range -- exactly the common real-world mistake of an under-sized initial
        // drag.
        var targetRange = Range(sheet, "D3", "E4");
        var noteAddress = Addr(sheet, "D5");
        sheet.SetCell(noteAddress, new TextValue("Notes: keep"));

        var command = new AddPivotTableCommand(
            sheet.Id,
            Range(sheet, "A1", "B3"),
            targetRange,
            "PivotTable1",
            rowFieldIndexes: [0],
            dataFieldIndexes: [1]);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("overwrite");
        sheet.GetCell(noteAddress)!.Value.Should().Be(new TextValue("Notes: keep"));
        sheet.PivotTables.Should().BeEmpty();
        workbook.PivotCaches.Should().BeEmpty();

        command.Revert(ctx);
        sheet.GetCell(noteAddress)!.Value.Should().Be(new TextValue("Notes: keep"));
    }

    // ── ConfigurePivotTableLayoutCommand ─────────────────────────────────────────────────────────

    [Fact]
    public void ConfigurePivotTableLayoutCommand_FieldChangeRevealsHiddenCategory_RefusesAndRestoresFilter()
    {
        var workbook = new Workbook("LayoutGrowthGuardTest");
        var sheet = workbook.AddSheet("Data");
        SeedThreeCategoryData(sheet);
        var ctx = new TestCommandContext(workbook);

        // Starts filtered to A/B only (dragging a field into Rows with only 2 items initially picked
        // is the R140 gap's "most common real-world" scenario) -- footprint D3:E6.
        var pivot = CreateTwoCategoryPivot(sheet, Range(sheet, "D3", "F6"), selectedRowItems: ["A", "B"]);
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);
        pivot.LastRenderedRange.Should().Be(Range(sheet, "D3", "E6"));

        var noteAddress = Addr(sheet, "D7");
        sheet.SetCell(noteAddress, new TextValue("Notes: verify Q3"));

        // The user now drags Category back into Rows unfiltered (all 3 categories) -- exactly what
        // the Field List drag-and-drop UI does.
        var command = new ConfigurePivotTableLayoutCommand(
            sheet.Id,
            "PivotTable1",
            rowFields: [new PivotFieldModel(0)],
            columnFields: [],
            pageFields: [],
            dataFields: [new PivotDataFieldModel(1, "Sum of Amount", "sum")]);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("overwrite");
        sheet.GetCell(noteAddress)!.Value.Should().Be(new TextValue("Notes: verify Q3"));
        pivot.LastRenderedRange.Should().Be(Range(sheet, "D3", "E6"));
        pivot.RowFields.Single().SelectedItems.Should().BeEquivalentTo(["A", "B"]);
        sheet.GetCell(Addr(sheet, "D6"))!.Value.Should().Be(new TextValue("Grand Total"));

        command.Revert(ctx);
        sheet.GetCell(noteAddress)!.Value.Should().Be(new TextValue("Notes: verify Q3"));
    }

    // ── ConfigurePivotTableFieldFiltersCommand ───────────────────────────────────────────────────

    [Fact]
    public void ConfigurePivotTableFieldFiltersCommand_ClearingSelectionRevealsHiddenCategory_RefusesAndRestoresFilter()
    {
        var workbook = new Workbook("FieldFiltersGrowthGuardTest");
        var sheet = workbook.AddSheet("Data");
        SeedThreeCategoryData(sheet);
        var ctx = new TestCommandContext(workbook);

        var pivot = CreateTwoCategoryPivot(sheet, Range(sheet, "D3", "F6"), selectedRowItems: ["A", "B"]);
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);
        pivot.LastRenderedRange.Should().Be(Range(sheet, "D3", "E6"));

        var noteAddress = Addr(sheet, "D7");
        sheet.SetCell(noteAddress, new TextValue("Notes: verify Q3"));

        var command = new ConfigurePivotTableFieldFiltersCommand(
            sheet.Id,
            "PivotTable1",
            rowFields: [new PivotFieldModel(0)],
            columnFields: [],
            pageFields: [],
            labelFilters: [],
            valueFilters: [],
            sorts: []);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("overwrite");
        sheet.GetCell(noteAddress)!.Value.Should().Be(new TextValue("Notes: verify Q3"));
        pivot.RowFields.Single().SelectedItems.Should().BeEquivalentTo(["A", "B"]);

        command.Revert(ctx);
        sheet.GetCell(noteAddress)!.Value.Should().Be(new TextValue("Notes: verify Q3"));
    }

    // ── ConfigurePivotTableViewCommand ───────────────────────────────────────────────────────────

    [Fact]
    public void ConfigurePivotTableViewCommand_ClearingLabelFilterRevealsHiddenCategory_RefusesAndRestoresFilter()
    {
        var workbook = new Workbook("ViewGrowthGuardTest");
        var sheet = workbook.AddSheet("Data");
        SeedThreeCategoryData(sheet);
        var ctx = new TestCommandContext(workbook);

        // Unfiltered RowFields, but a label filter excludes "C" -- ConfigurePivotTableViewCommand
        // only ever touches LabelFilters/ValueFilters/Sorts, never RowFields directly.
        var pivot = CreateTwoCategoryPivot(sheet, Range(sheet, "D3", "F6"));
        pivot.LabelFilters.Add(new PivotLabelFilterModel(0, PivotLabelFilterKind.DoesNotEqual, "C"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);
        pivot.LastRenderedRange.Should().Be(Range(sheet, "D3", "E6"));

        var noteAddress = Addr(sheet, "D7");
        sheet.SetCell(noteAddress, new TextValue("Notes: verify Q3"));

        var command = new ConfigurePivotTableViewCommand(
            sheet.Id,
            "PivotTable1",
            labelFilters: [],
            valueFilters: [],
            sorts: []);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("overwrite");
        sheet.GetCell(noteAddress)!.Value.Should().Be(new TextValue("Notes: verify Q3"));
        pivot.LabelFilters.Should().ContainSingle();

        command.Revert(ctx);
        sheet.GetCell(noteAddress)!.Value.Should().Be(new TextValue("Notes: verify Q3"));
    }

    // ── ConfigurePivotTableOptionsCommand ────────────────────────────────────────────────────────

    [Fact]
    public void ConfigurePivotTableOptionsCommand_EnablingGrandTotalsAddsRow_RefusesAndRestoresOption()
    {
        var workbook = new Workbook("OptionsGrowthGuardTest");
        var sheet = workbook.AddSheet("Data");
        SeedThreeCategoryData(sheet);
        var ctx = new TestCommandContext(workbook);

        // Grand totals OFF up front (bypassing the command, mirroring a file loaded with that option
        // already set) -- footprint is just header + A + B + C = 3 rows, no trailing Grand Total row.
        var pivot = CreateTwoCategoryPivot(sheet, Range(sheet, "D3", "F6"));
        pivot.ShowRowGrandTotals = false;
        pivot.ShowColumnGrandTotals = false;
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);
        pivot.LastRenderedRange.Should().Be(Range(sheet, "D3", "E6"));
        sheet.GetCell(Addr(sheet, "D6"))!.Value.Should().Be(new TextValue("C"));

        var noteAddress = Addr(sheet, "D7");
        sheet.SetCell(noteAddress, new TextValue("Notes: verify Q3"));

        var command = new ConfigurePivotTableOptionsCommand(
            sheet.Id,
            "PivotTable1",
            showRowGrandTotals: true,
            showColumnGrandTotals: true,
            showSubtotals: true,
            PivotSubtotalPlacement.Bottom,
            repeatItemLabels: false,
            blankLineAfterItems: false,
            styleName: "PivotStyleMedium9");
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("overwrite");
        sheet.GetCell(noteAddress)!.Value.Should().Be(new TextValue("Notes: verify Q3"));
        pivot.ShowRowGrandTotals.Should().BeFalse();
        sheet.GetCell(Addr(sheet, "D6"))!.Value.Should().Be(new TextValue("C"));

        command.Revert(ctx);
        sheet.GetCell(noteAddress)!.Value.Should().Be(new TextValue("Notes: verify Q3"));
    }

    // ── ConfigurePivotTableCalculatedItemsCommand ────────────────────────────────────────────────

    [Fact]
    public void ConfigurePivotTableCalculatedItemsCommand_FieldChangeRevealsHiddenCategory_RefusesAndRestoresFilter()
    {
        var workbook = new Workbook("CalculatedItemsGrowthGuardTest");
        var sheet = workbook.AddSheet("Data");
        SeedThreeCategoryData(sheet);
        var ctx = new TestCommandContext(workbook);

        var pivot = CreateTwoCategoryPivot(sheet, Range(sheet, "D3", "F6"), selectedRowItems: ["A", "B"]);
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);
        pivot.LastRenderedRange.Should().Be(Range(sheet, "D3", "E6"));

        var noteAddress = Addr(sheet, "D7");
        sheet.SetCell(noteAddress, new TextValue("Notes: verify Q3"));

        var command = new ConfigurePivotTableCalculatedItemsCommand(
            sheet.Id,
            "PivotTable1",
            rowFields: [new PivotFieldModel(0)],
            columnFields: [],
            pageFields: [],
            calculatedFields: [],
            calculatedItems: []);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("overwrite");
        sheet.GetCell(noteAddress)!.Value.Should().Be(new TextValue("Notes: verify Q3"));
        pivot.RowFields.Single().SelectedItems.Should().BeEquivalentTo(["A", "B"]);

        command.Revert(ctx);
        sheet.GetCell(noteAddress)!.Value.Should().Be(new TextValue("Notes: verify Q3"));
    }

    // ── ChangePivotTableSourceCommand ────────────────────────────────────────────────────────────

    [Fact]
    public void ChangePivotTableSourceCommand_WiderSourceRevealsNewCategory_RefusesAndRestoresSource()
    {
        var workbook = new Workbook("ChangeSourceGrowthGuardTest");
        var sheet = workbook.AddSheet("Data");
        SeedThreeCategoryData(sheet);
        var ctx = new TestCommandContext(workbook);

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B3"), // excludes the seeded "C" row
            TargetRange = Range(sheet, "D3", "F6"),
            ReportLayout = PivotReportLayout.Tabular
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        var cache = new PivotCacheModel { CacheId = 1, SourceType = PivotCacheSourceType.WorksheetRange, SourceSheetName = sheet.Name, SourceReference = pivot.SourceRange.ToString() };
        workbook.PivotCaches.Add(cache);
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);
        pivot.LastRenderedRange.Should().Be(Range(sheet, "D3", "E6"));

        var noteAddress = Addr(sheet, "D7");
        sheet.SetCell(noteAddress, new TextValue("Notes: verify Q3"));

        var command = new ChangePivotTableSourceCommand(sheet.Id, "PivotTable1", Range(sheet, "A1", "B4"));
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("overwrite");
        sheet.GetCell(noteAddress)!.Value.Should().Be(new TextValue("Notes: verify Q3"));
        pivot.SourceRange.Should().Be(Range(sheet, "A1", "B3"));
        pivot.LastRenderedRange.Should().Be(Range(sheet, "D3", "E6"));
        sheet.GetCell(Addr(sheet, "D6"))!.Value.Should().Be(new TextValue("Grand Total"));

        command.Revert(ctx);
        sheet.GetCell(noteAddress)!.Value.Should().Be(new TextValue("Notes: verify Q3"));
    }

    // ── ClearPivotTableViewCommand ───────────────────────────────────────────────────────────────

    [Fact]
    public void ClearPivotTableViewCommand_ClearingSelectionRevealsHiddenCategory_RefusesAndRestoresFilter()
    {
        var workbook = new Workbook("ClearViewGrowthGuardTest");
        var sheet = workbook.AddSheet("Data");
        SeedThreeCategoryData(sheet);
        var ctx = new TestCommandContext(workbook);

        var pivot = CreateTwoCategoryPivot(sheet, Range(sheet, "D3", "F6"), selectedRowItems: ["A", "B"]);
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);
        pivot.LastRenderedRange.Should().Be(Range(sheet, "D3", "E6"));

        var noteAddress = Addr(sheet, "D7");
        sheet.SetCell(noteAddress, new TextValue("Notes: verify Q3"));

        var command = new ClearPivotTableViewCommand(sheet.Id, "PivotTable1");
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("overwrite");
        sheet.GetCell(noteAddress)!.Value.Should().Be(new TextValue("Notes: verify Q3"));
        pivot.RowFields.Single().SelectedItems.Should().BeEquivalentTo(["A", "B"]);

        command.Revert(ctx);
        sheet.GetCell(noteAddress)!.Value.Should().Be(new TextValue("Notes: verify Q3"));
    }

    // ── MovePivotTableCommand ────────────────────────────────────────────────────────────────────

    [Fact]
    public void MovePivotTableCommand_DestinationAlreadyOccupied_RefusesAndLeavesBothLocationsUntouched()
    {
        var workbook = new Workbook("MoveGrowthGuardTest");
        var sheet = workbook.AddSheet("Data");
        SeedThreeCategoryData(sheet);
        var ctx = new TestCommandContext(workbook);

        var pivot = CreateTwoCategoryPivot(sheet, Range(sheet, "D3", "F6"), selectedRowItems: ["A", "B"]);
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);
        pivot.LastRenderedRange.Should().Be(Range(sheet, "D3", "E6"));

        // Unrelated content sitting exactly where the move would land.
        var destinationNote = Addr(sheet, "H10");
        sheet.SetCell(destinationNote, new TextValue("Notes: Q4 budget"));

        var command = new MovePivotTableCommand(sheet.Id, "PivotTable1", Addr(sheet, "H10"));
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("overwrite");
        sheet.GetCell(destinationNote)!.Value.Should().Be(new TextValue("Notes: Q4 budget"));
        pivot.TargetRange.Should().Be(Range(sheet, "D3", "F6"));
        pivot.LastRenderedRange.Should().Be(Range(sheet, "D3", "E6"));
        sheet.GetCell(Addr(sheet, "D4"))!.Value.Should().Be(new TextValue("A"));
        sheet.GetCell(Addr(sheet, "D5"))!.Value.Should().Be(new TextValue("B"));

        command.Revert(ctx);
        sheet.GetCell(destinationNote)!.Value.Should().Be(new TextValue("Notes: Q4 budget"));
        sheet.GetCell(Addr(sheet, "D4"))!.Value.Should().Be(new TextValue("A"));
    }

    // ── SetSlicerSelectionCommand ────────────────────────────────────────────────────────────────

    [Fact]
    public void SetSlicerSelectionCommand_ClearingSelectionRevealsHiddenCategory_RefusesAndRestoresSelection()
    {
        var workbook = new Workbook("SlicerGrowthGuardTest");
        var sheet = workbook.AddSheet("Data");
        SeedThreeCategoryData(sheet);
        var ctx = new TestCommandContext(workbook);

        var pivot = CreateTwoCategoryPivot(sheet, Range(sheet, "D3", "F6"), selectedRowItems: ["A", "B"]);
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);
        pivot.LastRenderedRange.Should().Be(Range(sheet, "D3", "E6"));

        workbook.Slicers.Add(new SlicerModel
        {
            Name = "Category Slicer",
            CacheName = "Slicer_Category",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Category",
            SelectedItems = { "A", "B" },
            SelectionCaptured = true
        });

        var noteAddress = Addr(sheet, "D7");
        sheet.SetCell(noteAddress, new TextValue("Notes: verify Q3"));

        // Clearing the slicer's filter (Excel's "Clear Filter" button) selects every item again.
        var command = new SetSlicerSelectionCommand("Category Slicer", []);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("overwrite");
        sheet.GetCell(noteAddress)!.Value.Should().Be(new TextValue("Notes: verify Q3"));
        var slicer = workbook.Slicers.Single();
        slicer.SelectedItems.Should().BeEquivalentTo(["A", "B"]);
        pivot.RowFields.Single().SelectedItems.Should().BeEquivalentTo(["A", "B"]);

        command.Revert(ctx);
        sheet.GetCell(noteAddress)!.Value.Should().Be(new TextValue("Notes: verify Q3"));
    }

    // ── SetTimelineRangeCommand ──────────────────────────────────────────────────────────────────

    [Fact]
    public void SetTimelineRangeCommand_WideningRangeRevealsHiddenCategory_RefusesAndRestoresRange()
    {
        var workbook = new Workbook("TimelineGrowthGuardTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Category"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Date"));
        sheet.SetCell(Addr(sheet, "C1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("A"));
        sheet.SetCell(Addr(sheet, "B2"), DateTimeValue.FromDateTime(new DateTime(2026, 1, 5)));
        sheet.SetCell(Addr(sheet, "C2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("B"));
        sheet.SetCell(Addr(sheet, "B3"), DateTimeValue.FromDateTime(new DateTime(2026, 1, 10)));
        sheet.SetCell(Addr(sheet, "C3"), new NumberValue(20));
        // "C" only shows up in February -- outside the timeline's initial January-only range.
        sheet.SetCell(Addr(sheet, "A4"), new TextValue("C"));
        sheet.SetCell(Addr(sheet, "B4"), DateTimeValue.FromDateTime(new DateTime(2026, 2, 2)));
        sheet.SetCell(Addr(sheet, "C4"), new NumberValue(30));
        var ctx = new TestCommandContext(workbook);

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C4"),
            TargetRange = Range(sheet, "E3", "G6"),
            ReportLayout = PivotReportLayout.Tabular
        };
        // Category ("A") is the row field; Date ("B", index 1) is NOT in Row/Column/PageFields --
        // exactly the H10 "unplaced filter field" shape SetTimelineRangeCommand already handles.
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);
        pivot.LastRenderedRange.Should().Be(Range(sheet, "E3", "F7"));

        workbook.Timelines.Add(new TimelineModel
        {
            Name = "Date Timeline",
            CacheName = "Timeline_Date",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Date"
        });

        // First application narrows to January only (A, B) -- succeeds, shrinks the footprint by one
        // row, freeing up E7.
        var narrow = new SetTimelineRangeCommand("Date Timeline", "2026-01-01", "2026-01-31");
        narrow.Apply(ctx).Success.Should().BeTrue();
        pivot.LastRenderedRange.Should().Be(Range(sheet, "E3", "F6"));

        var noteAddress = Addr(sheet, "E7");
        sheet.SetCell(noteAddress, new TextValue("Notes: verify Q3"));

        // Widening back to include February brings "C" back into view -- growth into the note.
        var widen = new SetTimelineRangeCommand("Date Timeline", "2026-01-01", "2026-02-28");
        var outcome = widen.Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("overwrite");
        sheet.GetCell(noteAddress)!.Value.Should().Be(new TextValue("Notes: verify Q3"));
        var timeline = workbook.Timelines.Single();
        timeline.SelectedStartDate.Should().Be("2026-01-01");
        timeline.SelectedEndDate.Should().Be("2026-01-31");
        pivot.LastRenderedRange.Should().Be(Range(sheet, "E3", "F6"));

        widen.Revert(ctx);
        sheet.GetCell(noteAddress)!.Value.Should().Be(new TextValue("Notes: verify Q3"));
    }

    // ── AddPivotChartCommand ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void AddPivotChartCommand_RefreshOnInsertRevealsNewCategory_RefusesAndLeavesChartUnadded()
    {
        var workbook = new Workbook("PivotChartGrowthGuardTest");
        var sheet = workbook.AddSheet("Data");
        SeedThreeCategoryData(sheet);
        var ctx = new TestCommandContext(workbook);

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B3"), // excludes the seeded "C" row for now
            TargetRange = Range(sheet, "D3", "F6"),
            ReportLayout = PivotReportLayout.Tabular
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);
        pivot.LastRenderedRange.Should().Be(Range(sheet, "D3", "E6"));

        var noteAddress = Addr(sheet, "D7");
        sheet.SetCell(noteAddress, new TextValue("Notes: verify Q3"));

        // Someone widened the pivot's source (e.g. via ChangePivotTableSourceCommand) since the last
        // refresh; inserting a PivotChart triggers this command's own refresh-on-insert, which is
        // just as capable of growing into unrelated content.
        pivot.SourceRange = Range(sheet, "A1", "B4");

        var command = new AddPivotChartCommand(sheet.Id, "PivotTable1", ChartType.Column);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("overwrite");
        sheet.GetCell(noteAddress)!.Value.Should().Be(new TextValue("Notes: verify Q3"));
        sheet.Charts.Should().BeEmpty();
        pivot.LastRenderedRange.Should().Be(Range(sheet, "D3", "E6"));

        command.Revert(ctx);
        sheet.GetCell(noteAddress)!.Value.Should().Be(new TextValue("Notes: verify Q3"));
    }

    // ── sibling sanity: growth into genuinely blank space must still succeed for a non-Refresh site ──

    [Fact]
    public void ConfigurePivotTableLayoutCommand_GrowsIntoGenuinelyBlankSpace_Succeeds()
    {
        var workbook = new Workbook("LayoutGrowthGuardBlankTest");
        var sheet = workbook.AddSheet("Data");
        SeedThreeCategoryData(sheet);
        var ctx = new TestCommandContext(workbook);

        var pivot = CreateTwoCategoryPivot(sheet, Range(sheet, "D3", "F6"), selectedRowItems: ["A", "B"]);
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);
        pivot.LastRenderedRange.Should().Be(Range(sheet, "D3", "E6"));
        sheet.GetCell(Addr(sheet, "D7")).Should().BeNull();

        var command = new ConfigurePivotTableLayoutCommand(
            sheet.Id,
            "PivotTable1",
            rowFields: [new PivotFieldModel(0)],
            columnFields: [],
            pageFields: [],
            dataFields: [new PivotDataFieldModel(1, "Sum of Amount", "sum")]);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        pivot.LastRenderedRange.Should().Be(Range(sheet, "D3", "E7"));
        sheet.GetCell(Addr(sheet, "D6"))!.Value.Should().Be(new TextValue("C"));
        sheet.GetCell(Addr(sheet, "D7"))!.Value.Should().Be(new TextValue("Grand Total"));

        command.Revert(ctx);
        pivot.LastRenderedRange.Should().Be(Range(sheet, "D3", "E6"));
        sheet.GetCell(Addr(sheet, "D7")).Should().BeNull();
    }
}
