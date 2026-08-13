using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FluentAssertions;
using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class PivotWorkflowDialogTests
{
    [Fact]
    public void PivotTableDialog_CreateResult_CapturesExcelCreatePivotChoices()
    {
        var result = PivotTableDialog.CreateResult(
            "  Sales!A1:D20  ",
            PivotDestinationKind.ExistingWorksheet,
            "  Report!F3  ",
            openFieldList: true);

        result.SourceRangeText.Should().Be("Sales!A1:D20");
        result.DestinationKind.Should().Be(PivotDestinationKind.ExistingWorksheet);
        result.DestinationRangeText.Should().Be("Report!F3");
        result.OpenFieldList.Should().BeTrue();
    }

    [Fact]
    public void PivotTableDialog_DefaultResult_UsesNewWorksheetDestinationAndFieldList()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sales");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 20, 4));

        StaTestRunner.Run(() =>
        {
            var dialog = new PivotTableDialog(workbook, sheet.Id, range);

            dialog.Result.SourceRangeText.Should().Be("Sales!A1:D20");
            dialog.Result.DestinationKind.Should().Be(PivotDestinationKind.NewWorksheet);
            dialog.Result.DestinationRangeText.Should().BeEmpty();
            dialog.Result.OpenFieldList.Should().BeTrue();
        });
    }

    [Fact]
    public void PivotTableDialog_InitialLayoutKeepsActionButtonsVisible()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sales");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 20, 4));

        StaTestRunner.Run(() =>
        {
            var dialog = new PivotTableDialog(workbook, sheet.Id, range);
            dialog.Show();
            try
            {
                dialog.SizeToContent.Should().Be(SizeToContent.Height);
                dialog.UpdateLayout();
                var content = dialog.Content.Should().BeAssignableTo<FrameworkElement>().Subject;
                var buttons = WpfTestTree.FindVisualDescendants<Button>(dialog)
                    .Where(button => button.IsDefault || button.IsCancel)
                    .ToList();

                buttons.Should().HaveCount(2);
                foreach (var button in buttons)
                {
                    var bottom = button.TransformToAncestor(content)
                        .Transform(new Point(0, button.ActualHeight))
                        .Y;
                    bottom.Should().BeLessThanOrEqualTo(content.ActualHeight + 0.5);
                }
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void PivotTableDialog_ExposesReferencePickersForSourceAndExistingLocation()
    {
        var source = DialogSourceTestSupport.ReadHostSources("PivotTableDialog.cs");

        source.Should().Contain("AddLabeledReferenceEditor(");
        source.Should().Contain("_sourceRangeBox,");
        source.Should().Contain("_destinationRangeBox,");
        source.Should().Contain("CreateReferenceEditor(textBox, automationName, target, editorMargin)");
        source.Should().Contain("UiText.Get(\"PivotTable_SelectPivotTableSourceRange\")");
        source.Should().Contain("UiText.Get(\"PivotTable_SelectPivotTableLocation\")");
        source.Should().Contain("DialogReferencePicker.CreateEditor");
        source.Should().Contain("RequestRangeSelection");
        source.Should().Contain("_requestRangeSelection?.Invoke(RangeSelectionRequest)");
        source.Should().Contain("UpdateDestinationState");
    }

    [Fact]
    public void PivotTableDialog_ExposesOnlySupportedSourceAndPlacementChoices()
    {
        var source = DialogSourceTestSupport.ReadHostSources("PivotTableDialog.cs");

        source.Should().Contain("UiText.Get(\"PivotTable_ChooseDataHeader\")");
        source.Should().Contain("_selectTableRangeButton");
        source.Should().Contain("UiText.Get(\"PivotTable_NewWorksheet\")");
        source.Should().Contain("UiText.Get(\"PivotTable_ExistingWorksheet\")");
        source.Should().NotContain("_externalSourceButton");
        source.Should().NotContain("_dataModelBox");
        source.Should().NotContain("Use an _external data source");
        source.Should().NotContain("Add this data to the Data _Model");
    }

    [Fact]
    public void PivotTableDialog_ExposesKeyboardAccessKeysForChoicesAndButtons()
    {
        var source = DialogSourceTestSupport.ReadHostSources("PivotTableDialog.cs");

        source.Should().Contain("Content = UiText.Get(\"PivotTable_Create\")");
        source.Should().Contain("Content = UiText.Cancel");
        source.Should().Contain("Content = UiText.Get(\"PivotTable_NewWorksheet\")");
        source.Should().Contain("Content = UiText.Get(\"PivotTable_ExistingWorksheet\")");
        source.Should().Contain("Content = UiText.Get(\"PivotTable_OpenPivotTableFieldsPane\")");
        source.Should().NotContain("Content = \"Use an _external data source\"");
        source.Should().NotContain("Content = \"Add this data to the Data _Model\"");
    }

    [Fact]
    public void PivotTableDialog_LabelsRangeEditorsWithAccessKeyTargets()
    {
        var source = DialogSourceTestSupport.ReadHostSources("PivotTableDialog.cs");

        foreach (var content in new[]
        {
            "AddLabeledReferenceEditor(",
            "UiText.Get(\"PivotTable_TableRangeLabel\")",
            "UiText.Get(\"PivotTable_LocationLabel\")",
            "_sourceRangeBox,",
            "_destinationRangeBox,",
            "new Label",
            "Target = textBox",
            "private void AddLabeledReferenceEditor",
            "CreateReferenceEditor(textBox, automationName, target, editorMargin)"
        })
            source.Should().Contain(content);
    }

    [Fact]
    public void PivotTableDialog_RangeEditorsExposeAutomationNames()
    {
        var source = DialogSourceTestSupport.ReadHostSources("PivotTableDialog.cs");

        source.Should().Contain("AutomationProperties.SetName(_sourceRangeBox, UiText.Get(\"PivotTable_PivotTableSourceRange\"));");
        source.Should().Contain("AutomationProperties.SetName(_destinationRangeBox, UiText.Get(\"PivotTable_PivotTableLocation\"));");
    }

    [Fact]
    public void PivotTableDialogOpenedFromKeyboard_FocusesSourceRange()
    {
        var source = DialogSourceTestSupport.ReadHostSources("PivotTableDialog.cs");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("FocusRangeSelectionInput(_sourceRangeBox);");
    }

    [Fact]
    public void PivotTableDialogRangePicker_RefocusesSelectedInputAfterRequest()
    {
        var source = DialogSourceTestSupport.ReadHostSources("PivotTableDialog.cs");
        var handlerSource = source[
            source.IndexOf("private void RequestRangeSelection", StringComparison.Ordinal)..
            source.IndexOf("private void UpdateDestinationState", StringComparison.Ordinal)];

        handlerSource.Should().Contain("FocusRangeSelectionInput(request.Target);");
        source.Should().Contain("private static void FocusRangeSelectionInput(TextBox target)");
        source.Should().Contain("DialogFocus.FocusAndSelect(target);");
    }

    [Fact]
    public void PivotTableDialogInvalidRanges_ShowOwnedWarningAndRefocusBadInput()
    {
        var source = DialogSourceTestSupport.ReadHostSources("PivotTableDialog.cs");

        source.Should().Contain("if (!ValidateInputs())");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"PivotTable_EnterValidSourceRange\"), _sourceRangeBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"PivotTable_EnterDestinationCellOnActiveWorksheet\"), _destinationRangeBox);");
        source.Should().Contain("WorkbookRangeTextCodec.TryParse(_sourceSheetId, _sourceRangeBox.Text, ResolveSheetIdByName, out _)");
        source.Should().Contain("destinationRange.Start.Sheet != _sourceSheetId");
        source.Should().Contain("DialogFocus.ShowWarningAndFocus(this, message, Title, target);");
    }

    [Fact]
    public void PivotTableRangeSelectionRequest_TrimsCurrentTextAndCollapsesDialog()
    {
        PivotTableDialog.CreateRangeSelectionRequest(PivotTableRangeSelectionTarget.DestinationRange, " Report!F3 ")
            .Should()
            .Be(new PivotTableRangeSelectionRequest(
                PivotTableRangeSelectionTarget.DestinationRange,
                "Report!F3",
                CollapseDialog: true));
    }

    [Fact]
    public void PivotTableApplyRangeSelection_UpdatesRequestedReferenceBox()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sales");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 20, 4));

        StaTestRunner.Run(() =>
        {
            var dialog = new PivotTableDialog(workbook, sheet.Id, range);
            dialog.Show();
            try
            {
                var textBoxes = WpfTestTree.FindVisualDescendants<TextBox>(dialog).ToList();

                dialog.ApplyRangeSelection(PivotTableRangeSelectionTarget.SourceRange, "Sales!A1:E40");
                dialog.ApplyRangeSelection(PivotTableRangeSelectionTarget.DestinationRange, "Sales!H3");

                textBoxes[0].Text.Should().Be("Sales!A1:E40");
                textBoxes[1].Text.Should().Be("Sales!H3");
                textBoxes[1].IsEnabled.Should().BeTrue();
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void MainWindow_WiresPivotTableRangePickersToCurrentSelection()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.PivotCommands.cs");

        source.Should().Contain("new PivotTableDialog(");
        source.Should().Contain("request => ApplyPivotTableRangeSelection(dialog, request)");
        source.Should().Contain("private void ApplyPivotTableRangeSelection(");
        source.Should().Contain("PivotTableRangeSelectionRequest request");
        source.Should().Contain("BeginDialogRangeSelection(");
        source.Should().Contain("request.CollapseDialog");
        source.Should().Contain("FormatWorkbookRange(selectedRange)");
        source.Should().Contain("selectedRange => dialog.ApplyRangeSelection(request.Target, FormatWorkbookRange(selectedRange))");
    }

    [Theory]
    [InlineData("Select PivotTable source range", PivotTableRangeSelectionTarget.SourceRange, "Sales!A1:D20")]
    [InlineData("Select PivotTable location", PivotTableRangeSelectionTarget.DestinationRange, "Sales!F1")]
    public void PivotTableReferencePickers_RaiseRangeSelectionRequest(
        string automationName,
        PivotTableRangeSelectionTarget expectedTarget,
        string expectedText)
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sales");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 20, 4));

        StaTestRunner.Run(() =>
        {
            var requests = new List<PivotTableRangeSelectionRequest>();
            var dialog = new PivotTableDialog(workbook, sheet.Id, range, requests.Add);
            dialog.Show();
            try
            {
                var picker = WpfTestTree.FindVisualDescendants<Button>(dialog)
                    .Single(button => AutomationProperties.GetName(button) == automationName);

                DialogSourceTestSupport.ClickButton(picker);

                requests.Should().Equal(new PivotTableRangeSelectionRequest(
                    expectedTarget,
                    expectedText,
                    CollapseDialog: true));
                dialog.RangeSelectionRequest.Should().Be(requests[0]);
            }
            finally
            {
                dialog.Close();
            }
        });
    }
}
