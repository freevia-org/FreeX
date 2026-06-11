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
    public void ConsolidateDialog_ValidatesSameSizeSourceRanges()
    {
        var sheetId = SheetId.New();
        var first = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2));
        var second = new GridRange(new CellAddress(sheetId, 5, 4), new CellAddress(sheetId, 7, 5));
        var different = new GridRange(new CellAddress(sheetId, 10, 1), new CellAddress(sheetId, 12, 3));

        ConsolidateDialog.HaveSameSize([first, second]).Should().BeTrue();
        ConsolidateDialog.HaveSameSize([first, different]).Should().BeFalse();

        var result = ConsolidateDialog.CreateResult(
            [first, second],
            new CellAddress(sheetId, 9, 1),
            ConsolidateFunction.Sum);
        result.SourceRanges.Should().Equal(first, second);
        result.DestinationCell.Should().Be(new CellAddress(sheetId, 9, 1));
        result.Function.Should().Be(ConsolidateFunction.Sum);
    }

    [Fact]
    public void ConsolidateDialog_TryParse_DelegatesSourceAndDestinationParsing()
    {
        var sheetId = SheetId.New();

        var parsed = ConsolidateDialog.TryParse(
            sheetId,
            sourceRangesText: "A1:B3; D5:E7",
            destinationCellText: "G10",
            out var result,
            out var error);

        parsed.Should().BeTrue(error);
        result.SourceRanges.Should().Equal(
            new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            new GridRange(new CellAddress(sheetId, 5, 4), new CellAddress(sheetId, 7, 5)));
        result.DestinationCell.Should().Be(new CellAddress(sheetId, 10, 7));
        result.Function.Should().Be(ConsolidateFunction.Sum);
    }

    [Fact]
    public void ConsolidateDialog_TryParse_ResolvesSheetQualifiedRangesAndDestination()
    {
        var currentSheetId = SheetId.New();
        var dataSheetId = SheetId.New();
        var reportSheetId = SheetId.New();

        var parsed = ConsolidateDialog.TryParse(
            currentSheetId,
            sheetName => sheetName switch
            {
                "Data" => dataSheetId,
                "Report" => reportSheetId,
                _ => null
            },
            sourceRangesText: "Data!A1:B3; 'Report'!D5:E7",
            destinationCellText: "Report!G10",
            out var result,
            out var error);

        parsed.Should().BeTrue(error);
        result.SourceRanges.Should().Equal(
            new GridRange(new CellAddress(dataSheetId, 1, 1), new CellAddress(dataSheetId, 3, 2)),
            new GridRange(new CellAddress(reportSheetId, 5, 4), new CellAddress(reportSheetId, 7, 5)));
        result.DestinationCell.Should().Be(new CellAddress(reportSheetId, 10, 7));
    }

    [Fact]
    public void ConsolidateDialog_TryParse_CapturesSelectedFunctionAndOptions()
    {
        var sheetId = SheetId.New();

        var parsed = ConsolidateDialog.TryParse(
            sheetId,
            sourceRangesText: "A1:B3; D5:E7",
            destinationCellText: "G10",
            function: ConsolidateFunction.Average,
            useTopRowLabels: true,
            useLeftColumnLabels: true,
            createLinksToSourceData: true,
            out var result,
            out var error);

        parsed.Should().BeTrue(error);
        result.Function.Should().Be(ConsolidateFunction.Average);
        result.UseTopRowLabels.Should().BeTrue();
        result.UseLeftColumnLabels.Should().BeTrue();
        result.CreateLinksToSourceData.Should().BeTrue();
    }

    [Fact]
    public void ConsolidateDialog_JoinsAllReferencesListForExistingParser()
    {
        ConsolidateDialog.SplitSourceRangeText("A1:B3; D5:E7").Should().Equal("A1:B3", "D5:E7");
        ConsolidateDialog.JoinSourceRanges(["A1:B3", "D5:E7"]).Should().Be("A1:B3; D5:E7");
        ConsolidateDialogPlanner.JoinSourceRanges([" A1:B3 ", "", " D5:E7 "]).Should().Be("A1:B3; D5:E7");
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("A1:B3", false)]
    [InlineData("A1:B3; D5:E7", false)]
    [InlineData("not-a-range", true)]
    public void ConsolidateDialog_HasPendingReferenceText_IgnoresBlankOrAlreadyListedReferences(
        string referenceText,
        bool expected)
    {
        ConsolidateDialog.HasPendingReferenceText(["A1:B3", "D5:E7"], referenceText)
            .Should()
            .Be(expected);
    }

    [Fact]
    public void ConsolidateDialog_HasPendingReferenceText_DetectsUnaddedTypedReference()
    {
        ConsolidateDialog.HasPendingReferenceText(["A1:B3"], "D5:E7")
            .Should()
            .BeTrue();
    }

    [Fact]
    public void ConsolidateDialog_TryAddReference_RejectsMalformedReferenceImmediately()
    {
        var sheetId = SheetId.New();

        ConsolidateDialog.TryAddReference(
                sheetId,
                ["A1:B3"],
                "nope",
                out var unchanged,
                out var error)
            .Should()
            .BeFalse();

        unchanged.Should().Equal("A1:B3");
        error.Should().Be("Enter a valid source range: nope.");

        ConsolidateDialog.TryAddReference(
                sheetId,
                ["A1:B3"],
                "D5:E7",
                out var updated,
                out error)
            .Should()
            .BeTrue();

        updated.Should().Equal("A1:B3", "D5:E7");
        error.Should().BeNull();
    }

    [Fact]
    public void ConsolidateDialog_ExposesExcelStyleAllReferencesWorkflow()
    {
        var source = ReadConsolidateDialogSources();

        source.Should().Contain("_referenceBox");
        source.Should().Contain("_referencesList");
        source.Should().Contain("UiText.Get(\"Consolidate_Reference\")");
        source.Should().Contain("UiText.Get(\"Consolidate_AllReferences\")");
        source.Should().Contain("UiText.Get(\"Consolidate_DestinationCell\")");
        source.Should().Contain("Text = UiText.Get(\"Consolidate_UseLabelsIn\")");
        source.Should().NotContain("Use _labels in:");
        source.Should().Contain("Content = UiText.Get(\"Consolidate_Add\")");
        source.Should().Contain("Content = UiText.Get(\"Consolidate_Delete\")");
        source.Should().Contain("_deleteReferenceButton");
        source.Should().Contain("UpdateReferenceButtons");
        source.Should().Contain("_referencesList.SelectionChanged");
        source.Should().Contain("_referencesList.KeyDown");
        source.Should().Contain("private void ReferencesList_KeyDown");
        source.Should().Contain("if (e.Key == Key.Delete)");
        source.Should().Contain("AddReferenceButton_Click");
        source.Should().Contain("DeleteReferenceButton_Click");
        source.Should().Contain("CreateReferenceEditor(_referenceBox");
        source.Should().Contain("RequestRangeSelection");
        source.Should().Contain("_requestRangeSelection?.Invoke(RangeSelectionRequest)");
    }

    [Fact]
    public void ConsolidateDialog_AllReferencesListExposesAutomationName()
    {
        var source = DialogSourceTestSupport.ReadHostSources("ConsolidateDialog.cs");

        source.Should().Contain("AutomationProperties.SetName(_referencesList, UiText.Get(\"Consolidate_AllReferences2\"));");
    }

    [Fact]
    public void ConsolidateDialog_RangeEditorsExposeAutomationNames()
    {
        var source = DialogSourceTestSupport.ReadHostSources("ConsolidateDialog.cs");

        source.Should().Contain("AutomationProperties.SetName(_referenceBox, UiText.Get(\"Consolidate_Reference2\"));");
        source.Should().Contain("AutomationProperties.SetName(_destinationBox, UiText.Get(\"Consolidate_DestinationCell2\"));");
    }

    [Fact]
    public void ConsolidateDialog_ControlsExposeAutomationMetadata()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new ConsolidateDialog(SheetId.New(), "A1:B3; D5:E7", "G10");
            dialog.Show();
            try
            {
                var functionBox = WpfTestTree.FindVisualDescendants<ComboBox>(dialog)
                    .Single(box => AutomationProperties.GetAutomationId(box) == "ConsolidateFunctionBox");
                AutomationProperties.GetName(functionBox).Should().Be("Function");
                AutomationProperties.GetHelpText(functionBox).Should().Be("Choose the function used to combine source ranges.");

                AssertTextBoxAutomation("ConsolidateReferenceBox", "Reference", "Enter a source range to add to the All references list.");
                AssertTextBoxAutomation("ConsolidateDestinationCellBox", "Destination cell", "Enter the upper-left destination cell for the consolidated result.");

                var referencesList = WpfTestTree.FindVisualDescendants<ListBox>(dialog)
                    .Single(list => AutomationProperties.GetAutomationId(list) == "ConsolidateAllReferencesList");
                AutomationProperties.GetName(referencesList).Should().Be("All references");
                AutomationProperties.GetHelpText(referencesList).Should().Be("Lists the source ranges that will be consolidated.");

                AssertCheckBoxAutomation("ConsolidateTopRowLabelsBox", "Top row labels", "Use labels from the top row of each source range.");
                AssertCheckBoxAutomation("ConsolidateLeftColumnLabelsBox", "Left column labels", "Use labels from the left column of each source range.");
                AssertCheckBoxAutomation("ConsolidateCreateLinksBox", "Create links to source data", "Create formulas that link the result to the source cells.");

                AssertButtonAutomation("ConsolidateAddReferenceButton", "Add reference", "Add the reference range to the All references list.");
                AssertButtonAutomation("ConsolidateDeleteReferenceButton", "Delete reference", "Delete the selected reference range.");

                void AssertTextBoxAutomation(string automationId, string name, string helpText)
                {
                    var textBox = WpfTestTree.FindVisualDescendants<TextBox>(dialog)
                        .Single(box => AutomationProperties.GetAutomationId(box) == automationId);
                    AutomationProperties.GetName(textBox).Should().Be(name);
                    AutomationProperties.GetHelpText(textBox).Should().Be(helpText);
                }

                void AssertCheckBoxAutomation(string automationId, string name, string helpText)
                {
                    var checkBox = WpfTestTree.FindVisualDescendants<CheckBox>(dialog)
                        .Single(box => AutomationProperties.GetAutomationId(box) == automationId);
                    AutomationProperties.GetName(checkBox).Should().Be(name);
                    AutomationProperties.GetHelpText(checkBox).Should().Be(helpText);
                }

                void AssertButtonAutomation(string automationId, string name, string helpText)
                {
                    var button = WpfTestTree.FindVisualDescendants<Button>(dialog)
                        .Single(box => AutomationProperties.GetAutomationId(box) == automationId);
                    AutomationProperties.GetName(button).Should().Be(name);
                    AutomationProperties.GetHelpText(button).Should().Be(helpText);
                }
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void ConsolidateDialog_UsesScrollableBodyWithPinnedSharedActionButtons()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new ConsolidateDialog(SheetId.New(), "A1:B3; D5:E7", "G10");
            dialog.Show();
            try
            {
                dialog.Width.Should().Be(420);
                dialog.SizeToContent.Should().Be(SizeToContent.Height);
                dialog.MaxHeight.Should().Be(560);

                var root = dialog.Content.Should().BeOfType<DockPanel>().Subject;
                var buttonRow = root.Children.OfType<StackPanel>()
                    .Single(panel => panel.Children.OfType<Button>().Count() == 2);
                DockPanel.GetDock(buttonRow).Should().Be(Dock.Bottom);
                buttonRow.Children.OfType<Button>().Select(button => button.Content)
                    .Should()
                    .Equal(UiText.Ok, UiText.Cancel);

                var scrollViewer = root.Children.OfType<ScrollViewer>().Single();
                scrollViewer.VerticalScrollBarVisibility.Should().Be(ScrollBarVisibility.Auto);
                scrollViewer.HorizontalScrollBarVisibility.Should().Be(ScrollBarVisibility.Disabled);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void ConsolidateDialogOpenedFromKeyboard_FocusesFunctionChoice()
    {
        var source = DialogSourceTestSupport.ReadHostSources("ConsolidateDialog.cs");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_functionBox.Focus();");
        source.Should().Contain("Keyboard.Focus(_functionBox);");
    }

    [Fact]
    public void ConsolidateDialogInvalidFinalValidation_RefocusesInvalidEntry()
    {
        var source = DialogSourceTestSupport.ReadHostSources("ConsolidateDialog.cs");

        source.Should().Contain("FocusInvalidFinalValidation(error);");
        source.Should().Contain("private void FocusInvalidFinalValidation(string? error)");
        source.Should().Contain("FocusReferenceInput();");
        source.Should().Contain("FocusDestinationInput();");
        source.Should().Contain("_referencesList.Focus();");
        source.Should().Contain("DialogFocus.FocusAndSelect(_destinationBox);");
    }

    [Fact]
    public void ConsolidateDialogPendingReference_RequiresAddBeforeOk()
    {
        var source = DialogSourceTestSupport.ReadHostSources("ConsolidateDialog.cs");

        source.Should().Contain("HasPendingReferenceText(_referencesList.Items.Cast<string>(), _referenceBox.Text)");
        source.Should().Contain("DialogMessageHelper.ShowWarning(this, UiText.Get(\"Consolidate_AddTheReferenceBeforeClickingOk\")");
        source.Should().Contain("FocusPendingReferenceInput();");
        source.Should().Contain("private void FocusPendingReferenceInput()");
        source.Should().Contain("DialogFocus.FocusAndSelect(_referenceBox);");
    }

    [Fact]
    public void ConsolidateDialogInvalidAddReference_RefocusesReferenceWithKeyboardFocus()
    {
        var source = DialogSourceTestSupport.ReadHostSources("ConsolidateDialog.cs");
        var addHandlerSource = source[
            source.IndexOf("private void AddReferenceButton_Click", StringComparison.Ordinal)..
            source.IndexOf("private void DeleteReferenceButton_Click", StringComparison.Ordinal)];

        addHandlerSource.Should().Contain("FocusReferenceInput();");
        source.Should().Contain("DialogFocus.FocusAndSelect(_referenceBox);");
    }

    [Fact]
    public void ConsolidateRangeSelectionRequest_TrimsCurrentTextAndCollapsesDialog()
    {
        ConsolidateDialog.CreateRangeSelectionRequest(ConsolidateRangeSelectionTarget.Reference, " A1:B3 ")
            .Should()
            .Be(new ConsolidateRangeSelectionRequest(
                ConsolidateRangeSelectionTarget.Reference,
                "A1:B3",
                CollapseDialog: true));
    }

    [Fact]
    public void ConsolidateRangePicker_RefocusesSelectedInputAfterRequest()
    {
        var source = DialogSourceTestSupport.ReadHostSources("ConsolidateDialog.cs");
        var handlerSource = source[
            source.IndexOf("private void RequestRangeSelection", StringComparison.Ordinal)..
            source.IndexOf("private void FocusInitialKeyboardTarget", StringComparison.Ordinal)];

        handlerSource.Should().Contain("FocusRangeSelectionInput(request.Target);");
        source.Should().Contain("private static void FocusRangeSelectionInput(TextBox target)");
        source.Should().Contain("DialogFocus.FocusAndSelect(target);");
    }

    [Fact]
    public void MainWindow_WiresConsolidateReferencePickersToCurrentSelection()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.DataCommands.cs");

        source.Should().Contain("new ConsolidateDialog(");
        source.Should().Contain("request => ApplyConsolidateRangeSelection(dialog, request)");
        source.Should().Contain("ResolveSheetIdByName) { Owner = this };");
        source.Should().Contain("private void ApplyConsolidateRangeSelection(");
        source.Should().Contain("ConsolidateRangeSelectionRequest request");
        source.Should().Contain("BeginConsolidateRangeSelection(dialog, request);");
        source.Should().Contain("PreviewMouseLeftButtonUpEvent");
        source.Should().Contain("ConsolidateRangePicker_KeyDown");
        source.Should().Contain("target == ConsolidateRangeSelectionTarget.DestinationCell");
        source.Should().Contain("FormatWorkbookRange(selectedRange)");
        source.Should().Contain("FormatWorkbookCellReference(selectedRange.Start, defaultSheetId)");
        source.Should().Contain("WorkbookRangeTextCodec.Format(");
        source.Should().Contain("session.Dialog.ApplyRangeSelection(session.Request.Target, rangeText);");
        source.Should().Contain("catch (Exception ex)");
    }

    [Fact]
    public void ConsolidateApplyRangeSelection_UpdatesRequestedReferenceBox()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new ConsolidateDialog(SheetId.New(), "A1:B3", "G10");
            dialog.Show();
            try
            {
                var textBoxes = WpfTestTree.FindVisualDescendants<TextBox>(dialog).ToList();

                dialog.ApplyRangeSelection(ConsolidateRangeSelectionTarget.Reference, "Sheet2!A1:D20");
                dialog.ApplyRangeSelection(ConsolidateRangeSelectionTarget.DestinationCell, "K5");

                textBoxes[0].Text.Should().Be("Sheet2!A1:D20");
                textBoxes[1].Text.Should().Be("K5");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Theory]
    [InlineData("Select reference range", ConsolidateRangeSelectionTarget.Reference, "A1:B3")]
    [InlineData("Select destination cell", ConsolidateRangeSelectionTarget.DestinationCell, "G10")]
    public void ConsolidateReferencePickers_RaiseRangeSelectionRequest(
        string automationName,
        ConsolidateRangeSelectionTarget expectedTarget,
        string expectedText)
    {
        StaTestRunner.Run(() =>
        {
            var requests = new List<ConsolidateRangeSelectionRequest>();
            var dialog = new ConsolidateDialog(SheetId.New(), " A1:B3 ", " G10 ", requests.Add);
            dialog.Show();
            try
            {
                var picker = WpfTestTree.FindVisualDescendants<Button>(dialog)
                    .Single(button => AutomationProperties.GetName(button) == automationName);

                DialogSourceTestSupport.ClickButton(picker);

                requests.Should().Equal(new ConsolidateRangeSelectionRequest(
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

    [Fact]
    public void ConsolidateDialog_ExposesExcelStyleFunctionLabelsAndLinkOptions()
    {
        var source = ReadConsolidateDialogSources();

        source.Should().Contain("_functionBox");
        source.Should().Contain("_topRowBox");
        source.Should().Contain("_leftColumnBox");
        source.Should().Contain("_createLinksBox");
        source.Should().Contain("UiText.Get(\"Consolidate_Function\")");
        source.Should().Contain("UiText.Get(\"Consolidate_TopRow\")");
        source.Should().Contain("UiText.Get(\"Consolidate_LeftColumn\")");
        source.Should().Contain("UiText.Get(\"Consolidate_CreateLinksToSourceData\")");
        var accessKeyLabels = new[]
        {
            "_Function:",
            "_Reference:",
            "_All references:",
            "_Destination cell:",
            "_Top row",
            "Left _column",
            "Create _links to source data"
        };
        accessKeyLabels
            .GroupBy(GetAccessKey)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group)}")
            .Should()
            .BeEmpty();
        source.Should().Contain("Enum.GetValues<ConsolidateFunction>()");
        source.Should().Contain("FunctionLabel(function)");
        source.Should().Contain("ConsolidateFunction.CountNumbers => UiText.Get(\"Consolidate_FunctionCountNumbers\")");
        source.Should().Contain("SelectedFunction()");
        source.Should().NotContain("DisableUnsupported(_functionBox, SumOnlyHelpText)");
        source.Should().NotContain("DisableUnsupported(_topRowBox, LabelMatchingHelpText)");
        source.Should().NotContain("DisableUnsupported(_leftColumnBox, LabelMatchingHelpText)");
        source.Should().NotContain("DisableUnsupported(_createLinksBox, SourceLinksHelpText)");
        source.Should().NotContain("Source links are not available yet");
        source.Should().Contain("UseTopRowLabels");
        source.Should().Contain("UseLeftColumnLabels");
        source.Should().Contain("CreateLinksToSourceData");
        source.Should().Contain("UiText.Get(\"Consolidate_WriteFormulasThatReferenceTheSourceCellsWhileKeepingTheConsolidatedResul\")");
    }

    private static string ReadConsolidateDialogSources() =>
        DialogSourceTestSupport.ReadHostSourcesWithSeparator(
            "",
            "ConsolidateDialog.cs",
            "ConsolidateDialog.Planning.cs",
            "ConsolidateDialogPlanner.cs");

    [Fact]
    public void ConsolidateDialog_TryParse_RejectsMalformedSourceRange()
    {
        var sheetId = SheetId.New();

        var parsed = ConsolidateDialog.TryParse(
            sheetId,
            sourceRangesText: "A1:B3; nope",
            destinationCellText: "G10",
            out _,
            out var error);

        parsed.Should().BeFalse();
        error.Should().Be("Enter a valid source range: nope.");
    }

    [Fact]
    public void ConsolidateDialog_TryParse_RejectsMismatchedSourceSizes()
    {
        var sheetId = SheetId.New();

        var parsed = ConsolidateDialog.TryParse(
            sheetId,
            sourceRangesText: "A1:B3; D5:F7",
            destinationCellText: "G10",
            out _,
            out var error);

        parsed.Should().BeFalse();
        error.Should().Be("Source ranges must be the same size.");
    }

    [Fact]
    public void ConsolidateDialog_TryParse_RejectsInvalidDestinationCell()
    {
        var sheetId = SheetId.New();

        var parsed = ConsolidateDialog.TryParse(
            sheetId,
            sourceRangesText: "A1:B3",
            destinationCellText: "nope",
            out _,
            out var error);

        parsed.Should().BeFalse();
        error.Should().Be("Enter a valid destination cell.");
    }
}
