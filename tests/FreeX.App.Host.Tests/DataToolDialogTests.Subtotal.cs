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
    public void SubtotalDialog_CreatesOptionsUsingSubtotalFunctionServiceNames()
    {
        var result = SubtotalDialog.CreateResult(
            groupColumnOffset: 0,
            subtotalColumnOffsets: [1u, 3u],
            functionText: "average",
            replaceCurrentSubtotals: true,
            pageBreakBetweenGroups: true,
            summaryBelowData: false);

        result.GroupColumnOffset.Should().Be(0);
        result.SubtotalColumnOffsets.Should().Equal(1u, 3u);
        result.FunctionNumber.Should().Be(1);
        result.ReplaceCurrentSubtotals.Should().BeTrue();
        result.PageBreakBetweenGroups.Should().BeTrue();
        result.SummaryBelowData.Should().BeFalse();
        result.Action.Should().Be(SubtotalDialogAction.Apply);
    }

    [Fact]
    public void SubtotalDialog_CreatesRemoveAllResultWithoutSubtotalColumns()
    {
        var result = SubtotalDialog.CreateRemoveAllResult();

        result.Action.Should().Be(SubtotalDialogAction.RemoveAll);
        result.SubtotalColumnOffsets.Should().BeEmpty();
        result.ReplaceCurrentSubtotals.Should().BeFalse();
        result.PageBreakBetweenGroups.Should().BeFalse();
        result.SummaryBelowData.Should().BeTrue();
    }

    [Fact]
    public void SubtotalDialog_RejectsApplyWithoutSubtotalColumns()
    {
        var act = () => SubtotalDialog.CreateResult(
            groupColumnOffset: 0,
            subtotalColumnOffsets: [],
            functionText: "Sum",
            replaceCurrentSubtotals: true,
            pageBreakBetweenGroups: false,
            summaryBelowData: true);

        act.Should().Throw<ArgumentException>()
            .WithMessage($"{UiText.Get("Subtotal_AtLeastOneSubtotalColumnIsRequired")}*");
    }

    [Fact]
    public void SubtotalDialog_BuildsHeaderAwareColumnChoices()
    {
        var sheetId = SheetId.New();
        var sheet = new Sheet(sheetId, "Data");
        sheet.SetCell(new CellAddress(sheetId, 1, 2), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheetId, 1, 3), new TextValue("Sales"));

        var range = new GridRange(
            new CellAddress(sheetId, 1, 2),
            new CellAddress(sheetId, 8, 4));

        SubtotalDialog.BuildColumnChoices(sheet, range).Should().Equal(
            new SubtotalColumnChoice(0, "Region", false),
            new SubtotalColumnChoice(1, "Sales", true),
            new SubtotalColumnChoice(2, UiText.Format("Subtotal_ColumnLabel", "D"), true));
    }

    [Fact]
    public void SubtotalDialog_DefaultsMatchNoRiskExcelFlow()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new SubtotalDialog(
                [
                    new SubtotalColumnChoice(0, "Region", false),
                    new SubtotalColumnChoice(1, "Sales", true),
                    new SubtotalColumnChoice(2, "Units", true)
                ]);
            dialog.Show();
            try
            {
                var comboBoxes = FindVisualChildren<ComboBox>(dialog).ToList();
                var checkBoxes = FindVisualChildren<CheckBox>(dialog).ToList();
                var buttons = FindVisualChildren<Button>(dialog).ToList();

                comboBoxes[0].SelectedValue.Should().Be(0u);
                comboBoxes[1].SelectedValue.Should().Be("Sum");
                checkBoxes.Single(box => Equals(box.Content, "Region")).IsChecked.Should().BeFalse();
                checkBoxes.Single(box => Equals(box.Content, "Sales")).IsChecked.Should().BeTrue();
                checkBoxes.Single(box => Equals(box.Content, "Units")).IsChecked.Should().BeTrue();
                checkBoxes.Single(box => Equals(box.Content, UiText.Get("Subtotal_ReplaceCurrentSubtotals"))).IsChecked.Should().BeTrue();
                checkBoxes.Single(box => Equals(box.Content, UiText.Get("Subtotal_PageBreakBetweenGroups"))).IsChecked.Should().BeFalse();
                checkBoxes.Single(box => Equals(box.Content, UiText.Get("Subtotal_SummaryBelowData"))).IsChecked.Should().BeTrue();
                buttons.Should().Contain(button => Equals(button.Content, UiText.Get("Subtotal_RemoveAll")));
                buttons.Should().Contain(button => Equals(button.Content, UiText.Ok) && button.IsDefault);
                buttons.Should().Contain(button => Equals(button.Content, UiText.Cancel) && button.IsCancel);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void SubtotalDialog_ControlsExposeAutomationMetadata()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new SubtotalDialog(
                [
                    new SubtotalColumnChoice(0, "Region", false),
                    new SubtotalColumnChoice(1, "Sales", true),
                    new SubtotalColumnChoice(2, "Units", true)
                ]);
            dialog.Show();
            try
            {
                var comboBoxes = FindVisualChildren<ComboBox>(dialog).ToList();
                var groupColumnBox = comboBoxes.Single(box => AutomationProperties.GetAutomationId(box) == "SubtotalGroupColumnBox");
                AutomationProperties.GetName(groupColumnBox).Should().Be("At each change in");
                AutomationProperties.GetHelpText(groupColumnBox).Should().Be("Choose the column that defines each subtotal group.");

                var functionBox = comboBoxes.Single(box => AutomationProperties.GetAutomationId(box) == "SubtotalFunctionBox");
                AutomationProperties.GetName(functionBox).Should().Be("Use function");
                AutomationProperties.GetHelpText(functionBox).Should().Be("Choose the function used to calculate each subtotal.");

                var columnsPanel = FindVisualChildren<StackPanel>(dialog)
                    .Single(panel => AutomationProperties.GetAutomationId(panel) == "SubtotalColumnsPanel");
                AutomationProperties.GetName(columnsPanel).Should().Be("Add subtotal to");
                AutomationProperties.GetHelpText(columnsPanel).Should().Be("Choose columns that receive subtotal calculations.");

                var salesBox = FindVisualChildren<CheckBox>(dialog)
                    .Single(box => AutomationProperties.GetAutomationId(box) == "SubtotalColumn1Box");
                AutomationProperties.GetName(salesBox).Should().Be("Sales subtotal column");
                AutomationProperties.GetHelpText(salesBox).Should().Be("Select to add a subtotal calculation to this column.");

                AssertCheckBoxAutomation("SubtotalReplaceCurrentBox", "Replace current subtotals", "Replace existing subtotals with the new subtotal settings.");
                AssertCheckBoxAutomation("SubtotalPageBreakBox", "Page break between groups", "Insert a page break after each subtotal group.");
                AssertCheckBoxAutomation("SubtotalSummaryBelowBox", "Summary below data", "Place subtotal rows below each group.");

                var removeAll = FindVisualChildren<Button>(dialog)
                    .Single(button => AutomationProperties.GetAutomationId(button) == "SubtotalRemoveAllButton");
                AutomationProperties.GetName(removeAll).Should().Be("Remove all subtotals");
                AutomationProperties.GetHelpText(removeAll).Should().Be("Remove all subtotal rows from the selected data.");

                void AssertCheckBoxAutomation(string automationId, string name, string helpText)
                {
                    var checkBox = FindVisualChildren<CheckBox>(dialog)
                        .Single(box => AutomationProperties.GetAutomationId(box) == automationId);
                    AutomationProperties.GetName(checkBox).Should().Be(name);
                    AutomationProperties.GetHelpText(checkBox).Should().Be(helpText);
                }
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void SubtotalDialog_ExposesKeyboardAccessKeysForStaticOptions()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "SubtotalDialog.cs"));

        foreach (var key in new[]
        {
            "Subtotal_ReplaceCurrentSubtotals",
            "Subtotal_PageBreakBetweenGroups",
            "Subtotal_SummaryBelowData",
            "Subtotal_AtEachChangeIn",
            "Subtotal_AddSubtotalTo",
            "Subtotal_UseFunction",
            "Subtotal_RemoveAll"
        })
            source.Should().Contain($"UiText.Get(\"{key}\")");

        source.Should().Contain("new Label { Content = UiText.Get(\"Subtotal_AddSubtotalTo\"), Target = _subtotalColumnPanel");
        source.Should().Contain("_subtotalColumnPanel.Focusable = true");
        source.Should().Contain("_subtotalColumnPanel.GotKeyboardFocus");
    }

    [Fact]
    public void SubtotalDialog_ExposesExcelStyleFunctionDropdownAndSubtotalChecklist()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "SubtotalDialog.cs"));

        source.Should().Contain("ComboBox _functionBox = new()");
        source.Should().Contain("CreateSubtotalFunctionChoices");
        source.Should().Contain("ItemsSource = CreateSubtotalFunctionChoices()");
        source.Should().Contain("SelectedValue = DefaultSubtotalFunction");
        source.Should().Contain("SelectedValuePath = nameof(SubtotalFunctionChoice.FunctionText)");
        source.Should().NotContain("Header = \"Add subtotal to:\"");
        source.Should().Contain("_subtotalColumnPanel");
    }

    [Fact]
    public void SubtotalDialogOpenedFromKeyboard_FocusesGroupColumnChoice()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "SubtotalDialog.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_groupColumnBox.Focus();");
        source.Should().Contain("Keyboard.Focus(_groupColumnBox);");
    }

    [Fact]
    public void SubtotalDialogInvalidInputs_FocusInvalidControl()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "SubtotalDialog.cs"));

        source.Should().Contain("FocusInvalidInput(ex.Message);");
        source.Should().Contain("private void FocusInvalidInput(string message)");
        source.Should().Contain("FocusFunctionChoice();");
        source.Should().Contain("private void FocusFunctionChoice()");
        source.Should().Contain("_functionBox.Focus();");
        source.Should().Contain("Keyboard.Focus(_functionBox);");
        source.Should().Contain("FocusSubtotalColumnChoices();");
        source.Should().Contain("private void FocusSubtotalColumnChoices()");
        source.Should().Contain("_subtotalColumnBoxes.FirstOrDefault()");
        source.Should().Contain("firstColumnBox.Focus();");
        source.Should().Contain("Keyboard.Focus(firstColumnBox);");
    }

    [Fact]
    public void SubtotalDialog_OrdersControlsLikeExcelSubtotalDialog()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "SubtotalDialog.cs"));

        source.IndexOf("UiText.Get(\"Subtotal_AtEachChangeIn\")", StringComparison.Ordinal).Should()
            .BeLessThan(source.IndexOf("UiText.Get(\"Subtotal_UseFunction\")", StringComparison.Ordinal));
        source.IndexOf("UiText.Get(\"Subtotal_UseFunction\")", StringComparison.Ordinal).Should()
            .BeLessThan(source.IndexOf("UiText.Get(\"Subtotal_AddSubtotalTo\")", StringComparison.Ordinal));
        source.IndexOf("UiText.Get(\"Subtotal_AddSubtotalTo\")", StringComparison.Ordinal).Should()
            .BeLessThan(source.IndexOf("UiText.Get(\"Subtotal_ReplaceCurrentSubtotals\")", StringComparison.Ordinal));
        source.Should().Contain("CreateSubtotalButtonRow");
    }

    [Fact]
    public void SubtotalCommandSurface_RoutesRemoveAllToRemoveSubtotalRowsCommand()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.DataCommands.cs"));

        source.Should().Contain("SubtotalDialogAction.RemoveAll");
        source.Should().Contain("TryExecuteRepeatableGroupedSheetCommand(");
        source.Should().Contain("new RemoveSubtotalRowsCommand(");
        source.Should().Contain("var currentRange = SheetGrid.SelectedRange ?? range;");
        source.Should().Contain("GroupedSheetRangePlanner.RemapRangeToSheet(currentRange, sheetId)");
        source.Should().Contain("dialog.Result.ReplaceCurrentSubtotals");
        source.Should().Contain("new CompositeWorkbookCommand(\"Subtotal\", [new RemoveSubtotalRowsCommand(sheetId, sheetRange), subtotalCommand])");
        source.Should().Contain("dialog.Result.PageBreakBetweenGroups");
        source.Should().Contain("dialog.Result.SummaryBelowData");
    }
}
