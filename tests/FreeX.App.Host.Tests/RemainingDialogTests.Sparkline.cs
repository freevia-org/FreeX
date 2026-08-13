using FluentAssertions;
using FreeX.App.Presentation.SparklineUI;
using FreeX.Core.Model;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FreeX.App.Host.Tests;

public sealed partial class RemainingDialogTests
{
    [Fact]
    public void SparklineDialog_CreateResult_TrimsRangeAndLocation()
    {
        SparklineDialog.CreateResult(" A1:E1 ", " F1 ", SparklineKind.Column)
            .Should()
            .Be(new SparklineDialogResult("A1:E1", "F1", SparklineKind.Column));
    }

    [Fact]
    public void SparklineDialog_CreateRangeSelectionRequest_TrimsCurrentTextAndRequestsCollapse()
    {
        SparklineDialog.CreateRangeSelectionRequest(SparklineRangeSelectionTarget.DataRange, " A1:E1 ")
            .Should()
            .Be(new SparklineRangeSelectionRequest(SparklineRangeSelectionTarget.DataRange, "A1:E1", CollapseDialog: true));
    }

    [Fact]
    public void SparklineDialog_RangePickerButtonsTriggerWorksheetSelectionIntent()
    {
        StaTestRunner.Run(() =>
        {
            var requests = new List<SparklineRangeSelectionRequest>();
            var dialog = new SparklineDialog("A1:E1", "F1", SparklineKind.Line, requests.Add);

            DialogSourceTestSupport.ClickButton(GetField<Button>(dialog, "_dataRangePickerButton"));
            DialogSourceTestSupport.ClickButton(GetField<Button>(dialog, "_locationPickerButton"));

            requests.Should().Equal(
                new SparklineRangeSelectionRequest(SparklineRangeSelectionTarget.DataRange, "A1:E1", CollapseDialog: true),
                new SparklineRangeSelectionRequest(SparklineRangeSelectionTarget.Location, "F1", CollapseDialog: true));
            dialog.RangeSelectionRequest.Should().Be(requests[^1]);
        });
    }

