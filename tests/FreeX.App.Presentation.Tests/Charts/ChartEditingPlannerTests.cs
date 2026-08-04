using FluentAssertions;
using FreeX.App.Presentation.Charts.Editing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Charts;

public sealed class ChartEditingPlannerTests
{
    // ---- ChartWorkflowTargetPlanner -----------------------------------------------------------------

    [Fact]
    public void WorkflowTarget_IsContextualTarget_RejectsHiddenAndPivotCharts()
    {
        ChartWorkflowTargetPlanner.IsContextualTarget(new ChartModel()).Should().BeTrue();
        ChartWorkflowTargetPlanner.IsContextualTarget(new ChartModel { IsVisible = false }).Should().BeFalse();
        ChartWorkflowTargetPlanner.IsContextualTarget(new ChartModel { IsPivotChart = true }).Should().BeFalse();
    }

    [Fact]
    public void WorkflowTarget_FindSelectedChart_RequiresSelectedVisibleNonPivotChart()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var hidden = new ChartModel { IsVisible = false };
        var pivot = new ChartModel { IsPivotChart = true };
        var selected = new ChartModel();
        sheet.Charts.Add(hidden);
        sheet.Charts.Add(pivot);
        sheet.Charts.Add(selected);

        ChartWorkflowTargetPlanner.FindSelectedChart(sheet, selected.Id).Should().BeSameAs(selected);
        ChartWorkflowTargetPlanner.FindSelectedChart(sheet, hidden.Id).Should().BeNull();
        ChartWorkflowTargetPlanner.FindSelectedChart(sheet, pivot.Id).Should().BeNull();
        ChartWorkflowTargetPlanner.FindSelectedChart(sheet, Guid.Empty).Should().BeNull();
        ChartWorkflowTargetPlanner.HasSelectedChart(sheet, selected.Id).Should().BeTrue();
        ChartWorkflowTargetPlanner.HasSelectedChart(sheet, hidden.Id).Should().BeFalse();
    }

    [Fact]
    public void WorkflowTarget_FindSelectedOrFirstChart_PrefersSelectedThenFirstEligible()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var hidden = new ChartModel { IsVisible = false };
        var first = new ChartModel { Name = "First" };
        var selected = new ChartModel { Name = "Selected" };
        sheet.Charts.Add(hidden);
        sheet.Charts.Add(first);
        sheet.Charts.Add(selected);

        ChartWorkflowTargetPlanner.FindSelectedOrFirstChart(sheet, selected.Id).Should().BeSameAs(selected);
        ChartWorkflowTargetPlanner.FindSelectedOrFirstChart(sheet, Guid.NewGuid()).Should().BeSameAs(first);
        ChartWorkflowTargetPlanner.FindFirstChart(sheet).Should().BeSameAs(first);
        ChartWorkflowTargetPlanner.FindSelectedOrFirstChart(null, selected.Id).Should().BeNull();
    }

    [Fact]
    public void WorkflowCommandCatalog_SharesCoreChartDialogLabelsAndHostSelectionMessages()
    {
        ChartWorkflowCommandCatalog.All.Select(command => command.Id).Should().Equal(
            ChartWorkflowCommandId.ChangeChartType,
            ChartWorkflowCommandId.SelectDataSource,
            ChartWorkflowCommandId.MoveChart,
            ChartWorkflowCommandId.FormatChartArea,
            ChartWorkflowCommandId.ChartTitles,
            ChartWorkflowCommandId.FormatBarColumn,
            ChartWorkflowCommandId.FormatBubbleChart,
            ChartWorkflowCommandId.FormatPieDoughnut,
            ChartWorkflowCommandId.FormatStockChart,
            ChartWorkflowCommandId.FormatDataLabels,
            ChartWorkflowCommandId.FormatTrendline,
            ChartWorkflowCommandId.FormatErrorBars,
            ChartWorkflowCommandId.FormatDataSeries,
            ChartWorkflowCommandId.ComboChart,
            ChartWorkflowCommandId.SecondaryAxis);

        ChartWorkflowCommandCatalog.ChangeChartType.Label.Should().Be("Change Chart Type");
        ChartWorkflowCommandCatalog.SelectDataSource.Label.Should().Be("Select Data Source");
        ChartWorkflowCommandCatalog.MoveChart.Label.Should().Be("Move Chart");
        ChartWorkflowCommandCatalog.FormatChartArea.Label.Should().Be("Format Chart Area");

        ChartWorkflowCommandCatalog.ChangeChartType.HostMissingSelectionMessageResourceKey
            .Should().Be(ChartWorkflowCommandCatalog.DefaultHostMissingSelectionMessageResourceKey);
        ChartWorkflowCommandCatalog.SelectDataSource.HostMissingSelectionMessageResourceKey
            .Should().Be(ChartWorkflowCommandCatalog.DefaultHostMissingSelectionMessageResourceKey);
        ChartWorkflowCommandCatalog.MoveChart.HostMissingSelectionMessageResourceKey
            .Should().Be(ChartWorkflowCommandCatalog.DefaultHostMissingSelectionMessageResourceKey);
        ChartWorkflowCommandCatalog.FormatChartArea.HostMissingSelectionMessageResourceKey
            .Should().Be("MainWindowMessage_ChartSelectForChartAreaFormatting");
        ChartWorkflowCommandCatalog.Get(ChartWorkflowCommandId.FormatChartArea)
            .Should().BeSameAs(ChartWorkflowCommandCatalog.FormatChartArea);
    }

    [Fact]
    public void QuickCommandCatalog_SharesQuickLabelsAndHostMessages()
    {
        ChartQuickCommandCatalog.All.Select(command => command.Command)
            .Should().Equal(Enum.GetValues<ChartQuickCommand>());

        ChartQuickCommandCatalog.All.Should().OnlyContain(command => !string.IsNullOrWhiteSpace(command.Label));
        ChartQuickCommandCatalog.All.Should().OnlyContain(command =>
            !string.IsNullOrWhiteSpace(command.HostMissingSelectionMessageResourceKey));

        ChartQuickCommandCatalog.FirstSliceAngle.Label.Should().Be("First Slice Angle");
        ChartQuickCommandCatalog.FirstSliceAngle.HostMissingSelectionMessageResourceKey
            .Should().Be("MainWindowMessage_ChartSelectPieDoughnutForFirstSliceAngle");
        ChartQuickCommandCatalog.FirstSliceAngle.HostUnsupportedMessageResourceKey
            .Should().Be("MainWindowMessage_ChartFirstSliceAngleUnsupported");

        ChartQuickCommandCatalog.DataLabelFill.HostMissingSelectionMessageResourceKey
            .Should().Be(ChartQuickCommandCatalog.DataLabelOptionsHostMissingSelectionMessageResourceKey);
        ChartQuickCommandCatalog.DataLabelFill.HostUnsupportedMessageResourceKey.Should().BeNull();
        ChartQuickCommandCatalog.ChartTitleFontSize.Label.Should().Be("Chart Title Size");
        ChartQuickCommandCatalog.ChartTitleFontSize.HostMissingSelectionMessageResourceKey
            .Should().Be(ChartQuickCommandCatalog.ChartAreaFormattingHostMissingSelectionMessageResourceKey);
        ChartQuickCommandCatalog.ComboSeries.Label.Should().Be("Combo Chart Series");
        ChartQuickCommandCatalog.ComboSeries.HostUnsupportedMessageResourceKey
            .Should().Be("MainWindowMessage_ChartComboUnsupported");
        ChartQuickCommandCatalog.SeriesMarkerSize.Label.Should().Be("Marker Size");
        ChartQuickCommandCatalog.SeriesMarkerSize.HostUnsupportedMessageResourceKey
            .Should().Be("MainWindowMessage_ChartSeriesMarkersSupportedTypes");

        ChartQuickCommandCatalog.Get(ChartQuickCommand.ComboSeries)
            .Should().BeSameAs(ChartQuickCommandCatalog.ComboSeries);
    }

    // ---- ChartTypeChangePlanner ----------------------------------------------------------------------

    [Fact]
    public void TypeChange_SupportedChoices_AreAllAuthorable_AndCoverCommonFamilies()
    {
        var choices = ChartTypeChangePlanner.GetSupportedChoices();

        choices.Should().NotBeEmpty();
        choices.Should().OnlyContain(choice => ChartTypeSupport.IsAuthorable(choice.Type));
        choices.Select(c => c.Type).Should().Contain(new[]
        {
            ChartType.Column, ChartType.Bar, ChartType.Line, ChartType.Area,
            ChartType.Scatter, ChartType.Pie, ChartType.Doughnut, ChartType.Bubble,
            ChartType.Radar, ChartType.Stock
        });
        choices.Should().OnlyContain(choice => !string.IsNullOrWhiteSpace(choice.DisplayName));
    }

    [Fact]
    public void TypeChange_RecommendedTypes_AreAuthorableAndStableForInsertPicker()
    {
        ChartTypeChangePlanner.GetRecommendedTypes().Should().Equal(
            ChartType.Column,
            ChartType.Line,
            ChartType.Bar,
            ChartType.Pie,
            ChartType.Scatter);
        ChartTypeChangePlanner.GetRecommendedTypes().Should().OnlyContain(type => ChartTypeSupport.IsAuthorable(type));
    }

    [Fact]
    public void TypeChange_DisplayNameKeys_SurfaceShellLocalizationKeys()
    {
        ChartTypeChangePlanner.DisplayNameKey(ChartType.Column).Should().Be("ChartType_ClusteredColumn");
        ChartTypeChangePlanner.DisplayNameKey(ChartType.BoxAndWhisker).Should().Be("MainWindow_TooltipTitle_BoxAndWhiskerChart");
        ChartTypeChangePlanner.DisplayNameKey(ChartType.Map).Should().Be(nameof(ChartType.Map));
    }

    [Fact]
    public void TypeChange_DialogFrameSize_MatchesPairedVisualEvidenceContract()
    {
        ChartTypeChangePlanner.DialogWidth.Should().Be(640);
        ChartTypeChangePlanner.DialogHeight.Should().Be(390);
        ChartTypeChangePlanner.PickerPanelHeight.Should().Be(290);
        ChartTypeChangePlanner.PickerCategoryWidth.Should().Be(150);
        ChartTypeChangePlanner.PickerCategoryColumnWidth.Should().Be(162);
        ChartTypeChangePlanner.PickerSubtypeWidth.Should().Be(180);
        ChartTypeChangePlanner.PickerSubtypeColumnWidth.Should().Be(192);
        ChartTypeChangePlanner.PickerPreviewWidth.Should().Be(180);
        ChartTypeChangePlanner.PickerColumnGap.Should().Be(12);
        ChartTypeChangePlanner.PickerListHeight.Should().Be(230);
        ChartTypeChangePlanner.PickerButtonWidth.Should().Be(76);
    }

    [Fact]
    public void TypePicker_SupportedOptions_CarryLocalizationKeysAndRecommendationFlags()
    {
        var options = ChartTypePickerPlanner.GetSupportedOptions();

        options.Should().NotBeEmpty();
        options.Should().OnlyContain(option => ChartTypeSupport.IsAuthorable(option.Type));

        var percentStackedColumn = options.Single(option => option.Type == ChartType.PercentStackedColumn);
        percentStackedColumn.DisplayNameKey.Should().Be("ChartType_PercentStackedColumn");
        percentStackedColumn.FallbackDisplayName.Should().Be("100% Stacked Column");

        options.Single(option => option.Type == ChartType.Column).IsRecommended.Should().BeTrue();
        options.Single(option => option.Type == ChartType.Stock).IsRecommended.Should().BeFalse();
    }

    [Fact]
    public void TypePicker_GroupsOptionsIntoResourceKeyedExcelCategories()
    {
        var categories = ChartTypePickerPlanner.GetCategories();

        categories.Select(category => category.NameKey).Should().ContainInOrder(
            "ChartTypeCategory_Column",
            "ChartTypeCategory_Line",
            "ChartTypeCategory_Pie",
            "ChartTypeCategory_Bar",
            "ChartTypeCategory_Area",
            "ChartTypeCategory_Scatter",
            "ChartTypeCategory_Stock",
            "ChartTypeCategory_Radar",
            "ChartTypeCategory_Surface",
            "MainWindow_Content_Treemap",
            "MainWindow_Content_Sunburst",
            "MainWindow_Content_Histogram",
            "MainWindow_TooltipTitle_BoxAndWhiskerChart",
            "MainWindow_Content_Waterfall",
            "MainWindow_Content_Funnel");
        categories.Should().OnlyContain(category => category.Options.All(option => ChartTypeSupport.IsAuthorable(option.Type)));
        categories.Single(category => category.NameKey == "ChartTypeCategory_Column").Options.Select(option => option.Type).Should().ContainInOrder(
            ChartType.Column,
            ChartType.StackedColumn,
            ChartType.PercentStackedColumn,
            ChartType.ThreeDColumn);
        categories.Single(category => category.NameKey == "MainWindow_Content_Histogram").Options.Select(option => option.Type).Should().ContainInOrder(
            ChartType.Histogram,
            ChartType.Pareto);
    }

    [Fact]
    public void TypePicker_GalleryChoices_CarrySubtypeAndPreviewResourceKeys()
    {
        var choices = ChartTypePickerPlanner.GetGalleryChoices("ChartTypeCategory_Bar");

        choices.Select(choice => choice.SubtypeNameKey).Should().ContainInOrder(
            "ChartType_ClusteredBar",
            "ChartType_StackedBar",
            "ChartType_PercentStackedBar");
        choices.Should().OnlyContain(choice => choice.CategoryNameKey == "ChartTypeCategory_Bar");
        choices.Should().OnlyContain(choice => choice.PreviewTextFormatKey == ChartTypePickerPlanner.PreviewTextFormatKey);
        choices.Single(choice => choice.Type == ChartType.Bar).IsRecommended.Should().BeTrue();
        choices.Single(choice => choice.Type == ChartType.StackedBar).IsRecommended.Should().BeFalse();
    }

    [Fact]
    public void TypePicker_RecommendedGalleryChoices_CarryRecommendedCategoryAndPreviewKeys()
    {
        var choices = ChartTypePickerPlanner.GetRecommendedGalleryChoices();

        choices.Select(choice => choice.Type).Should().Equal(
            ChartType.Column,
            ChartType.Line,
            ChartType.Bar,
            ChartType.Pie,
            ChartType.Scatter);
        choices.Should().OnlyContain(choice => choice.CategoryNameKey == ChartTypePickerPlanner.RecommendedCategoryKey);
        choices.Should().OnlyContain(choice => choice.PreviewTextFormatKey == ChartTypePickerPlanner.PreviewTextFormatKey);
        choices.Should().OnlyContain(choice => choice.IsRecommended);
    }

    [Fact]
    public void TypeChange_Plan_ReturnsRequestedType_WhenDifferentAndAuthorable()
    {
        var plan = ChartTypeChangePlanner.Plan(ChartType.Column, ChartType.Line);

        plan.HasChange.Should().BeTrue();
        plan.AppliedType.Should().Be(ChartType.Line);
        plan.Message.Should().BeNull();
    }

    [Fact]
    public void TypeChange_Plan_IsNoOp_WhenTypeUnchanged()
    {
        var plan = ChartTypeChangePlanner.Plan(ChartType.Pie, ChartType.Pie);

        plan.HasChange.Should().BeFalse();
        plan.AppliedType.Should().BeNull();
        plan.Message.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TypeChange_Plan_Rejects_DeferredAuthoringFamily()
    {
        // Map is renderable-but-not-authorable: the planner must reject converting to it.
        var plan = ChartTypeChangePlanner.Plan(ChartType.Column, ChartType.Map);

        plan.HasChange.Should().BeFalse();
        plan.Message.Should().Be(ChartAuthoringPlanner.DeferredAuthoringMessage);
    }

    // ---- ChartTitlesPlanner --------------------------------------------------------------------------

    [Fact]
    public void Titles_Plan_TrimsAndCollapsesWhitespace()
    {
        var input = new ChartTitlesInput("  Sales  ", "  Quarter ", "   ");
        var options = ChartTitlesPlanner.Plan(ChartType.Column, input);

        options.Title.Should().Be("Sales");
        options.XAxisTitle.Should().Be("Quarter");
        options.YAxisTitle.Should().BeEmpty();
    }

    [Fact]
    public void Titles_Plan_DropsAxisTitles_ForAxislessChartTypes()
    {
        var input = new ChartTitlesInput("Revenue", "Category", "Value");
        var options = ChartTitlesPlanner.Plan(ChartType.Pie, input);

        options.Title.Should().Be("Revenue");
        options.XAxisTitle.Should().BeEmpty();
        options.YAxisTitle.Should().BeEmpty();
    }

    [Fact]
    public void Titles_Read_ProjectsModelTitles()
    {
        var chart = new ChartModel { Title = "T", XAxisTitle = "X", YAxisTitle = "Y" };
        var input = ChartTitlesPlanner.Read(chart);

        input.ChartTitle.Should().Be("T");
        input.XAxisTitle.Should().Be("X");
        input.YAxisTitle.Should().Be("Y");
    }

    [Fact]
    public void Titles_Plan_RoundTripsThroughSetChartLayoutCommand()
    {
        // The planner output applied via the Core command must land on the model.
        var chart = new ChartModel { Type = ChartType.Column, Title = "old" };
        var options = ChartTitlesPlanner.Plan(chart.Type, new ChartTitlesInput("New Title", "Months", "Units"));

        ApplyLayout(chart, options);

        chart.Title.Should().Be("New Title");
        chart.XAxisTitle.Should().Be("Months");
        chart.YAxisTitle.Should().Be("Units");
    }

    // ---- ChartLegendPlanner --------------------------------------------------------------------------

    [Fact]
    public void Legend_PositionChoices_AreTheFourPlacements()
    {
        var positions = ChartLegendPlanner.GetPositionChoices().Select(c => c.Position).ToList();

        positions.Should().BeEquivalentTo(new[]
        {
            ChartLegendPosition.Right, ChartLegendPosition.Top,
            ChartLegendPosition.Left, ChartLegendPosition.Bottom
        });
        positions.Should().NotContain(ChartLegendPosition.None);
    }

    [Fact]
    public void Legend_Read_SurfacesNoneAsRight()
    {
        var chart = new ChartModel { ShowLegend = false, LegendPosition = ChartLegendPosition.None };
        var input = ChartLegendPlanner.Read(chart);

        input.ShowLegend.Should().BeFalse();
        input.Position.Should().Be(ChartLegendPosition.Right);
    }

    [Fact]
    public void Legend_Plan_SetsShowAndPosition()
    {
        var options = ChartLegendPlanner.Plan(new ChartLegendInput(ShowLegend: true, ChartLegendPosition.Bottom));

        options.ShowLegend.Should().BeTrue();
        options.LegendPosition.Should().Be(ChartLegendPosition.Bottom);
    }

    [Fact]
    public void Legend_Plan_KeepsPosition_EvenWhenHidden()
    {
        var options = ChartLegendPlanner.Plan(new ChartLegendInput(ShowLegend: false, ChartLegendPosition.Left));

        options.ShowLegend.Should().BeFalse();
        options.LegendPosition.Should().Be(ChartLegendPosition.Left);
    }

    [Fact]
    public void Legend_Plan_FallsBackToRight_ForInvalidPosition()
    {
        var options = ChartLegendPlanner.Plan(new ChartLegendInput(ShowLegend: true, ChartLegendPosition.None));

        options.LegendPosition.Should().Be(ChartLegendPosition.Right);
    }

    [Fact]
    public void Legend_Plan_RoundTripsThroughSetChartLayoutCommand()
    {
        var chart = new ChartModel { Type = ChartType.Column, ShowLegend = true, LegendPosition = ChartLegendPosition.Right };
        var options = ChartLegendPlanner.Plan(new ChartLegendInput(ShowLegend: true, ChartLegendPosition.Top));

        ApplyLayout(chart, options);

        chart.ShowLegend.Should().BeTrue();
        chart.LegendPosition.Should().Be(ChartLegendPosition.Top);
    }

    // ---- ChartDataLabelsPlanner ----------------------------------------------------------------------

    [Fact]
    public void DataLabels_PositionChoices_CoverTheFourPlacements()
    {
        var positions = ChartDataLabelsPlanner.GetPositionChoices().Select(c => c.Position).ToList();

        positions.Should().BeEquivalentTo(new[]
        {
            ChartDataLabelPosition.BestFit, ChartDataLabelPosition.OutsideEnd,
            ChartDataLabelPosition.InsideEnd, ChartDataLabelPosition.Center
        });
        ChartDataLabelsPlanner.GetPositionChoices().Should().OnlyContain(c => !string.IsNullOrWhiteSpace(c.DisplayName));
        ChartDataLabelsPlanner.GetSeparatorChoices().Should().Equal(
            ChartDataLabelSeparator.Comma,
            ChartDataLabelSeparator.Semicolon,
            ChartDataLabelSeparator.NewLine,
            ChartDataLabelSeparator.Space);
        ChartDataLabelsPlanner.GetNumberFormatChoices().Should().Equal(
            ChartDataLabelNumberFormat.General,
            ChartDataLabelNumberFormat.Number,
            ChartDataLabelNumberFormat.Currency,
            ChartDataLabelNumberFormat.Percent);
    }

    [Fact]
    public void DataLabels_DialogDescriptor_CoversSharedOptionAndStyleFields()
    {
        var sections = ChartDataLabelsPlanner.GetDialogSections();
        var fields = sections.SelectMany(section => section.Fields).ToList();

        sections.Select(section => section.HeaderResourceKey)
            .Should().Equal("ChartDataLabels_LabelOptionsGroup", "ChartDialog_FillLineGroup");
        fields.Select(field => field.Id).Should().Equal(
            ChartDataLabelsDialogFieldId.ShowDataLabels,
            ChartDataLabelsDialogFieldId.Position,
            ChartDataLabelsDialogFieldId.Value,
            ChartDataLabelsDialogFieldId.LegendKey,
            ChartDataLabelsDialogFieldId.CategoryName,
            ChartDataLabelsDialogFieldId.SeriesName,
            ChartDataLabelsDialogFieldId.Percentage,
            ChartDataLabelsDialogFieldId.Separator,
            ChartDataLabelsDialogFieldId.NumberFormat,
            ChartDataLabelsDialogFieldId.Callouts,
            ChartDataLabelsDialogFieldId.FillColor,
            ChartDataLabelsDialogFieldId.BorderColor,
            ChartDataLabelsDialogFieldId.TextColor,
            ChartDataLabelsDialogFieldId.BorderThickness,
            ChartDataLabelsDialogFieldId.FontSize,
            ChartDataLabelsDialogFieldId.TextAngle);
        fields.Should().OnlyContain(field => !string.IsNullOrWhiteSpace(field.LabelResourceKey));
        fields.Should().OnlyContain(field => !string.IsNullOrWhiteSpace(field.AutomationId));
        ChartDataLabelsPlanner.GetDialogField(ChartDataLabelsDialogFieldId.BorderThickness)
            .HelpResourceKey.Should().Be("ChartDataLabels_BorderThicknessHelpText");
        ChartDataLabelsPlanner.GetDialogField(ChartDataLabelsDialogFieldId.FontSize)
            .HelpResourceKey.Should().Be("ChartDataLabels_FontSizeHelpText");
        ChartDataLabelsPlanner.GetDialogField(ChartDataLabelsDialogFieldId.TextAngle)
            .HelpResourceKey.Should().Be("ChartDataLabels_TextAngleHelpText");
    }

    [Fact]
    public void DataLabels_Read_ProjectsModelState()
    {
        var chart = new ChartModel
        {
            ShowDataLabels = true,
            DataLabelPosition = ChartDataLabelPosition.OutsideEnd,
            ShowDataLabelValue = false,
            ShowDataLabelLegendKey = true,
            ShowDataLabelCategoryName = true,
            ShowDataLabelSeriesName = true,
            ShowDataLabelPercentage = true,
            DataLabelSeparator = ChartDataLabelSeparator.NewLine,
            DataLabelNumberFormat = ChartDataLabelNumberFormat.Currency,
            ShowDataLabelCallouts = true,
            DataLabelFillColor = new CellColor(10, 20, 30),
            DataLabelBorderColor = new CellColor(40, 50, 60),
            DataLabelTextColor = new CellColor(70, 80, 90),
            DataLabelBorderThickness = 1.5,
            DataLabelFontSize = 14,
            DataLabelAngle = 25,
        };
        var input = ChartDataLabelsPlanner.Read(chart);

        input.ShowDataLabels.Should().BeTrue();
        input.Position.Should().Be(ChartDataLabelPosition.OutsideEnd);
        input.ShowValue.Should().BeFalse();
        input.ShowLegendKey.Should().BeTrue();
        input.ShowCategoryName.Should().BeTrue();
        input.ShowSeriesName.Should().BeTrue();
        input.ShowPercentage.Should().BeTrue();
        input.Separator.Should().Be(ChartDataLabelSeparator.NewLine);
        input.NumberFormat.Should().Be(ChartDataLabelNumberFormat.Currency);
        input.ShowCallouts.Should().BeTrue();
        input.FillColor.Should().Be(new CellColor(10, 20, 30));
        input.BorderColor.Should().Be(new CellColor(40, 50, 60));
        input.TextColor.Should().Be(new CellColor(70, 80, 90));
        input.BorderThickness.Should().Be(1.5);
        input.FontSize.Should().Be(14);
        input.Angle.Should().Be(25);
    }

    [Fact]
    public void DataLabels_Plan_ForcesValueWhenShownWithoutAnyToggle()
    {
        var input = new ChartDataLabelsInput(
            ShowDataLabels: true, ChartDataLabelPosition.Center,
            ShowValue: false, ShowCategoryName: false, ShowSeriesName: false,
            ShowPercentage: false, ShowLegendKey: false);
        var options = ChartDataLabelsPlanner.Plan(input);

        options.ShowDataLabels.Should().BeTrue();
        options.ShowDataLabelValue.Should().BeTrue();
        options.DataLabelPosition.Should().Be(ChartDataLabelPosition.Center);
    }

    [Fact]
    public void DataLabels_Plan_KeepsConfiguration_EvenWhenHidden()
    {
        var input = new ChartDataLabelsInput(
            ShowDataLabels: false, ChartDataLabelPosition.InsideEnd,
            ShowValue: false, ShowCategoryName: true, ShowSeriesName: false,
            ShowPercentage: false, ShowLegendKey: false);
        var options = ChartDataLabelsPlanner.Plan(input);

        options.ShowDataLabels.Should().BeFalse();
        options.DataLabelPosition.Should().Be(ChartDataLabelPosition.InsideEnd);
        options.ShowDataLabelCategoryName.Should().BeTrue();
        // Hidden labels must not force a value toggle on.
        options.ShowDataLabelValue.Should().BeFalse();
    }

    [Fact]
    public void DataLabels_Validate_RejectsStyleValuesOutOfRange()
    {
        var input = new ChartDataLabelsInput(
            ShowDataLabels: true,
            Position: ChartDataLabelPosition.Center,
            ShowValue: true,
            ShowCategoryName: false,
            ShowSeriesName: false,
            ShowPercentage: false,
            ShowLegendKey: false);

        ChartDataLabelsPlanner.ValidateIssue(input with { BorderThickness = 11 })
            .Should().Be(ChartDataLabelsValidationIssue.BorderThicknessOutOfRange);
        ChartDataLabelsPlanner.ValidateIssue(input with { FontSize = 5 })
            .Should().Be(ChartDataLabelsValidationIssue.FontSizeOutOfRange);
        ChartDataLabelsPlanner.ValidateIssue(input with { Angle = 120 })
            .Should().Be(ChartDataLabelsValidationIssue.AngleOutOfRange);
        ChartDataLabelsPlanner.Validate(input).Should().BeNull();
    }

    [Fact]
    public void DataLabels_Normalize_FallsBackAndClampsDialogDefaults()
    {
        var input = new ChartDataLabelsInput(
            ShowDataLabels: true,
            Position: (ChartDataLabelPosition)999,
            ShowValue: true,
            ShowCategoryName: false,
            ShowSeriesName: false,
            ShowPercentage: false,
            ShowLegendKey: false,
            Separator: (ChartDataLabelSeparator)999,
            NumberFormat: (ChartDataLabelNumberFormat)999,
            BorderThickness: 99,
            FontSize: double.NaN,
            Angle: -120);

        var normalized = ChartDataLabelsPlanner.Normalize(input);

        normalized.Position.Should().Be(ChartDataLabelPosition.BestFit);
        normalized.Separator.Should().Be(ChartDataLabelSeparator.Comma);
        normalized.NumberFormat.Should().Be(ChartDataLabelNumberFormat.General);
        normalized.BorderThickness.Should().Be(ChartDataLabelsPlanner.MaxBorderThickness);
        normalized.FontSize.Should().Be(11);
        normalized.Angle.Should().Be(ChartDataLabelsPlanner.MinAngle);
    }

    [Fact]
    public void DataLabels_TryParseDialogInput_ParsesColorsNumbersAndSelections()
    {
        ChartDataLabelsPlanner.TryParseDialogInput(
                showDataLabels: true,
                selectedPosition: ChartDataLabelPosition.OutsideEnd,
                showValue: false,
                showLegendKey: true,
                showCategoryName: true,
                showSeriesName: false,
                showPercentage: true,
                selectedSeparator: ChartDataLabelSeparator.NewLine,
                selectedNumberFormat: ChartDataLabelNumberFormat.Percent,
                showCallouts: true,
                fillColorText: "#010203",
                borderColorText: "none",
                textColorText: "#040506",
                borderThicknessText: "1.5",
                fontSizeText: "12",
                angleText: "-45",
                out var input,
                out var issue)
            .Should().BeTrue();

        issue.Should().Be(ChartDataLabelsParseIssue.None);
        input.Should().Be(new ChartDataLabelsInput(
            ShowDataLabels: true,
            Position: ChartDataLabelPosition.OutsideEnd,
            ShowValue: false,
            ShowCategoryName: true,
            ShowSeriesName: false,
            ShowPercentage: true,
            ShowLegendKey: true,
            Separator: ChartDataLabelSeparator.NewLine,
            NumberFormat: ChartDataLabelNumberFormat.Percent,
            ShowCallouts: true,
            FillColor: new CellColor(1, 2, 3),
            BorderColor: null,
            TextColor: new CellColor(4, 5, 6),
            BorderThickness: 1.5,
            FontSize: 12,
            Angle: -45));
    }

    [Theory]
    [InlineData("bad", "none", "#000000", "1", "12", "0", ChartDataLabelsParseIssue.FillColor)]
    [InlineData("none", "bad", "#000000", "1", "12", "0", ChartDataLabelsParseIssue.BorderColor)]
    [InlineData("none", "none", "bad", "1", "12", "0", ChartDataLabelsParseIssue.TextColor)]
    [InlineData("none", "none", "#000000", "11", "12", "0", ChartDataLabelsParseIssue.BorderThickness)]
    [InlineData("none", "none", "#000000", "1", "5", "0", ChartDataLabelsParseIssue.FontSize)]
    [InlineData("none", "none", "#000000", "1", "12", "120", ChartDataLabelsParseIssue.Angle)]
    public void DataLabels_TryParseDialogInput_ReportsFirstInvalidField(
        string fillColorText,
        string borderColorText,
        string textColorText,
        string borderThicknessText,
        string fontSizeText,
        string angleText,
        ChartDataLabelsParseIssue expectedIssue)
    {
        ChartDataLabelsPlanner.TryParseDialogInput(
                showDataLabels: true,
                selectedPosition: ChartDataLabelPosition.Center,
                showValue: true,
                showLegendKey: false,
                showCategoryName: false,
                showSeriesName: false,
                showPercentage: false,
                selectedSeparator: ChartDataLabelSeparator.Comma,
                selectedNumberFormat: ChartDataLabelNumberFormat.General,
                showCallouts: false,
                fillColorText,
                borderColorText,
                textColorText,
                borderThicknessText,
                fontSizeText,
                angleText,
                out _,
                out var issue)
            .Should().BeFalse();

        issue.Should().Be(expectedIssue);
    }

    [Fact]
    public void DataLabels_Plan_OmittedExtendedFieldsDoNotResetExistingStyle()
    {
        var options = ChartDataLabelsPlanner.Plan(new ChartDataLabelsInput(
            ShowDataLabels: true,
            Position: ChartDataLabelPosition.Center,
            ShowValue: true,
            ShowCategoryName: false,
            ShowSeriesName: false,
            ShowPercentage: false,
            ShowLegendKey: false));

        options.DataLabelSeparator.Should().BeNull();
        options.DataLabelNumberFormat.Should().BeNull();
        options.ShowDataLabelCallouts.Should().BeNull();
        options.DataLabelBorderThickness.Should().BeNull();
        options.DataLabelFontSize.Should().BeNull();
        options.DataLabelAngle.Should().BeNull();
    }

    [Fact]
    public void DataLabels_Plan_RoundTripsThroughSetChartLayoutCommand()
    {
        var chart = new ChartModel { Type = ChartType.Column };
        var options = ChartDataLabelsPlanner.Plan(new ChartDataLabelsInput(
            ShowDataLabels: true,
            Position: ChartDataLabelPosition.OutsideEnd,
            ShowValue: true,
            ShowCategoryName: true,
            ShowSeriesName: false,
            ShowPercentage: false,
            ShowLegendKey: true,
            Separator: ChartDataLabelSeparator.NewLine,
            NumberFormat: ChartDataLabelNumberFormat.Percent,
            ShowCallouts: true,
            FillColor: new CellColor(1, 2, 3),
            BorderColor: new CellColor(4, 5, 6),
            TextColor: new CellColor(7, 8, 9),
            BorderThickness: 1.5,
            FontSize: 13,
            Angle: -30));

        ApplyLayout(chart, options);

        chart.ShowDataLabels.Should().BeTrue();
        chart.DataLabelPosition.Should().Be(ChartDataLabelPosition.OutsideEnd);
        chart.ShowDataLabelValue.Should().BeTrue();
        chart.ShowDataLabelLegendKey.Should().BeTrue();
        chart.ShowDataLabelCategoryName.Should().BeTrue();
        chart.DataLabelSeparator.Should().Be(ChartDataLabelSeparator.NewLine);
        chart.DataLabelNumberFormat.Should().Be(ChartDataLabelNumberFormat.Percent);
        chart.ShowDataLabelCallouts.Should().BeTrue();
        chart.DataLabelFillColor.Should().Be(new CellColor(1, 2, 3));
        chart.DataLabelBorderColor.Should().Be(new CellColor(4, 5, 6));
        chart.DataLabelTextColor.Should().Be(new CellColor(7, 8, 9));
        chart.DataLabelBorderThickness.Should().Be(1.5);
        chart.DataLabelFontSize.Should().Be(13);
        chart.DataLabelAngle.Should().Be(-30);
    }

    // ---- ChartAxisPlanner ----------------------------------------------------------------------------

    [Fact]
    public void Axis_Read_ProjectsChosenAxisState()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            YAxisMinimum = 0,
            YAxisMaximum = 100,
            YAxisMajorUnit = 25,
            YAxisMinorUnit = 5,
            YAxisNumberFormat = ChartDataLabelNumberFormat.Currency,
            ShowYAxisMajorGridlines = true,
            ShowYAxisMinorGridlines = true,
            YAxisMajorGridlineColor = new CellColor(10, 20, 30),
            YAxisMinorGridlineColor = new CellColor(40, 50, 60),
            YAxisGridlineThickness = 1.5,
            YAxisMajorTickStyle = ChartAxisTickStyle.Cross,
            YAxisMinorTickStyle = ChartAxisTickStyle.Inside,
            ShowYAxisLabels = false,
            YAxisLabelTextColor = new CellColor(70, 80, 90),
            YAxisLabelFontSize = 13,
            YAxisLabelAngle = -30,
            YAxisLineColor = new CellColor(1, 2, 3),
            YAxisLineThickness = 2,
        };
        var input = ChartAxisPlanner.Read(chart, useXAxis: false);

        input.UseXAxis.Should().BeFalse();
        input.Minimum.Should().Be(0);
        input.Maximum.Should().Be(100);
        input.MajorUnit.Should().Be(25);
        input.MinorUnit.Should().Be(5);
        input.NumberFormat.Should().Be(ChartDataLabelNumberFormat.Currency);
        input.ShowMajorGridlines.Should().BeTrue();
        input.ShowMinorGridlines.Should().BeTrue();
        input.MajorGridlineColor.Should().Be(new CellColor(10, 20, 30));
        input.MinorGridlineColor.Should().Be(new CellColor(40, 50, 60));
        input.GridlineThickness.Should().Be(1.5);
        input.MajorTickStyle.Should().Be(ChartAxisTickStyle.Cross);
        input.MinorTickStyle.Should().Be(ChartAxisTickStyle.Inside);
        input.ShowLabels.Should().BeFalse();
        input.LabelTextColor.Should().Be(new CellColor(70, 80, 90));
        input.LabelFontSize.Should().Be(13);
        input.LabelAngle.Should().Be(-30);
        input.LineColor.Should().Be(new CellColor(1, 2, 3));
        input.LineThickness.Should().Be(2);
    }

    [Fact]
    public void Axis_TickStyleChoices_CoverExcelStyles()
    {
        ChartAxisPlanner.GetTickStyleChoices().Should().Equal(
            ChartAxisTickStyle.None,
            ChartAxisTickStyle.Inside,
            ChartAxisTickStyle.Outside,
            ChartAxisTickStyle.Cross);
    }

    [Fact]
    public void Axis_DialogDescriptor_CoversBoundsGridlinesTicksAndLineFields()
    {
        var sections = ChartAxisPlanner.GetDialogSections();
        var fields = sections.SelectMany(section => section.Fields).ToList();

        sections.Select(section => section.HeaderResourceKey)
            .Should().Equal(
                "ChartAxisFormat_AxisOptionsGroup",
                "ChartAxisFormat_GridlinesGroup",
                "ChartAxisFormat_TickMarksGroup");
        ChartAxisPlanner.GetAxisOptionsSection().HelpResourceKey
            .Should().Be("ChartAxisFormat_BoundsHelpText");
        fields.Select(field => field.Id).Should().Equal(
            ChartAxisDialogFieldId.Minimum,
            ChartAxisDialogFieldId.Maximum,
            ChartAxisDialogFieldId.MajorUnit,
            ChartAxisDialogFieldId.MinorUnit,
            ChartAxisDialogFieldId.LogScale,
            ChartAxisDialogFieldId.NumberFormat,
            ChartAxisDialogFieldId.MajorGridlines,
            ChartAxisDialogFieldId.MinorGridlines,
            ChartAxisDialogFieldId.MajorGridlineColor,
            ChartAxisDialogFieldId.MinorGridlineColor,
            ChartAxisDialogFieldId.GridlineThickness,
            ChartAxisDialogFieldId.MajorTickMarks,
            ChartAxisDialogFieldId.MinorTickMarks,
            ChartAxisDialogFieldId.ShowLabels,
            ChartAxisDialogFieldId.LabelTextColor,
            ChartAxisDialogFieldId.LabelFontSize,
            ChartAxisDialogFieldId.LabelAngle,
            ChartAxisDialogFieldId.LineColor,
            ChartAxisDialogFieldId.LineThickness);
        fields.Should().OnlyContain(field => !string.IsNullOrWhiteSpace(field.LabelResourceKey));
        fields.Should().OnlyContain(field => !string.IsNullOrWhiteSpace(field.AutomationId));
        ChartAxisPlanner.GetDialogField(ChartAxisDialogFieldId.Minimum)
            .HelpResourceKey.Should().Be("ChartAxisFormat_MinimumHelpText");
        ChartAxisPlanner.GetDialogField(ChartAxisDialogFieldId.GridlineThickness)
            .HelpResourceKey.Should().Be("ChartAxisFormat_GridlineWidthHelpText");
        ChartAxisPlanner.GetDialogField(ChartAxisDialogFieldId.LabelAngle)
            .HelpResourceKey.Should().Be("ChartAxisFormat_LabelAngleHelpText");
        ChartAxisPlanner.GetDialogField(ChartAxisDialogFieldId.LineThickness)
            .HelpResourceKey.Should().Be("ChartAxisFormat_AxisLineWidthHelpText");
    }

    [Fact]
    public void Axis_Validate_RejectsMinimumNotBelowMaximum()
    {
        var input = new ChartAxisInput(true, Minimum: 10, Maximum: 5, MajorUnit: null,
            LogScale: false, ChartDataLabelNumberFormat.General, ShowMajorGridlines: false, ShowMinorGridlines: false);

        ChartAxisPlanner.Validate(input).Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Axis_Validate_RejectsNonPositiveMajorUnit()
    {
        var input = new ChartAxisInput(true, Minimum: null, Maximum: null, MajorUnit: 0,
            LogScale: false, ChartDataLabelNumberFormat.General, ShowMajorGridlines: false, ShowMinorGridlines: false);

        ChartAxisPlanner.Validate(input).Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Axis_Validate_RejectsMinorUnitAndStyleValuesOutOfRange()
    {
        var input = new ChartAxisInput(
            UseXAxis: true,
            Minimum: null,
            Maximum: null,
            MajorUnit: null,
            LogScale: false,
            NumberFormat: ChartDataLabelNumberFormat.General,
            ShowMajorGridlines: false,
            ShowMinorGridlines: false);

        ChartAxisPlanner.ValidateIssue(input with { MinorUnit = 0 })
            .Should().Be(ChartAxisValidationIssue.MinorUnitNotPositive);
        ChartAxisPlanner.ValidateIssue(input with { GridlineThickness = 0 })
            .Should().Be(ChartAxisValidationIssue.GridlineThicknessNotPositive);
        ChartAxisPlanner.ValidateIssue(input with { LabelFontSize = 100 })
            .Should().Be(ChartAxisValidationIssue.LabelFontSizeOutOfRange);
        ChartAxisPlanner.ValidateIssue(input with { LabelAngle = -120 })
            .Should().Be(ChartAxisValidationIssue.LabelAngleOutOfRange);
        ChartAxisPlanner.ValidateIssue(input with { LineThickness = 0.1 })
            .Should().Be(ChartAxisValidationIssue.LineThicknessOutOfRange);
    }

    [Fact]
    public void Axis_Validate_AllowsAutoBounds()
    {
        var input = new ChartAxisInput(true, Minimum: null, Maximum: null, MajorUnit: null,
            LogScale: false, ChartDataLabelNumberFormat.General, ShowMajorGridlines: true, ShowMinorGridlines: false);

        ChartAxisPlanner.Validate(input).Should().BeNull();
    }

    [Fact]
    public void Axis_Plan_SetsClearBoundsFlag_WhenBothBoundsAuto()
    {
        var input = new ChartAxisInput(true, Minimum: null, Maximum: null, MajorUnit: null,
            LogScale: false, ChartDataLabelNumberFormat.General, ShowMajorGridlines: false, ShowMinorGridlines: false);

        var xOptions = ChartAxisPlanner.Plan(input);
        xOptions.ClearXAxisBounds.Should().BeTrue();

        var yOptions = ChartAxisPlanner.Plan(input with { UseXAxis = false });
        yOptions.ClearYAxisBounds.Should().BeTrue();
    }

    [Fact]
    public void Axis_Normalize_FallsBackAndClampsDialogDefaults()
    {
        var input = new ChartAxisInput(
            UseXAxis: false,
            Minimum: null,
            Maximum: null,
            MajorUnit: -1,
            LogScale: false,
            NumberFormat: (ChartDataLabelNumberFormat)999,
            ShowMajorGridlines: false,
            ShowMinorGridlines: false,
            MinorUnit: -2,
            GridlineThickness: 99,
            MajorTickStyle: (ChartAxisTickStyle)999,
            MinorTickStyle: (ChartAxisTickStyle)999,
            LabelFontSize: double.NaN,
            LabelAngle: -120,
            LineThickness: 0.1);

        var normalized = ChartAxisPlanner.Normalize(input);

        normalized.MajorUnit.Should().BeNull();
        normalized.MinorUnit.Should().BeNull();
        normalized.NumberFormat.Should().Be(ChartDataLabelNumberFormat.General);
        normalized.GridlineThickness.Should().Be(ChartAxisPlanner.MaxGridlineThickness);
        normalized.MajorTickStyle.Should().Be(ChartAxisTickStyle.Outside);
        normalized.MinorTickStyle.Should().Be(ChartAxisTickStyle.None);
        normalized.LabelFontSize.Should().Be(11);
        normalized.LabelAngle.Should().Be(ChartAxisPlanner.MinLabelAngle);
        normalized.LineThickness.Should().Be(ChartAxisPlanner.MinLineThickness);
    }

    [Fact]
    public void Axis_TryParseDialogInput_ParsesTextColorsAndSelections()
    {
        ChartAxisPlanner.TryParseDialogInput(
                useXAxis: false,
                minimumText: "0",
                maximumText: "50",
                majorUnitText: "10",
                minorUnitText: "5",
                logScale: true,
                selectedNumberFormat: ChartDataLabelNumberFormat.Currency,
                showMajorGridlines: true,
                showMinorGridlines: false,
                majorGridlineColorText: "#010203",
                minorGridlineColorText: "none",
                gridlineThicknessText: "1.5",
                selectedMajorTickStyle: ChartAxisTickStyle.Cross,
                selectedMinorTickStyle: ChartAxisTickStyle.Inside,
                showLabels: false,
                labelTextColorText: "#040506",
                labelFontSizeText: "13",
                labelAngleText: "-30",
                lineColorText: "#070809",
                lineThicknessText: "2",
                out var input,
                out var issue)
            .Should().BeTrue();

        issue.Should().Be(ChartAxisFormatParseIssue.None);
        input.Should().Be(new ChartAxisInput(
            UseXAxis: false,
            Minimum: 0,
            Maximum: 50,
            MajorUnit: 10,
            MinorUnit: 5,
            LogScale: true,
            NumberFormat: ChartDataLabelNumberFormat.Currency,
            ShowMajorGridlines: true,
            ShowMinorGridlines: false,
            MajorGridlineColor: new CellColor(1, 2, 3),
            MinorGridlineColor: null,
            GridlineThickness: 1.5,
            MajorTickStyle: ChartAxisTickStyle.Cross,
            MinorTickStyle: ChartAxisTickStyle.Inside,
            ShowLabels: false,
            LabelTextColor: new CellColor(4, 5, 6),
            LabelFontSize: 13,
            LabelAngle: -30,
            LineColor: new CellColor(7, 8, 9),
            LineThickness: 2));
    }

    [Theory]
    [InlineData("bad", "50", "10", "5", "#000000", "#000000", "1", "#000000", "12", "0", "#000000", "1", ChartAxisFormatParseIssue.Minimum)]
    [InlineData("0", "bad", "10", "5", "#000000", "#000000", "1", "#000000", "12", "0", "#000000", "1", ChartAxisFormatParseIssue.Maximum)]
    [InlineData("50", "0", "10", "5", "#000000", "#000000", "1", "#000000", "12", "0", "#000000", "1", ChartAxisFormatParseIssue.Maximum)]
    [InlineData("0", "50", "0", "5", "#000000", "#000000", "1", "#000000", "12", "0", "#000000", "1", ChartAxisFormatParseIssue.MajorUnit)]
    [InlineData("0", "50", "10", "0", "#000000", "#000000", "1", "#000000", "12", "0", "#000000", "1", ChartAxisFormatParseIssue.MinorUnit)]
    [InlineData("0", "50", "10", "5", "bad", "#000000", "1", "#000000", "12", "0", "#000000", "1", ChartAxisFormatParseIssue.MajorGridlineColor)]
    [InlineData("0", "50", "10", "5", "#000000", "bad", "1", "#000000", "12", "0", "#000000", "1", ChartAxisFormatParseIssue.MinorGridlineColor)]
    [InlineData("0", "50", "10", "5", "#000000", "#000000", "0", "#000000", "12", "0", "#000000", "1", ChartAxisFormatParseIssue.GridlineThickness)]
    [InlineData("0", "50", "10", "5", "#000000", "#000000", "1", "bad", "12", "0", "#000000", "1", ChartAxisFormatParseIssue.LabelTextColor)]
    [InlineData("0", "50", "10", "5", "#000000", "#000000", "1", "#000000", "5", "0", "#000000", "1", ChartAxisFormatParseIssue.LabelFontSize)]
    [InlineData("0", "50", "10", "5", "#000000", "#000000", "1", "#000000", "12", "120", "#000000", "1", ChartAxisFormatParseIssue.LabelAngle)]
    [InlineData("0", "50", "10", "5", "#000000", "#000000", "1", "#000000", "12", "0", "bad", "1", ChartAxisFormatParseIssue.LineColor)]
    [InlineData("0", "50", "10", "5", "#000000", "#000000", "1", "#000000", "12", "0", "#000000", "0.1", ChartAxisFormatParseIssue.LineThickness)]
    public void Axis_TryParseDialogInput_ReportsFirstInvalidField(
        string minimumText,
        string maximumText,
        string majorUnitText,
        string minorUnitText,
        string majorGridlineColorText,
        string minorGridlineColorText,
        string gridlineThicknessText,
        string labelTextColorText,
        string labelFontSizeText,
        string labelAngleText,
        string lineColorText,
        string lineThicknessText,
        ChartAxisFormatParseIssue expectedIssue)
    {
        ChartAxisPlanner.TryParseDialogInput(
                useXAxis: true,
                minimumText,
                maximumText,
                majorUnitText,
                minorUnitText,
                logScale: false,
                selectedNumberFormat: ChartDataLabelNumberFormat.General,
                showMajorGridlines: false,
                showMinorGridlines: false,
                majorGridlineColorText,
                minorGridlineColorText,
                gridlineThicknessText,
                selectedMajorTickStyle: ChartAxisTickStyle.Outside,
                selectedMinorTickStyle: ChartAxisTickStyle.None,
                showLabels: true,
                labelTextColorText,
                labelFontSizeText,
                labelAngleText,
                lineColorText,
                lineThicknessText,
                out _,
                out var issue)
            .Should().BeFalse();

        issue.Should().Be(expectedIssue);
    }

    [Fact]
    public void Axis_Plan_OmittedExtendedFieldsDoNotResetExistingStyle()
    {
        var options = ChartAxisPlanner.Plan(new ChartAxisInput(
            UseXAxis: true,
            Minimum: null,
            Maximum: null,
            MajorUnit: null,
            LogScale: false,
            NumberFormat: ChartDataLabelNumberFormat.General,
            ShowMajorGridlines: false,
            ShowMinorGridlines: false));

        options.XAxisMinorUnit.Should().BeNull();
        options.XAxisGridlineThickness.Should().BeNull();
        options.XAxisMajorTickStyle.Should().BeNull();
        options.XAxisMinorTickStyle.Should().BeNull();
        options.ShowXAxisLabels.Should().BeNull();
        options.XAxisLabelFontSize.Should().BeNull();
        options.XAxisLabelAngle.Should().BeNull();
        options.XAxisLineThickness.Should().BeNull();
    }

    [Fact]
    public void Axis_Plan_RoundTripsThroughSetChartLayoutCommand()
    {
        var chart = new ChartModel { Type = ChartType.Column };
        var input = new ChartAxisInput(
            UseXAxis: false,
            Minimum: 0,
            Maximum: 50,
            MajorUnit: 10,
            LogScale: false,
            NumberFormat: ChartDataLabelNumberFormat.Number,
            ShowMajorGridlines: true,
            ShowMinorGridlines: true,
            MinorUnit: 5,
            MajorGridlineColor: new CellColor(10, 20, 30),
            MinorGridlineColor: new CellColor(40, 50, 60),
            GridlineThickness: 1.5,
            MajorTickStyle: ChartAxisTickStyle.Cross,
            MinorTickStyle: ChartAxisTickStyle.Inside,
            ShowLabels: false,
            LabelTextColor: new CellColor(70, 80, 90),
            LabelFontSize: 13,
            LabelAngle: -30,
            LineColor: new CellColor(1, 2, 3),
            LineThickness: 2);

        ApplyLayout(chart, ChartAxisPlanner.Plan(input));

        chart.YAxisMinimum.Should().Be(0);
        chart.YAxisMaximum.Should().Be(50);
        chart.YAxisMajorUnit.Should().Be(10);
        chart.YAxisMinorUnit.Should().Be(5);
        chart.YAxisNumberFormat.Should().Be(ChartDataLabelNumberFormat.Number);
        chart.ShowYAxisMajorGridlines.Should().BeTrue();
        chart.ShowYAxisMinorGridlines.Should().BeTrue();
        chart.YAxisMajorGridlineColor.Should().Be(new CellColor(10, 20, 30));
        chart.YAxisMinorGridlineColor.Should().Be(new CellColor(40, 50, 60));
        chart.YAxisGridlineThickness.Should().Be(1.5);
        chart.YAxisMajorTickStyle.Should().Be(ChartAxisTickStyle.Cross);
        chart.YAxisMinorTickStyle.Should().Be(ChartAxisTickStyle.Inside);
        chart.ShowYAxisLabels.Should().BeFalse();
        chart.YAxisLabelTextColor.Should().Be(new CellColor(70, 80, 90));
        chart.YAxisLabelFontSize.Should().Be(13);
        chart.YAxisLabelAngle.Should().Be(-30);
        chart.YAxisLineColor.Should().Be(new CellColor(1, 2, 3));
        chart.YAxisLineThickness.Should().Be(2);
    }

    [Fact]
    public void Axis_CategoryAxisVisualStyle_IsNotClearedByUnsupportedNumericBoundsGuard()
    {
        var chart = new ChartModel { Type = ChartType.Column };

        ApplyLayout(chart, new ChartLayoutOptions(
            XAxisNumberFormat: ChartDataLabelNumberFormat.Number,
            XAxisMajorTickStyle: ChartAxisTickStyle.Cross,
            XAxisMinorTickStyle: ChartAxisTickStyle.Inside,
            ShowXAxisLabels: false,
            XAxisLabelFontSize: 13,
            XAxisLabelAngle: -45,
            XAxisLineColor: new CellColor(4, 5, 6),
            XAxisLineThickness: 2));

        chart.XAxisNumberFormat.Should().Be(ChartDataLabelNumberFormat.Number);
        chart.XAxisMajorTickStyle.Should().Be(ChartAxisTickStyle.Cross);
        chart.XAxisMinorTickStyle.Should().Be(ChartAxisTickStyle.Inside);
        chart.ShowXAxisLabels.Should().BeFalse();
        chart.XAxisLabelFontSize.Should().Be(13);
        chart.XAxisLabelAngle.Should().Be(-45);
        chart.XAxisLineColor.Should().Be(new CellColor(4, 5, 6));
        chart.XAxisLineThickness.Should().Be(2);
    }

    [Fact]
    public void Axis_CanToggleSecondaryAxis_RequiresSupportedChartAndEnoughSeries()
    {
        ChartAxisPlanner.CanToggleSecondaryAxis(MakeChartWithSeries(ChartType.Column, columns: 4))
            .Should().BeTrue();
        ChartAxisPlanner.CanToggleSecondaryAxis(MakeChartWithSeries(ChartType.Column, columns: 2))
            .Should().BeFalse();
        ChartAxisPlanner.CanToggleSecondaryAxis(MakeChartWithSeries(ChartType.Pie, columns: 4))
            .Should().BeFalse();

        var alreadyVisible = MakeChartWithSeries(ChartType.Column, columns: 2);
        alreadyVisible.ShowSecondaryAxis = true;
        ChartAxisPlanner.CanToggleSecondaryAxis(alreadyVisible).Should().BeTrue();
    }

    [Fact]
    public void Axis_PlanSecondaryAxisToggle_ClearsSeriesIndexes()
    {
        var chart = MakeChartWithSeries(ChartType.Column, columns: 4);
        chart.ShowSecondaryAxis = true;
        chart.SecondaryAxisSeriesIndexes = [1, 2];

        var options = ChartAxisPlanner.PlanSecondaryAxisToggle(chart);

        options.ShowSecondaryAxis.Should().BeFalse();
        options.SecondaryAxisSeriesIndexes.Should().BeEmpty();
    }

    [Fact]
    public void Axis_PlanQuickCommand_ProjectsRepeatableButtonOptions()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            YAxisMajorTickStyle = ChartAxisTickStyle.Outside,
            YAxisMinorTickStyle = ChartAxisTickStyle.None,
            ShowYAxisLabels = true,
            YAxisLabelFontSize = 14,
            YAxisLabelAngle = 0,
            YAxisLineThickness = 0.5,
            ShowYAxisMajorGridlines = false,
            ShowYAxisMinorGridlines = false,
            YAxisGridlineThickness = 3,
            YAxisNumberFormat = ChartDataLabelNumberFormat.General,
        };

        ChartAxisPlanner.PlanQuickCommand(chart, useXAxis: false, ChartAxisQuickCommand.TickMarks)
            .YAxisMajorTickStyle.Should().Be(ChartAxisTickStyle.Inside);
        ChartAxisPlanner.PlanQuickCommand(chart, useXAxis: false, ChartAxisQuickCommand.Labels)
            .ShowYAxisLabels.Should().BeFalse();
        ChartAxisPlanner.PlanQuickCommand(chart, useXAxis: false, ChartAxisQuickCommand.LabelFont)
            .YAxisLabelFontSize.Should().Be(9);
        ChartAxisPlanner.PlanQuickCommand(chart, useXAxis: false, ChartAxisQuickCommand.LabelAngle)
            .YAxisLabelAngle.Should().Be(-45);
        ChartAxisPlanner.PlanQuickCommand(chart, useXAxis: false, ChartAxisQuickCommand.AxisLine)
            .YAxisLineThickness.Should().Be(1.5);
        ChartAxisPlanner.PlanQuickCommand(chart, useXAxis: false, ChartAxisQuickCommand.Gridlines)
            .ShowYAxisMajorGridlines.Should().BeTrue();
        ChartAxisPlanner.PlanQuickCommand(chart, useXAxis: false, ChartAxisQuickCommand.GridlineStyle)
            .YAxisGridlineThickness.Should().Be(1);
        ChartAxisPlanner.PlanQuickCommand(chart, useXAxis: false, ChartAxisQuickCommand.NumberFormat)
            .YAxisNumberFormat.Should().Be(ChartDataLabelNumberFormat.Number);
    }

    [Fact]
    public void Axis_PlanLogScaleToggle_AddsPositiveBoundsWhenEnabling()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var chart = CreateChartWithRange(sheet.Id, ChartType.Column, columns: 2);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(-5));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(25));

        var plan = ChartAxisPlanner.PlanLogScaleToggle(sheet, chart, useXAxis: false);

        plan.Success.Should().BeTrue();
        plan.Options.Should().NotBeNull();
        plan.Options!.YAxisLogScale.Should().BeTrue();
        plan.Options.YAxisMinimum.Should().Be(1);
        plan.Options.YAxisMaximum.Should().Be(25);
    }

    [Fact]
    public void Axis_PlanBoundsToggle_ReportsUnsupportedAndNumericDataIssues()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var unsupported = CreateChartWithRange(sheet.Id, ChartType.Pie, columns: 2);
        var noNumericData = CreateChartWithRange(sheet.Id, ChartType.Column, columns: 2);

        ChartAxisPlanner.PlanBoundsToggle(sheet, unsupported, useXAxis: false).Issue
            .Should().Be(ChartAxisCommandIssue.UnsupportedBounds);
        ChartAxisPlanner.PlanBoundsToggle(sheet, noNumericData, useXAxis: false).Issue
            .Should().Be(ChartAxisCommandIssue.NumericBoundsRequired);
    }

    [Fact]
    public void Axis_PlanBoundsToggle_ClearsExistingBoundsBeforeReadingData()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var chart = CreateChartWithRange(sheet.Id, ChartType.Column, columns: 2);
        chart.YAxisMinimum = 10;

        var plan = ChartAxisPlanner.PlanBoundsToggle(sheet, chart, useXAxis: false);

        plan.Success.Should().BeTrue();
        plan.Options.Should().NotBeNull();
        plan.Options!.ClearYAxisBounds.Should().BeTrue();
    }

    // ---- ChartSeriesFormatPlanner --------------------------------------------------------------------

    [Fact]
    public void SeriesFormat_Read_ReturnsStoredFormat_ForChosenSeries()
    {
        // A 3-column range (category + two value columns) yields >= 2 data series so index 1 is valid.
        var chart = MultiSeriesChart();
        chart.SeriesFormats.Add(new ChartSeriesFormat(1, FillColor: new CellColor(10, 20, 30), StrokeThickness: 2.5));
        var input = ChartSeriesFormatPlanner.Read(chart, 1);

        input.SeriesIndex.Should().Be(1);
        input.FillColor.Should().Be(new CellColor(10, 20, 30));
        input.StrokeThickness.Should().Be(2.5);
    }

    [Fact]
    public void SeriesFormat_ReadDefault_UsesFirstStoredSeriesFormatIndexWithinSeriesRange()
    {
        var chart = MultiSeriesChart();
        chart.SeriesFormats.Add(new ChartSeriesFormat(1, DashStyle: ChartLineDashStyle.Dot));

        var input = ChartSeriesFormatPlanner.ReadDefault(chart);

        input.SeriesIndex.Should().Be(1);
        input.DashStyle.Should().Be(ChartLineDashStyle.Dot);
    }

    [Fact]
    public void SeriesFormat_DialogDescriptor_CoversSeriesAndFillLineFields()
    {
        var sections = ChartSeriesFormatPlanner.GetDialogSections();
        var fields = sections.SelectMany(section => section.Fields).ToList();

        sections.Select(section => section.HeaderResourceKey)
            .Should().Equal("ChartSeriesFormat_SeriesOptionsGroup", "ChartDialog_FillLineGroup");
        fields.Select(field => field.Id).Should().Equal(
            ChartSeriesFormatDialogFieldId.Series,
            ChartSeriesFormatDialogFieldId.FillColor,
            ChartSeriesFormatDialogFieldId.StrokeColor,
            ChartSeriesFormatDialogFieldId.StrokeThickness,
            ChartSeriesFormatDialogFieldId.DashStyle,
            ChartSeriesFormatDialogFieldId.MarkerStyle,
            ChartSeriesFormatDialogFieldId.MarkerSize);
        fields.Should().OnlyContain(field => !string.IsNullOrWhiteSpace(field.LabelResourceKey));
        fields.Should().OnlyContain(field => !string.IsNullOrWhiteSpace(field.AutomationId));
        ChartSeriesFormatPlanner.GetDialogField(ChartSeriesFormatDialogFieldId.StrokeThickness)
            .HelpResourceKey.Should().Be("ChartSeriesFormat_LineWidthHelpText");
        ChartSeriesFormatPlanner.GetDialogField(ChartSeriesFormatDialogFieldId.MarkerSize)
            .HelpResourceKey.Should().Be("ChartSeriesFormat_MarkerSizeHelpText");
    }

    [Fact]
    public void SeriesFormat_Read_ClampsRequestedIndexIntoSeriesRange()
    {
        // An empty data range has at most one series, so an out-of-range request clamps to 0.
        var chart = new ChartModel { Type = ChartType.Line };
        ChartSeriesFormatPlanner.Read(chart, 5).SeriesIndex.Should().Be(0);
    }

    private static ChartModel MultiSeriesChart()
    {
        var sheet = new SheetId(Guid.NewGuid());
        return new ChartModel
        {
            Type = ChartType.Line,
            FirstColIsCategories = true,
            DataRange = new GridRange(
                new CellAddress(sheet, 1, 1),
                new CellAddress(sheet, 4, 3)),
        };
    }

    [Fact]
    public void SeriesFormat_Validate_RejectsNonPositiveLineWidthAndMarkerSize()
    {
        ChartSeriesFormatPlanner.Validate(new ChartSeriesFormatInput(0, null, null, StrokeThickness: 0, null, null))
            .Should().NotBeNullOrWhiteSpace();
        ChartSeriesFormatPlanner.Validate(new ChartSeriesFormatInput(0, null, null, null, null, MarkerSize: -1))
            .Should().NotBeNullOrWhiteSpace();
        ChartSeriesFormatPlanner.Validate(new ChartSeriesFormatInput(0, null, null, null, null, null))
            .Should().BeNull();
    }

    [Fact]
    public void SeriesFormat_TryParseDialogInput_ParsesColorsOptionalStylesAndDefaultsSeriesIndex()
    {
        ChartSeriesFormatPlanner.TryParseDialogInput(
                seriesIndex: -2,
                fillColorText: "#0A141E",
                strokeColorText: "none",
                strokeThicknessText: "2.5",
                selectedDashStyle: ChartLineDashStyle.Dash,
                selectedMarkerStyle: ChartMarkerStyle.Diamond,
                markerSizeText: "9",
                out var input,
                out var issue)
            .Should().BeTrue();

        issue.Should().Be(ChartSeriesFormatParseIssue.None);
        input.Should().Be(new ChartSeriesFormatInput(
            0,
            new CellColor(10, 20, 30),
            null,
            2.5,
            ChartMarkerStyle.Diamond,
            9,
            ChartLineDashStyle.Dash));
    }

    [Theory]
    [InlineData("bad", "none", "", "", ChartSeriesFormatParseIssue.FillColor)]
    [InlineData("none", "bad", "", "", ChartSeriesFormatParseIssue.StrokeColor)]
    [InlineData("none", "none", "0", "", ChartSeriesFormatParseIssue.StrokeThickness)]
    [InlineData("none", "none", "", "-1", ChartSeriesFormatParseIssue.MarkerSize)]
    public void SeriesFormat_TryParseDialogInput_ReportsFirstInvalidField(
        string fillColorText,
        string strokeColorText,
        string strokeThicknessText,
        string markerSizeText,
        ChartSeriesFormatParseIssue expectedIssue)
    {
        ChartSeriesFormatPlanner.TryParseDialogInput(
                0,
                fillColorText,
                strokeColorText,
                strokeThicknessText,
                null,
                null,
                markerSizeText,
                out _,
                out var issue)
            .Should().BeFalse();

        issue.Should().Be(expectedIssue);
    }

    [Fact]
    public void SeriesFormat_Plan_ReplacesMatchingSeries_AndPreservesOthers()
    {
        var chart = new ChartModel { Type = ChartType.Line };
        chart.SeriesFormats.Add(new ChartSeriesFormat(0, FillColor: new CellColor(1, 1, 1)));
        chart.SeriesFormats.Add(new ChartSeriesFormat(1, FillColor: new CellColor(2, 2, 2)));

        var options = ChartSeriesFormatPlanner.Plan(chart, new ChartSeriesFormatInput(
            1, FillColor: new CellColor(9, 9, 9), StrokeColor: null, StrokeThickness: null,
            MarkerStyle: ChartMarkerStyle.Circle, MarkerSize: 6));

        options.SeriesFormats.Should().HaveCount(2);
        options.SeriesFormats!.Single(f => f.SeriesIndex == 0).FillColor.Should().Be(new CellColor(1, 1, 1));
        var updated = options.SeriesFormats!.Single(f => f.SeriesIndex == 1);
        updated.FillColor.Should().Be(new CellColor(9, 9, 9));
        updated.MarkerStyle.Should().Be(ChartMarkerStyle.Circle);
        updated.MarkerSize.Should().Be(6);
    }

    [Fact]
    public void SeriesFormat_Plan_MergesDashStyle_AndPreservesExistingFormatPolicy()
    {
        var chart = new ChartModel { Type = ChartType.Line };
        chart.SeriesFormats.Add(new ChartSeriesFormat(
            1,
            FillThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2),
            StrokeThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent3),
            DashStyle: ChartLineDashStyle.Dash,
            MarkerStyle: ChartMarkerStyle.Circle));

        var options = ChartSeriesFormatPlanner.Plan(chart, new ChartSeriesFormatInput(
            1,
            FillColor: null,
            StrokeColor: new CellColor(40, 50, 60),
            StrokeThickness: 2.5,
            MarkerStyle: ChartMarkerStyle.Diamond,
            MarkerSize: 9,
            DashStyle: ChartLineDashStyle.Dot));

        var updated = options.SeriesFormats.Should().ContainSingle(format => format.SeriesIndex == 1).Which;
        updated.FillThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2));
        updated.StrokeColor.Should().Be(new CellColor(40, 50, 60));
        updated.StrokeThemeColor.Should().BeNull();
        updated.DashStyle.Should().Be(ChartLineDashStyle.Dot);
        updated.MarkerStyle.Should().Be(ChartMarkerStyle.Diamond);
        updated.MarkerSize.Should().Be(9);
    }

    [Fact]
    public void SeriesFormat_Plan_NullDashStyle_ClearsExistingDash()
    {
        var chart = new ChartModel { Type = ChartType.Line };
        chart.SeriesFormats.Add(new ChartSeriesFormat(0, DashStyle: ChartLineDashStyle.Dash));

        var options = ChartSeriesFormatPlanner.Plan(chart, new ChartSeriesFormatInput(
            0, null, null, null, null, null, DashStyle: null));

        options.SeriesFormats.Should().ContainSingle(format => format.SeriesIndex == 0)
            .Which.DashStyle.Should().BeNull();
    }

    [Fact]
    public void SeriesFormat_Plan_AppendsWhenSeriesHasNoExistingFormat()
    {
        var chart = new ChartModel { Type = ChartType.Line };
        var options = ChartSeriesFormatPlanner.Plan(chart, new ChartSeriesFormatInput(
            2, FillColor: new CellColor(5, 5, 5), null, null, null, null));

        options.SeriesFormats.Should().ContainSingle(f => f.SeriesIndex == 2 && f.FillColor == new CellColor(5, 5, 5));
    }

    [Fact]
    public void SeriesFormat_Plan_RoundTripsThroughSetChartLayoutCommand()
    {
        var chart = new ChartModel { Type = ChartType.Line };
        var options = ChartSeriesFormatPlanner.Plan(chart, new ChartSeriesFormatInput(
            0, FillColor: new CellColor(7, 8, 9), StrokeColor: new CellColor(1, 2, 3),
            StrokeThickness: 3, MarkerStyle: ChartMarkerStyle.Square, MarkerSize: 8));

        ApplyLayout(chart, options);

        var format = chart.SeriesFormats.Single(f => f.SeriesIndex == 0);
        format.FillColor.Should().Be(new CellColor(7, 8, 9));
        format.StrokeColor.Should().Be(new CellColor(1, 2, 3));
        format.StrokeThickness.Should().Be(3);
        format.MarkerStyle.Should().Be(ChartMarkerStyle.Square);
        format.MarkerSize.Should().Be(8);
    }

    // ---- ChartTrendlinePlanner -----------------------------------------------------------------------

    [Fact]
    public void Trendline_TypeChoices_CoverAllSixTypes()
    {
        var types = ChartTrendlinePlanner.GetTypeChoices().Select(c => c.Type).ToList();

        types.Should().BeEquivalentTo(new[]
        {
            ChartTrendlineType.Linear, ChartTrendlineType.Exponential, ChartTrendlineType.Logarithmic,
            ChartTrendlineType.Power, ChartTrendlineType.MovingAverage, ChartTrendlineType.Polynomial
        });
    }

    [Fact]
    public void Trendline_DashStyleChoices_ComeFromSharedPlanner()
    {
        ChartTrendlinePlanner.GetDashStyleChoices()
            .Should().BeEquivalentTo(Enum.GetValues<ChartLineDashStyle>());
    }

    [Fact]
    public void Trendline_DialogDescriptor_CoversOptionsAndLineStyleFields()
    {
        var sections = ChartTrendlinePlanner.GetDialogSections();
        var fields = sections.SelectMany(section => section.Fields).ToList();

        sections.Select(section => section.HeaderResourceKey)
            .Should().Equal("ChartTrendline_OptionsGroup", "ChartDialog_FillLineGroup");
        fields.Select(field => field.Id).Should().Equal(
            ChartTrendlineDialogFieldId.ShowTrendline,
            ChartTrendlineDialogFieldId.Type,
            ChartTrendlineDialogFieldId.Period,
            ChartTrendlineDialogFieldId.Order,
            ChartTrendlineDialogFieldId.ShowEquation,
            ChartTrendlineDialogFieldId.ShowRSquared,
            ChartTrendlineDialogFieldId.LineColor,
            ChartTrendlineDialogFieldId.LineThickness,
            ChartTrendlineDialogFieldId.DashStyle);
        fields.Should().OnlyContain(field => !string.IsNullOrWhiteSpace(field.LabelResourceKey));
        fields.Should().OnlyContain(field => !string.IsNullOrWhiteSpace(field.AutomationId));
        ChartTrendlinePlanner.GetDialogField(ChartTrendlineDialogFieldId.Period)
            .HelpResourceKey.Should().Be("ChartTrendline_PeriodHelpText");
        ChartTrendlinePlanner.GetDialogField(ChartTrendlineDialogFieldId.LineThickness)
            .HelpResourceKey.Should().Be("ChartTrendline_LineWidthHelpText");
    }

    [Fact]
    public void Trendline_Supports_OnlyTrendlineCapableTypes()
    {
        ChartTrendlinePlanner.SupportsTrendlines(ChartType.Line).Should().BeTrue();
        ChartTrendlinePlanner.SupportsTrendlines(ChartType.Scatter).Should().BeTrue();
        ChartTrendlinePlanner.SupportsTrendlines(ChartType.Pie).Should().BeFalse();
    }

    [Fact]
    public void Trendline_Plan_ClampsPeriodAndOrder()
    {
        var options = ChartTrendlinePlanner.Plan(new ChartTrendlineInput(
            ShowTrendline: true, ChartTrendlineType.MovingAverage,
            Period: 1000, Order: 99, ShowEquation: true, ShowRSquared: true));

        options.ShowLinearTrendline.Should().BeTrue();
        options.TrendlineType.Should().Be(ChartTrendlineType.MovingAverage);
        options.TrendlinePeriod.Should().Be(ChartTrendlinePlanner.MaxPeriod);
        options.TrendlineOrder.Should().Be(ChartTrendlinePlanner.MaxOrder);
        options.ShowTrendlineEquation.Should().BeTrue();
        options.ShowTrendlineRSquared.Should().BeTrue();
    }

    [Fact]
    public void Trendline_TryParseDialogInput_BuildsInputAndDefaultsMissingSelections()
    {
        ChartTrendlinePlanner.TryParseDialogInput(
                showTrendline: true,
                selectedType: null,
                periodText: "4",
                orderText: "5",
                showEquation: true,
                showRSquared: false,
                colorText: "#506070",
                thicknessText: "2.25",
                selectedDashStyle: null,
                out var input,
                out var issue)
            .Should().BeTrue();

        issue.Should().Be(ChartTrendlineDialogParseIssue.None);
        input.ShowTrendline.Should().BeTrue();
        input.Type.Should().Be(ChartTrendlineType.Linear);
        input.Period.Should().Be(4);
        input.Order.Should().Be(5);
        input.ShowEquation.Should().BeTrue();
        input.ShowRSquared.Should().BeFalse();
        input.Color.Should().Be(new CellColor(0x50, 0x60, 0x70));
        input.Thickness.Should().Be(2.25);
        input.DashStyle.Should().Be(ChartLineDashStyle.Solid);
    }

    [Theory]
    [InlineData("1", "2", "#000000", "1", ChartTrendlineDialogParseIssue.Period)]
    [InlineData("2", "7", "#000000", "1", ChartTrendlineDialogParseIssue.Order)]
    [InlineData("2", "2", "bad", "1", ChartTrendlineDialogParseIssue.Color)]
    [InlineData("2", "2", "#000000", "0.1", ChartTrendlineDialogParseIssue.Thickness)]
    public void Trendline_TryParseDialogInput_ReportsFirstInvalidField(
        string periodText,
        string orderText,
        string colorText,
        string thicknessText,
        ChartTrendlineDialogParseIssue expectedIssue)
    {
        ChartTrendlinePlanner.TryParseDialogInput(
                showTrendline: false,
                selectedType: ChartTrendlineType.Polynomial,
                periodText,
                orderText,
                showEquation: false,
                showRSquared: false,
                colorText,
                thicknessText,
                selectedDashStyle: ChartLineDashStyle.Dash,
                out _,
                out var issue)
            .Should().BeFalse();

        issue.Should().Be(expectedIssue);
    }

    [Fact]
    public void Trendline_Read_ProjectsLineStyleForShellsThatSurfaceIt()
    {
        var chart = new ChartModel
        {
            ShowLinearTrendline = true,
            TrendlineColor = new CellColor(80, 90, 100),
            TrendlineThickness = 2.25,
            TrendlineDashStyle = ChartLineDashStyle.Dot,
        };

        var input = ChartTrendlinePlanner.Read(chart);

        input.Color.Should().Be(new CellColor(80, 90, 100));
        input.Thickness.Should().Be(2.25);
        input.DashStyle.Should().Be(ChartLineDashStyle.Dot);
    }

    [Fact]
    public void Trendline_Plan_ProjectsLineStyleWhenProvided()
    {
        var options = ChartTrendlinePlanner.Plan(new ChartTrendlineInput(
            ShowTrendline: true,
            Type: ChartTrendlineType.Polynomial,
            Period: 4,
            Order: 5,
            ShowEquation: true,
            ShowRSquared: true,
            Color: new CellColor(80, 90, 100),
            Thickness: 2.25,
            DashStyle: ChartLineDashStyle.Dot));

        options.TrendlineColor.Should().Be(new CellColor(80, 90, 100));
        options.TrendlineThickness.Should().Be(2.25);
        options.TrendlineDashStyle.Should().Be(ChartLineDashStyle.Dot);
    }

    [Fact]
    public void Trendline_Plan_LeavesLineStyleUnsetWhenShellDoesNotProvideIt()
    {
        var options = ChartTrendlinePlanner.Plan(new ChartTrendlineInput(
            ShowTrendline: true,
            Type: ChartTrendlineType.Linear,
            Period: 2,
            Order: 2,
            ShowEquation: false,
            ShowRSquared: false));

        options.TrendlineColor.Should().BeNull();
        options.TrendlineThickness.Should().BeNull();
        options.TrendlineDashStyle.Should().BeNull();
    }

    [Fact]
    public void Trendline_Plan_RoundTripsThroughSetChartLayoutCommand()
    {
        var chart = new ChartModel { Type = ChartType.Scatter };
        var options = ChartTrendlinePlanner.Plan(new ChartTrendlineInput(
            ShowTrendline: true, ChartTrendlineType.Exponential,
            Period: 3, Order: 2, ShowEquation: true, ShowRSquared: false));

        ApplyLayout(chart, options);

        chart.ShowLinearTrendline.Should().BeTrue();
        chart.TrendlineType.Should().Be(ChartTrendlineType.Exponential);
        chart.ShowTrendlineEquation.Should().BeTrue();
        chart.ShowTrendlineRSquared.Should().BeFalse();
    }

    // ---- ChartErrorBarsPlanner -----------------------------------------------------------------------

    [Fact]
    public void ErrorBars_KindChoices_CoverAllFourKinds()
    {
        var kinds = ChartErrorBarsPlanner.GetKindChoices().Select(c => c.Kind).ToList();

        kinds.Should().BeEquivalentTo(new[]
        {
            ChartErrorBarKind.StandardError, ChartErrorBarKind.Percentage,
            ChartErrorBarKind.FixedValue, ChartErrorBarKind.Custom
        });
    }

    [Fact]
    public void ErrorBars_DirectionChoices_CoverAllThreeDirections()
    {
        var directions = ChartErrorBarsPlanner.GetDirectionChoices().Select(c => c.Direction).ToList();

        directions.Should().BeEquivalentTo(new[]
        {
            ChartErrorBarDirection.Both, ChartErrorBarDirection.Plus, ChartErrorBarDirection.Minus
        });
    }

    [Fact]
    public void ErrorBars_DialogDescriptor_CoversErrorAmountFields()
    {
        var sections = ChartErrorBarsPlanner.GetDialogSections();
        var fields = sections.SelectMany(section => section.Fields).ToList();

        sections.Should().ContainSingle()
            .Which.HeaderResourceKey.Should().Be("ChartErrorBars_ErrorAmountGroup");
        fields.Select(field => field.Id).Should().Equal(
            ChartErrorBarsDialogFieldId.ShowErrorBars,
            ChartErrorBarsDialogFieldId.Kind,
            ChartErrorBarsDialogFieldId.Direction,
            ChartErrorBarsDialogFieldId.Value,
            ChartErrorBarsDialogFieldId.EndCaps);
        fields.Should().OnlyContain(field => !string.IsNullOrWhiteSpace(field.LabelResourceKey));
        fields.Should().OnlyContain(field => !string.IsNullOrWhiteSpace(field.AutomationId));
        ChartErrorBarsPlanner.GetDialogField(ChartErrorBarsDialogFieldId.Value)
            .HelpResourceKey.Should().Be("ChartErrorBars_ValueHelpText");
        ChartErrorBarsPlanner.GetDialogField(ChartErrorBarsDialogFieldId.Value)
            .AutomationNameResourceKey.Should().Be("ChartErrorBars_ValueAutomationName");
    }

    [Fact]
    public void ErrorBars_Supports_OnlyErrorBarCapableTypes()
    {
        ChartErrorBarsPlanner.SupportsErrorBars(ChartType.Column).Should().BeTrue();
        ChartErrorBarsPlanner.SupportsErrorBars(ChartType.Scatter).Should().BeTrue();
        ChartErrorBarsPlanner.SupportsErrorBars(ChartType.Pie).Should().BeFalse();
    }

    [Fact]
    public void ErrorBars_Plan_ClampsValue()
    {
        var options = ChartErrorBarsPlanner.Plan(new ChartErrorBarsInput(
            ShowErrorBars: true, ChartErrorBarKind.FixedValue,
            ChartErrorBarDirection.Plus, Value: 99999, EndCaps: true));

        options.ShowErrorBars.Should().BeTrue();
        options.ErrorBarKind.Should().Be(ChartErrorBarKind.FixedValue);
        options.ErrorBarDirection.Should().Be(ChartErrorBarDirection.Plus);
        options.ErrorBarValue.Should().Be(ChartErrorBarsPlanner.MaxValue);
        options.ErrorBarEndCaps.Should().BeTrue();
    }

    [Fact]
    public void ErrorBars_Normalize_DefaultsInvalidEnumsAndClampsValue()
    {
        var input = ChartErrorBarsPlanner.Normalize(new ChartErrorBarsInput(
            ShowErrorBars: true,
            Kind: (ChartErrorBarKind)999,
            Direction: (ChartErrorBarDirection)999,
            Value: double.NaN,
            EndCaps: false));

        input.Kind.Should().Be(ChartErrorBarKind.StandardError);
        input.Direction.Should().Be(ChartErrorBarDirection.Both);
        input.Value.Should().Be(5);
        input.EndCaps.Should().BeFalse();
    }

    [Fact]
    public void ErrorBars_TryParseDialogInput_ParsesValueAndSelections()
    {
        ChartErrorBarsPlanner.TryParseDialogInput(
                showErrorBars: true,
                selectedKind: ChartErrorBarKind.FixedValue,
                selectedDirection: ChartErrorBarDirection.Minus,
                valueText: "12.5",
                endCaps: false,
                out var input,
                out var issue)
            .Should().BeTrue();

        issue.Should().Be(ChartErrorBarsParseIssue.None);
        input.Should().Be(new ChartErrorBarsInput(
            ShowErrorBars: true,
            Kind: ChartErrorBarKind.FixedValue,
            Direction: ChartErrorBarDirection.Minus,
            Value: 12.5,
            EndCaps: false));
    }

    [Theory]
    [InlineData("bad")]
    [InlineData("-1")]
    [InlineData("1001")]
    public void ErrorBars_TryParseDialogInput_RejectsInvalidValue(string valueText)
    {
        ChartErrorBarsPlanner.TryParseDialogInput(
                showErrorBars: true,
                selectedKind: ChartErrorBarKind.Percentage,
                selectedDirection: ChartErrorBarDirection.Plus,
                valueText,
                endCaps: true,
                out _,
                out var issue)
            .Should().BeFalse();

        issue.Should().Be(ChartErrorBarsParseIssue.Value);
    }

    [Fact]
    public void ErrorBars_Read_FallsBackForNonFiniteValue()
    {
        var chart = new ChartModel { Type = ChartType.Line, ErrorBarValue = double.NaN };

        var input = ChartErrorBarsPlanner.Read(chart);

        double.IsFinite(input.Value).Should().BeTrue();
        input.Value.Should().BeInRange(ChartErrorBarsPlanner.MinValue, ChartErrorBarsPlanner.MaxValue);
    }

    [Fact]
    public void ErrorBars_Plan_RoundTripsThroughSetChartLayoutCommand()
    {
        var chart = new ChartModel { Type = ChartType.Column };
        var options = ChartErrorBarsPlanner.Plan(new ChartErrorBarsInput(
            ShowErrorBars: true, ChartErrorBarKind.Percentage,
            ChartErrorBarDirection.Minus, Value: 12.5, EndCaps: false));

        ApplyLayout(chart, options);

        chart.ShowErrorBars.Should().BeTrue();
        chart.ErrorBarKind.Should().Be(ChartErrorBarKind.Percentage);
        chart.ErrorBarDirection.Should().Be(ChartErrorBarDirection.Minus);
        chart.ErrorBarValue.Should().Be(12.5);
        chart.ErrorBarEndCaps.Should().BeFalse();
    }

    // ---- ChartComboPlanner ---------------------------------------------------------------------------

    [Fact]
    public void Combo_SupportsCombo_RequiresColumnOrAreaFamilyWithTwoSeries()
    {
        ChartComboPlanner.SupportsCombo(MakeChartWithSeries(ChartType.Column, columns: 3)).Should().BeTrue();
        ChartComboPlanner.SupportsCombo(MakeChartWithSeries(ChartType.Column, columns: 2)).Should().BeFalse();
        ChartComboPlanner.SupportsCombo(MakeChartWithSeries(ChartType.Pie, columns: 3)).Should().BeFalse();
    }

    [Fact]
    public void Combo_Read_AnchorsBaseSeries_AndReflectsStoredTreatment()
    {
        var chart = MakeChartWithSeries(ChartType.Column, columns: 4); // 3 series: 0,1,2
        chart.ComboLineSeriesIndexes = [2];
        chart.SecondaryAxisSeriesIndexes = [1];

        var input = ChartComboPlanner.Read(chart);

        input.Series.Should().HaveCount(3);
        input.Series[0].Should().Be(new ChartComboSeriesInput(0, AsLine: false, OnSecondaryAxis: false));
        input.Series[1].AsLine.Should().BeFalse();
        input.Series[1].OnSecondaryAxis.Should().BeTrue();
        input.Series[2].AsLine.Should().BeTrue();
        input.Series[2].OnSecondaryAxis.Should().BeFalse();
    }

    [Fact]
    public void Combo_Plan_ProjectsLineAndSecondarySets_DroppingBaseSeries()
    {
        var input = new ChartComboInput(new[]
        {
            new ChartComboSeriesInput(0, AsLine: true, OnSecondaryAxis: true), // base must be ignored
            new ChartComboSeriesInput(1, AsLine: true, OnSecondaryAxis: false),
            new ChartComboSeriesInput(2, AsLine: false, OnSecondaryAxis: true),
        });

        var options = ChartComboPlanner.Plan(input);

        options.ComboLineSeriesIndexes.Should().Equal(1);
        options.UseComboLineForSecondarySeries.Should().BeTrue();
        options.SecondaryAxisSeriesIndexes.Should().Equal(2);
        options.ShowSecondaryAxis.Should().BeTrue();
    }

    [Fact]
    public void Combo_Plan_ClearsOverlayFlags_WhenNothingSelected()
    {
        var input = new ChartComboInput(new[]
        {
            new ChartComboSeriesInput(0, false, false),
            new ChartComboSeriesInput(1, false, false),
        });

        var options = ChartComboPlanner.Plan(input);

        options.ComboLineSeriesIndexes.Should().BeEmpty();
        options.UseComboLineForSecondarySeries.Should().BeFalse();
        options.SecondaryAxisSeriesIndexes.Should().BeEmpty();
        options.ShowSecondaryAxis.Should().BeFalse();
    }

    [Fact]
    public void Combo_Plan_AppliesToChart_ViaSetChartLayoutCommand()
    {
        var chart = new ChartModel { Type = ChartType.Column };
        var input = new ChartComboInput(new[]
        {
            new ChartComboSeriesInput(0, false, false),
            new ChartComboSeriesInput(1, AsLine: true, OnSecondaryAxis: false),
        });

        ApplyLayout(chart, ChartComboPlanner.Plan(input));

        chart.ComboLineSeriesIndexes.Should().Contain(1);
        chart.UseComboLineForSecondarySeries.Should().BeTrue();
    }

    // ---- ChartMovePlanner ----------------------------------------------------------------------------

    [Fact]
    public void Move_Plan_TrimsName_AndAllowsNewSheetWithoutResolving()
    {
        var plan = ChartMovePlanner.Plan(
            new ChartMoveInput(ChartMoveTargetKind.NewSheet, "  Chart1  "),
            _ => false);

        plan.IsValid.Should().BeTrue();
        plan.TargetName.Should().Be("Chart1");
        plan.TargetKind.Should().Be(ChartMoveTargetKind.NewSheet);
    }

    [Fact]
    public void Move_Plan_RejectsBlankName()
    {
        var plan = ChartMovePlanner.Plan(
            new ChartMoveInput(ChartMoveTargetKind.ObjectInSheet, "   "),
            _ => true);

        plan.IsValid.Should().BeFalse();
        plan.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Move_Plan_RejectsExistingSheetTarget_WhenNameDoesNotResolve()
    {
        var plan = ChartMovePlanner.Plan(
            new ChartMoveInput(ChartMoveTargetKind.ObjectInSheet, "Ghost"),
            name => name == "Sheet1");

        plan.IsValid.Should().BeFalse();
        plan.Error.Should().Contain("Ghost");
    }

    [Fact]
    public void Move_Plan_AcceptsExistingSheetTarget_WhenNameResolves()
    {
        var plan = ChartMovePlanner.Plan(
            new ChartMoveInput(ChartMoveTargetKind.ObjectInSheet, "Sheet1"),
            name => name == "Sheet1");

        plan.IsValid.Should().BeTrue();
        plan.TargetName.Should().Be("Sheet1");
    }

    // ---- ChartAreaFormatPlanner ----------------------------------------------------------------------

    [Fact]
    public void ChartArea_Read_CapturesFillAndBorderState()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            ChartAreaFillColor = new CellColor(10, 20, 30),
            PlotAreaFillColor = new CellColor(40, 50, 60),
            PlotAreaBorderColor = new CellColor(70, 80, 90),
            PlotAreaBorderThickness = 2.5,
        };

        var input = ChartAreaFormatPlanner.Read(chart);

        input.ChartAreaFillColor.Should().Be(new CellColor(10, 20, 30));
        input.PlotAreaFillColor.Should().Be(new CellColor(40, 50, 60));
        input.PlotAreaBorderColor.Should().Be(new CellColor(70, 80, 90));
        input.PlotAreaBorderThickness.Should().Be(2.5);
    }

    [Fact]
    public void ChartArea_DialogDescriptor_CoversSharedFillLineAndLegendFields()
    {
        var sections = ChartAreaFormatPlanner.GetDialogSections();
        var fields = sections.SelectMany(section => section.Fields).ToList();

        ChartAreaFormatPlanner.DialogWidth.Should().Be(420);
        ChartAreaFormatPlanner.DialogHeight.Should().Be(590);
        sections.Select(section => section.HeaderResourceKey)
            .Should().Equal("ChartDialog_FillLineGroup", "ChartAreaLegend_LegendGroup");
        ChartAreaFormatPlanner.GetFillLineSection().HelpResourceKey.Should().Be("ChartAreaLegend_FillLineHelpText");
        ChartAreaFormatPlanner.GetLegendPositionChoices().Should().Equal(
            ChartLegendPosition.Right,
            ChartLegendPosition.Top,
            ChartLegendPosition.Left,
            ChartLegendPosition.Bottom);
        ChartAreaFormatPlanner.GetLegendPositionChoices().Should().NotContain(ChartLegendPosition.None);
        fields.Select(field => field.Id).Should().Equal(
            ChartAreaFormatDialogFieldId.ChartAreaFillColor,
            ChartAreaFormatDialogFieldId.PlotAreaFillColor,
            ChartAreaFormatDialogFieldId.PlotAreaBorderColor,
            ChartAreaFormatDialogFieldId.PlotAreaBorderThickness,
            ChartAreaFormatDialogFieldId.ShowLegend,
            ChartAreaFormatDialogFieldId.LegendPosition,
            ChartAreaFormatDialogFieldId.LegendOverlay,
            ChartAreaFormatDialogFieldId.LegendTextColor,
            ChartAreaFormatDialogFieldId.LegendFillColor,
            ChartAreaFormatDialogFieldId.LegendBorderColor,
            ChartAreaFormatDialogFieldId.LegendBorderThickness,
            ChartAreaFormatDialogFieldId.LegendFontSize);
        fields.Should().OnlyContain(field => !string.IsNullOrWhiteSpace(field.LabelResourceKey));
        fields.Should().OnlyContain(field => !string.IsNullOrWhiteSpace(field.AutomationId));
        ChartAreaFormatPlanner.GetDialogField(ChartAreaFormatDialogFieldId.PlotAreaBorderThickness)
            .HelpResourceKey.Should().Be("ChartDialog_LineWidthHelpText");
        ChartAreaFormatPlanner.GetDialogField(ChartAreaFormatDialogFieldId.LegendFontSize)
            .HelpResourceKey.Should().Be("ChartAreaLegend_LegendFontSizeHelpText");
    }

    [Fact]
    public void ChartArea_Validate_RejectsOutOfRangeOrNonFiniteWidth()
    {
        ChartAreaFormatPlanner.Validate(new ChartAreaFormatInput(null, null, null, -1)).Should().NotBeNull();
        ChartAreaFormatPlanner.Validate(new ChartAreaFormatInput(null, null, null, 99)).Should().NotBeNull();
        ChartAreaFormatPlanner.Validate(new ChartAreaFormatInput(null, null, null, double.NaN)).Should().NotBeNull();
        ChartAreaFormatPlanner.Validate(new ChartAreaFormatInput(null, null, null, 1.5, LegendBorderThickness: -1)).Should().NotBeNull();
        ChartAreaFormatPlanner.Validate(new ChartAreaFormatInput(null, null, null, 1.5, LegendFontSize: 100)).Should().NotBeNull();
        ChartAreaFormatPlanner.Validate(new ChartAreaFormatInput(null, null, null, 1.5)).Should().BeNull();
    }

    [Fact]
    public void ChartArea_Normalize_ClampsDialogDefaultsAndFallbacks()
    {
        var input = ChartAreaFormatPlanner.Normalize(new ChartAreaFormatInput(
            null,
            null,
            null,
            double.NaN,
            LegendPosition: (ChartLegendPosition)999,
            LegendBorderThickness: -4,
            LegendFontSize: 100));

        input.PlotAreaBorderThickness.Should().Be(1);
        input.LegendPosition.Should().Be(ChartLegendPosition.Right);
        input.LegendBorderThickness.Should().Be(0);
        input.LegendFontSize.Should().Be(ChartAreaFormatPlanner.MaxLegendFontSize);
    }

    [Fact]
    public void ChartArea_TryParseDialogInput_ParsesColorsAndLegendFields()
    {
        ChartAreaFormatPlanner.TryParseDialogInput(
                chartAreaFillColorText: "#010203",
                plotAreaFillColorText: "none",
                plotAreaBorderColorText: "#040506",
                plotAreaBorderThicknessText: "2.25",
                showLegend: true,
                selectedLegendPosition: ChartLegendPosition.Bottom,
                legendOverlay: true,
                legendTextColorText: "#070809",
                legendFillColorText: "clear",
                legendBorderColorText: "#0A0B0C",
                legendBorderThicknessText: "1.25",
                legendFontSizeText: "11",
                out var input,
                out var issue)
            .Should().BeTrue();

        issue.Should().Be(ChartAreaFormatParseIssue.None);
        input.Should().Be(new ChartAreaFormatInput(
            new CellColor(1, 2, 3),
            null,
            new CellColor(4, 5, 6),
            2.25,
            true,
            ChartLegendPosition.Bottom,
            true,
            new CellColor(7, 8, 9),
            null,
            new CellColor(10, 11, 12),
            1.25,
            11));
    }

    [Theory]
    [InlineData("bad", "none", "none", "1", "none", "none", "none", "0", "12", ChartAreaFormatParseIssue.ChartAreaFillColor)]
    [InlineData("none", "bad", "none", "1", "none", "none", "none", "0", "12", ChartAreaFormatParseIssue.PlotAreaFillColor)]
    [InlineData("none", "none", "bad", "1", "none", "none", "none", "0", "12", ChartAreaFormatParseIssue.PlotAreaBorderColor)]
    [InlineData("none", "none", "none", "11", "none", "none", "none", "0", "12", ChartAreaFormatParseIssue.PlotAreaBorderThickness)]
    [InlineData("none", "none", "none", "1", "bad", "none", "none", "0", "12", ChartAreaFormatParseIssue.LegendTextColor)]
    [InlineData("none", "none", "none", "1", "none", "bad", "none", "0", "12", ChartAreaFormatParseIssue.LegendFillColor)]
    [InlineData("none", "none", "none", "1", "none", "none", "bad", "0", "12", ChartAreaFormatParseIssue.LegendBorderColor)]
    [InlineData("none", "none", "none", "1", "none", "none", "none", "11", "12", ChartAreaFormatParseIssue.LegendBorderThickness)]
    [InlineData("none", "none", "none", "1", "none", "none", "none", "0", "100", ChartAreaFormatParseIssue.LegendFontSize)]
    public void ChartArea_TryParseDialogInput_ReportsFirstInvalidField(
        string chartAreaFillText,
        string plotAreaFillText,
        string plotAreaBorderText,
        string plotAreaBorderThicknessText,
        string legendTextColorText,
        string legendFillColorText,
        string legendBorderColorText,
        string legendBorderThicknessText,
        string legendFontSizeText,
        ChartAreaFormatParseIssue expectedIssue)
    {
        ChartAreaFormatPlanner.TryParseDialogInput(
                chartAreaFillText,
                plotAreaFillText,
                plotAreaBorderText,
                plotAreaBorderThicknessText,
                showLegend: true,
                selectedLegendPosition: ChartLegendPosition.Right,
                legendOverlay: false,
                legendTextColorText,
                legendFillColorText,
                legendBorderColorText,
                legendBorderThicknessText,
                legendFontSizeText,
                out _,
                out var issue)
            .Should().BeFalse();

        issue.Should().Be(expectedIssue);
    }

    [Fact]
    public void ChartArea_Plan_AppliesFillAndBorder_ViaSetChartLayoutCommand()
    {
        var chart = new ChartModel { Type = ChartType.Column };
        var options = ChartAreaFormatPlanner.Plan(new ChartAreaFormatInput(
            new CellColor(1, 2, 3), new CellColor(4, 5, 6), new CellColor(7, 8, 9), 3));

        ApplyLayout(chart, options);

        chart.ChartAreaFillColor.Should().Be(new CellColor(1, 2, 3));
        chart.PlotAreaFillColor.Should().Be(new CellColor(4, 5, 6));
        chart.PlotAreaBorderColor.Should().Be(new CellColor(7, 8, 9));
        chart.PlotAreaBorderThickness.Should().Be(3);
    }

    [Fact]
    public void ChartArea_Plan_AppliesLegendFields()
    {
        var options = ChartAreaFormatPlanner.Plan(new ChartAreaFormatInput(
            ChartAreaFillColor: null,
            PlotAreaFillColor: null,
            PlotAreaBorderColor: null,
            PlotAreaBorderThickness: 1,
            ShowLegend: true,
            LegendPosition: ChartLegendPosition.Top,
            LegendOverlay: true,
            LegendTextColor: new CellColor(40, 40, 40),
            LegendFillColor: new CellColor(248, 248, 248),
            LegendBorderColor: new CellColor(180, 180, 180),
            LegendBorderThickness: 1.25,
            LegendFontSize: 11));

        options.ShowLegend.Should().BeTrue();
        options.LegendPosition.Should().Be(ChartLegendPosition.Top);
        options.LegendOverlay.Should().BeTrue();
        options.LegendTextColor.Should().Be(new CellColor(40, 40, 40));
        options.LegendFillColor.Should().Be(new CellColor(248, 248, 248));
        options.LegendBorderColor.Should().Be(new CellColor(180, 180, 180));
        options.LegendBorderThickness.Should().Be(1.25);
        options.LegendFontSize.Should().Be(11);
    }

    // ---- ChartBarFormatPlanner -----------------------------------------------------------------------

    [Fact]
    public void BarFormat_Supports_OnlyBarColumnFamilies()
    {
        ChartBarFormatPlanner.Supports(new ChartModel { Type = ChartType.Column }).Should().BeTrue();
        ChartBarFormatPlanner.Supports(new ChartModel { Type = ChartType.Bar }).Should().BeTrue();
        ChartBarFormatPlanner.Supports(new ChartModel { Type = ChartType.Line }).Should().BeFalse();
    }

    [Fact]
    public void BarFormat_Read_FallsBackToExcelDefaults()
    {
        var read = ChartBarFormatPlanner.Read(new ChartModel { Type = ChartType.Column });
        read.Should().Be(new ChartBarFormatInput(150, 0));
    }

    [Fact]
    public void BarFormat_Read_ProjectsAndClampsModelValues()
    {
        var read = ChartBarFormatPlanner.Read(new ChartModel
        {
            Type = ChartType.Column,
            BarGapWidth = 700,
            BarOverlap = -250
        });

        read.Should().Be(new ChartBarFormatInput(
            ChartBarFormatPlanner.MaxGapWidth,
            ChartBarFormatPlanner.MinOverlap));
    }

    [Fact]
    public void BarFormat_Plan_ClampsAndApplies()
    {
        var chart = new ChartModel { Type = ChartType.Column };
        var options = ChartBarFormatPlanner.Plan(new ChartBarFormatInput(600, -200));
        options.BarGapWidth.Should().Be(ChartBarFormatPlanner.MaxGapWidth);
        options.BarOverlap.Should().Be(ChartBarFormatPlanner.MinOverlap);
        ApplyLayout(chart, options);
        chart.BarGapWidth.Should().Be(ChartBarFormatPlanner.MaxGapWidth);
        chart.BarOverlap.Should().Be(ChartBarFormatPlanner.MinOverlap);
    }

    [Fact]
    public void BarFormat_Validate_RejectsOutOfRange()
    {
        ChartBarFormatPlanner.Validate(new ChartBarFormatInput(100, 0)).Should().BeNull();
        ChartBarFormatPlanner.Validate(new ChartBarFormatInput(600, 0)).Should().NotBeNull();
        ChartBarFormatPlanner.Validate(new ChartBarFormatInput(100, 200)).Should().NotBeNull();
    }

    [Fact]
    public void BarFormat_TryParseDialogInput_ParsesAndReportsFieldIssues()
    {
        ChartBarFormatPlanner.TryParseDialogInput("200", "-50", out var input, out var issue).Should().BeTrue();
        input.Should().Be(new ChartBarFormatInput(200, -50));
        issue.Should().Be(ChartBarFormatParseIssue.None);

        ChartBarFormatPlanner.TryParseDialogInput("501", "0", out _, out issue).Should().BeFalse();
        issue.Should().Be(ChartBarFormatParseIssue.GapWidth);

        ChartBarFormatPlanner.TryParseDialogInput("150", "101", out _, out issue).Should().BeFalse();
        issue.Should().Be(ChartBarFormatParseIssue.Overlap);
    }

    [Fact]
    public void TypeFormat_DialogDescriptors_CoverBarPieBubbleAndStockFields()
    {
        ChartBarFormatPlanner.TitleResourceKey.Should().Be("ChartBarFormat_Title");
        ChartBarFormatPlanner.DialogAutomationId.Should().Be("ChartBarFormatDialog");
        ChartBarFormatPlanner.GetOptionsSection().HeaderResourceKey.Should().Be("ChartBarFormat_OptionsGroup");
        ChartBarFormatPlanner.GetOptionsSection().Fields.Select(field => field.Id).Should().Equal(
            ChartBarFormatDialogFieldId.GapWidth,
            ChartBarFormatDialogFieldId.Overlap);
        ChartBarFormatPlanner.GetDialogField(ChartBarFormatDialogFieldId.GapWidth).AutomationId
            .Should().Be("ChartBarFormatGapWidthBox");
        ChartBarFormatPlanner.InvalidInputMessageResourceKey(ChartBarFormatParseIssue.Overlap)
            .Should().Be("ChartBarFormat_InvalidOverlapMessage");

        ChartPieFormatPlanner.TitleResourceKey.Should().Be("ChartPieFormat_Title");
        ChartPieFormatPlanner.DialogAutomationId.Should().Be("ChartPieFormatDialog");
        ChartPieFormatPlanner.GetOptionsSection().HeaderResourceKey.Should().Be("ChartPieFormat_OptionsGroup");
        ChartPieFormatPlanner.GetOptionsSection().Fields.Select(field => field.Id).Should().Equal(
            ChartPieFormatDialogFieldId.FirstSliceAngle,
            ChartPieFormatDialogFieldId.ExplodedSliceIndex,
            ChartPieFormatDialogFieldId.ExplodedSliceDistance,
            ChartPieFormatDialogFieldId.DoughnutHoleSize);
        ChartPieFormatPlanner.GetDialogField(ChartPieFormatDialogFieldId.DoughnutHoleSize).HelpResourceKey
            .Should().Be("ChartPieFormat_HoleSizeHelpText");
        ChartPieFormatPlanner.InvalidInputMessageResourceKey(ChartPieFormatParseIssue.DoughnutHoleSize)
            .Should().Be("ChartPieFormat_InvalidHoleSizeMessage");

        ChartBubbleFormatPlanner.TitleResourceKey.Should().Be("ChartBubbleFormat_Title");
        ChartBubbleFormatPlanner.DialogAutomationId.Should().Be("ChartBubbleFormatDialog");
        ChartBubbleFormatPlanner.GetOptionsSection().HeaderResourceKey.Should().Be("ChartBubbleFormat_OptionsGroup");
        ChartBubbleFormatPlanner.GetOptionsSection().Fields.Select(field => field.Id).Should().Equal(
            ChartBubbleFormatDialogFieldId.BubbleScale,
            ChartBubbleFormatDialogFieldId.ShowNegativeBubbles,
            ChartBubbleFormatDialogFieldId.SizeRepresents);
        ChartBubbleFormatPlanner.GetDialogField(ChartBubbleFormatDialogFieldId.SizeRepresents).AutomationId
            .Should().Be("ChartBubbleFormatSizeCombo");

        ChartStockFormatPlanner.TitleResourceKey.Should().Be("ChartStockFormat_Title");
        ChartStockFormatPlanner.DialogAutomationId.Should().Be("ChartStockFormatDialog");
        ChartStockFormatPlanner.BarsGroupResourceKey.Should().Be("ChartFmt_StockBarsLabel");
        ChartStockFormatPlanner.HighLowGroupResourceKey.Should().Be("ChartFmt_StockHighLowLabel");
        ChartStockFormatPlanner.GetOptionsSection().HeaderResourceKey.Should().Be("ChartStockFormat_OptionsGroup");
        ChartStockFormatPlanner.GetOptionsSection().Fields.Select(field => field.Id).Should().Equal(
            ChartStockFormatDialogFieldId.GapWidth,
            ChartStockFormatDialogFieldId.UpBarFill,
            ChartStockFormatDialogFieldId.UpBarBorder,
            ChartStockFormatDialogFieldId.DownBarFill,
            ChartStockFormatDialogFieldId.DownBarBorder,
            ChartStockFormatDialogFieldId.HighLowLineColor,
            ChartStockFormatDialogFieldId.HighLowLineThickness);
        ChartStockFormatPlanner.GetDialogField(ChartStockFormatDialogFieldId.HighLowLineThickness).HelpResourceKey
            .Should().Be("ChartStockFormat_LineThicknessHelpText");
        ChartStockFormatPlanner.InvalidInputMessageResourceKey(ChartStockFormatParseIssue.HighLowLineThickness)
            .Should().Be("ChartStockFormat_InvalidLineThicknessMessage");
    }

    // ---- ChartPieFormatPlanner -----------------------------------------------------------------------

    [Fact]
    public void PieFormat_Supports_AndHoleSizeGating()
    {
        ChartPieFormatPlanner.Supports(new ChartModel { Type = ChartType.Pie }).Should().BeTrue();
        ChartPieFormatPlanner.Supports(new ChartModel { Type = ChartType.Doughnut }).Should().BeTrue();
        ChartPieFormatPlanner.Supports(new ChartModel { Type = ChartType.Column }).Should().BeFalse();
        ChartPieFormatPlanner.SupportsHoleSize(new ChartModel { Type = ChartType.Doughnut }).Should().BeTrue();
        ChartPieFormatPlanner.SupportsHoleSize(new ChartModel { Type = ChartType.Pie }).Should().BeFalse();
    }

    [Fact]
    public void PieFormat_Plan_ClampsAndApplies()
    {
        var chart = new ChartModel { Type = ChartType.Doughnut };
        var options = ChartPieFormatPlanner.Plan(new ChartPieFormatInput(400, 1, 0.9, 0.05));
        ApplyLayout(chart, options);
        chart.FirstSliceAngle.Should().Be(ChartPieFormatPlanner.MaxFirstSliceAngle);
        chart.ExplodedSliceIndex.Should().Be(1);
        chart.ExplodedSliceDistance.Should().Be(ChartPieFormatPlanner.MaxExplodedDistance);
        chart.DoughnutHoleSize.Should().Be(ChartPieFormatPlanner.MinHoleSize);
    }

    [Fact]
    public void PieFormat_Read_ProjectsAndClampsModelValues()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Doughnut,
            FirstSliceAngle = 400,
            ExplodedSliceIndex = 2,
            ExplodedSliceDistance = 0.9,
            DoughnutHoleSize = 0.05
        };

        var read = ChartPieFormatPlanner.Read(chart);

        read.FirstSliceAngle.Should().Be(ChartPieFormatPlanner.MaxFirstSliceAngle);
        read.ExplodedSliceIndex.Should().Be(2);
        read.ExplodedSliceDistance.Should().Be(ChartPieFormatPlanner.MaxExplodedDistance);
        read.DoughnutHoleSize.Should().Be(ChartPieFormatPlanner.MinHoleSize);
    }

    [Fact]
    public void PieFormat_Validate_RejectsOutOfRange()
    {
        ChartPieFormatPlanner.Validate(new ChartPieFormatInput(90, 0, 0.2, 0.5)).Should().BeNull();
        ChartPieFormatPlanner.Validate(new ChartPieFormatInput(400, 0, 0.2, 0.5)).Should().NotBeNull();
        ChartPieFormatPlanner.Validate(new ChartPieFormatInput(90, 0, 0.9, 0.5)).Should().NotBeNull();
        ChartPieFormatPlanner.Validate(new ChartPieFormatInput(90, 0, 0.2, 0.95)).Should().NotBeNull();
    }

    [Fact]
    public void PieFormat_PercentHelpers_ConvertDisplayValues()
    {
        ChartPieFormatPlanner.ToDisplayPercent(0.5).Should().Be(50);
        ChartPieFormatPlanner.ToDisplayPercent(0.555).Should().Be(56);
        ChartPieFormatPlanner.ToDisplayPercent(0.525).Should().Be(52);
        ChartPieFormatPlanner.FromDisplayPercent(25).Should().BeApproximately(0.25, 0.0001);
    }

    [Fact]
    public void PieFormat_TryParseDialogInput_ParsesPercentFieldsAndReportsIssues()
    {
        ChartPieFormatPlanner.TryParseDialogInput("90", "2", "25", "60", includeDoughnutHoleSize: true, out var input, out var issue)
            .Should().BeTrue();
        input.Should().Be(new ChartPieFormatInput(90, 2, 0.25, 0.60));
        issue.Should().Be(ChartPieFormatParseIssue.None);

        ChartPieFormatPlanner.TryParseDialogInput("90", "2", "25", "", includeDoughnutHoleSize: false, out input, out issue)
            .Should().BeTrue();
        input.DoughnutHoleSize.Should().BeApproximately(0.55, 0.0001);

        ChartPieFormatPlanner.TryParseDialogInput("360", "2", "25", "60", includeDoughnutHoleSize: true, out _, out issue)
            .Should().BeFalse();
        issue.Should().Be(ChartPieFormatParseIssue.FirstSliceAngle);

        ChartPieFormatPlanner.TryParseDialogInput("90", "bad", "25", "60", includeDoughnutHoleSize: true, out _, out issue)
            .Should().BeFalse();
        issue.Should().Be(ChartPieFormatParseIssue.ExplodedSliceIndex);

        ChartPieFormatPlanner.TryParseDialogInput("90", "2", "51", "60", includeDoughnutHoleSize: true, out _, out issue)
            .Should().BeFalse();
        issue.Should().Be(ChartPieFormatParseIssue.ExplodedSliceDistance);

        ChartPieFormatPlanner.TryParseDialogInput("90", "2", "25", "5", includeDoughnutHoleSize: true, out _, out issue)
            .Should().BeFalse();
        issue.Should().Be(ChartPieFormatParseIssue.DoughnutHoleSize);
    }

    // ---- ChartBubbleFormatPlanner --------------------------------------------------------------------

    [Fact]
    public void BubbleFormat_Supports_OnlyBubble()
    {
        ChartBubbleFormatPlanner.Supports(new ChartModel { Type = ChartType.Bubble }).Should().BeTrue();
        ChartBubbleFormatPlanner.Supports(new ChartModel { Type = ChartType.Scatter }).Should().BeFalse();
    }

    [Fact]
    public void BubbleFormat_Plan_ClampsAndApplies()
    {
        var chart = new ChartModel { Type = ChartType.Bubble };
        var options = ChartBubbleFormatPlanner.Plan(new ChartBubbleFormatInput(500, true, ChartBubbleSizeRepresents.Width));
        options.BubbleScale.Should().Be(ChartBubbleFormatPlanner.MaxBubbleScale);
        ApplyLayout(chart, options);
        chart.BubbleScale.Should().Be(ChartBubbleFormatPlanner.MaxBubbleScale);
        chart.ShowNegativeBubbles.Should().BeTrue();
        chart.BubbleSizeRepresents.Should().Be(ChartBubbleSizeRepresents.Width);
    }

    [Fact]
    public void BubbleFormat_ReadAndPlan_DefaultInvalidSizeRepresentsToArea()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Bubble,
            BubbleScale = 0,
            ShowNegativeBubbles = true,
            BubbleSizeRepresents = (ChartBubbleSizeRepresents)999
        };

        var read = ChartBubbleFormatPlanner.Read(chart);

        read.BubbleScale.Should().Be(ChartBubbleFormatPlanner.MinBubbleScale);
        read.ShowNegativeBubbles.Should().BeTrue();
        read.BubbleSizeRepresents.Should().Be(ChartBubbleSizeRepresents.Area);

        ChartBubbleFormatPlanner.Plan(new ChartBubbleFormatInput(100, false, (ChartBubbleSizeRepresents)999))
            .BubbleSizeRepresents.Should().Be(ChartBubbleSizeRepresents.Area);
    }

    [Fact]
    public void BubbleFormat_TryParseDialogInput_UsesSelectedOrDefaultSizeRepresents()
    {
        ChartBubbleFormatPlanner.TryParseDialogInput("120", true, ChartBubbleSizeRepresents.Width, out var input, out var issue)
            .Should().BeTrue();
        input.Should().Be(new ChartBubbleFormatInput(120, true, ChartBubbleSizeRepresents.Width));
        issue.Should().Be(ChartBubbleFormatParseIssue.None);

        ChartBubbleFormatPlanner.TryParseDialogInput("120", false, null, out input, out issue).Should().BeTrue();
        input.BubbleSizeRepresents.Should().Be(ChartBubbleSizeRepresents.Area);

        ChartBubbleFormatPlanner.TryParseDialogInput("0", false, ChartBubbleSizeRepresents.Area, out _, out issue)
            .Should().BeFalse();
        issue.Should().Be(ChartBubbleFormatParseIssue.BubbleScale);
    }

    // ---- ChartStockFormatPlanner ---------------------------------------------------------------------

    [Fact]
    public void StockFormat_Supports_OnlyStock()
    {
        ChartStockFormatPlanner.Supports(new ChartModel { Type = ChartType.Stock }).Should().BeTrue();
        ChartStockFormatPlanner.Supports(new ChartModel { Type = ChartType.Line }).Should().BeFalse();
    }

    [Fact]
    public void StockFormat_Plan_ClampsAndApplies()
    {
        var chart = new ChartModel { Type = ChartType.Stock };
        var up = new CellColor(10, 20, 30);
        var options = ChartStockFormatPlanner.Plan(new ChartStockFormatInput(600, up, null, null, null, null, 50));
        options.UpDownBarGapWidth.Should().Be(ChartStockFormatPlanner.MaxGapWidth);
        ApplyLayout(chart, options);
        chart.UpDownBarGapWidth.Should().Be(ChartStockFormatPlanner.MaxGapWidth);
        chart.UpBarFillColor.Should().Be(up);
        chart.HighLowLineThickness.Should().Be(ChartStockFormatPlanner.MaxLineThickness);
    }

    [Fact]
    public void StockFormat_Read_ProjectsAndClampsModelValues()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Stock,
            UpDownBarGapWidth = 700,
            HighLowLineThickness = 0.1,
            UpBarFillColor = new CellColor(1, 2, 3)
        };

        var read = ChartStockFormatPlanner.Read(chart);

        read.UpDownBarGapWidth.Should().Be(ChartStockFormatPlanner.MaxGapWidth);
        read.HighLowLineThickness.Should().Be(ChartStockFormatPlanner.MinLineThickness);
        read.UpBarFillColor.Should().Be(new CellColor(1, 2, 3));
    }

    [Fact]
    public void StockFormat_Validate_RejectsOutOfRange()
    {
        ChartStockFormatPlanner.Validate(new ChartStockFormatInput(100, null, null, null, null, null, 1)).Should().BeNull();
        ChartStockFormatPlanner.Validate(new ChartStockFormatInput(600, null, null, null, null, null, 1)).Should().NotBeNull();
        ChartStockFormatPlanner.Validate(new ChartStockFormatInput(100, null, null, null, null, null, 50)).Should().NotBeNull();
    }

    [Fact]
    public void StockFormat_TryParseDialogInput_ParsesColorsAndReportsFieldIssues()
    {
        var up = new CellColor(10, 20, 30);
        ChartStockFormatPlanner.TryParseDialogInput("150", up, null, null, null, null, "1.5", out var input, out var issue)
            .Should().BeTrue();
        input.UpDownBarGapWidth.Should().Be(150);
        input.UpBarFillColor.Should().Be(up);
        input.HighLowLineThickness.Should().BeApproximately(1.5, 0.0001);
        issue.Should().Be(ChartStockFormatParseIssue.None);

        ChartStockFormatPlanner.TryParseDialogInput("-1", null, null, null, null, null, "1", out _, out issue)
            .Should().BeFalse();
        issue.Should().Be(ChartStockFormatParseIssue.UpDownBarGapWidth);

        ChartStockFormatPlanner.TryParseDialogInput("150", null, null, null, null, null, "0.1", out _, out issue)
            .Should().BeFalse();
        issue.Should().Be(ChartStockFormatParseIssue.HighLowLineThickness);
    }

    // ---- ChartQuickCommandPlanner -------------------------------------------------------------------

    [Fact]
    public void QuickCommand_PieCommands_GateAndCycleLayoutOptions()
    {
        var doughnut = MakeChartWithSeries(ChartType.Doughnut, columns: 2);
        doughnut.FirstSliceAngle = 270;
        doughnut.DoughnutHoleSize = 0.4;
        doughnut.ExplodedSliceIndex = -1;
        doughnut.ExplodedSliceDistance = 0.1;

        ChartQuickCommandPlanner.CanApply(new ChartModel { Type = ChartType.Column }, ChartQuickCommand.FirstSliceAngle)
            .Should().BeFalse();
        ChartQuickCommandPlanner.CanApply(doughnut, ChartQuickCommand.FirstSliceAngle).Should().BeTrue();
        ChartQuickCommandPlanner.CanApply(doughnut, ChartQuickCommand.DoughnutHoleSize).Should().BeTrue();
        ChartQuickCommandPlanner.CanApply(doughnut, ChartQuickCommand.ExplodedSlice).Should().BeTrue();

        ChartQuickCommandPlanner.Plan(doughnut, ChartQuickCommand.FirstSliceAngle)
            .FirstSliceAngle.Should().Be(0);
        ChartQuickCommandPlanner.Plan(doughnut, ChartQuickCommand.DoughnutHoleSize)
            .DoughnutHoleSize.Should().Be(0.55);
        var exploded = ChartQuickCommandPlanner.Plan(doughnut, ChartQuickCommand.ExplodedSlice);
        exploded.ExplodedSliceIndex.Should().Be(0);
        exploded.ExplodedSliceDistance.Should().BeApproximately(0.16, 0.0001);
    }

    [Fact]
    public void QuickCommand_DataLabelCommands_ProjectLabelAndPointOptions()
    {
        var chart = MakeChartWithSeries(ChartType.Column, columns: 3);
        chart.DataLabelSeparator = ChartDataLabelSeparator.Comma;
        chart.DataLabelBorderThickness = 3;
        chart.DataLabelFontSize = 16;

        var category = ChartQuickCommandPlanner.Plan(chart, ChartQuickCommand.DataLabelCategoryName);
        category.ShowDataLabels.Should().BeTrue();
        category.ShowDataLabelCategoryName.Should().BeTrue();

        ChartQuickCommandPlanner.Plan(chart, ChartQuickCommand.DataLabelSeparator)
            .DataLabelSeparator.Should().Be(ChartDataLabelSeparator.Semicolon);
        ChartQuickCommandPlanner.Plan(chart, ChartQuickCommand.DataLabelBorder)
            .DataLabelBorderThickness.Should().Be(0.75);
        ChartQuickCommandPlanner.Plan(chart, ChartQuickCommand.DataLabelFontSize)
            .DataLabelFontSize.Should().Be(9);

        var point = ChartQuickCommandPlanner.Plan(chart, ChartQuickCommand.PointDataLabel);
        point.ShowDataLabels.Should().BeTrue();
        var format = point.PointDataLabelFormats.Should().ContainSingle().Which;
        format.SeriesIndex.Should().Be(0);
        format.PointIndex.Should().Be(0);
        format.FillColor.Should().Be(ChartQuickFormatCycler.DefaultSeriesColor);
        format.BorderThickness.Should().Be(0.75);
        format.FontSize.Should().Be(9);
    }

    [Fact]
    public void QuickCommand_TextPlotAndLegendCommands_ProjectStyleOptions()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            ChartTitleFontSize = 24,
            AxisTitleFontSize = 18,
            PlotAreaBorderThickness = 3,
            LegendBorderThickness = 3,
            LegendOverlay = false,
        };

        ChartQuickCommandPlanner.Plan(chart, ChartQuickCommand.ChartAreaFill)
            .ChartAreaFillColor.Should().Be(ChartQuickFormatCycler.DefaultSeriesColor);
        ChartQuickCommandPlanner.Plan(chart, ChartQuickCommand.ChartTitleFontSize)
            .ChartTitleFontSize.Should().Be(12);
        ChartQuickCommandPlanner.Plan(chart, ChartQuickCommand.AxisTitleFontSize)
            .AxisTitleFontSize.Should().Be(9);
        ChartQuickCommandPlanner.Plan(chart, ChartQuickCommand.PlotAreaBorder)
            .PlotAreaBorderThickness.Should().Be(1);
        ChartQuickCommandPlanner.Plan(chart, ChartQuickCommand.LegendBorder)
            .LegendBorderThickness.Should().Be(0.75);
        ChartQuickCommandPlanner.Plan(chart, ChartQuickCommand.LegendOverlay)
            .LegendOverlay.Should().BeTrue();
    }

    [Fact]
    public void QuickCommand_TrendlineCommands_HonorSupportAndCycleOptions()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Line,
            TrendlinePeriod = 6,
            TrendlineOrder = 6,
            ShowTrendlineEquation = false,
            TrendlineDashStyle = ChartLineDashStyle.Dash,
            TrendlineThickness = 3,
        };

        ChartQuickCommandPlanner.CanApply(new ChartModel { Type = ChartType.Pie }, ChartQuickCommand.TrendlineEquation)
            .Should().BeFalse();
        ChartQuickCommandPlanner.CanApply(chart, ChartQuickCommand.TrendlineEquation).Should().BeTrue();

        var period = ChartQuickCommandPlanner.Plan(chart, ChartQuickCommand.TrendlineMovingAveragePeriod);
        period.ShowLinearTrendline.Should().BeTrue();
        period.TrendlineType.Should().Be(ChartTrendlineType.MovingAverage);
        period.TrendlinePeriod.Should().Be(2);

        ChartQuickCommandPlanner.Plan(chart, ChartQuickCommand.TrendlinePolynomialOrder)
            .TrendlineOrder.Should().Be(2);
        ChartQuickCommandPlanner.Plan(chart, ChartQuickCommand.TrendlineEquation)
            .ShowTrendlineEquation.Should().BeTrue();
        ChartQuickCommandPlanner.Plan(chart, ChartQuickCommand.TrendlineDash)
            .TrendlineDashStyle.Should().Be(ChartLineDashStyle.Dot);
        ChartQuickCommandPlanner.Plan(chart, ChartQuickCommand.TrendlineThickness)
            .TrendlineThickness.Should().Be(1.5);
    }

    [Fact]
    public void QuickCommand_SecondaryComboAndSeriesCommands_ReuseSharedPolicy()
    {
        var chart = MakeChartWithSeries(ChartType.Column, columns: 4);
        ChartQuickCommandPlanner.CanApply(chart, ChartQuickCommand.SecondaryAxisSeries).Should().BeTrue();
        ChartQuickCommandPlanner.CanApply(chart, ChartQuickCommand.ComboSeries).Should().BeTrue();

        var secondary = ChartQuickCommandPlanner.Plan(chart, ChartQuickCommand.SecondaryAxisSeries);
        secondary.ShowSecondaryAxis.Should().BeTrue();
        secondary.SecondaryAxisSeriesIndexes.Should().Equal(1);

        var combo = ChartQuickCommandPlanner.Plan(chart, ChartQuickCommand.ComboSeries);
        combo.UseComboLineForSecondarySeries.Should().BeTrue();
        combo.ComboLineSeriesIndexes.Should().Equal(1);

        chart.SeriesFormats.Add(new ChartSeriesFormat(0)
        {
            StrokeThickness = 4,
            DashStyle = ChartLineDashStyle.Solid,
        });

        ChartQuickCommandPlanner.Plan(chart, ChartQuickCommand.SeriesWidth)
            .SeriesFormats.Should().ContainSingle(format => format.SeriesIndex == 0 && format.StrokeThickness == 1.5);
        ChartQuickCommandPlanner.Plan(chart, ChartQuickCommand.SeriesDash)
            .SeriesFormats.Should().ContainSingle(format => format.SeriesIndex == 0 && format.DashStyle == null);

        var line = MakeChartWithSeries(ChartType.Line, columns: 3);
        ChartQuickCommandPlanner.CanApply(chart, ChartQuickCommand.SeriesMarkerSize).Should().BeFalse();
        ChartQuickCommandPlanner.CanApply(line, ChartQuickCommand.SeriesMarkerSize).Should().BeTrue();
        ChartQuickCommandPlanner.Plan(line, ChartQuickCommand.SeriesMarkerSize)
            .SeriesFormats.Should().ContainSingle(format => format.SeriesIndex == 0 && format.MarkerSize == 5);
    }

    [Fact]
    public void QuickCommand_RoundTripsThroughSetChartLayoutCommand()
    {
        var chart = MakeChartWithSeries(ChartType.Line, columns: 3);

        ApplyLayout(chart, ChartQuickCommandPlanner.Plan(chart, ChartQuickCommand.SeriesDash));
        ApplyLayout(chart, ChartQuickCommandPlanner.Plan(chart, ChartQuickCommand.LegendOverlay));

        chart.SeriesFormats.Single(format => format.SeriesIndex == 0).DashStyle.Should().Be(ChartLineDashStyle.Dash);
        chart.ShowLegend.Should().BeTrue();
        chart.LegendOverlay.Should().BeTrue();
    }

    // ---- ChartQuickFormatCycler ----------------------------------------------------------------------

    [Fact]
    public void QuickCycler_SeriesColor_WalksPalette()
    {
        ChartQuickFormatCycler.DefaultSeriesColor.Should().Be(new CellColor(0, 114, 178));
        var first = ChartQuickFormatCycler.NextSeriesColor(null);
        first.Should().Be(ChartQuickFormatCycler.DefaultSeriesColor);
        ChartQuickFormatCycler.NextSeriesColor(first).Should().Be(new CellColor(213, 94, 0));
    }

    [Theory]
    [InlineData(ChartDataLabelPosition.BestFit, ChartDataLabelPosition.OutsideEnd)]
    [InlineData(ChartDataLabelPosition.OutsideEnd, ChartDataLabelPosition.InsideEnd)]
    [InlineData(ChartDataLabelPosition.InsideEnd, ChartDataLabelPosition.Center)]
    [InlineData(ChartDataLabelPosition.Center, ChartDataLabelPosition.BestFit)]
    public void QuickCycler_DataLabelPosition_CyclesExcelLikePositions(
        ChartDataLabelPosition current,
        ChartDataLabelPosition expected)
    {
        ChartQuickFormatCycler.NextDataLabelPosition(current).Should().Be(expected);
    }

    [Fact]
    public void QuickCycler_FontSizes_StepAndWrap()
    {
        ChartQuickFormatCycler.NextChartTitleFontSize(16).Should().Be(18);
        ChartQuickFormatCycler.NextChartTitleFontSize(24).Should().Be(12);
        ChartQuickFormatCycler.NextAxisTitleFontSize(18).Should().Be(9);
        ChartQuickFormatCycler.NextLegendFontSize(16).Should().Be(9);
        ChartQuickFormatCycler.NextDataLabelBorderThickness(0.75).Should().Be(1.5);
        ChartQuickFormatCycler.NextDataLabelBorderThickness(3).Should().Be(0.75);
        ChartQuickFormatCycler.NextPointDataLabelBorderThickness(null).Should().Be(0.75);
        ChartQuickFormatCycler.NextPointDataLabelBorderThickness(2.25).Should().Be(3);
        ChartQuickFormatCycler.NextPlotAreaBorderThickness(3).Should().Be(1);
        ChartQuickFormatCycler.NextLegendBorderThickness(3).Should().Be(0.75);
        ChartQuickFormatCycler.NextTrendlineThickness(3).Should().Be(1.5);
        ChartQuickFormatCycler.NextChartStyleId(null).Should().Be(4);
        ChartQuickFormatCycler.NextChartStyleId(44).Should().Be(48);
        ChartQuickFormatCycler.NextChartStyleId(45).Should().Be(1);
    }

    [Fact]
    public void QuickCycler_GridlineState_CyclesOffMajorMajorMinor()
    {
        ChartQuickFormatCycler.NextGridlineState(false, false).Should().Be((true, false));
        ChartQuickFormatCycler.NextGridlineState(true, false).Should().Be((true, true));
        ChartQuickFormatCycler.NextGridlineState(true, true).Should().Be((false, false));
    }

    [Fact]
    public void QuickCycler_SeriesDash_Cycles()
    {
        ChartQuickFormatCycler.NextSeriesDash(null).Should().Be(ChartLineDashStyle.Dash);
        ChartQuickFormatCycler.NextSeriesDash(ChartLineDashStyle.Dash).Should().Be(ChartLineDashStyle.Dot);
        ChartQuickFormatCycler.NextSeriesDash(ChartLineDashStyle.Dot).Should().Be(ChartLineDashStyle.Solid);
        ChartQuickFormatCycler.NextSeriesDash(ChartLineDashStyle.Solid).Should().BeNull();
    }

    [Fact]
    public void QuickCycler_MarkerSize_StepAndWrap()
    {
        ChartQuickFormatCycler.NextMarkerSize(null).Should().Be(5);
        ChartQuickFormatCycler.NextMarkerSize(5).Should().Be(7);
        ChartQuickFormatCycler.NextMarkerSize(12).Should().Be(5);
    }

    [Fact]
    public void QuickCycler_MergeFirstSeriesFormat_ReplacesOrAppends_AndPreservesOthers()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Line,
            SeriesFormats = { new ChartSeriesFormat(0), new ChartSeriesFormat(1) { MarkerSize = 3 } },
        };
        var updated = ChartQuickFormatCycler.ReadFirstSeriesFormat(chart) with { DashStyle = ChartLineDashStyle.Dot };
        var merged = ChartQuickFormatCycler.MergeFirstSeriesFormat(chart, updated);
        merged.Should().HaveCount(2);
        merged.Single(f => f.SeriesIndex == 0).DashStyle.Should().Be(ChartLineDashStyle.Dot);
        merged.Single(f => f.SeriesIndex == 1).MarkerSize.Should().Be(3);

        var fill = ChartQuickFormatCycler.MergeFirstSeriesFillColor(chart, new CellColor(9, 8, 7));
        fill.Single(f => f.SeriesIndex == 0).FillColor.Should().Be(new CellColor(9, 8, 7));
        fill.Single(f => f.SeriesIndex == 0).FillThemeColor.Should().BeNull();
        fill.Single(f => f.SeriesIndex == 1).MarkerSize.Should().Be(3);
    }

    [Fact]
    public void QuickCycler_ComboLineSeries_StepsAndClears()
    {
        var chart = MakeChartWithSeries(ChartType.Column, columns: 4); // 3 series: 0,1,2
        ChartQuickFormatCycler.NextComboLineSeries(chart).Should().Equal(1);
        chart.UseComboLineForSecondarySeries = true;
        chart.ComboLineSeriesIndexes.Add(1);
        ChartQuickFormatCycler.NextComboLineSeries(chart).Should().Equal(2);
        chart.ComboLineSeriesIndexes.Clear();
        chart.ComboLineSeriesIndexes.Add(2);
        ChartQuickFormatCycler.NextComboLineSeries(chart).Should().BeEmpty();
    }

    private static ChartModel MakeChartWithSeries(ChartType type, int columns)
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Series");
        return CreateChartWithRange(sheet.Id, type, columns);
    }

    private static ChartModel CreateChartWithRange(SheetId sheetId, ChartType type, int columns)
    {
        return new ChartModel
        {
            Type = type,
            FirstColIsCategories = true,
            DataRange = new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 6, (uint)columns)),
        };
    }

    private static void ApplyLayout(ChartModel chart, ChartLayoutOptions options)
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        if (chart.DataRange.Start.Sheet != sheet.Id)
        {
            chart.DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 3));
        }

        sheet.Charts.Add(chart);
        var ctx = new InMemoryCommandContext(workbook);
        var outcome = new SetChartLayoutCommand(sheet.Id, chart.Id, options).Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
    }

    private sealed class InMemoryCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }
}
