using FluentAssertions;
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

        SelectDataSourcePlanner.GetSwitchRowColumnField().AutomationId
            .Should().Be("SelectChartDataSwitchRowColumnCheck");
        SelectDataSourcePlanner.GetFirstColumnCategoriesField().LabelResourceKey
            .Should().Be("SelectDataSource_FirstColumnCategories");

        var series = SelectDataSourcePlanner.GetSeriesPanel();
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
    }
}
