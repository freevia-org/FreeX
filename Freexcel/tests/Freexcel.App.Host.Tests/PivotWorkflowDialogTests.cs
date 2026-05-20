using System.IO;
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
    public void PivotTableDialog_ExposesReferencePickersForSourceAndExistingLocation()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PivotTableDialog.cs"));

        source.Should().Contain("CreateReferenceEditor(_sourceRangeBox");
        source.Should().Contain("CreateReferenceEditor(_destinationRangeBox");
        source.Should().Contain("Select PivotTable source range");
        source.Should().Contain("Select PivotTable location");
        source.Should().Contain("ReferencePickerButton_Click");
        source.Should().Contain("UpdateDestinationState");
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

    [Fact]
    public void PivotFieldGroupingDialog_CreateResult_TrimsFieldAndClampsNumberRangeInterval()
    {
        var result = PivotFieldGroupingDialog.CreateResult(
            "  Order Date  ",
            sourceFieldIndex: -3,
            PivotFieldGrouping.NumberRange,
            "  10  ",
            "  90  ",
            "  -5  ",
            ungroup: false);

        result.Should().Be(new PivotFieldGroupingDialogResult(
            "Order Date",
            0,
            PivotFieldGrouping.NumberRange,
            10,
            90,
            1,
            false));
    }

    [Fact]
    public void PivotFieldGroupingDialog_CreateResult_UngroupClearsGroupingSettings()
    {
        var result = PivotFieldGroupingDialog.CreateResult(
            " Region ",
            sourceFieldIndex: 2,
            PivotFieldGrouping.Month,
            "1",
            "12",
            "3",
            ungroup: true);

        result.Should().Be(new PivotFieldGroupingDialogResult(
            "Region",
            2,
            PivotFieldGrouping.None,
            null,
            null,
            null,
            true));
    }

    [Fact]
    public void PivotFieldGroupingDialog_FromPivotField_UsesCurrentFieldSettings()
    {
        var field = new PivotFieldModel(
            SourceFieldIndex: 1,
            Grouping: PivotFieldGrouping.Month,
            GroupStart: 44562,
            GroupEnd: 44927,
            GroupInterval: 2);

        PivotFieldGroupingDialog.FromPivotField(["Region", "Order Date"], field)
            .Should()
            .Be(new PivotFieldGroupingDialogResult(
                "Order Date",
                1,
                PivotFieldGrouping.Month,
                44562,
                44927,
                2,
                false));
    }

    [Fact]
    public void PivotFieldGroupingDialog_FromPivotField_DefaultsToFirstFieldWhenCurrentSettingsAreMissing()
    {
        PivotFieldGroupingDialog.FromPivotField(["Region", "Order Date"], currentField: null)
            .Should()
            .Be(new PivotFieldGroupingDialogResult(
                "Region",
                0,
                PivotFieldGrouping.None,
                null,
                null,
                null,
                false));
    }

    [Fact]
    public void PivotCalculatedFieldDialog_CreateResult_TrimsAndBuildsModel()
    {
        var result = PivotCalculatedFieldDialog.CreateResult("  Revenue  ", "  Sales-Cost  ");

        result.Should().Be(new PivotCalculatedFieldDialogResult("Revenue", "Sales-Cost"));
        result.ToModel().Should().Be(new PivotCalculatedFieldModel("Revenue", "Sales-Cost"));
    }

    [Fact]
    public void PivotCalculatedItemDialog_CreateResult_TrimsClampsAndBuildsModel()
    {
        var result = PivotCalculatedItemDialog.CreateResult(
            "  Region  ",
            sourceFieldIndex: -8,
            "  East + West  ",
            "  East+West  ");

        result.Should().Be(new PivotCalculatedItemDialogResult("Region", 0, "East + West", "East+West"));
        result.ToModel().Should().Be(new PivotCalculatedItemModel(0, "East + West", "East+West"));
    }

    [Fact]
    public void PivotChartOptionsDialog_CreateResult_ParsesAndClampsStyle()
    {
        PivotChartOptionsDialog.CreateResult(" 99 ", showFieldButtons: false)
            .Should()
            .Be(new PivotChartOptionsDialogResult(48, false));

        PivotChartOptionsDialog.CreateResult("not-a-style", showFieldButtons: true)
            .Should()
            .Be(new PivotChartOptionsDialogResult(null, true));
    }

    [Fact]
    public void PivotChartOptionsDialog_FromChart_UsesCurrentSettings()
    {
        var chart = new ChartModel
        {
            ChartStyleId = 12,
            ShowPivotChartFieldButtons = false
        };

        PivotChartOptionsDialog.FromChart(chart)
            .Should()
            .Be(new PivotChartOptionsDialogResult(12, false));
    }
}
