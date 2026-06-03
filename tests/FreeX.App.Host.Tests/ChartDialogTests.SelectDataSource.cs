using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class ChartDialogTests
{
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
        dialogSource.Should().Contain("MessageBox.Show(owner,"); // static handler with dynamic owner â€” kept as raw call
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

}
