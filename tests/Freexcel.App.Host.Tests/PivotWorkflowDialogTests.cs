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
}
