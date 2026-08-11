using FluentAssertions;
using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PivotUI;

/// <summary>
/// Unit tests for <see cref="PivotGridAdornmentPlanner"/>: header dropdown target identification
/// and row-label expand/collapse adornment planning. Cells are baked by PivotTableRefreshService
/// (shared with the WPF planner tests) so the coordinate assertions match real pivot layout.
/// </summary>
public sealed class PivotGridAdornmentPlannerTests
{
    // ---------------------------------------------------------------------------
    // Helpers for the canonical cross-renderer pivot adornment planner.
    // ---------------------------------------------------------------------------

    private static GridRange Range(Sheet sheet, uint r1, uint c1, uint r2, uint c2) =>
        new(new CellAddress(sheet.Id, r1, c1), new CellAddress(sheet.Id, r2, c2));

    private static void SeedSalesData(Sheet sheet)
    {
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Quarter"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Amount"));
        SetRow(sheet, 2, "East", "Q1", 10);
        SetRow(sheet, 3, "East", "Q2", 15);
        SetRow(sheet, 4, "West", "Q1", 20);
        SetRow(sheet, 5, "West", "Q2", 25);
    }

    private static void SetRow(Sheet sheet, uint row, string region, string quarter, double amount)
    {
        sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue(region));
        sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue(quarter));
        sheet.SetCell(new CellAddress(sheet.Id, row, 3), new NumberValue(amount));
    }

    // ---------------------------------------------------------------------------
    // Header dropdown targets
    // ---------------------------------------------------------------------------

    [Fact]
    public void BuildHeaderTargets_ReturnsRowAndColumnDropdownsMatchingWpfPlannerOutput()
    {
        // This test verifies that PivotGridAdornmentPlanner.BuildHeaderTargets produces the SAME
        // cell addresses for a basic row+column pivot.
        var workbook = new Workbook("PivotGridAdornmentPlannerTest");
        var sheet    = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name   = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, 1, 1, 5, 3),
            TargetRange = Range(sheet, 2, 5, 8, 9),
        };
        pivot.RowFields.Add(new PivotFieldModel(0, SelectedItems: ["East"]));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        pivot.LabelFilters.Add(new PivotLabelFilterModel(1, PivotLabelFilterKind.Contains, "Q"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var targets = PivotGridAdornmentPlanner.BuildHeaderTargets(workbook, sheet);

        targets.Should().HaveCount(2);
        // Row header coordinates.
        targets.Should().Contain(t =>
            t.HeaderCell == new CellAddress(sheet.Id, 2, 5) &&
            t.IsActive &&
            t.MenuTarget.Area == PivotHeaderArea.Row &&
            t.MenuTarget.FieldCaption == "Region");
        // Column header.
        targets.Should().Contain(t =>
            t.HeaderCell == new CellAddress(sheet.Id, 2, 6) &&
            t.IsActive &&
            t.MenuTarget.Area == PivotHeaderArea.Column &&
            t.MenuTarget.FieldCaption == "Quarter");
    }

    [Fact]
    public void BuildHeaderTargets_ShowFieldHeadersFalseProducesNoTargets()
    {
        var workbook = new Workbook("PivotGridAdornmentHiddenHeadersTest");
        var sheet    = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, 1, 1, 5, 3),
            TargetRange = Range(sheet, 2, 5, 8, 9),
            ShowFieldHeaders = false,
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        PivotGridAdornmentPlanner.BuildHeaderTargets(workbook, sheet).Should().BeEmpty();
    }

    [Fact]
    public void BuildHeaderTargets_ShowDropDownsFalseSkipsField()
    {
        var workbook = new Workbook("PivotGridAdornmentDropDownFlagTest");
        var sheet    = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, 1, 1, 5, 3),
            TargetRange = Range(sheet, 2, 5, 8, 9),
            ShowFieldHeaders = true,
        };
        pivot.RowFields.Add(new PivotFieldModel(0, ShowDropDowns: false));
        pivot.ColumnFields.Add(new PivotFieldModel(1, ShowDropDowns: false));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        PivotGridAdornmentPlanner.BuildHeaderTargets(workbook, sheet).Should().BeEmpty(
            "ShowDropDowns=false on every field suppresses all dropdown targets");
    }

    [Fact]
    public void BuildHeaderTargets_CompactLayoutUsesRowLabelHeader()
    {
        var workbook = new Workbook("PivotGridAdornmentCompactTest");
        var sheet    = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, 1, 1, 5, 3),
            TargetRange = Range(sheet, 2, 5, 10, 9),
            ReportLayout = PivotReportLayout.Compact,
        };
        pivot.PageFields.Add(new PivotFieldModel(1, SelectedItem: "Q1"));
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var targets = PivotGridAdornmentPlanner.BuildHeaderTargets(workbook, sheet);

        // In compact layout, multiple row fields collapse to a single "Row Labels" header.
        targets.Should().Contain(t =>
            t.MenuTarget.Area == PivotHeaderArea.Row,
            "compact multi-level pivot should still have a row dropdown target");
        targets.Should().Contain(t =>
            t.MenuTarget.Area == PivotHeaderArea.Page,
            "page field should have a dropdown target");
    }

    [Fact]
    public void BuildHeaderTargets_EmptySheetReturnsEmpty()
    {
        var workbook = new Workbook("PivotGridAdornmentEmpty");
        var sheet    = workbook.AddSheet("Data");

        PivotGridAdornmentPlanner.BuildHeaderTargets(workbook, sheet).Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------
    // Row-label adornments
    // ---------------------------------------------------------------------------

    [Fact]
    public void BuildRowLabelAdornments_SingleRowFieldProducesNoAdornments()
    {
        var workbook = new Workbook("PivotGridAdornmentSingleRowField");
        var sheet    = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PT1",
            CacheId = 1,
            SourceRange = Range(sheet, 1, 1, 5, 3),
            TargetRange = Range(sheet, 2, 5, 8, 9),
            ReportLayout = PivotReportLayout.Compact,
            ShowExpandCollapseButtons = true,
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        PivotGridAdornmentPlanner.BuildRowLabelAdornments(workbook, sheet)
            .Should().BeEmpty("a single row field has no parent/child levels to expand");
    }

    [Fact]
    public void BuildRowLabelAdornments_EmptySheetReturnsEmpty()
    {
        var workbook = new Workbook("PivotGridAdornmentEmptyAdornment");
        var sheet    = workbook.AddSheet("Data");

        PivotGridAdornmentPlanner.BuildRowLabelAdornments(workbook, sheet).Should().BeEmpty();
    }

    [Fact]
    public void BuildRowLabelAdornments_CompactMultiLevelProducesExpandCollapseAdornments()
    {
        var workbook = new Workbook("PivotGridAdornmentMultiLevel");
        var sheet    = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PT1",
            CacheId = 1,
            SourceRange = Range(sheet, 1, 1, 5, 3),
            TargetRange = Range(sheet, 2, 5, 12, 9),
            ReportLayout = PivotReportLayout.Compact,
            ShowExpandCollapseButtons = true,
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var adornments = PivotGridAdornmentPlanner.BuildRowLabelAdornments(workbook, sheet);

        // Two-level compact layout (Region → Quarter) should produce expand/collapse buttons
        // on parent rows (Region = "East", "West") and padding reservation on child rows (Quarters).
        adornments.Should().HaveCountGreaterThan(0,
            "two-level compact pivot should produce row-label adornments");
        adornments.Should().Contain(a => a.ShowExpandCollapseButton,
            "parent-level row labels should show the collapse/expand button");
        adornments.Should().Contain(a => a.ReserveTextPadding,
            "child-level row labels should reserve text padding for the button area");
    }

    // ---------------------------------------------------------------------------
    // Regression: null-SourceFieldIndex ValueFilter must not badge unrelated fields
    // ---------------------------------------------------------------------------

    [Fact]
    public void IsFieldActive_NullSourceFieldIndexValueFilter_DoesNotBadgeUnrelatedFields()
    {
        // Regression for: IsFieldActive previously returned true for ANY field when the pivot
        // contained a ValueFilter whose SourceFieldIndex is null (i.e. the filter is malformed /
        // not yet assigned to a specific field).  That caused every field header to light up the
        // active-filter glyph even though the refresh engine ignores such filters.
        var workbook = new Workbook("NullFieldValueFilterTest");
        var sheet    = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name        = "PT1",
            CacheId     = 1,
            SourceRange = Range(sheet, 1, 1, 5, 3),
            TargetRange = Range(sheet, 2, 5, 8, 9),
        };
        // Two row fields: Region (index 0) and Quarter (index 1).
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        // Add a ValueFilter whose SourceFieldIndex is null (default) — should NOT badge any field.
        // DataFieldIndex=0 is the first data field; SourceFieldIndex is left at its default (null).
        pivot.ValueFilters.Add(new PivotValueFilterModel(DataFieldIndex: 0, Kind: PivotValueFilterKind.GreaterThan, ComparisonValue: 100));

        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var targets = PivotGridAdornmentPlanner.BuildHeaderTargets(workbook, sheet);

        targets.Should().NotBeEmpty("the pivot has two row fields so at least one dropdown target is expected");
        targets.Should().OnlyContain(t => !t.IsActive,
            "a null-SourceFieldIndex ValueFilter must not activate any field header badge");
    }
}
