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
    public void PivotTableDialog_ExposesExcelLikeSourcePlacementAndDataModelAffordances()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PivotTableDialog.cs"));

        source.Should().Contain("Choose the data that you want to analyze");
        source.Should().Contain("_selectTableRangeButton");
        source.Should().Contain("_externalSourceButton");
        source.Should().Contain("_dataModelBox");
        source.Should().Contain("Use an external data source");
        source.Should().Contain("Add this data to the Data Model");
        source.Should().Contain("New worksheet");
        source.Should().Contain("Existing worksheet");
    }

    [Fact]
    public void PivotTableDialog_ExposesKeyboardAccessKeysForChoicesAndButtons()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PivotTableDialog.cs"));

        source.Should().Contain("Content = \"_Create\"");
        source.Should().Contain("Content = \"_Cancel\"");
        source.Should().Contain("Content = \"Use an _external data source\"");
        source.Should().Contain("Content = \"_New worksheet\"");
        source.Should().Contain("Content = \"_Existing worksheet\"");
        source.Should().Contain("Content = \"Add this data to the Data _Model\"");
        source.Should().Contain("Content = \"Open PivotTable _Fields pane\"");
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
    public void PivotTableDataSourceDialog_ExposesReferencePickerForSourceRange()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PivotWorkflowDialogs.cs"));

        source.Should().Contain("CreateReferenceEditor(_sourceBox");
        source.Should().Contain("Select PivotTable source range");
        source.Should().Contain("ReferencePickerButton_Click");
    }

    [Fact]
    public void InsertSlicerDialog_CreateResult_CapturesFieldAndSlicerName()
    {
        InsertSlicerDialog.CreateResult("  Region  ", "  Region Slicer  ")
            .Should()
            .Be(new InsertSlicerDialogResult("Region", "Region Slicer"));
    }

    [Fact]
    public void InsertSlicerDialog_ExposesExcelLikeFieldSelectionShell()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PivotWorkflowDialogs.cs"));

        source.Should().Contain("Choose fields");
        source.Should().Contain("Slicers make it faster to filter a PivotTable");
        source.Should().Contain("Field to connect");
        source.Should().Contain("Slicer caption");
        source.Should().Contain("DialogButtonRowFactory.Create");
    }

    [Fact]
    public void InsertTimelineDialog_CreateResult_CapturesDateFieldAndTimelineName()
    {
        InsertTimelineDialog.CreateResult("  Order Date  ", "  Order Date Timeline  ")
            .Should()
            .Be(new InsertTimelineDialogResult("Order Date", "Order Date Timeline"));
    }

    [Fact]
    public void InsertTimelineDialog_ExposesExcelLikeDateFieldSelectionShell()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PivotWorkflowDialogs.cs"));

        source.Should().Contain("Choose date fields");
        source.Should().Contain("Timelines filter PivotTables by date");
        source.Should().Contain("Date field to connect");
        source.Should().Contain("Timeline caption");
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
    public void PivotChartTypeDialog_ExposesPreviewAndRecommendedChartTypeCopy()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PivotWorkflowDialogs.cs"));

        source.Should().Contain("Recommended PivotCharts");
        source.Should().Contain("All Charts");
        source.Should().Contain("Chart preview");
        source.Should().Contain("Pick a chart type for the selected PivotTable data");
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
    public void PivotTableOptionsDialog_UsesExcelStyleTabbedOptionShell()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PivotWorkflowDialogs.cs"));

        foreach (var content in new[]
        {
            "Layout & Format",
            "Totals & Filters",
            "Display",
            "Printing",
            "Data",
            "Alt Text",
            "_emptyCellsBox",
            "_autofitColumnsBox",
            "_preserveFormattingBox",
            "_refreshOnOpenBox"
        })
            source.Should().Contain(content);
    }

    [Fact]
    public void PivotTableOptionsDialog_ExposesExcelLikeGroupsInsideTabs()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PivotWorkflowDialogs.cs"));

        foreach (var content in new[]
        {
            "Layout section",
            "Format section",
            "Grand totals",
            "Field list and buttons",
            "PivotTable Style Options",
            "Data options",
            "Preserve source sort and filter settings"
        })
            source.Should().Contain(content);
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
    public void PivotFieldGroupingDialog_ExposesExcelLikeGroupingSections()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PivotWorkflowDialogs.cs"));

        source.Should().Contain("Selection");
        source.Should().Contain("Group by");
        source.Should().Contain("Range");
        source.Should().Contain("Select the PivotTable field and grouping interval");
    }

    [Fact]
    public void PivotCalculatedFieldDialog_CreateResult_TrimsAndBuildsModel()
    {
        var result = PivotCalculatedFieldDialog.CreateResult("  Revenue  ", "  Sales-Cost  ");

        result.Should().Be(new PivotCalculatedFieldDialogResult("Revenue", "Sales-Cost"));
        result.ToModel().Should().Be(new PivotCalculatedFieldModel("Revenue", "Sales-Cost"));
    }

    [Fact]
    public void PivotCalculatedFieldDialog_ExposesExcelLikeFormulaEditorShell()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PivotWorkflowDialogs.cs"));

        source.Should().Contain("Name and formula");
        source.Should().Contain("Formula:");
        source.Should().Contain("Use field names in formulas");
        source.Should().Contain("Calculated fields are added to the Values area");
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
    public void PivotCalculatedItemDialog_ExposesExcelLikeFormulaEditorShell()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PivotWorkflowDialogs.cs"));

        source.Should().Contain("Field and item");
        source.Should().Contain("Calculated items are evaluated within the selected field");
        source.Should().Contain("Source field");
        source.Should().Contain("Item formula");
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

    [Fact]
    public void PivotChartOptionsDialog_ExposesExcelLikeStyleAndFieldButtonGroups()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PivotWorkflowDialogs.cs"));

        source.Should().Contain("Chart style");
        source.Should().Contain("Style IDs match the built-in Excel chart style gallery");
        source.Should().Contain("Field buttons");
        source.Should().Contain("Show field buttons on chart");
    }
}
