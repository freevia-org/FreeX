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
    public void PivotTableDataSourceDialog_CreateResult_TrimsSourceRangeText()
    {
        PivotTableDataSourceDialog.CreateResult("  Sales!A1:E200  ")
            .SourceRangeText
            .Should()
            .Be("Sales!A1:E200");
    }

    [Fact]
    public void PivotTableDataSourceRangeSelectionRequest_TrimsCurrentTextAndCollapsesDialog()
    {
        PivotTableDataSourceDialog.CreateRangeSelectionRequest(" Sales!A1:E200 ")
            .Should()
            .Be(new PivotTableDataSourceRangeSelectionRequest("Sales!A1:E200", CollapseDialog: true));
    }

    [Fact]
    public void PivotTableDataSourceDialog_ExposesReferencePickerForSourceRange()
    {
        var source = ReadPivotWorkflowSource();

        source.Should().Contain("CreateReferenceEditor(_sourceBox");
        source.Should().Contain("UiText.Get(\"PivotTableDataSource_SelectPivotTableSourceRange\")");
        source.Should().Contain("DialogReferencePicker.CreateEditor");
        source.Should().Contain("PivotTableDataSourceRangeSelectionRequest");
        source.Should().Contain("_requestRangeSelection?.Invoke(RangeSelectionRequest)");
    }

    [Fact]
    public void PivotTableDataSourceDialog_SourceRangeEditorExposesAutomationName()
    {
        var source = ReadClassSource(
            "PivotTableDataSourceDialog.cs",
            "public sealed class PivotTableDataSourceDialog",
            "internal static class PivotDialogLayout");

        source.Should().Contain("AutomationProperties.SetName(_sourceBox, UiText.Get(\"PivotTableDataSource_PivotTableSourceRange\"));");
    }

    [Fact]
    public void PivotTableDataSourceDialogOpenedFromKeyboard_FocusesSourceRange()
    {
        var source = ReadClassSource(
            "PivotTableDataSourceDialog.cs",
            "public sealed class PivotTableDataSourceDialog",
            "internal static class PivotDialogLayout");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("FocusRangeSelectionInput(_sourceBox);");
    }

    [Fact]
    public void PivotTableDataSourceRangePicker_RefocusesSourceInputAfterRequest()
    {
        var source = ReadClassSource(
            "PivotTableDataSourceDialog.cs",
            "public sealed class PivotTableDataSourceDialog",
            "internal static class PivotDialogLayout");

        source.Should().Contain("FocusRangeSelectionInput(request.Target);");
        source.Should().Contain("private static void FocusRangeSelectionInput(TextBox target)");
        source.Should().Contain("DialogFocus.FocusAndSelect(target);");
    }

    [Fact]
    public void PivotTableDataSourceDialogInvalidRange_ShowsOwnedWarningAndRefocusesSource()
    {
        var source = ReadClassSource(
            "PivotTableDataSourceDialog.cs",
            "public sealed class PivotTableDataSourceDialog",
            "internal static class PivotDialogLayout");
        var commandSource = DialogSourceTestSupport.ReadHostSources("MainWindow.PivotCommands.cs");

        source.Should().Contain("if (!TryValidateInputs(out var change))");
        source.Should().Contain("PivotDataSourcePlanner.TryCreateChange(_sourceBox.Text, _resolveReference, out change, out _)");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"PivotTableDataSource_EnterValidSourceRange\"), _sourceBox);");
        source.Should().Contain("DialogFocus.ShowWarningAndFocus(this, message, Title, target);");
        commandSource.Should().Contain("sheetId: target.Sheet.Id");
        commandSource.Should().Contain("resolveReference: (string reference, out GridRange range) =>");
        commandSource.Should().Contain("TryParseWorkbookRange(target.Sheet.Id, reference, out range)");
        commandSource.Should().Contain("dialog.Result.SourceRange is null");
    }

    [Fact]
    public void PivotTableDataSourceReferencePicker_RaisesRangeSelectionRequest()
    {
        StaTestRunner.Run(() =>
        {
            var requests = new List<PivotTableDataSourceRangeSelectionRequest>();
            var dialog = new PivotTableDataSourceDialog(" Sales!A1:E200 ", requests.Add);
            dialog.Show();
            try
            {
                var picker = WpfTestTree.FindVisualDescendants<Button>(dialog)
                    .Single(button => AutomationProperties.GetName(button) == "Select PivotTable source range");

                DialogSourceTestSupport.ClickButton(picker);

                requests.Should().Equal(new PivotTableDataSourceRangeSelectionRequest(
                    "Sales!A1:E200",
                    CollapseDialog: true));
                dialog.RangeSelectionRequest.Should().Be(requests[0]);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void PivotTableDataSourceApplyRangeSelection_UpdatesSourceBox()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new PivotTableDataSourceDialog("Sales!A1:E200");
            dialog.Show();
            try
            {
                dialog.ApplyRangeSelection("Sales!B2:F40");

                var sourceBox = WpfTestTree.FindVisualDescendants<TextBox>(dialog).Single();
                sourceBox.Text.Should().Be("Sales!B2:F40");
                sourceBox.SelectionLength.Should().Be("Sales!B2:F40".Length);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void MainWindow_WiresPivotTableDataSourceRangePickerToCurrentSelection()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.PivotCommands.cs");

        source.Should().Contain("new PivotTableDataSourceDialog(");
        source.Should().Contain("request => ApplyPivotTableDataSourceRangeSelection(dialog, request)");
        source.Should().Contain("private void ApplyPivotTableDataSourceRangeSelection(");
        source.Should().Contain("PivotTableDataSourceRangeSelectionRequest request");
        source.Should().Contain("BeginDialogRangeSelection(");
        source.Should().Contain("request.CollapseDialog");
        source.Should().Contain("FormatWorkbookRange(selectedRange)");
        source.Should().Contain("selectedRange => dialog.ApplyRangeSelection(FormatWorkbookRange(selectedRange))");
    }

    [Fact]
    public void PivotTableDataSourceDialog_DelegatesResultAndRequestNormalizationToSharedPlanner()
    {
        var source = ReadClassSource(
            "PivotTableDataSourceDialog.cs",
            "public sealed class PivotTableDataSourceDialog",
            "internal static class PivotDialogLayout");

        source.Should().Contain("PivotDataSourcePlanner.NormalizeReferenceText(sourceRangeText)");
        source.Should().Contain("PivotDataSourcePlanner.NormalizeReferenceText(currentText)");
        source.Should().Contain("public static PivotTableDataSourceDialogResult CreateResult(PivotDataSourceChange change)");
    }
}
