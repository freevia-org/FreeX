using FluentAssertions;
using Free.Shared.Shell;
using FreeX.App.Presentation.Charts.Editing;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Charts;

public sealed class ChartWorkflowDescriptorPlannerTests
{
    [Fact]
    public void TypePicker_DialogPanels_SurfaceSharedPreviewAndAutomationKeys()
    {
        var recommended = ChartTypePickerPlanner.GetRecommendedPanel();
        var allCharts = ChartTypePickerPlanner.GetAllChartsPanel();

        recommended.Kind.Should().Be(ChartTypePickerPanelKind.Recommended);
        recommended.HeadingResourceKey.Should().Be(ChartTypePickerPlanner.ChooseChartTypeHeadingKey);
        recommended.HelpResourceKey.Should().Be("ChartTypePicker_RecommendedHelpText");
        recommended.SubtypeGalleryAutomationNameResourceKey.Should().Be("ChartTypePicker_SubtypeGalleryAutomationName");
        recommended.Preview.BodyResourceKey.Should().Be("ChartTypePicker_RecommendedPreviewBody");

        allCharts.Kind.Should().Be(ChartTypePickerPanelKind.AllCharts);
        allCharts.HeadingResourceKey.Should().Be("ChartTypePicker_AllChartsHeading");
        allCharts.HelpResourceKey.Should().Be("ChartTypePicker_AllChartsHelpText");
        allCharts.CategoryListAutomationNameResourceKey.Should().Be("ChartTypePicker_CategoriesAutomationName");
        allCharts.Preview.TitleResourceKey.Should().Be("ChartTypePicker_PreviewTitle");
        allCharts.Preview.BodyResourceKey.Should().Be("ChartTypePicker_ChartPreviewBody");
        allCharts.Preview.SampleLabelResourceKey.Should().Be("ChartTypePicker_PreviewSampleLabel");
    }

    [Fact]
    public void MoveChart_DialogDescriptors_CoverTargetsAndTargetNameField()
    {
        var targets = ChartMovePlanner.GetTargetChoices();

        targets.Select(target => target.TargetKind).Should().Equal(
            ChartMoveTargetKind.ObjectInSheet,
            ChartMoveTargetKind.NewSheet);
        targets.Select(target => target.LabelResourceKey).Should().Equal(
            "MoveChart_ObjectInSheet",
            "MoveChart_NewChartSheet");
        targets.Select(target => target.AutomationId).Should().Equal(
            "MoveChartObjectRadio",
            "MoveChartNewSheetRadio");

        var targetName = ChartMovePlanner.GetTargetNameField();
        targetName.LabelResourceKey.Should().Be("MoveChart_TargetNameLabel");
        targetName.AutomationNameResourceKey.Should().Be("MoveChart_TargetNameAutomationName");
        targetName.HelpResourceKey.Should().Be("MoveChart_TargetNameHelpText");
        targetName.AutomationId.Should().Be("MoveChartTargetBox");
        ChartMovePlanner.DialogAutomationId.Should().Be("MoveChartDialog");
        ChartMovePlanner.TargetGroupName.Should().Be("MoveChartTarget");
    }

    [Fact]
    public void SelectDataSource_DialogDescriptors_CoverRangeListsAndActions()
    {
        SelectDataSourcePlanner.DialogTitleResourceKey.Should().Be("SelectDataSource_Title");
        SelectDataSourcePlanner.DialogAutomationId.Should().Be("SelectChartDataDialog");
        SelectDataSourcePlanner.GetChartDataRangeField().Should().Be(
            new SelectDataSourceDialogFieldDescriptor(
                SelectDataSourceDialogFieldId.ChartDataRange,
                "SelectDataSource_ChartDataRangeLabel",
                "SelectChartDataRangeBox",
                "SelectDataSource_ChartDataRangeAutomationName"));
        SelectDataSourcePlanner.GetChartDataRangeField()
            .Should().BeAssignableTo<DialogFieldPlan<SelectDataSourceDialogFieldId>>()
            .Which.ControlKind.Should().Be(DialogControlKind.Text);

        SelectDataSourcePlanner.GetSwitchRowColumnField().AutomationId
            .Should().Be("SelectChartDataSwitchRowColumnCheck");
        SelectDataSourcePlanner.GetSwitchRowColumnField().ControlKind
            .Should().Be(DialogControlKind.Toggle);
        SelectDataSourcePlanner.GetFirstColumnCategoriesField().LabelResourceKey
            .Should().Be("SelectDataSource_FirstColumnCategories");

        var series = SelectDataSourcePlanner.GetSeriesPanel();
        series.ListField.ControlKind.Should().Be(DialogControlKind.List);
        series.ListField.AutomationNameResourceKey.Should().Be("SelectDataSource_SeriesListAutomationName");
        series.ListField.HelpResourceKey.Should().Be("SelectDataSource_SeriesListHelpText");
        series.Actions.Select(action => action.Id).Should().Equal(
            SelectDataSourceDialogActionId.AddSeries,
            SelectDataSourceDialogActionId.EditSeries,
            SelectDataSourceDialogActionId.RemoveSeries);

        var axis = SelectDataSourcePlanner.GetAxisLabelsPanel();
        axis.ListField.AutomationId.Should().Be("SelectChartDataAxisLabelsList");
        axis.Actions.Should().ContainSingle()
            .Which.Id.Should().Be(SelectDataSourceDialogActionId.EditAxisLabels);
        SelectDataSourcePlanner.GetHiddenEmptyCellsAction().LabelResourceKey
            .Should().Be("SelectDataSource_HiddenEmptyCellsButton");
        SelectDataSourcePlanner.GetHiddenEmptyCellsAction()
            .Should().BeAssignableTo<DialogSurfaceActionPlan<SelectDataSourceDialogActionId>>();
    }

