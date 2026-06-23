using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class PivotRowLabelAdornmentPlannerTests
{
    [Fact]
    public void BuildAdornments_MarksOnlyParentCompactGroupedRowsExpandable()
    {
        var workbook = new Workbook("PivotRowLabelAdornmentPlannerTest");
        var source = workbook.AddSheet("SalesData");
        var sheet = workbook.AddSheet("Pivot");
        var childStyle = workbook.RegisterStyle(new CellStyle { IndentLevel = 1 });
        var pivot = new PivotTableModel
        {
            Name = "NativePivotDateGrouping",
            SourceRange = new GridRange(new CellAddress(source.Id, 1, 1), new CellAddress(source.Id, 13, 7)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 3, 1), new CellAddress(sheet.Id, 9, 2)),
            ReportLayout = PivotReportLayout.Compact,
            FirstDataRow = 1,
            ShowExpandCollapseButtons = true
        };
        pivot.RowFields.Add(new PivotFieldModel(8, Grouping: PivotFieldGrouping.Year));
        pivot.RowFields.Add(new PivotFieldModel(7, Grouping: PivotFieldGrouping.Month));
        pivot.DataFields.Add(new PivotDataFieldModel(6, "Sum of Sales", "sum"));
        sheet.PivotTables.Add(pivot);
        SetText(sheet, 4, 1, "2026");
        SetText(sheet, 5, 1, "Jan", childStyle);
        SetText(sheet, 6, 1, "Feb", childStyle);
        SetText(sheet, 7, 1, "Mar", childStyle);
        SetText(sheet, 8, 1, "Apr", childStyle);
        SetText(sheet, 9, 1, "Grand Total");

        var adornments = PivotRowLabelAdornmentPlanner.BuildAdornments(workbook, sheet);

        adornments.Should().Equal(
            new FreeX.App.UI.PivotRowLabelAdornment(
                new CellAddress(sheet.Id, 4, 1),
                IndentLevel: 0,
                ShowExpandCollapseButton: true,
                IsExpanded: true),
            new FreeX.App.UI.PivotRowLabelAdornment(
                new CellAddress(sheet.Id, 5, 1),
                IndentLevel: 1,
                ShowExpandCollapseButton: false,
                IsExpanded: true),
            new FreeX.App.UI.PivotRowLabelAdornment(
                new CellAddress(sheet.Id, 6, 1),
                IndentLevel: 1,
                ShowExpandCollapseButton: false,
                IsExpanded: true),
            new FreeX.App.UI.PivotRowLabelAdornment(
                new CellAddress(sheet.Id, 7, 1),
                IndentLevel: 1,
                ShowExpandCollapseButton: false,
                IsExpanded: true),
            new FreeX.App.UI.PivotRowLabelAdornment(
                new CellAddress(sheet.Id, 8, 1),
                IndentLevel: 1,
                ShowExpandCollapseButton: false,
                IsExpanded: true));
    }

    [Fact]
    public void BuildAdornments_HonorsHiddenExpandCollapseButtons()
    {
        var workbook = new Workbook("PivotRowLabelAdornmentHiddenButtonsTest");
        var source = workbook.AddSheet("SalesData");
        var sheet = workbook.AddSheet("Pivot");
        var childStyle = workbook.RegisterStyle(new CellStyle { IndentLevel = 1 });
        var pivot = new PivotTableModel
        {
            SourceRange = new GridRange(new CellAddress(source.Id, 1, 1), new CellAddress(source.Id, 13, 7)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 3, 1), new CellAddress(sheet.Id, 6, 2)),
            ReportLayout = PivotReportLayout.Compact,
            FirstDataRow = 1,
            ShowExpandCollapseButtons = false
        };
        pivot.RowFields.Add(new PivotFieldModel(8, Grouping: PivotFieldGrouping.Year));
        pivot.RowFields.Add(new PivotFieldModel(7, Grouping: PivotFieldGrouping.Month));
        sheet.PivotTables.Add(pivot);
        SetText(sheet, 4, 1, "2026");
        SetText(sheet, 5, 1, "Jan", childStyle);

        PivotRowLabelAdornmentPlanner.BuildAdornments(workbook, sheet).Should().BeEmpty();
    }

    [Fact]
    public void BuildAdornments_ReservesRepeatedParentLabelPaddingInTabularLayout()
    {
        var workbook = new Workbook("PivotRowLabelAdornmentTabularPlannerTest");
        var source = workbook.AddSheet("SalesData");
        var sheet = workbook.AddSheet("Pivot");
        var pivot = new PivotTableModel
        {
            Name = "NativePivotLayoutOptions",
            SourceRange = new GridRange(new CellAddress(source.Id, 1, 1), new CellAddress(source.Id, 13, 7)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 3, 1), new CellAddress(sheet.Id, 9, 4)),
            ReportLayout = PivotReportLayout.Tabular,
            FirstDataRow = 2,
            ShowExpandCollapseButtons = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(6, "Sum of Sales", "sum"));
        sheet.PivotTables.Add(pivot);
        SetText(sheet, 5, 1, "East");
        SetText(sheet, 5, 2, "Direct");
        SetText(sheet, 6, 1, "East");
        SetText(sheet, 6, 2, "Partner");
        SetText(sheet, 7, 1, "West");
        SetText(sheet, 7, 2, "Direct");
        SetText(sheet, 8, 1, "West");
        SetText(sheet, 8, 2, "Partner");
        SetText(sheet, 9, 1, "Grand Total");

        var adornments = PivotRowLabelAdornmentPlanner.BuildAdornments(workbook, sheet);

        adornments.Should().Equal(
            new FreeX.App.UI.PivotRowLabelAdornment(
                new CellAddress(sheet.Id, 5, 1),
                IndentLevel: 0,
                ShowExpandCollapseButton: true,
                IsExpanded: true),
            new FreeX.App.UI.PivotRowLabelAdornment(
                new CellAddress(sheet.Id, 6, 1),
                IndentLevel: 0,
                ShowExpandCollapseButton: false,
                IsExpanded: true),
            new FreeX.App.UI.PivotRowLabelAdornment(
                new CellAddress(sheet.Id, 7, 1),
                IndentLevel: 0,
                ShowExpandCollapseButton: true,
                IsExpanded: true),
            new FreeX.App.UI.PivotRowLabelAdornment(
                new CellAddress(sheet.Id, 8, 1),
                IndentLevel: 0,
                ShowExpandCollapseButton: false,
                IsExpanded: true));
    }

    [Fact]
    public void BuildAdornments_MarksOutlineParentRowsWithBlankContinuationChildrenExpandable()
    {
        var workbook = new Workbook("PivotRowLabelAdornmentOutlinePlannerTest");
        var source = workbook.AddSheet("SalesData");
        var sheet = workbook.AddSheet("Pivot");
        var pivot = new PivotTableModel
        {
            Name = "NativePivotSubtotalGrandTotals",
            SourceRange = new GridRange(new CellAddress(source.Id, 1, 1), new CellAddress(source.Id, 13, 7)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 3, 1), new CellAddress(sheet.Id, 11, 5)),
            ReportLayout = PivotReportLayout.Outline,
            FirstDataRow = 2,
            ShowExpandCollapseButtons = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(6, "Sum of Sales", "sum"));
        sheet.PivotTables.Add(pivot);
        SetText(sheet, 5, 1, "East");
        SetText(sheet, 6, 2, "Direct");
        SetText(sheet, 7, 2, "Partner");
        SetText(sheet, 8, 1, "East Total");
        SetText(sheet, 9, 1, "North");
        SetText(sheet, 10, 2, "Direct");
        SetText(sheet, 11, 1, "Grand Total");

        var adornments = PivotRowLabelAdornmentPlanner.BuildAdornments(workbook, sheet);

        adornments.Should().Equal(
            new FreeX.App.UI.PivotRowLabelAdornment(
                new CellAddress(sheet.Id, 5, 1),
                IndentLevel: 0,
                ShowExpandCollapseButton: true,
                IsExpanded: true),
            new FreeX.App.UI.PivotRowLabelAdornment(
                new CellAddress(sheet.Id, 9, 1),
                IndentLevel: 0,
                ShowExpandCollapseButton: true,
                IsExpanded: true));
    }

    [Fact]
    public void BuildAdornments_UsesLastRenderedRangeForLoadedNativeOutlinePivots()
    {
        var workbook = new Workbook("PivotRowLabelAdornmentLoadedNativeRangeTest");
        var source = workbook.AddSheet("SalesData");
        var sheet = workbook.AddSheet("Pivot");
        var pivot = new PivotTableModel
        {
            Name = "NativePivotSubtotalGrandTotals",
            SourceRange = new GridRange(new CellAddress(source.Id, 1, 1), new CellAddress(source.Id, 13, 7)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 3, 1), new CellAddress(sheet.Id, 3, 1)),
            LastRenderedRange = new GridRange(new CellAddress(sheet.Id, 3, 1), new CellAddress(sheet.Id, 11, 5)),
            ReportLayout = PivotReportLayout.Outline,
            FirstDataRow = 2,
            ShowExpandCollapseButtons = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(6, "Sum of Sales", "sum"));
        sheet.PivotTables.Add(pivot);
        SetText(sheet, 5, 1, "East");
        SetText(sheet, 6, 2, "Direct");
        SetText(sheet, 7, 2, "Partner");
        SetText(sheet, 8, 1, "East Total");
        SetText(sheet, 9, 1, "North");
        SetText(sheet, 10, 2, "Direct");
        SetText(sheet, 11, 1, "Grand Total");

        var adornments = PivotRowLabelAdornmentPlanner.BuildAdornments(workbook, sheet);

        adornments.Should().Equal(
            new FreeX.App.UI.PivotRowLabelAdornment(
                new CellAddress(sheet.Id, 5, 1),
                IndentLevel: 0,
                ShowExpandCollapseButton: true,
                IsExpanded: true),
            new FreeX.App.UI.PivotRowLabelAdornment(
                new CellAddress(sheet.Id, 9, 1),
                IndentLevel: 0,
                ShowExpandCollapseButton: true,
                IsExpanded: true));
    }

    private static void SetText(Sheet sheet, uint row, uint col, string text, StyleId? styleId = null)
    {
        var cell = Cell.FromValue(new TextValue(text));
        if (styleId is { } id)
            cell.StyleId = id;
        sheet.SetCell(new CellAddress(sheet.Id, row, col), cell);
    }
}
