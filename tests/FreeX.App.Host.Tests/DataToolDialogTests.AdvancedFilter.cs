using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FluentAssertions;
using FreeX.App.Presentation.Filtering;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using AdvancedFilterRangeSelectionRequest = FreeX.App.Presentation.Filtering.AdvancedFilterRangeSelectionRequest;
using AdvancedFilterRangeSelectionTarget = FreeX.App.Presentation.Filtering.AdvancedFilterRangeSelectionTarget;

namespace FreeX.App.Host.Tests;

public sealed partial class DataToolDialogTests
{
    [Fact]
    public void AdvancedFilterDialog_ParsesRangesAndOptionalCopyToCellOnCurrentSheet()
    {
        var sheetId = SheetId.New();

        var planResult = AdvancedFilterPlanner.CreatePlan(
            sheetId,
            listRangeText: "A1:D20",
            criteriaRangeText: "F1:G2",
            copyToRangeText: "J1",
            AdvancedFilterOutputMode.CopyToAnotherLocation,
            uniqueRecordsOnly: true);

        AdvancedFilterPlanner.TryCreateDialogResult(planResult, out var result).Should().BeTrue();
        result.ListRange.Should().Be(new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 20, 4)));
        result.CriteriaRange.Should().Be(new GridRange(new CellAddress(sheetId, 1, 6), new CellAddress(sheetId, 2, 7)));
        result.CopyToCell.Should().Be(new CellAddress(sheetId, 1, 10));
        result.CopyToRange.Should().Be(new GridRange(new CellAddress(sheetId, 1, 10), new CellAddress(sheetId, 1, 10)));
        result.UniqueRecordsOnly.Should().BeTrue();
    }

    [Fact]
    public void AdvancedFilterDialog_ParsesCopyToHeaderRange()
    {
        var sheetId = SheetId.New();

        var planResult = AdvancedFilterPlanner.CreatePlan(
            sheetId,
            listRangeText: "A1:D20",
            criteriaRangeText: "F1:G2",
            copyToRangeText: "J1:L1",
            AdvancedFilterOutputMode.CopyToAnotherLocation,
            uniqueRecordsOnly: true);

        AdvancedFilterPlanner.TryCreateDialogResult(planResult, out var result).Should().BeTrue();
        result.CopyToCell.Should().Be(new CellAddress(sheetId, 1, 10));
        result.CopyToRange.Should().Be(new GridRange(new CellAddress(sheetId, 1, 10), new CellAddress(sheetId, 1, 12)));
    }

    [Fact]
    public void AdvancedFilterDialog_RejectsListRangeWithoutDataRows()
    {
        var sheetId = SheetId.New();

        var planResult = AdvancedFilterPlanner.CreatePlan(
            sheetId,
            listRangeText: "A1",
            criteriaRangeText: "C3",
            copyToRangeText: "",
            AdvancedFilterOutputMode.FilterInPlace,
            uniqueRecordsOnly: false);

        planResult.Error.Should().Be(AdvancedFilterPlanError.ListRangeRequiresDataRows);
    }

    [Fact]
    public void AdvancedFilterDialog_RejectsCriteriaRangeWithoutCriteriaRows()
    {
        var sheetId = SheetId.New();

        var planResult = AdvancedFilterPlanner.CreatePlan(
            sheetId,
            listRangeText: "A1:C5",
            criteriaRangeText: "F1:G1",
            copyToRangeText: "",
            AdvancedFilterOutputMode.FilterInPlace,
            uniqueRecordsOnly: false);

        planResult.Error.Should().Be(AdvancedFilterPlanError.CriteriaRangeRequiresCriteriaRows);
    }

    [Theory]
    [InlineData("A1:XFD1048576", "F1:G2", AdvancedFilterPlanError.ListRangeTooLarge)]
    [InlineData("A1:C5", "F1:XFD1048576", AdvancedFilterPlanError.CriteriaRangeTooLarge)]
    public void AdvancedFilterDialog_RejectsOversizedListOrCriteriaRanges(
        string listRangeText,
        string criteriaRangeText,
        AdvancedFilterPlanError expectedError)
    {
        var sheetId = SheetId.New();

        var planResult = AdvancedFilterPlanner.CreatePlan(
            sheetId,
            listRangeText: listRangeText,
            criteriaRangeText: criteriaRangeText,
            copyToRangeText: "",
            AdvancedFilterOutputMode.FilterInPlace,
            uniqueRecordsOnly: false);

        planResult.Error.Should().Be(expectedError);
    }

    [Theory]
    [InlineData("", "F1:G2", AdvancedFilterPlanError.InvalidListRange)]
    [InlineData("   ", "F1:G2", AdvancedFilterPlanError.InvalidListRange)]
    [InlineData("A1:C5", "", AdvancedFilterPlanError.InvalidCriteriaRange)]
    [InlineData("A1:C5", "   ", AdvancedFilterPlanError.InvalidCriteriaRange)]
    public void AdvancedFilterDialog_RejectsMissingRequiredRanges(
        string listRangeText,
        string criteriaRangeText,
        AdvancedFilterPlanError expectedError)
    {
        var sheetId = SheetId.New();

        var planResult = AdvancedFilterPlanner.CreatePlan(
            sheetId,
            listRangeText: listRangeText,
            criteriaRangeText: criteriaRangeText,
            copyToRangeText: "",
            AdvancedFilterOutputMode.FilterInPlace,
            uniqueRecordsOnly: false);

        planResult.Error.Should().Be(expectedError);
    }

    [Fact]
    public void AdvancedFilterDialog_ParsesSheetQualifiedListAndCriteriaRanges()
    {
        var currentSheetId = SheetId.New();
        var dataSheetId = SheetId.New();
        var criteriaSheetId = SheetId.New();

        var planResult = AdvancedFilterPlanner.CreatePlan(
            currentSheetId,
            listRangeText: "Data!A1:D20",
            criteriaRangeText: "Criteria!F1:G2",
            copyToRangeText: "",
            AdvancedFilterOutputMode.FilterInPlace,
            uniqueRecordsOnly: false,
            resolveSheetId: sheetName => sheetName switch
            {
                "Data" => dataSheetId,
                "Criteria" => criteriaSheetId,
                _ => null
            });

        AdvancedFilterPlanner.TryCreateDialogResult(planResult, out var result).Should().BeTrue();
        result.ListRange.Should().Be(new GridRange(new CellAddress(dataSheetId, 1, 1), new CellAddress(dataSheetId, 20, 4)));
        result.CriteriaRange.Should().Be(new GridRange(new CellAddress(criteriaSheetId, 1, 6), new CellAddress(criteriaSheetId, 2, 7)));
    }

    [Fact]
    public void AdvancedFilterDialog_RejectsInvalidCopyToCell()
    {
        var sheetId = SheetId.New();

        var planResult = AdvancedFilterPlanner.CreatePlan(
            sheetId,
            listRangeText: "A1:D20",
            criteriaRangeText: "F1:G2",
            copyToRangeText: "NotACell",
            AdvancedFilterOutputMode.CopyToAnotherLocation,
            uniqueRecordsOnly: false);

        planResult.Error.Should().Be(AdvancedFilterPlanError.InvalidCopyDestinationRange);
    }

    [Fact]
    public void AdvancedFilterDialog_RejectsMissingCopyToRangeWhenCopyModeSelected()
    {
        var sheetId = SheetId.New();

        var planResult = AdvancedFilterPlanner.CreatePlan(
            sheetId,
            listRangeText: "A1:D20",
            criteriaRangeText: "F1:G2",
            copyToRangeText: "",
            AdvancedFilterOutputMode.CopyToAnotherLocation,
            uniqueRecordsOnly: false,
            resolveSheetId: null);

        planResult.Error.Should().Be(AdvancedFilterPlanError.CopyDestinationRequired);
    }

    [Fact]
    public void AdvancedFilterDialog_InPlaceModeIgnoresCopyToText()
    {
        var sheetId = SheetId.New();

        var planResult = AdvancedFilterPlanner.CreatePlan(
            sheetId,
            listRangeText: "A1:D20",
            criteriaRangeText: "F1:G2",
            copyToRangeText: "NotACell",
            AdvancedFilterOutputMode.FilterInPlace,
            uniqueRecordsOnly: false,
            resolveSheetId: null);

        AdvancedFilterPlanner.TryCreateDialogResult(planResult, out var result).Should().BeTrue();
        result.CopyToCell.Should().BeNull();
    }

    [Fact]
    public void AdvancedFilterDialog_ExposesExcelStyleModesAndReferencePickers()
    {
        var source = DialogSourceTestSupport.ReadHostSources("AdvancedFilterDialog.cs");
        var pickerSource = DialogSourceTestSupport.ReadHostSources("DialogReferencePicker.cs");

        source.Should().Contain("_filterInPlaceButton");
        source.Should().Contain("_copyToAnotherLocationButton");
        source.Should().Contain("Content = UiText.Get(\"AdvancedFilter_FilterTheListInPlace\")");
        source.Should().Contain("Content = UiText.Get(\"AdvancedFilter_CopyToAnotherLocation\")");
        source.Should().Contain("Content = UiText.Get(\"AdvancedFilter_UniqueRecordsOnly\")");
        source.Should().Contain("new GroupBox { Header = UiText.Get(\"AdvancedFilter_Action\")");
        source.Should().NotContain("Text = \"Action\"");
        source.Should().Contain("AddReferenceRow(rangesGrid, 0, UiText.Get(\"AdvancedFilter_ListRange2\"), _listRangeBox");
        source.Should().Contain("AddReferenceRow(rangesGrid, 1, UiText.Get(\"AdvancedFilter_CriteriaRange2\"), _criteriaRangeBox");
        source.Should().Contain("AddReferenceRow(rangesGrid, 2, UiText.Get(\"AdvancedFilter_CopyTo2\"), _copyToBox");
        source.Should().Contain("var labelBlock = new Label");
        source.Should().Contain("Target = textBox");
        source.Should().Contain("DialogReferencePicker.CreateEditor");
        source.Should().Contain("RequestRangeSelection");
        source.Should().Contain("_requestRangeSelection?.Invoke(RangeSelectionRequest)");
        pickerSource.Should().Contain("UiText.Get(\"DialogReferencePicker_ToolTip\")");
        pickerSource.Should().Contain("UiText.Get(\"DialogReferencePicker_HelpText\")");
        source.Should().NotContain("Content = \"Collapse Dialog\"");
        source.Should().NotContain("Text = \"E1:F2\"");
        source.Should().Contain("Header = UiText.Get(\"AdvancedFilter_Action\")");
        source.Should().Contain("UiText.Get(\"AdvancedFilter_CriteriaShouldIncludeColumnLabelsInTheFirstRowMatchingExcelAdvancedFilte\")");
        source.Should().Contain("DialogReferencePicker.CreateEditor");
    }

    [Fact]
    public void AdvancedFilterDialog_UsesUniqueAccessKeysForActionAndRangeControls()
    {
        var accessKeyLabels = new[]
        {
            "_Filter the list, in-place",
            "_Copy to another location",
            "_List range:",
            "Criteria _range:",
            "Copy _to:",
            "_Unique records only"
        };

        accessKeyLabels
            .GroupBy(GetAccessKey)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group)}")
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void AdvancedFilterDialog_DefaultsToNoRiskInPlaceModeWithBlankCriteria()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new AdvancedFilterDialog(SheetId.New(), "A1:C12");
            dialog.Show();
            try
            {
                var textBoxes = WpfTestTree.FindVisualDescendants<TextBox>(dialog).ToList();
                var radioButtons = WpfTestTree.FindVisualDescendants<RadioButton>(dialog).ToList();
                var uniqueRecordsOnly = WpfTestTree.FindVisualDescendants<CheckBox>(dialog)
                    .Single(checkBox => Equals(checkBox.Content, UiText.Get("AdvancedFilter_UniqueRecordsOnly")));
                var copyToPicker = WpfTestTree.FindVisualDescendants<Button>(dialog)
                    .Single(button => AutomationProperties.GetName(button) == "Select copy-to cell");

                radioButtons.Single(button => Equals(button.Content, UiText.Get("AdvancedFilter_FilterTheListInPlace")))
                    .IsChecked.Should().BeTrue();
                radioButtons.Single(button => Equals(button.Content, UiText.Get("AdvancedFilter_CopyToAnotherLocation")))
                    .IsChecked.Should().BeFalse();
                textBoxes[0].Text.Should().Be("A1:C12");
                textBoxes[1].Text.Should().BeEmpty();
                textBoxes[2].Text.Should().BeEmpty();
                textBoxes[2].IsEnabled.Should().BeFalse();
                copyToPicker.IsEnabled.Should().BeFalse();
                uniqueRecordsOnly.IsChecked.Should().BeFalse();
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void AdvancedFilterDialog_ExposesAccessibleReferenceFields()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new AdvancedFilterDialog(SheetId.New(), "A1:C12");
            dialog.Show();
            try
            {
                var textBoxes = WpfTestTree.FindVisualDescendants<TextBox>(dialog).ToList();

                textBoxes.Select(AutomationProperties.GetAutomationId)
                    .Should()
                    .ContainInOrder(
                        "AdvancedFilterListRangeBox",
                        "AdvancedFilterCriteriaRangeBox",
                        "AdvancedFilterCopyToBox");
                textBoxes.Select(AutomationProperties.GetHelpText)
                    .Should()
                    .ContainInOrder(
                        UiText.Get("AdvancedFilter_EnterTheListRangeToFilterIncludingColumnLabels"),
                        UiText.Get("AdvancedFilter_EnterTheCriteriaRangeIncludingCriteriaLabels"),
                        UiText.Get("AdvancedFilter_EnterTheDestinationCellOrOneRowHeaderRangeWhenCopyingFilteredRecords"));
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void AdvancedFilterDialog_ActionControlsExposeAutomationMetadata()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new AdvancedFilterDialog(SheetId.New(), "A1:C12");
            dialog.Show();
            try
            {
                AssertRadioAutomation("AdvancedFilterInPlaceButton", "Filter the list, in-place", "Filter the list in its current location.");
                AssertRadioAutomation("AdvancedFilterCopyToAnotherLocationButton", "Copy to another location", "Copy filtered records to the Copy to destination.");

                var uniqueRecordsOnly = WpfTestTree.FindVisualDescendants<CheckBox>(dialog)
                    .Single(checkBox => AutomationProperties.GetAutomationId(checkBox) == "AdvancedFilterUniqueRecordsOnlyBox");
                AutomationProperties.GetName(uniqueRecordsOnly).Should().Be("Unique records only");
                AutomationProperties.GetHelpText(uniqueRecordsOnly).Should().Be("Show or copy only unique records.");

                void AssertRadioAutomation(string automationId, string name, string helpText)
                {
                    var radioButton = WpfTestTree.FindVisualDescendants<RadioButton>(dialog)
                        .Single(button => AutomationProperties.GetAutomationId(button) == automationId);
                    AutomationProperties.GetName(radioButton).Should().Be(name);
                    AutomationProperties.GetHelpText(radioButton).Should().Be(helpText);
                }
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void AdvancedFilterDialogOpenedFromKeyboard_FocusesInPlaceAction()
    {
        var source = DialogSourceTestSupport.ReadHostSources("AdvancedFilterDialog.cs");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_filterInPlaceButton.Focus();");
        source.Should().Contain("Keyboard.Focus(_filterInPlaceButton);");
    }

    [Fact]
    public void AdvancedFilterDialogInvalidRange_RefocusesAndSelectsInvalidRangeInput()
    {
        var source = DialogSourceTestSupport.ReadHostSources("AdvancedFilterDialog.cs");

        source.Should().Contain("FocusInvalidRangeInput(planResult.Error);");
        source.Should().Contain("private void FocusInvalidRangeInput(AdvancedFilterPlanError error)");
        source.Should().Contain("AdvancedFilterPlanner.FocusTargetForPlanError(error)");
        source.Should().Contain("AdvancedFilterErrorFocusTarget.CriteriaRange");
        source.Should().Contain("AdvancedFilterErrorFocusTarget.CopyTo");
        source.Should().Contain(".DescribeError(planResult)");
        source.Should().Contain(".Resolve(UiText.Get, UiText.Format)");
        source.Should().Contain("_copyToAnotherLocationButton.IsChecked = true;");
        source.Should().Contain("DialogFocus.FocusAndSelect(target);");
    }

    [Fact]
    public void AdvancedFilterRangePicker_RefocusesSelectedInputAfterRequest()
    {
        var source = DialogSourceTestSupport.ReadHostSources("AdvancedFilterDialog.cs");
        var handlerSource = source[
            source.IndexOf("private void RequestRangeSelection", StringComparison.Ordinal)..
            source.IndexOf("private void FocusInitialKeyboardTarget", StringComparison.Ordinal)];

        handlerSource.Should().Contain("FocusRangeSelectionInput(request.Target);");
        source.Should().Contain("private static void FocusRangeSelectionInput(TextBox target)");
        source.Should().Contain("DialogFocus.FocusAndSelect(target);");
    }

    [Fact]
    public void AdvancedFilterCopyToReferencePicker_DisabledUntilCopyToAnotherLocationSelected()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new AdvancedFilterDialog(SheetId.New(), "A1:C12");
            dialog.Show();
            try
            {
                var textBoxes = WpfTestTree.FindVisualDescendants<TextBox>(dialog).ToList();
                var copyToBox = textBoxes[2];
                var copyToPicker = WpfTestTree.FindVisualDescendants<Button>(dialog)
                    .Single(button => AutomationProperties.GetName(button) == "Select copy-to cell");
                var inPlace = WpfTestTree.FindVisualDescendants<RadioButton>(dialog)
                    .Single(button => Equals(button.Content, "_Filter the list, in-place"));
                var copyToAnotherLocation = WpfTestTree.FindVisualDescendants<RadioButton>(dialog)
                    .Single(button => Equals(button.Content, "_Copy to another location"));

                copyToBox.IsEnabled.Should().BeFalse();
                copyToPicker.IsEnabled.Should().BeFalse();

                copyToAnotherLocation.IsChecked = true;

                copyToBox.IsEnabled.Should().BeTrue();
                copyToPicker.IsEnabled.Should().BeTrue();

                inPlace.IsChecked = true;

                copyToBox.IsEnabled.Should().BeFalse();
                copyToPicker.IsEnabled.Should().BeFalse();
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void AdvancedFilterCopyToLabel_DisabledUntilCopyToAnotherLocationSelected()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new AdvancedFilterDialog(SheetId.New(), "A1:C12");
            dialog.Show();
            try
            {
                var copyToLabel = WpfTestTree.FindVisualDescendants<Label>(dialog)
                    .Single(label => Equals(label.Content, "Copy _to:"));
                var inPlace = WpfTestTree.FindVisualDescendants<RadioButton>(dialog)
                    .Single(button => Equals(button.Content, "_Filter the list, in-place"));
                var copyToAnotherLocation = WpfTestTree.FindVisualDescendants<RadioButton>(dialog)
                    .Single(button => Equals(button.Content, "_Copy to another location"));

                copyToLabel.IsEnabled.Should().BeFalse();

                copyToAnotherLocation.IsChecked = true;

                copyToLabel.IsEnabled.Should().BeTrue();

                inPlace.IsChecked = true;

                copyToLabel.IsEnabled.Should().BeFalse();
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void MainWindow_WiresAdvancedFilterReferencePickersToCurrentSelection()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.DataCommands.cs");

        source.Should().Contain("new AdvancedFilterDialog(");
        source.Should().Contain("AdvancedFilterPlanner.CreateDefaultListRange(sheet, selected)");
        source.Should().NotContain("AdvancedFilterDefaultListRangePlanner.");
        source.Should().Contain("ResolveSheetIdByName,");
        source.Should().Contain("request => ApplyAdvancedFilterRangeSelection(dialog, request)");
        source.Should().Contain("private void ApplyAdvancedFilterRangeSelection(");
        source.Should().Contain("AdvancedFilterRangeSelectionRequest request");
        source.Should().Contain("BeginDialogRangeSelection(");
        source.Should().Contain("request.CollapseDialog");
        source.Should().Contain("FormatWorkbookRange(selectedRange)");
        source.Should().Contain("selectedRange => dialog.ApplyRangeSelection(request.Target, FormatWorkbookRange(selectedRange))");
        source.Should().Contain("TryExecuteRepeatableCommand(");
        source.Should().Contain("new AdvancedFilterCommand(");
        source.Should().Contain("SetActiveCell(destinationCell);");
    }

    [Fact]
    public void AdvancedFilterApplyRangeSelection_UpdatesRequestedReferenceBox()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new AdvancedFilterDialog(SheetId.New(), "A1:C12");
            dialog.Show();
            try
            {
                var textBoxes = WpfTestTree.FindVisualDescendants<TextBox>(dialog).ToList();

                dialog.ApplyRangeSelection(AdvancedFilterRangeSelectionTarget.ListRange, "Sheet2!A1:D20");
                dialog.ApplyRangeSelection(AdvancedFilterRangeSelectionTarget.CriteriaRange, "E1:F4");
                dialog.ApplyRangeSelection(AdvancedFilterRangeSelectionTarget.CopyTo, "H1:J1");

                textBoxes[0].Text.Should().Be("Sheet2!A1:D20");
                textBoxes[1].Text.Should().Be("E1:F4");
                textBoxes[2].Text.Should().Be("H1:J1");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void AdvancedFilterRangeSelectionRequest_TrimsCurrentTextAndCollapsesDialog()
    {
        AdvancedFilterPlanner.CreateRangeSelectionRequest(AdvancedFilterRangeSelectionTarget.CriteriaRange, " E1:F4 ")
            .Should()
            .Be(new AdvancedFilterRangeSelectionRequest(
                AdvancedFilterRangeSelectionTarget.CriteriaRange,
                "E1:F4",
                CollapseDialog: true));

        var source = DialogSourceTestSupport.ReadHostSources("AdvancedFilterDialog.cs");
        source.Should().Contain("AdvancedFilterPlanner.CreateRangeSelectionRequest(target, request.CurrentText)");
        source.Should().NotContain("ToServicesRangeSelectionTarget(target)");
    }

    [Theory]
    [InlineData("Select list range", AdvancedFilterRangeSelectionTarget.ListRange, "A1:C12")]
    [InlineData("Select criteria range", AdvancedFilterRangeSelectionTarget.CriteriaRange, "E1:F4")]
    [InlineData("Select copy-to cell", AdvancedFilterRangeSelectionTarget.CopyTo, "H1:J1")]
    public void AdvancedFilterReferencePickers_RaiseRangeSelectionRequest(
        string automationName,
        AdvancedFilterRangeSelectionTarget expectedTarget,
        string expectedText)
    {
        StaTestRunner.Run(() =>
        {
            var requests = new List<AdvancedFilterRangeSelectionRequest>();
            var dialog = new AdvancedFilterDialog(SheetId.New(), " A1:C12 ", requestRangeSelection: requests.Add);
            dialog.Show();
            try
            {
                var textBoxes = WpfTestTree.FindVisualDescendants<TextBox>(dialog).ToList();
                textBoxes[1].Text = " E1:F4 ";
                textBoxes[2].Text = " H1:J1 ";
                var picker = WpfTestTree.FindVisualDescendants<Button>(dialog)
                    .Single(button => AutomationProperties.GetName(button) == automationName);

                DialogSourceTestSupport.ClickButton(picker);

                requests.Should().Equal(new AdvancedFilterRangeSelectionRequest(
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
