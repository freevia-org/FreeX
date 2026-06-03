using System.IO;
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
        var commandSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.PivotCommands.cs"));

        source.Should().Contain("if (!ValidateInputs())");
        source.Should().Contain("WorkbookRangeTextCodec.TryParse(_sheetId, _sourceBox.Text, ResolveSheetIdByName, out _)");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"PivotTableDataSource_EnterValidSourceRange\"), _sourceBox);");
        source.Should().Contain("DialogMessageHelper.ShowWarning(this, message, Title)");
        source.Should().Contain("FocusRangeSelectionInput(target);");
        commandSource.Should().Contain("sheetId: sheet.Id");
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
                var picker = FindVisualChildren<Button>(dialog)
                    .Single(button => AutomationProperties.GetName(button) == "Select PivotTable source range");

                picker.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

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

                var sourceBox = FindVisualChildren<TextBox>(dialog).Single();
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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.PivotCommands.cs"));

        source.Should().Contain("new PivotTableDataSourceDialog(");
        source.Should().Contain("request => ApplyPivotTableDataSourceRangeSelection(dialog, request)");
        source.Should().Contain("private void ApplyPivotTableDataSourceRangeSelection(");
        source.Should().Contain("PivotTableDataSourceRangeSelectionRequest request");
        source.Should().Contain("FormatWorkbookRange(selectedRange)");
        source.Should().Contain("dialog.ApplyRangeSelection(rangeText);");
        source.Should().Contain("dialog.Hide();");
        source.Should().Contain("dialog.Show();");
        source.Should().Contain("dialog.Activate();");
    }
}
