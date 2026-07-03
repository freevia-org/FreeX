using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class PivotWorkflowDialogTests
{
    [Fact]
    public void PivotAuxiliaryDialogs_LabelEditableFieldsWithAccessKeyTargets()
    {
        var source = ReadPivotWorkflowSource();

        foreach (var content in new[]
        {
            "PivotDialogLayout.AddLabeledControl(",
            "UiText.Get(\"PivotTableDataSource_TableRangeLabel\")",
            "CreateReferenceEditor(_sourceBox",
            "_sourceBox,",
            "PivotDialogLayout.AddLabeledControl(fieldPanel, UiText.Get(\"PivotSlicerTimeline_FieldToConnectLabel\"), _fieldBox",
            "PivotDialogLayout.AddLabeledControl(fieldPanel, UiText.Get(\"PivotSlicerTimeline_SlicerCaptionLabel\"), _nameBox",
            "PivotDialogLayout.AddLabeledControl(fieldPanel, UiText.Get(\"PivotSlicerTimeline_DateFieldToConnectLabel\"), _fieldBox",
            "PivotDialogLayout.AddLabeledControl(fieldPanel, UiText.Get(\"PivotSlicerTimeline_TimelineCaptionLabel\"), _nameBox",
            "InsertChartDialog.CreateAllChartsPanel(_categoryList, _subtypeGallery",
            "ApplyFieldAutomation(_styleGallery, PivotChartOptionsDialogFieldId.ChartStyle)",
            "AddCombo(selectionPanel, UiText.Get(\"PivotFieldGrouping_FieldLabel\"), _fieldBox",
            "AddCombo(groupingPanel, UiText.Get(\"PivotFieldGrouping_GroupByLabel\"), _groupingBox",
            "AddTextBox(rangePanel, UiText.Get(\"PivotFieldGrouping_StartingAtLabel\"), _startBox",
            "AddTextBox(rangePanel, UiText.Get(\"PivotFieldGrouping_EndingAtLabel\"), _endBox",
            "AddTextBox(rangePanel, UiText.Get(\"PivotFieldGrouping_ByLabel\"), _intervalBox",
            "AddTextBox(formulaPanel, UiText.Get(\"PivotCalculated_NameLabel\"), _nameBox",
            "AddTextBox(formulaPanel, UiText.Get(\"PivotCalculated_FormulaLabel\"), _formulaBox",
            "PivotDialogLayout.AddLabeledControl(itemPanel, UiText.Get(\"PivotCalculated_SourceFieldLabel\"), _fieldBox",
            "AddTextBox(itemPanel, UiText.Get(\"PivotCalculated_NameLabel\"), _nameBox",
            "AddTextBox(itemPanel, UiText.Get(\"PivotCalculated_ItemFormulaLabel\"), _formulaBox",
            "public static void AddLabeledControl(Panel stack, string label, UIElement control",
            "Target = target"
        })
            source.Should().Contain(content);
    }

    [Fact]
    public void InsertSlicerDialog_CreateResult_CapturesFieldAndSlicerName()
    {
        InsertSlicerDialog.CreateResult("  Region  ", "  Region Slicer  ")
            .Should()
            .Be(new InsertSlicerDialogResult("Region", "Region Slicer"));
    }

    [Fact]
    public void InsertSlicerDialog_TryCreateResult_RejectsBlankFieldOrCaption()
    {
        InsertSlicerDialog.TryCreateResult(" ", "Region Slicer", out _, out var fieldError)
            .Should()
            .BeFalse();
        fieldError.Should().Be("Select a field to connect.");

        InsertSlicerDialog.TryCreateResult("Region", " ", out _, out var captionError)
            .Should()
            .BeFalse();
        captionError.Should().Be("Enter a slicer caption.");
    }

    [Fact]
    public void InsertSlicerDialog_AcceptWarnsAndRefocusesInvalidInput()
    {
        var source = ReadClassSource(
            "PivotSlicerTimelineDialogs.cs",
            "public sealed class InsertSlicerDialog",
            "public sealed record InsertTimelineDialogResult");

        source.Should().Contain("if (!TryCreateResult(_fieldBox.Text, _nameBox.Text, out var result, out var error))");
        source.Should().Contain("ShowInvalidInputWarning(error ?? UiText.Get(\"PivotSlicerTimeline_EnterSlicerOptions\")");
        source.Should().Contain("DialogFocus.ShowWarningAndFocus(this, message, Title, target);");
    }

    [Fact]
    public void InsertSlicerDialog_ExposesExcelLikeFieldSelectionShell()
    {
        var source = ReadPivotWorkflowSource();

        source.Should().Contain("Width = PivotSlicerTimelineDialogContract.Width");
        source.Should().Contain("Height = PivotSlicerTimelineDialogContract.Height");
        source.Should().Contain("UiText.Get(\"PivotSlicerTimeline_ChooseFieldsGroup\")");
        source.Should().Contain("UiText.Get(\"PivotSlicerTimeline_FieldToConnectLabel\")");
        source.Should().Contain("UiText.Get(\"PivotSlicerTimeline_SlicerCaptionLabel\")");
        source.Should().Contain("DialogButtonRowFactory.Create");
        source.Should().NotContain("Slicers make it faster to filter a PivotTable");
    }

    [Fact]
    public void InsertSlicerDialogOpenedFromKeyboard_FocusesFieldBox()
    {
        var source = ReadClassSource(
            "PivotSlicerTimelineDialogs.cs",
            "public sealed class InsertSlicerDialog",
            "public sealed record InsertTimelineDialogResult");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_fieldBox.Focus();");
        source.Should().Contain("Keyboard.Focus(_fieldBox);");
    }

    [Fact]
    public void InsertTimelineDialog_CreateResult_CapturesDateFieldAndTimelineName()
    {
        InsertTimelineDialog.CreateResult("  Order Date  ", "  Order Date Timeline  ")
            .Should()
            .Be(new InsertTimelineDialogResult("Order Date", "Order Date Timeline"));
    }

    [Fact]
    public void InsertTimelineDialog_TryCreateResult_RejectsBlankDateFieldOrCaption()
    {
        InsertTimelineDialog.TryCreateResult(" ", "Order Date Timeline", out _, out var fieldError)
            .Should()
            .BeFalse();
        fieldError.Should().Be("Select a date field to connect.");

        InsertTimelineDialog.TryCreateResult("Order Date", " ", out _, out var captionError)
            .Should()
            .BeFalse();
        captionError.Should().Be("Enter a timeline caption.");
    }

    [Fact]
    public void InsertTimelineDialog_AcceptWarnsAndRefocusesInvalidInput()
    {
        var source = ReadClassSource(
            "PivotSlicerTimelineDialogs.cs",
            "public sealed class InsertTimelineDialog",
            "");

        source.Should().Contain("if (!TryCreateResult(_fieldBox.Text, _nameBox.Text, out var result, out var error))");
        source.Should().Contain("ShowInvalidInputWarning(error ?? UiText.Get(\"PivotSlicerTimeline_EnterTimelineOptions\")");
        source.Should().Contain("DialogFocus.ShowWarningAndFocus(this, message, Title, target);");
    }

    [Fact]
    public void InsertTimelineDialog_ExposesExcelLikeDateFieldSelectionShell()
    {
        var source = ReadPivotWorkflowSource();

        source.Should().Contain("UiText.Get(\"PivotSlicerTimeline_ChooseDateFieldsGroup\")");
        source.Should().Contain("UiText.Get(\"PivotSlicerTimeline_DateFieldToConnectLabel\")");
        source.Should().Contain("UiText.Get(\"PivotSlicerTimeline_TimelineCaptionLabel\")");
        source.Should().NotContain("Timelines filter PivotTables by date");
    }

    [Fact]
    public void InsertTimelineDialogOpenedFromKeyboard_FocusesFieldBox()
    {
        var source = ReadClassSource(
            "PivotSlicerTimelineDialogs.cs",
            "public sealed class InsertTimelineDialog",
            "");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_fieldBox.Focus();");
        source.Should().Contain("Keyboard.Focus(_fieldBox);");
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
    public void PivotChartTypeDialog_ExposesSelectableRecommendedPivotCharts()
    {
        var source = ReadPivotWorkflowSource();

        source.Should().Contain("Header = UiText.Get(\"PivotChartType_RecommendedPivotCharts\")");
        source.Should().Contain("Header = UiText.Get(\"PivotChartType_AllCharts\")");
        source.Should().Contain("private readonly ListBox _recommendedGallery");
        source.Should().Contain("CreateRecommendedChartsPanel(_recommendedGallery)");
        source.Should().Contain("SelectedGalleryChoice()");
        source.Should().NotContain("Pick a chart type for the selected PivotTable data");
        source.Should().Contain("InsertChartDialog.CreateAllChartsPanel");
        source.Should().Contain("UiText.Get(\"PivotChartType_ChartCategoriesAndChartSubtypeGalleryMatchTheInsertChartPicker\")");
        source.Should().NotContain("private readonly ComboBox _chartTypeBox");
    }

    [Fact]
    public void PivotChartTypeDialogOpenedFromKeyboard_FocusesRecommendedGallery()
    {
        var source = ReadClassSource(
            "PivotChartTypeDialog.cs",
            "public sealed class PivotChartTypeDialog",
            "");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_recommendedGallery.Focus();");
        source.Should().Contain("Keyboard.Focus(_recommendedGallery);");
    }

    [Fact]
    public void PivotValueFilterDialog_DelegatesOptionAndInputPolicyToPresentationPlanner()
    {
        var source = DialogSourceTestSupport.ReadHostSources("PivotValueFilterDialog.xaml.cs");

        source.Should().Contain("PivotFieldFilterPlanner.ValueFilterKinds");
        source.Should().Contain("PivotFieldFilterPlanner.DefaultValueKindIndex");
        source.Should().Contain("PivotFieldFilterPlanner.TryCreateValueFilter");
        source.Should().Contain("PivotFieldFilterPlanner.ValueKindNeedsPrimaryInput");
        source.Should().Contain("PivotFieldFilterPlanner.ValueKindNeedsSecondValue");
        source.Should().Contain("PivotFieldFilterPlanner.DescribeValueFilterValidationError");
        source.Should().NotContain("PivotValueFilterInputParser.TryCreateFilter");
        source.Should().NotContain("bool UsesCount");
    }

    [Fact]
    public void PivotLabelFilterDialog_DelegatesOptionAndInputPolicyToPresentationPlanner()
    {
        var source = DialogSourceTestSupport.ReadHostSources("PivotLabelFilterDialog.xaml.cs");

        source.Should().Contain("PivotFieldFilterPlanner.LabelFilterKinds");
        source.Should().Contain("PivotFieldFilterPlanner.DefaultLabelKindIndex");
        source.Should().Contain("PivotFieldFilterPlanner.FindLabelKindIndex");
        source.Should().Contain("PivotFieldFilterPlanner.TryCreateLabelFilterWithValidationError");
        source.Should().Contain("PivotFieldFilterPlanner.LabelKindFromIndex");
        source.Should().Contain("PivotFieldFilterPlanner.LabelKindNeedsSecondValue");
        source.Should().Contain("PivotFieldFilterPlanner.DescribeLabelFilterValidationError");
        source.Should().NotContain("new PivotLabelFilterModel");
        source.Should().NotContain("LabelFilterValueBox.Text.Trim");
        source.Should().NotContain("LabelFilterValue2Box.Text.Trim");
        source.Should().NotContain("PivotLabelFilter_Equals");
        source.Should().NotContain("PivotLabelFilter_Contains");
        source.Should().NotContain("PivotLabelFilter_Between");
    }

    [Fact]
    public void PivotChartInsert_UsesTypeDialogInsteadOfHardCodedColumn()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.PivotChartCommands.cs");
        var methodStart = source.IndexOf("private void PivotChartBtn_Click", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private void PivotChartChangeTypeBtn_Click", StringComparison.Ordinal);
        methodStart.Should().BeGreaterThanOrEqualTo(0);
        methodEnd.Should().BeGreaterThan(methodStart);
        var method = source[methodStart..methodEnd];

        method.Should().Contain("new PivotChartTypeDialog(ChartType.Column)");
        method.Should().Contain("dialog.Result.ChartType");
        method.Should().NotContain("new AddPivotChartCommand(_currentSheetId, pivotTable.Name, ChartType.Column");
    }
}