    [Fact]
    public void ChartWorkflowCommands_SurfaceDialogResourceKeysAndSupportGates()
    {
        var bar = ChartWorkflowCommandCatalog.FormatBarColumn;
        bar.TitleResourceKey.Should().Be(ChartBarFormatPlanner.TitleResourceKey);
        bar.HostMissingSelectionMessageResourceKey.Should().Be("MainWindowMessage_ChartSelectBarColumnForGapWidth");
        bar.HostUnsupportedMessageResourceKey.Should().Be("MainWindowMessage_ChartGapWidthUnsupported");
        bar.UnsupportedStatusResourceKey.Should().Be("ChartLoc_GapWidthOverlapAvailableOn");

        ChartWorkflowCommandCatalog.FormatDataLabels.TitleResourceKey.Should().Be("ChartDataLabels_Title");
        ChartWorkflowCommandCatalog.FormatTrendline.HostUnsupportedMessageResourceKey
            .Should().Be("MainWindowMessage_ChartTrendlinesSupportedTypes");
        ChartWorkflowCommandCatalog.FormatErrorBars.UnsupportedStatusResourceKey
            .Should().Be("ChartLoc_ErrorBarsAvailableOn");
        ChartWorkflowCommandCatalog.SecondaryAxis.HostMissingSelectionMessageResourceKey
            .Should().Be("MainWindowMessage_ChartSecondaryAxisRequiresChart");

        var column = Chart(ChartType.Column, endCol: 3);
        var noSeriesColumn = Chart(ChartType.Column, endCol: 1);
        var pie = Chart(ChartType.Pie, endCol: 2);
        var bubble = Chart(ChartType.Bubble, endCol: 3);

        ChartWorkflowCommandCatalog.CanOpenDialog(column, ChartWorkflowCommandCatalog.FormatBarColumn).Should().BeTrue();
        ChartWorkflowCommandCatalog.CanOpenDialog(pie, ChartWorkflowCommandCatalog.FormatBarColumn).Should().BeFalse();
        ChartWorkflowCommandCatalog.CanOpenDialog(bubble, ChartWorkflowCommandCatalog.FormatBubbleChart).Should().BeTrue();
        ChartWorkflowCommandCatalog.CanOpenDialog(pie, ChartWorkflowCommandCatalog.FormatTrendline).Should().BeFalse();
        ChartWorkflowCommandCatalog.CanOpenDialog(noSeriesColumn, ChartWorkflowCommandCatalog.FormatDataSeries).Should().BeFalse();
        ChartWorkflowCommandCatalog.CanOpenDialog(column, ChartWorkflowCommandCatalog.FormatDataSeries).Should().BeTrue();
        ChartWorkflowCommandCatalog.CanOpenDialog(column, ChartWorkflowCommandCatalog.ComboChart).Should().BeTrue();
        ChartWorkflowCommandCatalog.CanOpenDialog(column, ChartWorkflowCommandCatalog.SecondaryAxis).Should().BeTrue();
    }

    [Fact]
    public void FormatDataSeries_UsesSharedPlannerForEverySeriesField()
    {
        var chart = Chart(ChartType.Line, endCol: 3);
        var plan = ChartSeriesFormatPlanner.Plan(chart, new ChartSeriesFormatInput(
            SeriesIndex: 1,
            FillColor: new CellColor(10, 20, 30),
            StrokeColor: new CellColor(40, 50, 60),
            StrokeThickness: 2.5,
            MarkerStyle: ChartMarkerStyle.Diamond,
            MarkerSize: 9,
            DashStyle: ChartLineDashStyle.Dash));

        ChartWorkflowCommandCatalog.CanOpenDialog(chart, ChartWorkflowCommandCatalog.FormatDataSeries)
            .Should().BeTrue();
        plan.SeriesFormats.Should().ContainSingle(format => format.SeriesIndex == 1)
            .Which.Should().Match<ChartSeriesFormat>(format =>
                format.FillColor == new CellColor(10, 20, 30) &&
                format.StrokeColor == new CellColor(40, 50, 60) &&
                format.StrokeThickness == 2.5 &&
                format.MarkerStyle == ChartMarkerStyle.Diamond &&
                format.MarkerSize == 9 &&
                format.DashStyle == ChartLineDashStyle.Dash);
    }

