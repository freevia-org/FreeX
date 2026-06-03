using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class DataToolDialogTests
{
    [Fact]
    public void CreateTableDialog_ExposesHeadersCheckboxAndRangePicker()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "CreateTableDialog.cs"));

        source.Should().Contain("_headersBox");
        source.Should().Contain("Content = UiText.Get(\"CreateTable_HeadersCheckBox\")");
        source.Should().Contain("new Label { Content = UiText.Get(\"CreateTable_RangeLabel\"), Target = _rangeBox");
        source.Should().Contain("CreateReferenceEditor(_rangeBox");
        source.Should().Contain("DialogReferencePicker.CreateEditor");
        source.Should().Contain("RequestRangeSelection");
        source.Should().Contain("_requestRangeSelection?.Invoke(RangeSelectionRequest)");
        source.Should().Contain("UiText.Get(\"CreateTable_RangePickerAutomationName\")");
        UiText.Get("CreateTable_HeadersCheckBox").Should().Be("_My table has headers");
    }

    [Fact]
    public void CreateTableDialog_ControlsExposeAutomationMetadata()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "CreateTableDialog.cs"));

        StaTestRunner.Run(() =>
        {
            var dialog = new CreateTableDialog(SheetId.New(), "A1:C12", "TableStyleMedium2");
            dialog.Show();
            try
            {
                var rangeBox = FindVisualChildren<TextBox>(dialog).Single();
                AutomationProperties.GetName(rangeBox).Should().Be(UiText.Get("CreateTable_RangeAutomationName"));
                AutomationProperties.GetAutomationId(rangeBox).Should().Be("CreateTableRangeBox");
                AutomationProperties.GetHelpText(rangeBox).Should().Be(UiText.Get("CreateTable_RangeAutomationHelpText"));

                var headersBox = FindVisualChildren<CheckBox>(dialog)
                    .Single(box => Equals(box.Content, UiText.Get("CreateTable_HeadersCheckBox")));
                AutomationProperties.GetName(headersBox).Should().Be(UiText.Get("CreateTable_HeadersAutomationName"));
                AutomationProperties.GetAutomationId(headersBox).Should().Be("CreateTableHeadersBox");
                AutomationProperties.GetHelpText(headersBox).Should().Be(UiText.Get("CreateTable_HeadersAutomationHelpText"));
            }
            finally
            {
                dialog.Close();
            }
        });

        source.Should().Contain("AutomationProperties.SetName(_rangeBox, UiText.Get(\"CreateTable_RangeAutomationName\"));");
        source.Should().Contain("AutomationProperties.SetHelpText(_rangeBox, UiText.Get(\"CreateTable_RangeAutomationHelpText\"));");
        source.Should().Contain("AutomationProperties.SetName(_headersBox, UiText.Get(\"CreateTable_HeadersAutomationName\"));");
        source.Should().Contain("AutomationProperties.SetHelpText(_headersBox, UiText.Get(\"CreateTable_HeadersAutomationHelpText\"));");
    }

    [Fact]
    public void CreateTableDialogOpenedFromKeyboard_FocusesRangeBox()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "CreateTableDialog.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("DialogFocus.FocusAndSelect(_rangeBox);");
    }

    [Fact]
    public void CreateTableDialogInvalidRange_RefocusesAndSelectsRangeBox()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "CreateTableDialog.cs"));

        source.Should().Contain("FocusRangeBox();");
        source.Should().Contain("private void FocusRangeBox()");
        source.Should().Contain("DialogFocus.FocusAndSelect(_rangeBox);");
    }

    [Fact]
    public void CreateTableDialog_ParsesRangeHeadersAndStyle()
    {
        var sheetId = SheetId.New();

        var parsed = CreateTableDialog.TryParse(
            sheetId,
            rangeText: " A1:C12 ",
            firstRowHasHeaders: false,
            tableStyleName: "TableStyleMedium2",
            out var result,
            out var error);

        parsed.Should().BeTrue(error);
        result.Range.Should().Be(new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 12, 3)));
        result.FirstRowHasHeaders.Should().BeFalse();
        result.TableStyleName.Should().Be("TableStyleMedium2");
    }

    [Fact]
    public void CreateTableDialog_RangePickerRaisesRangeSelectionRequest()
    {
        StaTestRunner.Run(() =>
        {
            var requests = new List<CreateTableRangeSelectionRequest>();
            var dialog = new CreateTableDialog(
                SheetId.New(),
                " A1:C12 ",
                "TableStyleMedium2",
                requests.Add);
            dialog.Show();
            try
            {
                var picker = FindVisualChildren<Button>(dialog)
                    .Where(button => Equals(button.Content, "..."))
                    .Single();

                picker.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                requests.Should().Equal(new CreateTableRangeSelectionRequest("A1:C12", CollapseDialog: true));
                dialog.RangeSelectionRequest.Should().Be(requests[0]);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void MainWindow_WiresCreateTableRangePickerToCurrentSelection()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.HomeFormatting.cs"));

        source.Should().Contain("new CreateTableDialog(");
        source.Should().Contain("request => ApplyCreateTableRangeSelection(dialog, request)");
        source.Should().Contain("private void ApplyCreateTableRangeSelection(");
        source.Should().Contain("CreateTableRangeSelectionRequest request");
        source.Should().Contain("if (request.CollapseDialog)");
        source.Should().Contain("dialog.Hide();");
        source.Should().Contain("dialog.ApplyRangeSelection(FormatRangeReference(selectedRange.Start, selectedRange.End));");
        source.Should().Contain("dialog.Show();");
        source.Should().Contain("dialog.Activate();");
    }

    [Fact]
    public void CreateTableApplyRangeSelection_UpdatesRangeBox()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new CreateTableDialog(SheetId.New(), "A1:C12", "TableStyleMedium2");
            dialog.Show();
            try
            {
                dialog.ApplyRangeSelection("B2:D8");

                FindVisualChildren<TextBox>(dialog).Single().Text.Should().Be("B2:D8");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void CreateTableDialogRangePicker_RefocusesRangeBoxAfterRequest()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "CreateTableDialog.cs"));
        var handlerSource = source[
            source.IndexOf("private void RequestRangeSelection", StringComparison.Ordinal)..
            source.IndexOf("private void FocusInitialKeyboardTarget", StringComparison.Ordinal)];

        handlerSource.Should().Contain("FocusRangeBox();");
        source.Should().Contain("private void FocusRangeBox()");
        source.Should().Contain("DialogFocus.FocusAndSelect(_rangeBox);");
    }

    [Fact]
    public void CreateTableRangeSelectionRequest_TrimsCurrentTextAndCollapsesDialog()
    {
        CreateTableDialog.CreateRangeSelectionRequest(" A1:C12 ")
            .Should()
            .Be(new CreateTableRangeSelectionRequest("A1:C12", CollapseDialog: true));
    }
}
