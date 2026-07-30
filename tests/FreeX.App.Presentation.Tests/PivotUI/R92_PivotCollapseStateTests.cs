using FluentAssertions;
using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PivotUI;

/// <summary>
/// R92-app-pivot-drilldown-5-1: before this round, <see cref="PivotFieldModel"/>/<see cref="PivotTableModel"/>
/// had no expand/collapse concept at all, and <see cref="PivotGridAdornmentPlanner"/> hardcoded
/// <c>IsExpanded: true</c> for every row-label adornment regardless of anything -- the drawn +/- glyph
/// was decorative. These tests exercise the real product entry point
/// (<see cref="PivotTableRefreshService.Refresh"/> materializing real pivot cells, then
/// <see cref="PivotGridAdornmentPlanner.BuildRowLabelAdornments"/> reading them) with a real
/// <see cref="PivotCollapseState"/> to prove a collapsed item's adornment now actually reports
/// collapsed.
///
/// SCOPE: this is a partial fix -- the collapse state here is session-only (not yet parsed from /
/// written to the .xlsx <c>&lt;item e="0"/&gt;</c> attribute) and nothing yet hides a collapsed
/// item's descendant rows at pivot-refresh time or wires a click handler to it. See
/// <see cref="PivotCollapseState"/>'s class doc comment for the exact remaining gaps.
/// </summary>
public sealed class R92_PivotCollapseStateTests
{
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

    private static string? LabelText(Sheet sheet, PivotRowLabelAdornment adornment) =>
        sheet.GetCell(adornment.Cell.Row, adornment.Cell.Col)?.Value is TextValue text ? text.Value : null;

    private static (Workbook Workbook, Sheet Sheet, PivotTableModel Pivot) BuildTwoLevelCompactPivot()
    {
        var workbook = new Workbook("PivotCollapseState");
        var sheet = workbook.AddSheet("Data");
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
        return (workbook, sheet, pivot);
    }

    [Fact]
    public void BuildRowLabelAdornments_CollapsedItem_ReportsIsExpandedFalse()
    {
        var (workbook, sheet, pivot) = BuildTwoLevelCompactPivot();
        var collapseState = new PivotCollapseState();
        collapseState.SetCollapsed(pivot.Name, pivot.RowFields[0].SourceFieldIndex, "East", collapsed: true);

        var adornments = PivotGridAdornmentPlanner.BuildRowLabelAdornments(workbook, sheet, collapseState);
        var buttoned = adornments.Where(a => a.ShowExpandCollapseButton).ToList();

        var eastAdornment = buttoned.Should()
            .ContainSingle(a => LabelText(sheet, a) == "East")
            .Subject;
        eastAdornment.IsExpanded.Should().BeFalse("the test explicitly collapsed the \"East\" item");

        var westAdornment = buttoned.Should()
            .ContainSingle(a => LabelText(sheet, a) == "West")
            .Subject;
        westAdornment.IsExpanded.Should().BeTrue("\"West\" was never collapsed");
    }

    [Fact]
    public void BuildRowLabelAdornments_ToggleCollapsed_FlipsBackToExpanded()
    {
        var (workbook, sheet, pivot) = BuildTwoLevelCompactPivot();
        var collapseState = new PivotCollapseState();
        var sourceFieldIndex = pivot.RowFields[0].SourceFieldIndex;
        collapseState.ToggleCollapsed(pivot.Name, sourceFieldIndex, "East");
        collapseState.IsCollapsed(pivot.Name, sourceFieldIndex, "East").Should().BeTrue();

        collapseState.ToggleCollapsed(pivot.Name, sourceFieldIndex, "East");

        collapseState.IsCollapsed(pivot.Name, sourceFieldIndex, "East").Should().BeFalse();
        var adornments = PivotGridAdornmentPlanner.BuildRowLabelAdornments(workbook, sheet, collapseState);
        adornments.Should().Contain(a =>
            a.ShowExpandCollapseButton &&
            LabelText(sheet, a) == "East" &&
            a.IsExpanded);
    }

    [Fact]
    public void BuildRowLabelAdornments_NoCollapseStateSupplied_AllAdornmentsStillReportExpanded()
    {
        // No-regression sibling: omitting the new optional parameter must reproduce the exact
        // pre-fix always-expanded behavior for every existing caller (WPF/Avalonia shells) that
        // has not been updated to own a PivotCollapseState yet.
        var (workbook, sheet, _) = BuildTwoLevelCompactPivot();

        var adornments = PivotGridAdornmentPlanner.BuildRowLabelAdornments(workbook, sheet);

        adornments.Should().NotBeEmpty();
        adornments.Where(a => a.ShowExpandCollapseButton).Should().OnlyContain(a => a.IsExpanded);
    }

    [Fact]
    public void PivotCollapseState_SetCollapsedFalse_RemovesEntry()
    {
        var state = new PivotCollapseState();
        state.SetCollapsed("PT1", 0, "East", collapsed: true);
        state.IsCollapsed("PT1", 0, "East").Should().BeTrue();

        state.SetCollapsed("PT1", 0, "East", collapsed: false);

        state.IsCollapsed("PT1", 0, "East").Should().BeFalse();
    }

    [Fact]
    public void PivotCollapseState_IsCaseInsensitiveOnItemValueButScopedPerFieldAndTable()
    {
        var state = new PivotCollapseState();
        state.SetCollapsed("PT1", 0, "East", collapsed: true);

        state.IsCollapsed("PT1", 0, "EAST").Should().BeTrue("item-value comparison matches the grid's own label comparisons");
        state.IsCollapsed("PT1", 1, "East").Should().BeFalse("collapse is scoped to its own source field");
        state.IsCollapsed("PT2", 0, "East").Should().BeFalse("collapse is scoped to its own pivot table");
    }
}