    [Fact]
    public void SparklineDialogApplyRangeSelection_UpdatesRequestedInput()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new SparklineDialog("A1:E1", "F1", SparklineKind.Line);
            try
            {
                dialog.ApplyRangeSelection(SparklineRangeSelectionTarget.DataRange, "Sheet2!A1:D6");
                dialog.ApplyRangeSelection(SparklineRangeSelectionTarget.Location, "K5");

                GetField<TextBox>(dialog, "_dataRangeBox").Text.Should().Be("Sheet2!A1:D6");
                GetField<TextBox>(dialog, "_locationBox").Text.Should().Be("K5");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void SparklineDialogOk_FromModelessWindowClosesWithoutDialogResultCrash()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new SparklineDialog("A1:E1", "F1", SparklineKind.Line, sheetId: SheetId.New());
            try
            {
                dialog.Show();

                DialogSourceTestSupport.ClickButton(GetSparklineOkButton(dialog));

                dialog.IsVisible.Should().BeFalse();
                dialog.Result.Should().Be(new SparklineDialogResult("A1:E1", "F1", SparklineKind.Line));
            }
            finally
            {
                if (dialog.IsVisible)
                    dialog.Close();
            }
        });
    }

    [Fact]
    public void SparklineDialogOk_FromModalDialogReturnsAcceptedResult()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new SparklineDialog(" A1:E1 ", " F1 ", SparklineKind.Column, sheetId: SheetId.New());
            dialog.Loaded += (_, _) =>
                dialog.Dispatcher.BeginInvoke(
                    () => DialogSourceTestSupport.ClickButton(GetSparklineOkButton(dialog)),
                    DispatcherPriority.Background);

            dialog.ShowDialog().Should().BeTrue();
            dialog.Result.Should().Be(new SparklineDialogResult("A1:E1", "F1", SparklineKind.Column));
        });
    }

    [Fact]
    public void MainWindow_WiresSparklineRangePickersToCurrentSelection()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.InsertCommands.cs");

        source.Should().Contain("new SparklineDialog(");
        source.Should().Contain("request => ApplySparklineRangeSelection(dialog, request)");
        source.Should().Contain("private void ApplySparklineRangeSelection(");
        source.Should().Contain("SparklineRangeSelectionRequest request");
        source.Should().Contain("BeginDialogRangeSelection(");
        source.Should().Contain("request.CollapseDialog");
        source.Should().Contain("request.Target == SparklineRangeSelectionTarget.Location");
        source.Should().Contain("FormatCellReference(selectedRange.Start)");
        source.Should().Contain("FormatWorkbookRange(selectedRange)");
        source.Should().Contain("dialog.ApplyRangeSelection(request.Target, rangeText);");
    }

    [Fact]
    public void SparklineDialog_ExposesRangePickerButtonsForDataAndLocation()
    {
        var source = ReadRemainingDialogSources();

        source.Should().Contain("_dataRangePickerButton");
        source.Should().Contain("_locationPickerButton");
        source.Should().Contain("UiText.Get(\"Sparkline_SelectDataRange\")");
        source.Should().Contain("UiText.Get(\"Sparkline_SelectLocationRange\")");
        source.Should().Contain("AutomationProperties.SetName(_dataRangePickerButton, UiText.Get(\"Sparkline_SelectSparklineDataRange\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_dataRangePickerButton, \"SparklineDataRangePickerButton\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_dataRangePickerButton, UiText.Get(\"Sparkline_SelectTheWorksheetDataRangeForTheSparkline\"));");
        source.Should().Contain("AutomationProperties.SetName(_locationPickerButton, UiText.Get(\"Sparkline_SelectSparklineLocationRange\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_locationPickerButton, \"SparklineLocationRangePickerButton\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_locationPickerButton, UiText.Get(\"Sparkline_SelectTheDestinationCellForTheSparkline\"));");
        source.Should().Contain("RequestRangeSelection");
    }

    [Fact]
    public void SparklineDialog_LabelsEditableControlsWithAccessKeyTargets()
    {
        var source = ReadRemainingDialogSources();

        source.Should().Contain("new Label { Content = UiText.Get(\"Sparkline_DataRange\"), Target = _dataRangeBox");
        source.Should().Contain("new Label { Content = UiText.Get(\"Sparkline_LocationRange\"), Target = _locationBox");
        source.Should().Contain("new Label { Content = UiText.Get(\"Sparkline_SparklineType\"), Target = _kindBox");
        source.Should().Contain("AutomationProperties.SetName(_kindBox, UiText.Get(\"Sparkline_SparklineTypeAutomationName\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_kindBox, \"SparklineTypeBox\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_kindBox, UiText.Get(\"Sparkline_ChooseWhetherTheSparklineIsLineColumnOrWinLoss\"));");
    }

    [Fact]
    public void SparklineDialog_RangeEditorsExposeAutomationMetadata()
    {
        var source = ReadRemainingDialogSources();

        source.Should().Contain("AutomationProperties.SetName(_dataRangeBox, UiText.Get(\"Sparkline_SparklineDataRange\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_dataRangeBox, \"SparklineDataRangeBox\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_dataRangeBox, UiText.Get(\"Sparkline_EnterTheWorksheetDataRangeForTheSparkline\"));");
        source.Should().Contain("AutomationProperties.SetName(_locationBox, UiText.Get(\"Sparkline_SparklineLocationRange\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_locationBox, \"SparklineLocationRangeBox\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_locationBox, UiText.Get(\"Sparkline_EnterTheDestinationCellForTheSparkline\"));");
    }

    [Fact]
    public void SparklineDialog_SizesVerticallyToKeepButtonsReachable()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new SparklineDialog("A1:E1", "F1", SparklineKind.Line);
            try
            {
                dialog.SizeToContent.Should().Be(SizeToContent.Height);
                dialog.MinHeight.Should().BeGreaterThanOrEqualTo(280);
                double.IsNaN(dialog.Height).Should().BeTrue();

                var stack = dialog.Content.Should().BeOfType<StackPanel>().Subject;
                var buttonRow = stack.Children[stack.Children.Count - 1].Should().BeOfType<StackPanel>().Subject;
                buttonRow.Children.OfType<Button>().Should().Contain(button => button.IsDefault);
                buttonRow.Children.OfType<Button>().Should().Contain(button => button.IsCancel);
            }
            finally
            {
                dialog.Close();
            }
        });

        var source = DialogSourceTestSupport.ReadHostSources("SparklineDialog.cs");
        source.Should().Contain("SizeToContent = SizeToContent.Height;");
        source.Should().NotContain("Height = 240;");
    }

    [Fact]
    public void SparklineDialog_UsesExcelWinLossLabel()
    {
        SparklineDialog.GetKindLabel(SparklineKind.Line).Should().Be("Line");
        SparklineDialog.GetKindLabel(SparklineKind.Column).Should().Be("Column");
        SparklineDialog.GetKindLabel(SparklineKind.WinLoss).Should().Be("Win/Loss");

        var source = ReadRemainingDialogSources();
        source.Should().Contain("GetKindLabel(choice)");
        source.Should().Contain("Tag = choice");
    }

    [Fact]
    public void SparklineDialogOpenedFromKeyboard_FocusesDataRangeBox()
    {
        var source = ReadClassSource("SparklineDialog.cs", "public sealed class SparklineDialog", "private void RequestRangeSelection");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("FocusRangeSelectionInput(_dataRangeBox);");
    }

    [Fact]
    public void SparklineDialogRangePicker_RefocusesSelectedInputWithKeyboardFocus()
    {
        var source = DialogSourceTestSupport.ReadHostSources("SparklineDialog.cs");
        var handlerSource = source[source.IndexOf("private void RequestRangeSelection", StringComparison.Ordinal)..];

        handlerSource.Should().Contain("_requestRangeSelection?.Invoke(RangeSelectionRequest);");
        handlerSource.Should().Contain("FocusRangeSelectionInput(textBox);");
        source.Should().Contain("private static void FocusRangeSelectionInput(TextBox textBox)");
        source.Should().Contain("DialogFocus.FocusAndSelect(textBox);");
    }

    [Fact]
    public void SparklineDialogInvalidRanges_ShowOwnedWarningAndRefocusBadInput()
    {
        var source = DialogSourceTestSupport.ReadHostSources("SparklineDialog.cs");
        var insertSource = DialogSourceTestSupport.ReadHostSources("MainWindow.InsertCommands.cs");

        source.Should().Contain("if (!ValidateInputs())");
        source.Should().Contain("SparklinePlanner.ValidateInsertGroup(_dataRangeBox.Text, _locationBox.Text, _sheetId, out _)");
        source.Should().NotContain("SparklineDialogPlanner");
        source.Should().Contain("SparklineInputValidation.InvalidDataRange");
        source.Should().Contain("SparklineInputValidation.InvalidLocation");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"Sparkline_InvalidDataRange\"), _dataRangeBox)");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"Sparkline_InvalidLocationCell\"), _locationBox)");
        source.Should().Contain("DialogFocus.ShowWarningAndFocus(this, message, Title, textBox);");
        insertSource.Should().Contain("_currentSheetId,");
    }

    [Fact]
    public void SparklineDialog_ValidatesInputsWithPresentationPlanner()
    {
        var sheetId = SheetId.New();

        SparklinePlanner.ValidateDialogInputs("A1:E1", "F1", sheetId)
            .Should().Be(SparklineInputValidation.Valid);
        SparklinePlanner.ValidateDialogInputs("A1:E1", "F1:G1", sheetId)
            .Should().Be(SparklineInputValidation.InvalidLocation);
        SparklinePlanner.ValidateDialogInputs("A1", "F1", sheetId)
            .Should().Be(SparklineInputValidation.InvalidDataRange);
        SparklinePlanner.ValidateDialogInputs("A1:A4097", "F1", sheetId)
            .Should().Be(SparklineInputValidation.InvalidDataRange);
    }

    private static Button GetSparklineOkButton(SparklineDialog dialog)
    {
        var stack = dialog.Content.Should().BeOfType<StackPanel>().Subject;
        var buttonRow = stack.Children[stack.Children.Count - 1].Should().BeOfType<StackPanel>().Subject;
        return buttonRow.Children.OfType<Button>().Single(button => button.IsDefault);
    }
}
