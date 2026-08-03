using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class DataToolDialogTests
{
    [Fact]
    public void CreateTableDialog_ExposesHeadersCheckboxAndRangePicker()
    {
        var source = DialogSourceTestSupport.ReadHostSources("CreateTableDialog.cs");

        source.Should().Contain("_headersBox");
        source.Should().Contain("Content = UiText.Get(CreateTableDialogPlanner.HeadersCheckBoxKey)");
        source.Should().Contain("Content = UiText.Get(CreateTableDialogPlanner.RangeLabelKey)");
        source.Should().Contain("Target = _rangeBox");
        source.Should().Contain("CreateReferenceEditor(_rangeBox");
        source.Should().Contain("DialogReferencePicker.CreateEditor");
        source.Should().Contain("RequestRangeSelection");
        source.Should().Contain("_requestRangeSelection?.Invoke(RangeSelectionRequest)");
        source.Should().Contain("UiText.Get(CreateTableDialogPlanner.RangePickerAutomationNameKey)");
        UiText.Get("CreateTable_HeadersCheckBox").Should().Be("_My table has headers");
    }

    [Fact]
    public void CreateTableDialog_ControlsExposeAutomationMetadata()
    {
        var source = DialogSourceTestSupport.ReadHostSources("CreateTableDialog.cs");

        StaTestRunner.Run(() =>
        {
            var dialog = new CreateTableDialog(SheetId.New(), "A1:C12", "TableStyleMedium2");
            dialog.Show();
            try
            {
                var rangeBox = WpfTestTree.FindVisualDescendants<TextBox>(dialog).Single();
                AutomationProperties.GetName(rangeBox).Should().Be(UiText.Get("CreateTable_RangeAutomationName"));
                AutomationProperties.GetAutomationId(rangeBox).Should().Be(CreateTableDialogPlanner.RangeBoxAutomationId);
                AutomationProperties.GetHelpText(rangeBox).Should().Be(UiText.Get("CreateTable_RangeAutomationHelpText"));

                var headersBox = WpfTestTree.FindVisualDescendants<CheckBox>(dialog)
                    .Single(box => Equals(box.Content, UiText.Get("CreateTable_HeadersCheckBox")));
                AutomationProperties.GetName(headersBox).Should().Be(UiText.Get("CreateTable_HeadersAutomationName"));
                AutomationProperties.GetAutomationId(headersBox).Should().Be(CreateTableDialogPlanner.HeadersBoxAutomationId);
                AutomationProperties.GetHelpText(headersBox).Should().Be(UiText.Get("CreateTable_HeadersAutomationHelpText"));
            }
            finally
            {
                dialog.Close();
            }
        });

        source.Should().Contain("AutomationProperties.SetAutomationId(this, CreateTableDialogPlanner.DialogAutomationId);");
        source.Should().Contain("AutomationProperties.SetName(_rangeBox, UiText.Get(CreateTableDialogPlanner.RangeAutomationNameKey));");
        source.Should().Contain("AutomationProperties.SetHelpText(_rangeBox, UiText.Get(CreateTableDialogPlanner.RangeAutomationHelpTextKey));");
        source.Should().Contain("AutomationProperties.SetName(_headersBox, UiText.Get(CreateTableDialogPlanner.HeadersAutomationNameKey));");
        source.Should().Contain("AutomationProperties.SetHelpText(_headersBox, UiText.Get(CreateTableDialogPlanner.HeadersAutomationHelpTextKey));");
    }

    [Fact]
    public void CreateTableDialogOpenedFromKeyboard_FocusesRangeBox()
    {
        var source = DialogSourceTestSupport.ReadHostSources("CreateTableDialog.cs");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("DialogFocus.FocusAndSelect(_rangeBox);");
    }

    [Fact]
    public void CreateTableDialogInvalidRange_RefocusesAndSelectsRangeBox()
    {
        var source = DialogSourceTestSupport.ReadHostSources("CreateTableDialog.cs");

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
                var picker = WpfTestTree.FindVisualDescendants<Button>(dialog)
                    .Where(button => Equals(button.Content, "..."))
                    .Single();

                DialogSourceTestSupport.ClickButton(picker);

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
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.HomeFormatting.cs");

        source.Should().Contain("new CreateTableDialog(");
        source.Should().Contain("request => ApplyCreateTableRangeSelection(dialog, request)");
        source.Should().Contain("private void ApplyCreateTableRangeSelection(");
        source.Should().Contain("CreateTableRangeSelectionRequest request");
        source.Should().Contain("BeginDialogRangeSelection(");
        source.Should().Contain("request.CollapseDialog");
        source.Should().Contain("selectedRange => dialog.ApplyRangeSelection(FormatRangeReference(selectedRange.Start, selectedRange.End))");
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

                WpfTestTree.FindVisualDescendants<TextBox>(dialog).Single().Text.Should().Be("B2:D8");
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
        var source = DialogSourceTestSupport.ReadHostSources("CreateTableDialog.cs");
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
