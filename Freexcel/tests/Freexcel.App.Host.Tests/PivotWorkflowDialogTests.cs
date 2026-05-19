using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class PivotWorkflowDialogTests
{
    [Fact]
    public void PivotTableDialog_CreateResult_CapturesExcelCreatePivotChoices()
    {
        var result = PivotTableDialog.CreateResult(
            "  Sales!A1:D20  ",
            PivotTableDestinationKind.ExistingWorksheet,
            "  Report!F3  ",
            openFieldList: true);

        result.SourceRangeText.Should().Be("Sales!A1:D20");
        result.DestinationKind.Should().Be(PivotTableDestinationKind.ExistingWorksheet);
        result.DestinationRangeText.Should().Be("Report!F3");
        result.OpenFieldList.Should().BeTrue();
    }

    [Fact]
    public void PivotTableDialog_DefaultResult_UsesCurrentWorksheetDestinationAndFieldList()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sales");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 20, 4));

        StaTestRunner.Run(() =>
        {
            var dialog = new PivotTableDialog(workbook, sheet.Id, range);

            dialog.Result.SourceRangeText.Should().Be("Sales!A1:D20");
            dialog.Result.DestinationKind.Should().Be(PivotTableDestinationKind.ExistingWorksheet);
            dialog.Result.DestinationRangeText.Should().Be("Sales!F1");
            dialog.Result.OpenFieldList.Should().BeTrue();
        });
    }

    [Fact]
    public void PivotTableDataSourceDialog_CreateResult_TrimsSourceRangeText()
    {
        PivotTableDataSourceDialog.CreateResult("  Sales!A1:E200  ")
            .SourceRangeText
            .Should()
            .Be("Sales!A1:E200");
    }

    [Fact]
    public void InsertSlicerDialog_CreateResult_CapturesFieldAndSlicerName()
    {
        InsertSlicerDialog.CreateResult("  Region  ", "  Region Slicer  ")
            .Should()
            .Be(new InsertSlicerDialogResult("Region", "Region Slicer"));
    }

    [Fact]
    public void InsertTimelineDialog_CreateResult_CapturesDateFieldAndTimelineName()
    {
        InsertTimelineDialog.CreateResult("  Order Date  ", "  Order Date Timeline  ")
            .Should()
            .Be(new InsertTimelineDialogResult("Order Date", "Order Date Timeline"));
    }

    [Fact]
    public void PivotChartTypeDialog_PreselectsCurrentTypeAndBuildsResult()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new PivotChartTypeDialog(ChartType.Line);

            dialog.SelectedChartType.Should().Be(ChartType.Line);
            PivotChartTypeDialog.CreateResult(ChartType.StackedColumn)
                .Should()
                .Be(new PivotChartTypeDialogResult(ChartType.StackedColumn));
        });
    }

    [Fact]
    public void PivotTableOptionsDialog_CreateResult_CapturesModeledLayoutAndStyleSettings()
    {
        var result = PivotTableOptionsDialog.CreateResult(
            showRowGrandTotals: true,
            showColumnGrandTotals: false,
            showSubtotals: true,
            subtotalPlacement: PivotSubtotalPlacement.Top,
            repeatItemLabels: false,
            blankLineAfterItems: true,
            styleName: "  PivotStyleMedium9  ",
            showRowHeaders: false,
            showColumnHeaders: true,
            showRowStripes: true,
            showColumnStripes: false,
            reportLayout: PivotReportLayout.Outline);

        result.Should().Be(new PivotTableOptionsDialogResult(
            true,
            false,
            true,
            PivotSubtotalPlacement.Top,
            false,
            true,
            "PivotStyleMedium9",
            false,
            true,
            true,
            false,
            PivotReportLayout.Outline));
    }

    [Fact]
    public void PivotTableOptionsDialog_FromPivotTable_UsesCurrentPivotSettings()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var pivotTable = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 12, 4)),
            TargetRange = new GridRange(new CellAddress(sheetId, 15, 1), new CellAddress(sheetId, 22, 4)),
            ShowRowGrandTotals = false,
            ShowColumnGrandTotals = true,
            ShowSubtotals = true,
            SubtotalPlacement = PivotSubtotalPlacement.Top,
            RepeatItemLabels = false,
            BlankLineAfterItems = true,
            ReportLayout = PivotReportLayout.Compact,
            StyleName = "PivotStyleDark4",
            ShowRowHeaders = true,
            ShowColumnHeaders = false,
            ShowRowStripes = true,
            ShowColumnStripes = true
        };

        PivotTableOptionsDialog.FromPivotTable(pivotTable)
            .Should()
            .Be(new PivotTableOptionsDialogResult(
                false,
                true,
                true,
                PivotSubtotalPlacement.Top,
                false,
                true,
                "PivotStyleDark4",
                true,
                false,
                true,
                true,
                PivotReportLayout.Compact));
    }
}