    [Fact]
    public void AxisWorkflowCommands_MapLabelsMessagesAndQuickCommands()
    {
        var xGridlines = ChartAxisWorkflowCommandCatalog.Gridlines(useXAxis: true);
        xGridlines.Id.Should().Be(ChartAxisWorkflowCommandId.XAxisGridlines);
        xGridlines.Label.Should().Be("X Axis Gridlines");
        xGridlines.HostMissingSelectionMessageResourceKey.Should().Be("MainWindowMessage_ChartAxisGridlinesRequiresChart");
        xGridlines.QuickCommand.Should().Be(ChartAxisQuickCommand.Gridlines);

        var yLabels = ChartAxisWorkflowCommandCatalog.Labels(useXAxis: false);
        yLabels.Id.Should().Be(ChartAxisWorkflowCommandId.YAxisLabels);
        yLabels.UseXAxis.Should().BeFalse();
        yLabels.Label.Should().Be("Y Axis Labels");
        yLabels.QuickCommand.Should().Be(ChartAxisQuickCommand.Labels);

        ChartAxisWorkflowCommandCatalog.LogScale(useXAxis: true).HostMissingSelectionMessageResourceKey
            .Should().Be("MainWindowMessage_ChartAxisScaleRequiresChart");
        ChartAxisWorkflowCommandCatalog.Bounds(useXAxis: false).Label.Should().Be("Y Axis Bounds");
        ChartAxisWorkflowCommandCatalog.All.Select(command => command.Id)
            .Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void PivotChartOptions_ReadCreateAndDescriptors_CoverSharedDialogSurface()
    {
        var chart = new ChartModel
        {
            ChartStyleId = 99,
            ShowPivotChartFieldButtons = false,
            ShowPivotChartReportFilterButtons = true,
            ShowPivotChartAxisFieldButtons = false,
            ShowPivotChartValueFieldButtons = true,
            DataTable = new ChartDataTableModel { ShowLegendKeys = true },
            RoundedCorners = true,
            ShowDataInHiddenRowsAndColumns = true,
            BlankDisplayMode = ChartBlankDisplayMode.Span,
        };

        var input = PivotChartOptionsPlanner.Read(chart);

        input.Should().Be(new PivotChartOptionsInput(
            48,
            false,
            true,
            false,
            true,
            true,
            true,
            true,
            true,
            ChartBlankDisplayMode.Span));

        PivotChartOptionsPlanner.CreateResult(
                " not a number ",
                showFieldButtons: true,
                showReportFilterButtons: false,
                showAxisFieldButtons: true,
                showValueFieldButtons: false)
            .ChartStyleId.Should().BeNull();
        PivotChartOptionsPlanner.CreateResult(99, true).ChartStyleId.Should().Be(48);

        PivotChartOptionsPlanner.GetDialogSections().Select(section => section.HeaderResourceKey)
            .Should().Equal(
                "PivotChartOptions_ChartStyleGroup",
                "PivotChartOptions_FieldButtonsGroup",
                "PivotChartOptions_LayoutGroup");
        PivotChartOptionsPlanner.GetDialogField(PivotChartOptionsDialogFieldId.ShowHiddenData)
            .LabelResourceKey.Should().Be("PivotChartOptions_ShowDataInHiddenRowsAndColumns");
        PivotChartOptionsPlanner.GetDialogField(PivotChartOptionsDialogFieldId.BlankDisplayMode)
            .AutomationId.Should().Be("PivotChartOptionsBlankDisplayMode");
        PivotChartOptionsPlanner.GetBlankDisplayChoices().Select(choice => choice.Mode)
            .Should().Equal(ChartBlankDisplayMode.Gap, ChartBlankDisplayMode.Span, ChartBlankDisplayMode.Zero);
        PivotChartOptionsPlanner.GetResolvedBlankDisplayChoices(key => $"resolved:{key}")
            .Should().Equal(
                new PivotChartOptionsResolvedBlankDisplayChoice(
                    "resolved:PivotChartOptions_BlankDisplayGaps",
                    ChartBlankDisplayMode.Gap),
                new PivotChartOptionsResolvedBlankDisplayChoice(
                    "resolved:PivotChartOptions_BlankDisplayConnectDataPoints",
                    ChartBlankDisplayMode.Span),
                new PivotChartOptionsResolvedBlankDisplayChoice(
                    "resolved:PivotChartOptions_BlankDisplayZero",
                    ChartBlankDisplayMode.Zero));
    }

    private static ChartModel Chart(ChartType type, uint endCol)
    {
        var sheetId = SheetId.New();
        return new ChartModel
        {
            Type = type,
            FirstColIsCategories = true,
            DataRange = new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 4, endCol)),
        };
    }
}
