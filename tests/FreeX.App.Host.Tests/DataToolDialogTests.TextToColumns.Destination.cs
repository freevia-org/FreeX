using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class DataToolDialogTests
{
    [Fact]
    public void TextToColumnsRangeSelectionRequest_TrimsCurrentTextAndCollapsesDialog()
    {
        TextToColumnsDialog.CreateRangeSelectionRequest(" F2 ")
            .Should()
            .Be(new TextToColumnsRangeSelectionRequest("F2", CollapseDialog: true));
    }

    [Fact]
    public void TextToColumnsDestinationPicker_RaisesRangeSelectionRequest()
    {
        StaTestRunner.Run(() =>
        {
            var sheetId = SheetId.New();
            var requests = new List<TextToColumnsRangeSelectionRequest>();
            var dialog = new TextToColumnsDialog(
                ["East,42"],
                new CellAddress(sheetId, 2, 6),
                requests.Add);
            dialog.Show();
            try
            {
                var picker = WpfTestTree.FindVisualDescendants<Button>(dialog)
                    .Single(button => AutomationProperties.GetName(button) == "Select destination cell");

                DialogSourceTestSupport.ClickButton(picker);

                requests.Should().Equal(new TextToColumnsRangeSelectionRequest("F2", CollapseDialog: true));
                dialog.RangeSelectionRequest.Should().Be(requests[0]);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void TextToColumnsDestinationPicker_RefocusesDestinationAfterRequest()
    {
        var source = DialogSourceTestSupport.ReadHostSources("TextToColumnsDialog.Delimiters.cs");
        var handlerSource = source[source.IndexOf("private DockPanel CreateReferenceEditor", StringComparison.Ordinal)..];

        handlerSource.Should().Contain("FocusRangeSelectionInput(request.Target);");
        source.Should().Contain("private static void FocusRangeSelectionInput(TextBox target)");
        source.Should().Contain("DialogFocus.FocusAndSelect(target);");
    }

    [Fact]
    public void MainWindow_WiresTextToColumnsDestinationPickerToCurrentSelection()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.DataCommands.cs");

        source.Should().Contain("new TextToColumnsDialog(");
        source.Should().Contain("request => ApplyTextToColumnsRangeSelection(dialog, request)");
        source.Should().Contain("private void ApplyTextToColumnsRangeSelection(");
        source.Should().Contain("TextToColumnsRangeSelectionRequest request");
        source.Should().Contain("BeginDialogRangeSelection(");
        source.Should().Contain("request.CollapseDialog");
        source.Should().Contain("selectedRange => dialog.ApplyRangeSelection(selectedRange.Start)");
    }

    [Fact]
    public void TextToColumnsApplyRangeSelection_UpdatesDestinationBox()
    {
        StaTestRunner.Run(() =>
        {
            var sheetId = SheetId.New();
            var dialog = new TextToColumnsDialog(["East,42"], new CellAddress(sheetId, 2, 6));
            dialog.Show();
            try
            {
                dialog.ApplyRangeSelection(new CellAddress(sheetId, 4, 8));

                WpfTestTree.FindVisualDescendants<TextBox>(dialog)
                    .Single(box => box.Text == "H4")
                    .Text.Should().Be("H4");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void TextToColumnsResult_RequiresSingleDestinationCell()
    {
        var sheetId = SheetId.New();
        var defaultDestination = new CellAddress(sheetId, 2, 1);

        TextToColumnsDialog.TryParseDestination("", defaultDestination, out _).Should().BeFalse();

        TextToColumnsDialog.TryParseDestination(" F2 ", defaultDestination, out var parsedDestination).Should().BeTrue();
        parsedDestination.Should().Be(new CellAddress(sheetId, 2, 6));

        TextToColumnsDialog.TryParseDestination("$F$2", defaultDestination, out parsedDestination).Should().BeTrue();
        parsedDestination.Should().Be(new CellAddress(sheetId, 2, 6));

        TextToColumnsDialog.TryParseDestination("F$2", defaultDestination, out parsedDestination).Should().BeTrue();
        parsedDestination.Should().Be(new CellAddress(sheetId, 2, 6));

        TextToColumnsDialog.TryParseDestination("$F2", defaultDestination, out parsedDestination).Should().BeTrue();
        parsedDestination.Should().Be(new CellAddress(sheetId, 2, 6));

        TextToColumnsDialog.TryParseDestination("R2C6", defaultDestination, out parsedDestination).Should().BeTrue();
        parsedDestination.Should().Be(new CellAddress(sheetId, 2, 6));

        TextToColumnsDialog.TryParseDestination(" ", defaultDestination, out _).Should().BeFalse();
        TextToColumnsDialog.TryParseDestination("F2:G3", defaultDestination, out _).Should().BeFalse();
    }

    [Fact]
    public void TextToColumnsCommand_WarnsBeforeOverwritingDestinationData()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.DataCommands.cs");

        source.Should().Contain("FindOverwriteTargets");
        source.Should().Contain("UiText.Get(\"MainWindowMessage_TextToColumnsReplaceDataPrompt\")");
        source.Should().Contain("_messageService.AskYesNo");
        source.Should().Contain("TextToColumnsApplyPlanner.FindOverwriteTargets(_workbook, targetSheetIds, currentRange, dialog.Result)");
    }
}
