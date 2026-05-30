using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class ChartDialogTests
{
    [Fact]
    public void ChartTypePickerPlanner_ReturnsOnlyRenderableChartTypesWithFriendlyLabels()
    {
        var options = ChartTypePickerPlanner.GetSupportedOptions();

        options.Select(option => option.Type).Should().ContainInOrder(
            ChartType.Column,
            ChartType.StackedColumn,
            ChartType.PercentStackedColumn,
            ChartType.Line,
            ChartType.ThreeDLine,
            ChartType.Pie,
            ChartType.ThreeDPie,
            ChartType.Doughnut,
            ChartType.Bar,
            ChartType.StackedBar,
            ChartType.PercentStackedBar,
            ChartType.ThreeDBar,
            ChartType.Scatter,
            ChartType.Bubble,
            ChartType.Area,
            ChartType.ThreeDArea,
            ChartType.Radar,
            ChartType.Stock,
            ChartType.Surface,
            ChartType.ThreeDSurface);
        options.Should().NotContain(option => !ChartTypeSupport.IsRenderable(option.Type));
        options.Single(option => option.Type == ChartType.PercentStackedColumn).DisplayName
            .Should()
            .Be("100% Stacked Column");
    }

    [Fact]
    public void ChartTypePickerPlanner_RecommendsDefaultChartTypes()
    {
        var recommendations = ChartTypePickerPlanner.GetRecommendedOptions();

        recommendations.Select(option => option.Type).Should().ContainInOrder(
            ChartType.Column,
            ChartType.Line,
            ChartType.Bar,
            ChartType.Pie,
            ChartType.Scatter);
        recommendations.Should().OnlyContain(option => option.IsRecommended);
    }

    [Fact]
    public void ChartTypePickerPlanner_GroupsRenderableTypesIntoExcelCategories()
    {
        var categories = ChartTypePickerPlanner.GetCategories();

        categories.Select(category => category.Name).Should().ContainInOrder(
            "Column",
            "Line",
            "Pie",
            "Bar",
            "Area",
            "X Y (Scatter)",
            "Stock",
            "Radar",
            "Surface");
        categories.Should().OnlyContain(category => category.Options.All(option => ChartTypeSupport.IsRenderable(option.Type)));
        categories.Single(category => category.Name == "Column").Options.Select(option => option.Type).Should().ContainInOrder(
            ChartType.Column,
            ChartType.StackedColumn,
            ChartType.PercentStackedColumn,
            ChartType.ThreeDColumn);
        categories.Single(category => category.Name == "Line").Options.Select(option => option.Type).Should().ContainInOrder(
            ChartType.Line,
            ChartType.ThreeDLine);
        categories.Single(category => category.Name == "Pie").Options.Select(option => option.Type).Should().ContainInOrder(
            ChartType.Pie,
            ChartType.ThreeDPie,
            ChartType.Doughnut);
        categories.Single(category => category.Name == "Bar").Options.Select(option => option.Type).Should().ContainInOrder(
            ChartType.Bar,
            ChartType.StackedBar,
            ChartType.PercentStackedBar,
            ChartType.ThreeDBar);
        categories.Single(category => category.Name == "Area").Options.Select(option => option.Type).Should().ContainInOrder(
            ChartType.Area,
            ChartType.ThreeDArea);
        categories.Single(category => category.Name == "Surface").Options.Select(option => option.Type).Should().ContainInOrder(
            ChartType.Surface,
            ChartType.ThreeDSurface);
    }

    [Fact]
    public void ChartTypePickerPlanner_BuildsSubtypeGalleryChoicesWithPreviewText()
    {
        var choices = ChartTypePickerPlanner.GetGalleryChoices("Bar");

        choices.Select(choice => choice.SubtypeName).Should().ContainInOrder(
            "Clustered Bar",
            "Stacked Bar",
            "100% Stacked Bar");
        choices.Should().OnlyContain(choice => choice.CategoryName == "Bar");
        choices.Should().OnlyContain(choice => !string.IsNullOrWhiteSpace(choice.PreviewText));
    }

    [Fact]
    public void ChartTypeDialogs_ExposeExcelInsertAndChangeSurfaces()
    {
        var source = ReadChartTypeDialogSource();

        source.Should().Contain("UiText.Get(\"InsertChart_RecommendedChartsTab\")");
        source.Should().Contain("UiText.Get(\"InsertChart_AllChartsTab\")");
        source.Should().Contain("UiText.Get(\"ChartTypePicker_CategoriesAutomationName\")");
        source.Should().Contain("UiText.Get(\"ChartTypePicker_SubtypeGalleryAutomationName\")");
        source.Should().Contain("UiText.Get(\"ChartTypePicker_PreviewTitle\")");
        source.Should().Contain("UiText.Get(\"ChartTypePicker_ChooseChartTypeHeading\")");
        source.Should().Contain("UiText.Get(\"ChartTypePicker_RecommendedHelpText\")");
        source.Should().Contain("UiText.Get(\"ChartTypePicker_PreviewSampleLabel\")");
        source.Should().Contain("UiText.Get(\"ChartTypePicker_AllChartsHelpText\")");
    }

    [Fact]
    public void InsertChartDialog_BuildsResultForSelectedChartType()
    {
        var result = InsertChartDialog.CreateResult(ChartType.Line);

        result.ChartType.Should().Be(ChartType.Line);
        result.UseRecommendedLayout.Should().BeFalse();
    }

    [Fact]
    public void InsertChartDialog_UsesFirstRecommendationForRecommendedResult()
    {
        var result = InsertChartDialog.CreateRecommendedResult();

        result.ChartType.Should().Be(ChartType.Column);
        result.UseRecommendedLayout.Should().BeTrue();
    }

    [Fact]
    public void InsertChartDialogOpenedFromKeyboard_FocusesRecommendedGallery()
    {
        var source = ReadChartTypeDialogSource();
        var dialogSource = source[
            source.IndexOf("public sealed partial class InsertChartDialog", StringComparison.Ordinal)..
            source.IndexOf("public sealed record ChangeChartTypeDialogResult", StringComparison.Ordinal)];

        dialogSource.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        dialogSource.Should().Contain("private void FocusInitialKeyboardTarget()");
        dialogSource.Should().Contain("_recommendedGallery.Focus();");
        dialogSource.Should().Contain("Keyboard.Focus(_recommendedGallery);");
    }

    [Fact]
    public void ChartTypeGalleries_DoubleClickAcceptsSelectedSubtype()
    {
        var source = ReadChartTypeDialogSource();

        source.Should().Contain("_recommendedGallery.MouseDoubleClick += (_, _) => Accept();");
        source.Should().Contain("_subtypeGallery.MouseDoubleClick += (_, _) => Accept();");
        source.Should().Contain("private void Accept()");
        source.Should().Contain("_subtypeGallery.MouseDoubleClick += (_, _) => AcceptSelectedChartType();");
        source.Should().Contain("private void AcceptSelectedChartType()");
    }

    [Fact]
    public void ChangeChartTypeDialog_PreselectsCurrentTypeAndBuildsResult()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new ChangeChartTypeDialog(ChartType.Bar);

            dialog.SelectedChartType.Should().Be(ChartType.Bar);
        });
        ChangeChartTypeDialog.CreateResult(ChartType.Area).ChartType.Should().Be(ChartType.Area);
    }

    [Fact]
    public void ChangeChartTypeDialogOpenedFromKeyboard_FocusesSubtypeGallery()
    {
        var source = ReadChartTypeDialogSource();
        var dialogSource = source[source.IndexOf("public sealed class ChangeChartTypeDialog", StringComparison.Ordinal)..];

        dialogSource.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        dialogSource.Should().Contain("private void FocusInitialKeyboardTarget()");
        dialogSource.Should().Contain("_subtypeGallery.Focus();");
        dialogSource.Should().Contain("Keyboard.Focus(_subtypeGallery);");
    }

    [Fact]
    public void ChartTitlesDialogResult_MapsTitleTextToLayoutOptions()
    {
        var result = ChartTitlesDialog.CreateResult(" Revenue ", " Quarter ", " Amount ");

        result.Should().Be(new ChartTitlesDialogResult("Revenue", "Quarter", "Amount"));
        result.ToOptions().Should().Be(new ChartLayoutOptions(
            Title: "Revenue",
            XAxisTitle: "Quarter",
            YAxisTitle: "Amount"));
    }

    [Fact]
    public void ChartTitlesDialog_LabelsTitleEditorsWithExcelAccessKeys()
    {
        var source = ReadChartDialogSource();

        source.Should().Contain("AddInput(stack, UiText.Get(\"ChartTitles_ChartTitleLabel\"), _chartTitleBox)");
        source.Should().Contain("AddInput(stack, UiText.Get(\"ChartTitles_XAxisTitleLabel\"), _xAxisTitleBox)");
        source.Should().Contain("AddInput(stack, UiText.Get(\"ChartTitles_YAxisTitleLabel\"), _yAxisTitleBox)");
        source.Should().Contain("new Label { Content = label, Target = box");
    }

    [Fact]
    public void ChartTitlesDialog_EditorsExposeAutomationNames()
    {
        var source = ReadChartDialogSource();
        var dialogSource = source[
            source.IndexOf("public sealed class ChartTitlesDialog", StringComparison.Ordinal)..
            source.IndexOf("public sealed record ChartStyleDialogResult", StringComparison.Ordinal)];

        dialogSource.Should().Contain("AutomationProperties.SetName(_chartTitleBox, UiText.Get(\"ChartTitles_ChartTitleAutomationName\"));");
        dialogSource.Should().Contain("AutomationProperties.SetName(_xAxisTitleBox, UiText.Get(\"ChartTitles_XAxisTitleAutomationName\"));");
        dialogSource.Should().Contain("AutomationProperties.SetName(_yAxisTitleBox, UiText.Get(\"ChartTitles_YAxisTitleAutomationName\"));");
    }

    [Fact]
    public void ChartTitlesDialogOpenedFromKeyboard_FocusesChartTitleBox()
    {
        var source = ReadChartDialogSource();
        var dialogSource = source[
            source.IndexOf("public sealed class ChartTitlesDialog", StringComparison.Ordinal)..
            source.IndexOf("public sealed record ChartStyleDialogResult", StringComparison.Ordinal)];

        dialogSource.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        dialogSource.Should().Contain("private void FocusInitialKeyboardTarget()");
        dialogSource.Should().Contain("_chartTitleBox.Focus();");
        dialogSource.Should().Contain("_chartTitleBox.SelectAll();");
        dialogSource.Should().Contain("Keyboard.Focus(_chartTitleBox);");
    }

    [Fact]
    public void ChartStyleDialog_ExposesAutomaticAndCommonStyleOptions()
    {
        var options = ChartStyleDialog.GetStyleOptions();

        options.Should().HaveCount(49);
        options[0].Should().Be(new ChartStyleOption(null, "Automatic", "Use current chart formatting"));
        options.Skip(1).Select(option => option.StyleId).Should().Equal(Enumerable.Range(1, 48).Cast<int?>());
        options.Skip(1).Should().OnlyContain(option => !string.IsNullOrWhiteSpace(option.PreviewLabel));
    }

    [Fact]
    public void ChartStyleDialog_UsesVisualGalleryInsteadOfPlainStyleCombo()
    {
        var source = ReadChartDialogSource();

        source.Should().Contain("UiText.Get(\"ChartStyle_GalleryAutomationName\")");
        source.Should().Contain("CreateStyleGalleryTemplate");
        source.Should().Contain("CreateStylePreviewSwatch");
        source.Should().Contain("UniformGrid");
        source.Should().NotContain("private readonly ComboBox _styleBox");
    }

    [Fact]
    public void ChartStyleDialog_ResultNormalizesCurrentAndSelectedStyle()
    {
        var chart = new ChartModel { ChartStyleId = 99 };

        ChartStyleDialog.FromChart(chart).Should().Be(new ChartStyleDialogResult(48));
        ChartStyleDialog.CreateResult(0).Should().Be(new ChartStyleDialogResult(1));
        ChartStyleDialog.CreateResult(null).Should().Be(new ChartStyleDialogResult(null));
    }

    [Fact]
    public void ChartStyleDialogOpenedFromKeyboard_FocusesStyleGallery()
    {
        var source = ReadChartDialogSource();
        var dialogSource = source[
            source.IndexOf("public sealed class ChartStyleDialog", StringComparison.Ordinal)..
            source.IndexOf("public sealed record MoveChartDialogResult", StringComparison.Ordinal)];

        dialogSource.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        dialogSource.Should().Contain("private void FocusInitialKeyboardTarget()");
        dialogSource.Should().Contain("_styleGallery.Focus();");
        dialogSource.Should().Contain("Keyboard.Focus(_styleGallery);");
    }

    [Fact]
    public void MoveChartDialog_CreatesObjectAndNewSheetResults()
    {
        MoveChartDialog.CreateObjectResult("Sheet2").Should().Be(
            new MoveChartDialogResult(MoveChartTargetKind.ObjectInSheet, "Sheet2"));
        MoveChartDialog.CreateNewSheetResult("Revenue Chart").Should().Be(
            new MoveChartDialogResult(MoveChartTargetKind.NewChartSheet, "Revenue Chart"));
    }

    [Fact]
    public void MoveChartDialogOpenedFromKeyboard_FocusesObjectInSheetChoice()
    {
        var source = ReadChartDialogSource();
        var dialogSource = source[
            source.IndexOf("public sealed class MoveChartDialog", StringComparison.Ordinal)..
            source.IndexOf("public sealed record SelectDataSourceDialogResult", StringComparison.Ordinal)];

        dialogSource.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        dialogSource.Should().Contain("private void FocusInitialKeyboardTarget()");
        dialogSource.Should().Contain("_objectInSheet.Focus();");
        dialogSource.Should().Contain("Keyboard.Focus(_objectInSheet);");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MoveChartDialog_RejectsMissingTargetName(string? targetName)
    {
        var act = () => MoveChartDialog.CreateNewSheetResult(targetName);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MoveChartDialogInvalidTargetName_ShowsOwnedWarningAndRefocusesTargetBox()
    {
        var source = ReadChartDialogSource();
        var dialogSource = source[
            source.IndexOf("public sealed class MoveChartDialog", StringComparison.Ordinal)..
            source.IndexOf("public sealed record SelectDataSourceDialogResult", StringComparison.Ordinal)];

        dialogSource.Should().Contain("catch (ArgumentException ex)");
        dialogSource.Should().Contain("DialogMessageHelper.ShowWarning(this, ex.Message, Title);");
        dialogSource.Should().Contain("FocusInvalidTargetName();");
        dialogSource.Should().Contain("_targetBox.Focus();");
        dialogSource.Should().Contain("_targetBox.SelectAll();");
        dialogSource.Should().Contain("Keyboard.Focus(_targetBox);");
    }

    [Fact]
    public void MoveChartDialog_LabelsTargetNameEditorWithAccessKeyAndAutomationName()
    {
        var source = ReadChartDialogSource();
        var dialogSource = source[
            source.IndexOf("public sealed class MoveChartDialog", StringComparison.Ordinal)..
            source.IndexOf("public sealed record SelectDataSourceDialogResult", StringComparison.Ordinal)];

        dialogSource.Should().Contain("new Label { Content = UiText.Get(\"MoveChart_TargetNameLabel\"), Target = _targetBox");
        dialogSource.Should().Contain("AutomationProperties.SetName(_targetBox, UiText.Get(\"MoveChart_TargetNameAutomationName\"));");
        dialogSource.Should().Contain("AutomationProperties.SetHelpText(_targetBox, UiText.Get(\"MoveChart_TargetNameHelpText\"));");
    }

    [Fact]
    public void ChartDataAndMoveDialogs_ExposeKeyboardAccessKeys()
    {
        var source = ReadChartDialogSource();

        foreach (var key in new[]
        {
            "MoveChart_ObjectInSheet",
            "MoveChart_NewChartSheet",
            "MoveChart_TargetNameLabel",
            "SelectDataSource_ChartDataRangeLabel",
            "SelectDataSource_SwitchRowColumn",
            "SelectDataSource_FirstColumnCategories",
            "SelectDataSource_AddSeriesButton",
            "SelectDataSource_EditSeriesButton",
            "SelectDataSource_RemoveSeriesButton",
            "SelectDataSource_EditAxisLabelsButton"
        })
        {
            source.Should().Contain($"UiText.Get(\"{key}\")");
        }
    }

    [Fact]
    public void SelectDataSourceDialog_NormalizesSourceRangeAndCategoryState()
    {
        var result = SelectDataSourceDialog.CreateResult("  A1:D12  ", true);

        result.SourceRangeText.Should().Be("A1:D12");
        result.FirstColumnIsCategories.Should().BeTrue();
        result.SwitchRowColumn.Should().BeFalse();
    }

    [Fact]
    public void SelectDataSourceDialogOpenedFromKeyboard_FocusesChartDataRangeBox()
    {
        var source = ReadChartDialogSource();
        var dialogSource = source[source.IndexOf("public sealed partial class SelectDataSourceDialog", StringComparison.Ordinal)..];

        dialogSource.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        dialogSource.Should().Contain("private void FocusInitialKeyboardTarget()");
        dialogSource.Should().Contain("FocusRangeSelectionInput(_rangeBox);");
    }

    [Fact]
    public void SelectDataSourceDialog_RangeEditorExposesAutomationName()
    {
        var source = ReadChartDialogSource();
        var dialogSource = source[source.IndexOf("public sealed partial class SelectDataSourceDialog", StringComparison.Ordinal)..];

        dialogSource.Should().Contain("AutomationProperties.SetName(_rangeBox, UiText.Get(\"SelectDataSource_ChartDataRangeAutomationName\"));");
    }

    [Fact]
    public void SelectDataSourceDialog_ExposesExcelStylePickerSeriesAndAxisControls()
    {
        var source = ReadChartDialogSource();

        source.Should().Contain("CreateReferenceEditor(_rangeBox");
        source.Should().Contain("UiText.Get(\"SelectDataSource_SelectChartDataRangeAutomationName\")");
        source.Should().Contain("DialogReferencePicker.CreateEditor");
        source.Should().Contain("SelectDataSourceRangeSelectionRequest");
        source.Should().Contain("_switchRowColumnBox");
        source.Should().Contain("_seriesList");
        source.Should().Contain("_axisLabelsList");
        source.Should().Contain("UiText.Get(\"SelectDataSource_SeriesPanelTitle\")");
        source.Should().Contain("UiText.Get(\"SelectDataSource_AxisLabelsPanelTitle\")");
        source.Should().Contain("AddEditRemoveButtons");
        source.Should().Contain("UiText.Get(\"SelectDataSource_SeriesListAutomationName\")");
        source.Should().Contain("UiText.Get(\"SelectDataSource_AxisLabelsListAutomationName\")");
        source.Should().Contain("UiText.Get(\"SelectDataSource_AddSeriesButton\")");
        source.Should().Contain("UiText.Get(\"SelectDataSource_EditSeriesButton\")");
        source.Should().Contain("UiText.Get(\"SelectDataSource_EditAxisLabelsButton\")");
        source.Should().Contain("_seriesList.MouseDoubleClick += EditSeriesButton_Click;");
        source.Should().Contain("_axisLabelsList.MouseDoubleClick += EditAxisLabelsButton_Click;");
        source.Should().Contain("UiText.Get(\"SelectDataSource_SeriesListHelpText\")");
    }

    [Fact]
    public void SelectDataSourceDialog_EnablesExcelStyleSeriesAndAxisActions()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new SelectDataSourceDialog("A1:D12");
            var buttons = FindLogicalDescendants<Button>(dialog)
                .Where(button => button.Content is string)
                .ToDictionary(button => (string)button.Content);

            foreach (var label in new[] { "_Add series", "_Edit series", "_Remove series", "_Edit Axis Labels" })
            {
                buttons[label].IsEnabled.Should().BeTrue();
                buttons[label].ToolTip.Should().BeNull();
                AutomationProperties.GetHelpText(buttons[label]).Should().BeEmpty();
            }

            buttons.Should().ContainKey("_Hidden and Empty Cells");
        });
    }

    [Fact]
    public void SelectDataSourceDialog_SelectsFirstPreviewRowsAndDisablesSelectionActionsWhenEmpty()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new SelectDataSourceDialog("A1:D12");
            var buttons = FindLogicalDescendants<Button>(dialog)
                .Where(button => button.Content is string)
                .ToDictionary(button => (string)button.Content);
            var lists = FindLogicalDescendants<ListBox>(dialog).ToList();

            lists[0].SelectedIndex.Should().Be(0);
            lists[1].SelectedIndex.Should().Be(0);
            buttons["_Edit series"].IsEnabled.Should().BeTrue();
            buttons["_Remove series"].IsEnabled.Should().BeTrue();
            buttons["_Edit Axis Labels"].IsEnabled.Should().BeTrue();

            dialog.ApplyRangeSelection("");

            lists[0].Items.Count.Should().Be(0);
            lists[1].Items.Count.Should().Be(0);
            lists[0].SelectedIndex.Should().Be(-1);
            lists[1].SelectedIndex.Should().Be(-1);
            buttons["_Edit series"].IsEnabled.Should().BeFalse();
            buttons["_Remove series"].IsEnabled.Should().BeFalse();
            buttons["_Edit Axis Labels"].IsEnabled.Should().BeFalse();
        });
    }

    [Fact]
    public void SelectDataSourceDialog_HiddenEmptyCellsMessageBoxUsesDialogOwner()
    {
        var source = ReadChartDialogSource();
        var dialogSource = source[source.IndexOf("public sealed partial class SelectDataSourceDialog", StringComparison.Ordinal)..];

        dialogSource.Should().Contain("Window.GetWindow(dependencyObject)");
        dialogSource.Should().Contain("MessageBox.Show(owner,"); // static handler with dynamic owner — kept as raw call
        dialogSource.Should().Contain("UiText.Get(\"SelectDataSource_HiddenEmptyCellsTitle\")");
    }

    [Fact]
    public void SelectDataSourceDialog_RangePickerRaisesSelectionIntent()
    {
        StaTestRunner.Run(() =>
        {
            var requests = new List<SelectDataSourceRangeSelectionRequest>();
            var dialog = new SelectDataSourceDialog(" A1:D12 ", requestRangeSelection: requests.Add);
            var picker = FindLogicalDescendants<Button>(dialog)
                .Single(button => AutomationProperties.GetName(button) == "Select chart data range");

            picker.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            requests.Should().Equal(new SelectDataSourceRangeSelectionRequest("A1:D12", CollapseDialog: true));
            dialog.RangeSelectionRequest.Should().Be(requests[0]);
        });
    }

    [Fact]
    public void SelectDataSourceApplyRangeSelection_UpdatesRangeBox()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new SelectDataSourceDialog("A1:D12");

            dialog.ApplyRangeSelection("Sheet2!B2:E20");

            FindLogicalDescendants<TextBox>(dialog)
                .Single()
                .Text.Should().Be("Sheet2!B2:E20");
        });
    }

    [Fact]
    public void MainWindow_WiresSelectDataSourceRangePickerToCurrentSelection()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.ChartCommands.cs"));

        source.Should().Contain("new SelectDataSourceDialog(");
        source.Should().Contain("request => ApplySelectDataSourceRangeSelection(dialog, request)");
        source.Should().Contain("private void ApplySelectDataSourceRangeSelection(");
        source.Should().Contain("SelectDataSourceRangeSelectionRequest request");
        source.Should().Contain("FormatWorkbookRange(selectedRange)");
        source.Should().Contain("dialog.ApplyRangeSelection(rangeText);");
        source.Should().Contain("dialog.Hide();");
        source.Should().Contain("dialog.Show();");
        source.Should().Contain("dialog.Activate();");
    }

    [Fact]
    public void SelectDataSourceDialogRangePicker_RefocusesDataRangeAfterRequest()
    {
        var source = ReadChartDialogSource();
        var dialogSource = source[source.IndexOf("public sealed partial class SelectDataSourceDialog", StringComparison.Ordinal)..];

        dialogSource.Should().Contain("FocusRangeSelectionInput(request.Target);");
        dialogSource.Should().Contain("private static void FocusRangeSelectionInput(TextBox target)");
        dialogSource.Should().Contain("DialogFocus.FocusAndSelect(target);");
    }

    [Fact]
    public void SelectDataSourceDialogInvalidRange_ShowsOwnedWarningAndRefocusesRange()
    {
        var source = ReadChartDialogSource();
        var dialogSource = source[source.IndexOf("public sealed partial class SelectDataSourceDialog", StringComparison.Ordinal)..];
        var chartCommandSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.ChartCommands.cs"));

        dialogSource.Should().Contain("if (!ValidateInputs())");
        dialogSource.Should().Contain("ChartInputParser.TryParseDataRange(_rangeBox.Text, _sheetId, out _)");
        dialogSource.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"SelectDataSource_InvalidRangeMessage\"), _rangeBox);");
        dialogSource.Should().Contain("DialogMessageHelper.ShowWarning(this, message, Title);");
        dialogSource.Should().Contain("FocusRangeSelectionInput(target);");
        chartCommandSource.Should().Contain("sheetId: _currentSheetId");
    }

    [Fact]
    public void SelectDataSourceDialog_InferPreviewEntriesFromChartRange()
    {
        var preview = SelectDataSourceDialog.InferPreviewEntries("Sheet1!$A$1:$C$5", firstColumnIsCategories: true);

        preview.Series.Select(series => series.Name).Should().ContainInOrder("Series 1", "Series 2");
        preview.Series.Select(series => series.ValuesRangeText).Should().ContainInOrder(
            "Sheet1!$B$2:$B$5",
            "Sheet1!$C$2:$C$5");
        preview.Categories.Select(category => category.Label).Should().ContainInOrder(
            "Category 1",
            "Category 2",
            "Category 3",
            "Category 4");
        preview.CategoryRangeText.Should().Be("Sheet1!$A$2:$A$5");
    }

    [Fact]
    public void ChartFormatDialogs_RouteColorFieldsThroughColorPickerButtons()
    {
        var source = ReadChartFormatDialogSource();
        var helperSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ChartDialogHelpers.cs"));

        source.Should().Contain("AddColorText");
        helperSource.Should().Contain("new ColorPickerDialog(initialColor, allowNoColor: true)");
        foreach (var key in new[]
        {
            "ChartAreaLegend_ChartAreaFillColorLabel",
            "ChartAreaLegend_PlotAreaFillColorLabel",
            "ChartAreaLegend_LegendTextColorLabel",
            "ChartSeriesFormat_FillColorLabel",
            "ChartTrendline_LineColorLabel",
            "ChartAxisFormat_MajorGridlineColorLabel",
            "ChartAxisFormat_AxisLineColorLabel"
        })
        {
            source.Should().Contain($"AddColorText(stack, UiText.Get(\"{key}\")");
        }
    }

    [Fact]
    public void ChartFormatDialogs_GroupLongStacksIntoExcelLikeSections()
    {
        var source = ReadChartFormatDialogSource();
        var helperSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ChartDialogHelpers.cs"));

        source.Should().Contain("CreateGroupBox(UiText.Get(\"ChartDialog_FillLineGroup\")");
        source.Should().Contain("CreateGroupBox(UiText.Get(\"ChartAreaLegend_LegendGroup\")");
        source.Should().Contain("CreateGroupBox(UiText.Get(\"ChartDataLabels_LabelOptionsGroup\")");
        source.Should().Contain("CreateGroupBox(UiText.Get(\"ChartAxisFormat_AxisOptionsGroup\")");
        source.Should().Contain("CreateGroupBox(UiText.Get(\"ChartAxisFormat_TickMarksGroup\")");
        source.Should().Contain("CreateGroupBox(UiText.Get(\"ChartSeriesFormat_SeriesOptionsGroup\")");
        source.Should().Contain("CreateInlineHelp(");
        source.Should().Contain("AddNumericText");
        helperSource.Should().Contain("AutomationProperties.SetHelpText");
    }

    [Fact]
    public void ChartFormatDialogs_ExposeKeyboardAccessKeysForOptionControls()
    {
        var source = string.Concat(
            ReadChartTypeDialogSource(),
            ReadChartFormatDialogSource());

        foreach (var key in new[]
        {
            "InsertChart_UseRecommendedLayout",
            "ChartAreaLegend_ShowLegend",
            "ChartAreaLegend_OverlayLegend",
            "ChartDataLabels_ShowDataLabels",
            "ChartDataLabels_Value",
            "ChartDataLabels_LegendKey",
            "ChartDataLabels_CategoryName",
            "ChartDataLabels_SeriesName",
            "ChartDataLabels_Percentage",
            "ChartDataLabels_Callouts",
            "ChartTrendline_ShowTrendline",
            "ChartTrendline_DisplayEquation",
            "ChartTrendline_DisplayRSquared",
            "ChartErrorBars_ShowErrorBars",
            "ChartErrorBars_EndCaps",
            "ChartAxisFormat_LogScale",
            "ChartAxisFormat_MajorGridlines",
            "ChartAxisFormat_MinorGridlines",
            "ChartAxisFormat_ShowLabels"
        })
        {
            source.Should().Contain($"UiText.Get(\"{key}\")");
        }

        foreach (var key in new[]
        {
            "ChartAreaLegend_ChartAreaFillColorLabel",
            "ChartAreaLegend_PlotAreaFillColorLabel",
            "ChartAreaLegend_PlotAreaBorderWidthLabel",
            "ChartAreaLegend_LegendPositionLabel",
            "ChartAreaLegend_LegendTextColorLabel",
            "ChartAreaLegend_LegendFontSizeLabel",
            "ChartDataLabels_PositionLabel",
            "ChartDataLabels_SeparatorLabel",
            "ChartDataLabels_NumberFormatLabel",
            "ChartDataLabels_BorderThicknessLabel",
            "ChartAxisFormat_MinimumLabel",
            "ChartAxisFormat_MaximumLabel",
            "ChartAxisFormat_MajorTickMarksLabel",
            "ChartAxisFormat_MinorTickMarksLabel",
            "ChartSeriesFormat_SeriesLabel",
            "ChartSeriesFormat_DashStyleLabel",
            "ChartSeriesFormat_MarkerLabel",
            "ChartTrendline_TypeLabel",
            "ChartErrorBars_DirectionLabel",
            "ChartBarFormat_GapWidthLabel",
            "ChartBarFormat_OverlapLabel",
            "ChartPieFormat_FirstSliceAngleLabel",
            "ChartBubbleFormat_BubbleScaleLabel",
            "ChartBubbleFormat_SizeRepresentsLabel"
        })
        {
            source.Should().Contain($"UiText.Get(\"{key}\")");
        }
    }

    [Fact]
    public void ChartDataLabelsDialog_UsesUniqueAccessKeys()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ChartDataLabelsDialog.cs"));
        var labels = new[]
        {
            "ChartDataLabels_ShowDataLabels",
            "ChartDataLabels_Value",
            "ChartDataLabels_LegendKey",
            "ChartDataLabels_CategoryName",
            "ChartDataLabels_SeriesName",
            "ChartDataLabels_Percentage",
            "ChartDataLabels_Callouts",
            "ChartDataLabels_PositionLabel",
            "ChartDataLabels_SeparatorLabel",
            "ChartDataLabels_NumberFormatLabel",
            "ChartDataLabels_FillColorLabel",
            "ChartDataLabels_BorderColorLabel",
            "ChartDataLabels_TextColorLabel",
            "ChartDataLabels_BorderThicknessLabel",
            "ChartDataLabels_FontSizeLabel",
            "ChartDataLabels_TextAngleLabel"
        }.Select(UiText.Get);
        var duplicateAccessKeys = labels
            .Select(label => new { Label = label, AccessKey = GetAccessKey(label) })
            .GroupBy(item => item.AccessKey)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(item => item.Label))}");

        duplicateAccessKeys.Should().BeEmpty();
    }

    private static string ReadChartFormatDialogSource()
    {
        return string.Join(
            "\n",
            new[]
            {
                "ChartFormatDialogs.cs",
                "ChartAxisFormatDialog.cs",
                "ChartDataLabelsDialog.cs",
                "ChartErrorBarsDialog.cs",
                "ChartTrendlineOptionsDialog.cs",
                "ChartSeriesFormatDialog.cs",
                "ChartTypeFormatDialogs.cs"
            }.Select(fileName => File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", fileName))));
    }

    private static char GetAccessKey(string label)
    {
        var index = label.IndexOf('_', StringComparison.Ordinal);
        return char.ToUpperInvariant(label[index + 1]);
    }

    [Fact]
    public void ChartAreaLegendDialogResult_BuildsLayoutOptions()
    {
        var result = ChartAreaLegendDialog.CreateResult(
            chartAreaFillColor: new CellColor(250, 250, 250),
            plotAreaFillColor: new CellColor(245, 250, 255),
            plotAreaBorderColor: new CellColor(120, 120, 120),
            plotAreaBorderThickness: 2.25,
            showLegend: true,
            legendPosition: ChartLegendPosition.Bottom,
            legendOverlay: true,
            legendTextColor: new CellColor(40, 40, 40),
            legendFillColor: new CellColor(248, 248, 248),
            legendBorderColor: new CellColor(180, 180, 180),
            legendBorderThickness: 1.25,
            legendFontSize: 11);

        result.ToOptions().Should().Be(new ChartLayoutOptions(
            ChartAreaFillColor: new CellColor(250, 250, 250),
            PlotAreaFillColor: new CellColor(245, 250, 255),
            PlotAreaBorderColor: new CellColor(120, 120, 120),
            PlotAreaBorderThickness: 2.25,
            LegendTextColor: new CellColor(40, 40, 40),
            LegendFillColor: new CellColor(248, 248, 248),
            LegendBorderColor: new CellColor(180, 180, 180),
            LegendBorderThickness: 1.25,
            LegendFontSize: 11,
            LegendPosition: ChartLegendPosition.Bottom,
            LegendOverlay: true,
            ShowLegend: true));
    }

    [Fact]
    public void ChartAreaLegendDialog_FromChart_UsesCurrentSettingsAndClampsNumbers()
    {
        var chart = new ChartModel
        {
            ChartAreaFillColor = new CellColor(1, 2, 3),
            PlotAreaBorderThickness = 99,
            ShowLegend = false,
            LegendPosition = ChartLegendPosition.Top,
            LegendBorderThickness = -4,
            LegendFontSize = 100
        };

        ChartAreaLegendDialog.FromChart(chart)
            .Should()
            .Be(new ChartAreaLegendDialogResult(
                new CellColor(1, 2, 3),
                null,
                null,
                10,
                false,
                ChartLegendPosition.Top,
                false,
                null,
                null,
                null,
                0,
                72));
    }

    [Fact]
    public void ChartAreaLegendDialogOpenedFromKeyboard_FocusesChartAreaFillBox()
    {
        var dialogSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ChartFormatDialogs.cs"));

        dialogSource.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        dialogSource.Should().Contain("private void FocusInitialKeyboardTarget()");
        dialogSource.Should().Contain("_chartAreaFillBox.Focus();");
        dialogSource.Should().Contain("_chartAreaFillBox.SelectAll();");
        dialogSource.Should().Contain("Keyboard.Focus(_chartAreaFillBox);");
    }

    [Fact]
    public void ChartAreaLegendDialogInvalidInputs_ShowOwnedWarningsAndRefocusEditors()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ChartFormatDialogs.cs"));

        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartDialog_InvalidOptionalColorMessage\"), _chartAreaFillBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartDialog_InvalidOptionalColorMessage\"), _plotAreaFillBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartDialog_InvalidOptionalColorMessage\"), _plotAreaBorderBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartAreaLegend_InvalidPlotAreaBorderWidthMessage\"), _plotAreaBorderThicknessBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartDialog_InvalidOptionalColorMessage\"), _legendTextBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartDialog_InvalidOptionalColorMessage\"), _legendFillBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartDialog_InvalidOptionalColorMessage\"), _legendBorderBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartAreaLegend_InvalidLegendBorderWidthMessage\"), _legendBorderThicknessBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartAreaLegend_InvalidLegendFontSizeMessage\"), _legendFontSizeBox);");
        source.Should().Contain("DialogMessageHelper.ShowWarning(this,");
        source.Should().Contain("private bool ShowInvalidInputWarning(string message, TextBox target)");
        source.Should().Contain("target.SelectAll();");
        source.Should().Contain("Keyboard.Focus(target);");
    }

    [Fact]
    public void ChartDataLabelsDialogResult_BuildsLayoutOptions()
    {
        var result = ChartDataLabelsDialog.CreateResult(
            showDataLabels: true,
            position: ChartDataLabelPosition.OutsideEnd,
            showValue: false,
            showLegendKey: true,
            showCategoryName: true,
            showSeriesName: false,
            showPercentage: true,
            separator: ChartDataLabelSeparator.NewLine,
            numberFormat: ChartDataLabelNumberFormat.Percent,
            showCallouts: true,
            fillColor: new CellColor(240, 240, 240),
            borderColor: new CellColor(10, 20, 30),
            textColor: new CellColor(40, 50, 60),
            borderThickness: 1.5,
            fontSize: 12,
            angle: -45);

        result.ToOptions().Should().Be(new ChartLayoutOptions(
            ShowDataLabels: true,
            DataLabelPosition: ChartDataLabelPosition.OutsideEnd,
            ShowDataLabelValue: false,
            ShowDataLabelLegendKey: true,
            ShowDataLabelCategoryName: true,
            ShowDataLabelSeriesName: false,
            ShowDataLabelPercentage: true,
            DataLabelSeparator: ChartDataLabelSeparator.NewLine,
            DataLabelNumberFormat: ChartDataLabelNumberFormat.Percent,
            ShowDataLabelCallouts: true,
            DataLabelFillColor: new CellColor(240, 240, 240),
            DataLabelBorderColor: new CellColor(10, 20, 30),
            DataLabelTextColor: new CellColor(40, 50, 60),
            DataLabelBorderThickness: 1.5,
            DataLabelFontSize: 12,
            DataLabelAngle: -45));
    }

    [Fact]
    public void ChartDataLabelsDialog_FromChart_RoundTripsValueAndLegendKeyToggles()
    {
        var chart = new ChartModel
        {
            ShowDataLabels = true,
            ShowDataLabelValue = false,
            ShowDataLabelLegendKey = true,
            ShowDataLabelCategoryName = true
        };

        var result = ChartDataLabelsDialog.FromChart(chart);

        result.ShowValue.Should().BeFalse();
        result.ShowLegendKey.Should().BeTrue();
        result.ShowCategoryName.Should().BeTrue();
        result.ToOptions().ShowDataLabelValue.Should().BeFalse();
        result.ToOptions().ShowDataLabelLegendKey.Should().BeTrue();
    }

    [Fact]
    public void ChartDataLabelsDialogOpenedFromKeyboard_FocusesShowDataLabelsChoice()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ChartDataLabelsDialog.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_showBox.Focus();");
        source.Should().Contain("Keyboard.Focus(_showBox);");
    }

    [Fact]
    public void ChartDataLabelsDialogInvalidInputs_ShowOwnedWarningsAndRefocusEditors()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ChartDataLabelsDialog.cs"));

        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartDialog_InvalidOptionalColorMessage\"), _fillBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartDialog_InvalidOptionalColorMessage\"), _borderBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartDialog_InvalidOptionalColorMessage\"), _textBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartDataLabels_InvalidBorderThicknessMessage\"), _borderThicknessBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartDataLabels_InvalidFontSizeMessage\"), _fontSizeBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartDataLabels_InvalidAngleMessage\"), _angleBox);");
        source.Should().Contain("DialogMessageHelper.ShowWarning(this,");
        source.Should().Contain("private bool ShowInvalidInputWarning(string message, TextBox target)");
        source.Should().Contain("target.SelectAll();");
        source.Should().Contain("Keyboard.Focus(target);");
    }

    [Fact]
    public void ChartTrendlineOptionsDialogResult_BuildsLayoutOptions()
    {
        var result = ChartTrendlineOptionsDialog.CreateResult(
            showTrendline: true,
            type: ChartTrendlineType.Polynomial,
            period: 4,
            order: 5,
            showEquation: true,
            showRSquared: true,
            color: new CellColor(80, 90, 100),
            thickness: 2.25,
            dashStyle: ChartLineDashStyle.Dot);

        result.ToOptions().Should().Be(new ChartLayoutOptions(
            ShowLinearTrendline: true,
            TrendlineType: ChartTrendlineType.Polynomial,
            TrendlinePeriod: 4,
            TrendlineOrder: 5,
            ShowTrendlineEquation: true,
            ShowTrendlineRSquared: true,
            TrendlineColor: new CellColor(80, 90, 100),
            TrendlineThickness: 2.25,
            TrendlineDashStyle: ChartLineDashStyle.Dot));
    }

    [Fact]
    public void ChartTrendlineOptionsDialogOpenedFromKeyboard_FocusesShowTrendlineChoice()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ChartTrendlineOptionsDialog.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_showBox.Focus();");
        source.Should().Contain("Keyboard.Focus(_showBox);");
    }

    [Fact]
    public void ChartTrendlineOptionsDialogInvalidInputs_ShowOwnedWarningsAndRefocusEditors()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ChartTrendlineOptionsDialog.cs"));

        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartTrendline_InvalidPeriodMessage\"), _periodBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartTrendline_InvalidOrderMessage\"), _orderBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartDialog_InvalidOptionalColorMessage\"), _colorBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartTrendline_InvalidWidthMessage\"), _thicknessBox);");
        source.Should().Contain("DialogMessageHelper.ShowWarning(this,");
        source.Should().Contain("private bool ShowInvalidInputWarning(string message, TextBox target)");
        source.Should().Contain("target.SelectAll();");
        source.Should().Contain("Keyboard.Focus(target);");
    }

    [Fact]
    public void ChartErrorBarsDialogResult_BuildsLayoutOptions()
    {
        var result = ChartErrorBarsDialog.CreateResult(
            showErrorBars: true,
            kind: ChartErrorBarKind.FixedValue,
            direction: ChartErrorBarDirection.Minus,
            value: 7.5,
            endCaps: false);

        result.ToOptions().Should().Be(new ChartLayoutOptions(
            ShowErrorBars: true,
            ErrorBarKind: ChartErrorBarKind.FixedValue,
            ErrorBarDirection: ChartErrorBarDirection.Minus,
            ErrorBarValue: 7.5,
            ErrorBarEndCaps: false));
    }

    [Fact]
    public void ChartErrorBarsDialog_FromChart_UsesCurrentSettingsAndClampsValue()
    {
        var chart = new ChartModel
        {
            ShowErrorBars = true,
            ErrorBarKind = ChartErrorBarKind.Percentage,
            ErrorBarDirection = ChartErrorBarDirection.Plus,
            ErrorBarValue = 5000,
            ErrorBarEndCaps = false
        };

        ChartErrorBarsDialog.FromChart(chart)
            .Should()
            .Be(new ChartErrorBarsDialogResult(
                true,
                ChartErrorBarKind.Percentage,
                ChartErrorBarDirection.Plus,
                1000,
                false));
    }

    [Fact]
    public void ChartErrorBarsDialogOpenedFromKeyboard_FocusesShowErrorBarsChoice()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ChartErrorBarsDialog.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_showBox.Focus();");
        source.Should().Contain("Keyboard.Focus(_showBox);");
    }

    [Fact]
    public void ChartErrorBarsDialog_ValueEditorExposesAutomationName()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ChartErrorBarsDialog.cs"));

        source.Should().Contain("AutomationProperties.SetName(_valueBox, UiText.Get(\"ChartErrorBars_ValueAutomationName\"));");
    }

    [Fact]
    public void ChartErrorBarsDialogInvalidValue_ShowsOwnedWarningAndRefocusesValueBox()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ChartErrorBarsDialog.cs"));

        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartErrorBars_InvalidValueMessage\"), _valueBox);");
        source.Should().Contain("DialogMessageHelper.ShowWarning(this,");
        source.Should().Contain("private bool ShowInvalidInputWarning(string message, TextBox target)");
        source.Should().Contain("target.SelectAll();");
        source.Should().Contain("Keyboard.Focus(target);");
    }

    [Fact]
    public void ChartAxisFormatDialogResult_BuildsAxisSpecificLayoutOptions()
    {
        var yAxis = ChartAxisFormatDialog.CreateResult(
            useXAxis: false,
            minimum: 0,
            maximum: 100,
            majorUnit: 10,
            minorUnit: 5,
            logScale: true,
            numberFormat: ChartDataLabelNumberFormat.Number,
            showMajorGridlines: true,
            showMinorGridlines: false,
            majorGridlineColor: new CellColor(200, 200, 200),
            minorGridlineColor: new CellColor(220, 220, 220),
            gridlineThickness: 1.25,
            majorTickStyle: ChartAxisTickStyle.Cross,
            minorTickStyle: ChartAxisTickStyle.Inside,
            showLabels: true,
            labelTextColor: new CellColor(1, 2, 3),
            labelFontSize: 13,
            labelAngle: 30,
            lineColor: new CellColor(4, 5, 6),
            lineThickness: 2);

        yAxis.ToOptions().Should().Be(new ChartLayoutOptions(
            YAxisMinimum: 0,
            YAxisMaximum: 100,
            YAxisMajorUnit: 10,
            YAxisMinorUnit: 5,
            YAxisLogScale: true,
            YAxisNumberFormat: ChartDataLabelNumberFormat.Number,
            ShowYAxisMajorGridlines: true,
            ShowYAxisMinorGridlines: false,
            YAxisMajorGridlineColor: new CellColor(200, 200, 200),
            YAxisMinorGridlineColor: new CellColor(220, 220, 220),
            YAxisGridlineThickness: 1.25,
            YAxisMajorTickStyle: ChartAxisTickStyle.Cross,
            YAxisMinorTickStyle: ChartAxisTickStyle.Inside,
            ShowYAxisLabels: true,
            YAxisLabelTextColor: new CellColor(1, 2, 3),
            YAxisLabelFontSize: 13,
            YAxisLabelAngle: 30,
            YAxisLineColor: new CellColor(4, 5, 6),
            YAxisLineThickness: 2));
    }

    [Fact]
    public void ChartAxisFormatDialogOpenedFromKeyboard_FocusesMinimumBox()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ChartAxisFormatDialog.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_minimumBox.Focus();");
        source.Should().Contain("_minimumBox.SelectAll();");
        source.Should().Contain("Keyboard.Focus(_minimumBox);");
    }

    [Fact]
    public void ChartAxisFormatDialogInvalidInputs_ShowOwnedWarningsAndRefocusEditors()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ChartAxisFormatDialog.cs"));

        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartAxisFormat_InvalidMinimumMessage\"), _minimumBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartAxisFormat_InvalidMaximumMessage\"), _maximumBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartAxisFormat_InvalidMajorUnitMessage\"), _majorUnitBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartAxisFormat_InvalidMinorUnitMessage\"), _minorUnitBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartDialog_InvalidOptionalColorMessage\"), _majorGridColorBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartDialog_InvalidOptionalColorMessage\"), _minorGridColorBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartAxisFormat_InvalidGridlineWidthMessage\"), _gridlineThicknessBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartDialog_InvalidOptionalColorMessage\"), _labelColorBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartAxisFormat_InvalidLabelFontSizeMessage\"), _labelFontSizeBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartAxisFormat_InvalidLabelAngleMessage\"), _labelAngleBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartDialog_InvalidOptionalColorMessage\"), _lineColorBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartAxisFormat_InvalidAxisLineWidthMessage\"), _lineThicknessBox);");
        source.Should().Contain("DialogMessageHelper.ShowWarning(this,");
        source.Should().Contain("private bool ShowInvalidInputWarning(string message, TextBox target)");
        source.Should().Contain("target.SelectAll();");
        source.Should().Contain("Keyboard.Focus(target);");
    }

    [Fact]
    public void ChartSeriesFormatDialogResult_ReplacesSelectedSeriesFormat()
    {
        var result = ChartSeriesFormatDialog.CreateResult(
            seriesIndex: 2,
            fillColor: new CellColor(10, 20, 30),
            strokeColor: new CellColor(40, 50, 60),
            strokeThickness: 2.5,
            dashStyle: ChartLineDashStyle.Dash,
            markerStyle: ChartMarkerStyle.Diamond,
            markerSize: 9);

        var options = result.ToOptions([
            new ChartSeriesFormat(0, FillColor: new CellColor(1, 1, 1)),
            new ChartSeriesFormat(2, FillColor: new CellColor(2, 2, 2))
        ]);

        options.SeriesFormats.Should().NotBeNull();
        options.SeriesFormats!.Should().ContainSingle(format => format.SeriesIndex == 2)
            .Which.Should().Be(new ChartSeriesFormat(
                2,
                FillColor: new CellColor(10, 20, 30),
                StrokeColor: new CellColor(40, 50, 60),
                StrokeThickness: 2.5,
                DashStyle: ChartLineDashStyle.Dash,
                MarkerStyle: ChartMarkerStyle.Diamond,
                MarkerSize: 9));
        options.SeriesFormats.Should().ContainSingle(format => format.SeriesIndex == 0);
    }

    [Fact]
    public void ChartSeriesFormatDialogOpenedFromKeyboard_FocusesSeriesSelector()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ChartSeriesFormatDialog.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_seriesBox.Focus();");
        source.Should().Contain("Keyboard.Focus(_seriesBox);");
    }

    [Fact]
    public void ChartSeriesFormatDialogInvalidInputs_ShowOwnedWarningsAndRefocusEditors()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ChartSeriesFormatDialog.cs"));

        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartDialog_InvalidOptionalColorMessage\"), _fillBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartDialog_InvalidOptionalColorMessage\"), _strokeBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartSeriesFormat_InvalidLineWidthMessage\"), _strokeThicknessBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartSeriesFormat_InvalidMarkerSizeMessage\"), _markerSizeBox);");
        source.Should().Contain("DialogMessageHelper.ShowWarning(this,");
        source.Should().Contain("private bool ShowInvalidInputWarning(string message, TextBox target)");
        source.Should().Contain("target.SelectAll();");
        source.Should().Contain("Keyboard.Focus(target);");
    }

    [Fact]
    public void ChartDialogs_LabelEditableHelperControlsWithTargets()
    {
        var source = ReadChartDialogSource();
        var helperSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ChartDialogHelpers.cs"));

        foreach (var expected in new[]
        {
            "new Label { Content = label, Target = box",
            "new Label { Content = UiText.Get(\"ChartStyle_StyleLabel\"), Target = _styleGallery"
        })
            source.Should().Contain(expected);

        foreach (var expected in new[]
        {
            "new Label { Content = label, Target = comboBox",
            "new Label { Content = label, Target = textBox"
        })
            helperSource.Should().Contain(expected);

        source.Should().NotContain("stack.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 3, 0, 4) })");
        helperSource.Should().NotContain("stack.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 3, 0, 4) })");
    }

    private static IEnumerable<T> FindLogicalDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T typedChild)
                yield return typedChild;

            foreach (var descendant in FindLogicalDescendants<T>(child))
                yield return descendant;
        }
    }

    [Fact]
    public void ChartBarFormatDialogResult_ClampsGapWidthTo0To500()
    {
        ChartBarFormatDialogResult.CreateResult(-10, 0).BarGapWidth.Should().Be(0);
        ChartBarFormatDialogResult.CreateResult(600, 0).BarGapWidth.Should().Be(500);
        ChartBarFormatDialogResult.CreateResult(150, 0).BarGapWidth.Should().Be(150);
        ChartBarFormatDialogResult.CreateResult(0, 0).BarGapWidth.Should().Be(0);
    }

    [Fact]
    public void ChartBarFormatDialogResult_ClampsOverlapToMinus100To100()
    {
        ChartBarFormatDialogResult.CreateResult(150, -200).BarOverlap.Should().Be(-100);
        ChartBarFormatDialogResult.CreateResult(150, 200).BarOverlap.Should().Be(100);
        ChartBarFormatDialogResult.CreateResult(150, 50).BarOverlap.Should().Be(50);
        ChartBarFormatDialogResult.CreateResult(150, -50).BarOverlap.Should().Be(-50);
    }

    [Fact]
    public void ChartBarFormatDialogResult_LoadsFromChart()
    {
        var chart = new ChartModel { Type = ChartType.Column, BarGapWidth = 200, BarOverlap = 30 };
        var result = ChartBarFormatDialogResult.FromChart(chart);
        result.BarGapWidth.Should().Be(200);
        result.BarOverlap.Should().Be(30);
    }

    [Fact]
    public void ChartBarFormatDialogResult_UsesDefaultsWhenChartHasNoGapWidth()
    {
        var chart = new ChartModel { Type = ChartType.Column };
        var result = ChartBarFormatDialogResult.FromChart(chart);
        result.BarGapWidth.Should().Be(150);
        result.BarOverlap.Should().Be(0);
    }

    [Fact]
    public void ChartBarFormatDialogResult_MapsToLayoutOptions()
    {
        var result = ChartBarFormatDialogResult.CreateResult(200, 30);
        result.ToOptions().BarGapWidth.Should().Be(200);
        result.ToOptions().BarOverlap.Should().Be(30);
    }

    [Fact]
    public void ChartBubbleFormatDialogResult_ClampsBubbleScaleTo1To300()
    {
        ChartBubbleFormatDialogResult.CreateResult(0, false, ChartBubbleSizeRepresents.Area).BubbleScale.Should().Be(1);
        ChartBubbleFormatDialogResult.CreateResult(400, false, ChartBubbleSizeRepresents.Area).BubbleScale.Should().Be(300);
        ChartBubbleFormatDialogResult.CreateResult(100, false, ChartBubbleSizeRepresents.Area).BubbleScale.Should().Be(100);
    }

    [Fact]
    public void ChartBubbleFormatDialogResult_LoadsFromChart()
    {
        var chart = new ChartModel { Type = ChartType.Bubble, BubbleScale = 150, ShowNegativeBubbles = true, BubbleSizeRepresents = ChartBubbleSizeRepresents.Width };
        var result = ChartBubbleFormatDialogResult.FromChart(chart);
        result.BubbleScale.Should().Be(150);
        result.ShowNegativeBubbles.Should().BeTrue();
        result.BubbleSizeRepresents.Should().Be(ChartBubbleSizeRepresents.Width);
    }

    [Fact]
    public void ChartBubbleFormatDialogResult_MapsToLayoutOptions()
    {
        var result = ChartBubbleFormatDialogResult.CreateResult(150, true, ChartBubbleSizeRepresents.Width);
        result.ToOptions().BubbleScale.Should().Be(150);
        result.ToOptions().ShowNegativeBubbles.Should().BeTrue();
        result.ToOptions().BubbleSizeRepresents.Should().Be(ChartBubbleSizeRepresents.Width);
    }

    [Fact]
    public void ChartPieFormatDialogResult_ClampsFirstSliceAngleTo0To359()
    {
        ChartPieFormatDialogResult.CreateResult(-10, -1, 0.1, 0.55).FirstSliceAngle.Should().Be(0);
        ChartPieFormatDialogResult.CreateResult(400, -1, 0.1, 0.55).FirstSliceAngle.Should().Be(359);
        ChartPieFormatDialogResult.CreateResult(180, -1, 0.1, 0.55).FirstSliceAngle.Should().Be(180);
    }

    [Fact]
    public void ChartPieFormatDialogResult_ClampsExplodedSliceDistanceTo0To50Percent()
    {
        ChartPieFormatDialogResult.CreateResult(0, 0, -0.1, 0.55).ExplodedSliceDistance.Should().Be(0);
        ChartPieFormatDialogResult.CreateResult(0, 0, 0.8, 0.55).ExplodedSliceDistance.Should().Be(0.5);
        ChartPieFormatDialogResult.CreateResult(0, 0, 0.25, 0.55).ExplodedSliceDistance.Should().BeApproximately(0.25, 0.0001);
    }

    [Fact]
    public void ChartPieFormatDialogResult_ClampsDoughnutHoleSizeTo10To90Percent()
    {
        ChartPieFormatDialogResult.CreateResult(0, -1, 0.1, 0.05).DoughnutHoleSize.Should().Be(0.1);
        ChartPieFormatDialogResult.CreateResult(0, -1, 0.1, 0.95).DoughnutHoleSize.Should().Be(0.9);
        ChartPieFormatDialogResult.CreateResult(0, -1, 0.1, 0.75).DoughnutHoleSize.Should().BeApproximately(0.75, 0.0001);
    }

    [Fact]
    public void ChartPieFormatDialogResult_LoadsFromChart()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Doughnut,
            FirstSliceAngle = 45,
            ExplodedSliceIndex = 2,
            ExplodedSliceDistance = 0.2,
            DoughnutHoleSize = 0.6
        };
        var result = ChartPieFormatDialogResult.FromChart(chart);
        result.FirstSliceAngle.Should().Be(45);
        result.ExplodedSliceIndex.Should().Be(2);
        result.ExplodedSliceDistance.Should().BeApproximately(0.2, 0.0001);
        result.DoughnutHoleSize.Should().BeApproximately(0.6, 0.0001);
    }

    [Fact]
    public void ChartPieFormatDialogResult_MapsToLayoutOptions()
    {
        var result = ChartPieFormatDialogResult.CreateResult(90, 1, 0.3, 0.7);
        result.ToOptions().FirstSliceAngle.Should().Be(90);
        result.ToOptions().ExplodedSliceIndex.Should().Be(1);
        result.ToOptions().ExplodedSliceDistance.Should().BeApproximately(0.3, 0.0001);
        result.ToOptions().DoughnutHoleSize.Should().BeApproximately(0.7, 0.0001);
    }

    [Fact]
    public void ChartStockFormatDialogResult_ClampsUpDownBarGapWidthTo0To500()
    {
        ChartStockFormatDialogResult.CreateResult(-5, null, null, null, null, null, 1.0).UpDownBarGapWidth.Should().Be(0);
        ChartStockFormatDialogResult.CreateResult(600, null, null, null, null, null, 1.0).UpDownBarGapWidth.Should().Be(500);
        ChartStockFormatDialogResult.CreateResult(150, null, null, null, null, null, 1.0).UpDownBarGapWidth.Should().Be(150);
    }

    [Fact]
    public void ChartStockFormatDialogResult_ClampsHighLowLineThicknessTo05To10()
    {
        ChartStockFormatDialogResult.CreateResult(150, null, null, null, null, null, 0.1).HighLowLineThickness.Should().BeApproximately(0.5, 0.001);
        ChartStockFormatDialogResult.CreateResult(150, null, null, null, null, null, 20.0).HighLowLineThickness.Should().BeApproximately(10.0, 0.001);
        ChartStockFormatDialogResult.CreateResult(150, null, null, null, null, null, 1.5).HighLowLineThickness.Should().BeApproximately(1.5, 0.001);
    }

    [Fact]
    public void ChartStockFormatDialogResult_LoadsFromChart()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Stock,
            UpDownBarGapWidth = 200,
            UpBarFillColor = new CellColor(0, 128, 0),
            DownBarFillColor = new CellColor(255, 0, 0),
            HighLowLineColor = new CellColor(100, 100, 100),
            HighLowLineThickness = 2.0
        };
        var result = ChartStockFormatDialogResult.FromChart(chart);
        result.UpDownBarGapWidth.Should().Be(200);
        result.UpBarFillColor.Should().Be(new CellColor(0, 128, 0));
        result.DownBarFillColor.Should().Be(new CellColor(255, 0, 0));
        result.HighLowLineColor.Should().Be(new CellColor(100, 100, 100));
        result.HighLowLineThickness.Should().BeApproximately(2.0, 0.001);
    }

    [Fact]
    public void ChartStockFormatDialogResult_MapsToLayoutOptions()
    {
        var result = ChartStockFormatDialogResult.CreateResult(
            150, new CellColor(0, 200, 0), new CellColor(0, 100, 0),
            new CellColor(200, 0, 0), new CellColor(100, 0, 0),
            new CellColor(80, 80, 80), 1.5);
        result.ToOptions().UpDownBarGapWidth.Should().Be(150);
        result.ToOptions().UpBarFillColor.Should().Be(new CellColor(0, 200, 0));
        result.ToOptions().UpBarBorderColor.Should().Be(new CellColor(0, 100, 0));
        result.ToOptions().DownBarFillColor.Should().Be(new CellColor(200, 0, 0));
        result.ToOptions().DownBarBorderColor.Should().Be(new CellColor(100, 0, 0));
        result.ToOptions().HighLowLineColor.Should().Be(new CellColor(80, 80, 80));
        result.ToOptions().HighLowLineThickness.Should().BeApproximately(1.5, 0.001);
    }

    private static string ReadChartDialogSource() =>
        string.Join(Environment.NewLine, new[]
        {
            "ChartDialogs.cs",
            "SelectDataSourceDialog.cs",
            "SelectDataSourceDialog.Planning.cs",
            "SelectDataSourceDialog.Controls.cs",
            "SelectDataSourceDialog.Actions.cs"
        }.Select(file => File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", file))));

    private static string ReadChartTypeDialogSource() =>
        string.Join(Environment.NewLine, new[]
        {
            "ChartTypeDialogs.cs",
            "ChartTypeDialogs.Planner.cs",
            "ChartTypeDialogs.PickerUi.cs",
            "ChartTypeDialogs.Change.cs"
        }.Select(file => File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", file))));
}
