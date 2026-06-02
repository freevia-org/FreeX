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
    public void RemoveDuplicatesDialog_BuildsColumnOffsetSelectionAndBulkToggleStates()
    {
        var columns = RemoveDuplicatesDialog.SelectAll(4);
        columns.Should().AllSatisfy(column => column.IsSelected.Should().BeTrue());

        var cleared = RemoveDuplicatesDialog.ClearAll(columns);
        cleared.Should().AllSatisfy(column => column.IsSelected.Should().BeFalse());

        var selected = RemoveDuplicatesDialog.CreateResult(
            [
                new RemoveDuplicateColumnChoice(0, "Region", true),
                new RemoveDuplicateColumnChoice(1, "Sales", false),
                new RemoveDuplicateColumnChoice(2, "Rep", true)
            ]);

        selected.SelectedColumnOffsets.Should().Equal(0u, 2u);
    }

    [Fact]
    public void RemoveDuplicatesDialog_BulkButtonsReflectCurrentColumnSelectionState()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new RemoveDuplicatesDialog(
                [
                    new RemoveDuplicateColumnChoice(0, "Region", true),
                    new RemoveDuplicateColumnChoice(1, "Sales", true)
                ]);
            dialog.Show();
            try
            {
                var buttons = FindVisualChildren<Button>(dialog)
                    .Where(button => button.Content is string)
                    .ToDictionary(button => (string)button.Content);
                var boxes = FindVisualChildren<CheckBox>(dialog)
                    .Where(box => box.Content is "Region" or "Sales")
                    .ToList();

                buttons["_Select All"].IsEnabled.Should().BeFalse();
                buttons["_Unselect All"].IsEnabled.Should().BeTrue();

                buttons["_Unselect All"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                boxes.Should().AllSatisfy(box => box.IsChecked.Should().BeFalse());
                buttons["_Select All"].IsEnabled.Should().BeTrue();
                buttons["_Unselect All"].IsEnabled.Should().BeFalse();

                boxes[0].IsChecked = true;

                buttons["_Select All"].IsEnabled.Should().BeTrue();
                buttons["_Unselect All"].IsEnabled.Should().BeTrue();
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void RemoveDuplicatesDialog_ControlsExposeAutomationMetadata()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new RemoveDuplicatesDialog(
                [
                    new RemoveDuplicateColumnChoice(0, "Region", true),
                    new RemoveDuplicateColumnChoice(1, "Sales", true)
                ],
                [
                    new RemoveDuplicateColumnChoice(0, "Column A", true),
                    new RemoveDuplicateColumnChoice(1, "Column B", true)
                ]);
            dialog.Show();
            try
            {
                var headersBox = FindVisualChildren<CheckBox>(dialog)
                    .Single(box => Equals(box.Content, "_My data has headers"));
                AutomationProperties.GetName(headersBox).Should().Be("My data has headers");
                AutomationProperties.GetAutomationId(headersBox).Should().Be("RemoveDuplicatesHasHeadersBox");
                AutomationProperties.GetHelpText(headersBox).Should().Be("Select when the first row contains column headers.");

                var columnsPanel = FindVisualChildren<StackPanel>(dialog)
                    .Single(panel => AutomationProperties.GetAutomationId(panel) == "RemoveDuplicatesColumnsPanel");
                AutomationProperties.GetName(columnsPanel).Should().Be("Columns");
                AutomationProperties.GetHelpText(columnsPanel).Should().Be("Choose the columns used to identify duplicate rows.");

                var buttons = FindVisualChildren<Button>(dialog)
                    .Where(button => button.Content is string)
                    .ToDictionary(button => (string)button.Content);
                AutomationProperties.GetAutomationId(buttons["_Select All"]).Should().Be("RemoveDuplicatesSelectAllButton");
                AutomationProperties.GetName(buttons["_Select All"]).Should().Be("Select all columns");
                AutomationProperties.GetHelpText(buttons["_Select All"]).Should().Be("Select every column for duplicate detection.");
                AutomationProperties.GetAutomationId(buttons["_Unselect All"]).Should().Be("RemoveDuplicatesUnselectAllButton");
                AutomationProperties.GetName(buttons["_Unselect All"]).Should().Be("Unselect all columns");
                AutomationProperties.GetHelpText(buttons["_Unselect All"]).Should().Be("Clear every column selection.");

                var regionBox = FindVisualChildren<CheckBox>(dialog)
                    .Single(box => AutomationProperties.GetAutomationId(box) == "RemoveDuplicatesColumn0Box");
                AutomationProperties.GetName(regionBox).Should().Be("Region column");
                AutomationProperties.GetHelpText(regionBox).Should().Be("Select to include this column when identifying duplicate rows.");

                headersBox.IsChecked = false;

                regionBox.Content.Should().Be("Column A");
                AutomationProperties.GetName(regionBox).Should().Be("Column A column");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void RemoveDuplicatesDialog_BuildsHeaderAwareColumnChoices()
    {
        var sheetId = SheetId.New();
        var sheet = new Sheet(sheetId, "Data");
        sheet.SetCell(new CellAddress(sheetId, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheetId, 1, 2), new TextValue("Sales"));

        var range = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 8, 3));

        RemoveDuplicatesDialog.BuildColumnChoices(sheet, range).Should().Equal(
            new RemoveDuplicateColumnChoice(0, "Region", true),
            new RemoveDuplicateColumnChoice(1, "Sales", true),
            new RemoveDuplicateColumnChoice(2, "Column C", true));
    }

    [Fact]
    public void RemoveDuplicatesDialog_BuildsGenericColumnChoicesWhenHeadersAreDisabled()
    {
        var sheetId = SheetId.New();
        var sheet = new Sheet(sheetId, "Data");
        sheet.SetCell(new CellAddress(sheetId, 1, 2), new TextValue("Region"));
        var range = new GridRange(
            new CellAddress(sheetId, 1, 2),
            new CellAddress(sheetId, 8, 4));

        RemoveDuplicatesDialog.BuildColumnChoices(sheet, range, hasHeaders: false).Should().Equal(
            new RemoveDuplicateColumnChoice(0, "Column B", true),
            new RemoveDuplicateColumnChoice(1, "Column C", true),
            new RemoveDuplicateColumnChoice(2, "Column D", true));
    }

    [Fact]
    public void RemoveDuplicatesDialog_GuessesHeadersFromFirstRowShape()
    {
        var sheetId = SheetId.New();
        var sheet = new Sheet(sheetId, "Data");
        sheet.SetCell(new CellAddress(sheetId, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheetId, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheetId, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheetId, 2, 2), new NumberValue(42));
        var range = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 4, 2));

        RemoveDuplicatesDialog.GuessHasHeaders(sheet, range).Should().BeTrue();

        var numericSheet = new Sheet(sheetId, "Numbers");
        numericSheet.SetCell(new CellAddress(sheetId, 1, 1), new NumberValue(10));
        numericSheet.SetCell(new CellAddress(sheetId, 1, 2), new NumberValue(20));
        numericSheet.SetCell(new CellAddress(sheetId, 2, 1), new NumberValue(10));
        numericSheet.SetCell(new CellAddress(sheetId, 2, 2), new NumberValue(30));

        RemoveDuplicatesDialog.GuessHasHeaders(numericSheet, range).Should().BeFalse();
    }

    [Fact]
    public void RemoveDuplicatesDialog_ExcludesHeaderRowOnlyWhenHeadersAreEnabled()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 8, 3));

        RemoveDuplicatesDialog.ExcludeHeaderRow(range, hasHeaders: true).Should().Be(new GridRange(
            new CellAddress(sheetId, 2, 1),
            new CellAddress(sheetId, 8, 3)));
        RemoveDuplicatesDialog.ExcludeHeaderRow(range, hasHeaders: false).Should().Be(range);
        RemoveDuplicatesDialog.ExcludeHeaderRow(new GridRange(range.Start, range.Start), hasHeaders: true)
            .Should()
            .Be(new GridRange(range.Start, range.Start));
    }

    [Fact]
    public void RemoveDuplicatesDialog_ResultCapturesHeaderFlag()
    {
        var result = new RemoveDuplicatesDialogResult([0u, 2u], HasHeaders: true);

        result.SelectedColumnOffsets.Should().Equal(0u, 2u);
        result.HasHeaders.Should().BeTrue();
    }

    [Fact]
    public void RemoveDuplicatesDialog_ExposesExcelStyleBulkHeaderAndColumnListControls()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "RemoveDuplicatesDialog.cs")) +
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "RemoveDuplicatesDialog.Planning.cs"));
        var mainWindowSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.DataCommands.cs"));

        source.Should().Contain("UiText.Get(\"RemoveDuplicates_SelectAll\")");
        source.Should().Contain("UiText.Get(\"RemoveDuplicates_UnselectAll\")");
        source.Should().Contain("UiText.Get(\"RemoveDuplicates_MyDataHasHeaders\")");
        source.Should().Contain("_columnsPanel");
        source.Should().Contain("Content = UiText.Get(\"RemoveDuplicates_Columns\")");
        source.Should().Contain("Target = _columnsPanel");
        source.Should().Contain("_columnsPanel.Focusable = true");
        source.Should().Contain("_columnsPanel.GotKeyboardFocus");
        source.Should().NotContain("new TextBlock { Text = \"Columns:\"");
        source.Should().Contain("SelectAllButton_Click");
        source.Should().Contain("UnselectAllButton_Click");
        source.Should().Contain("RefreshColumnLabels");
        source.Should().Contain("HasHeaders");
        mainWindowSource.Should().Contain("TryExecuteRepeatableGroupedSheetCommand(");
        mainWindowSource.Should().Contain("var currentRange = SheetGrid.SelectedRange ?? range;");
        mainWindowSource.Should().Contain("var activeRange = RemoveDuplicatesDialog.ExcludeHeaderRow(currentRange, dialog.Result.HasHeaders);");
        mainWindowSource.Should().Contain("GroupedSheetRangePlanner.RemapRangeToSheet(");
        mainWindowSource.Should().Contain("activeRange,");
        mainWindowSource.Should().Contain("new RemoveDuplicateRowsCommand(");
        mainWindowSource.Should().Contain("UiText.Format(\"MainWindowMessage_RemoveDuplicatesRemovedRows\", activeSheetCommand?.RemovedRowCount ?? 0)");
    }

    [Fact]
    public void RemoveDuplicatesDialogOpenedFromKeyboard_FocusesHeaderChoice()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "RemoveDuplicatesDialog.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_hasHeadersBox.Focus();");
        source.Should().Contain("Keyboard.Focus(_hasHeadersBox);");
    }

    [Fact]
    public void RemoveDuplicatesDialogInvalidColumnSelection_FocusesColumnChoice()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "RemoveDuplicatesDialog.cs"));

        source.Should().Contain("FocusFirstColumnChoice();");
        source.Should().Contain("private void FocusFirstColumnChoice()");
        source.Should().Contain("_boxes.FirstOrDefault()");
        source.Should().Contain("firstColumnBox.Focus();");
        source.Should().Contain("Keyboard.Focus(firstColumnBox);");
    }
}
