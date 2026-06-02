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
    public void DataTableDialog_ParsesOneAndTwoVariableInputs()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 2, 2),
            new CellAddress(sheetId, 8, 5));

        var oneVariableParsed = DataTableDialog.TryParse(
            sheetId,
            range,
            rowInputCellText: "",
            columnInputCellText: "C1",
            out var oneVariable,
            out var oneVariableError);

        oneVariableParsed.Should().BeTrue(oneVariableError);
        oneVariable.Mode.Should().Be(DataTableMode.OneVariable);
        oneVariable.Orientation.Should().Be(DataTableInputOrientation.Column);
        oneVariable.FormulaCell.Should().Be(new CellAddress(sheetId, 2, 3));
        oneVariable.RowInputCell.Should().BeNull();
        oneVariable.ColumnInputCell.Should().Be(new CellAddress(sheetId, 1, 3));

        var rowInputParsed = DataTableDialog.TryParse(
            sheetId,
            range,
            rowInputCellText: "A1",
            columnInputCellText: "",
            out var rowInput,
            out var rowInputError);

        rowInputParsed.Should().BeTrue(rowInputError);
        rowInput.Mode.Should().Be(DataTableMode.OneVariable);
        rowInput.Orientation.Should().Be(DataTableInputOrientation.Row);
        rowInput.FormulaCell.Should().Be(new CellAddress(sheetId, 3, 2));

        var twoVariableParsed = DataTableDialog.TryParse(
            sheetId,
            range,
            rowInputCellText: "A1",
            columnInputCellText: "C1",
            out var twoVariable,
            out var twoVariableError);

        twoVariableParsed.Should().BeTrue(twoVariableError);
        twoVariable.Mode.Should().Be(DataTableMode.TwoVariable);
        twoVariable.Orientation.Should().Be(DataTableInputOrientation.Column);
        twoVariable.FormulaCell.Should().Be(new CellAddress(sheetId, 2, 2));
        twoVariable.RowInputCell.Should().Be(new CellAddress(sheetId, 1, 1));
        twoVariable.ColumnInputCell.Should().Be(new CellAddress(sheetId, 1, 3));
    }

    [Fact]
    public void DataTableDialog_ParsesExcelAbsoluteAndR1C1InputCells()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 2, 2),
            new CellAddress(sheetId, 8, 5));

        var parsed = DataTableDialog.TryParse(
            sheetId,
            range,
            rowInputCellText: "$A$1",
            columnInputCellText: "R1C6",
            out var result,
            out var error);

        parsed.Should().BeTrue(error);
        result.RowInputCell.Should().Be(new CellAddress(sheetId, 1, 1));
        result.ColumnInputCell.Should().Be(new CellAddress(sheetId, 1, 6));
    }

    [Fact]
    public void DataTableDialog_RejectsMissingInputCells()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 2, 2),
            new CellAddress(sheetId, 8, 5));

        var parsed = DataTableDialog.TryParse(
            sheetId,
            range,
            rowInputCellText: "",
            columnInputCellText: "",
            out _,
            out var error);

        parsed.Should().BeFalse();
        error.Should().Be("Enter either a row input cell or a column input cell.");
    }

    [Fact]
    public void DataTableDialog_RejectsInvalidOptionalInputCell()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 2, 2),
            new CellAddress(sheetId, 8, 5));

        var parsed = DataTableDialog.TryParse(
            sheetId,
            range,
            rowInputCellText: "",
            columnInputCellText: "not-a-cell",
            out _,
            out var error);

        parsed.Should().BeFalse();
        error.Should().Be("Enter a valid column input cell.");
    }

    [Theory]
    [InlineData("B2", "", "Row input cell cannot be inside the data table range.")]
    [InlineData("", "C3", "Column input cell cannot be inside the data table range.")]
    public void DataTableDialog_RejectsInputCellInsideTableRange(
        string rowInputCellText,
        string columnInputCellText,
        string expectedError)
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 2, 2),
            new CellAddress(sheetId, 8, 5));

        var parsed = DataTableDialog.TryParse(
            sheetId,
            range,
            rowInputCellText,
            columnInputCellText,
            out _,
            out var error);

        parsed.Should().BeFalse();
        error.Should().Be(expectedError);
    }

    [Fact]
    public void DataTableDialog_RejectsSameRowAndColumnInputCell()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 2, 2),
            new CellAddress(sheetId, 8, 5));

        var parsed = DataTableDialog.TryParse(
            sheetId,
            range,
            rowInputCellText: "A1",
            columnInputCellText: "A1",
            out _,
            out var error);

        parsed.Should().BeFalse();
        error.Should().Be("Row and column input cells must be different.");
    }

    [Fact]
    public void DataTableDialog_ExposesReferencePickersForCellInputs()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "DataTableDialog.cs"));

        source.Should().Contain("UiText.Get(\"DataTable_RowInputLabel\")");
        source.Should().Contain("UiText.Get(\"DataTable_ColumnInputLabel\")");
        source.Should().NotContain("_formulaBox");
        source.Should().NotContain("_modeBox");
        source.Should().Contain("DialogReferencePicker.CreateEditor");
        source.Should().Contain("RequestRangeSelection");
        source.Should().Contain("_requestRangeSelection?.Invoke(RangeSelectionRequest)");
        source.Should().Contain("UiText.Get(\"DataTable_RowInputPickerAutomationName\")");
        source.Should().Contain("UiText.Get(\"DataTable_ColumnInputPickerAutomationName\")");
        source.Should().NotContain("Content = \"Collapse Dialog\"");
        source.Should().Contain("var labelBlock = new Label");
        source.Should().Contain("Target = textBox");
        source.Should().NotContain("Substitute values in the selected data table using worksheet input cells.");
        source.Should().NotContain("Header = \"Inputs\"");
        source.Should().Contain("DataTableInputParser.GetDefaultFormulaCell");
    }

    [Fact]
    public void DataTableDialog_CellInputEditorsExposeAutomationMetadata()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "DataTableDialog.cs"));

        StaTestRunner.Run(() =>
        {
            var sheetId = SheetId.New();
            var range = new GridRange(new CellAddress(sheetId, 2, 2), new CellAddress(sheetId, 8, 5));
            var dialog = new DataTableDialog(sheetId, range);
            dialog.Show();
            try
            {
                AssertTextBoxAutomation(
                    "DataTableRowInputCellBox",
                    UiText.Get("DataTable_RowInputAutomationName"),
                    UiText.Get("DataTable_RowInputAutomationHelpText"));
                AssertTextBoxAutomation(
                    "DataTableColumnInputCellBox",
                    UiText.Get("DataTable_ColumnInputAutomationName"),
                    UiText.Get("DataTable_ColumnInputAutomationHelpText"));

                void AssertTextBoxAutomation(string automationId, string name, string helpText)
                {
                    var textBox = FindVisualChildren<TextBox>(dialog)
                        .Single(box => AutomationProperties.GetAutomationId(box) == automationId);
                    AutomationProperties.GetName(textBox).Should().Be(name);
                    AutomationProperties.GetHelpText(textBox).Should().Be(helpText);
                }
            }
            finally
            {
                dialog.Close();
            }
        });

        source.Should().Contain("AutomationProperties.SetName(_rowInputBox, UiText.Get(\"DataTable_RowInputAutomationName\"));");
        source.Should().Contain("AutomationProperties.SetHelpText(_rowInputBox, UiText.Get(\"DataTable_RowInputAutomationHelpText\"));");
        source.Should().Contain("AutomationProperties.SetName(_columnInputBox, UiText.Get(\"DataTable_ColumnInputAutomationName\"));");
        source.Should().Contain("AutomationProperties.SetHelpText(_columnInputBox, UiText.Get(\"DataTable_ColumnInputAutomationHelpText\"));");
    }

    [Fact]
    public void DataTableDialogOpenedFromKeyboard_FocusesRowInputCell()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "DataTableDialog.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("FocusRangeSelectionInput(_rowInputBox);");
    }

    [Fact]
    public void DataTableDialogInvalidInput_RefocusesInvalidCellEntry()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "DataTableDialog.cs"));

        source.Should().Contain("FocusInvalidInput(error);");
        source.Should().Contain("private void FocusInvalidInput(string? error)");
        source.Should().Contain("UiText.Get(\"DataTable_ColumnInputInsideRangeMessage\")");
        source.Should().Contain("UiText.Get(\"DataTable_SameInputCellMessage\")");
        source.Should().Contain("DialogFocus.FocusAndSelect(target);");
    }

    [Fact]
    public void DataTableDialogRangePicker_RefocusesSelectedInputAfterRequest()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "DataTableDialog.cs"));
        var handlerSource = source[
            source.IndexOf("private void RequestRangeSelection", StringComparison.Ordinal)..
            source.IndexOf("private void FocusInitialKeyboardTarget", StringComparison.Ordinal)];

        handlerSource.Should().Contain("FocusRangeSelectionInput(request.Target);");
        source.Should().Contain("private static void FocusRangeSelectionInput(TextBox target)");
        source.Should().Contain("DialogFocus.FocusAndSelect(target);");
    }

    [Fact]
    public void DataTableDialogRangeSelectionRequest_TrimsCurrentTextAndCollapsesDialog()
    {
        DataTableDialog.CreateRangeSelectionRequest(DataTableRangeSelectionTarget.ColumnInputCell, " $C$1 ")
            .Should()
            .Be(new DataTableRangeSelectionRequest(
                DataTableRangeSelectionTarget.ColumnInputCell,
                "$C$1",
                CollapseDialog: true));
    }

    [Fact]
    public void MainWindow_WiresDataTableReferencePickersToCurrentSelection()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.DataCommands.cs"));

        source.Should().Contain("new DataTableDialog(");
        source.Should().Contain("request => ApplyDataTableRangeSelection(dialog, request)");
        source.Should().Contain("private void ApplyDataTableRangeSelection(");
        source.Should().Contain("DataTableRangeSelectionRequest request");
        source.Should().Contain("if (request.CollapseDialog)");
        source.Should().Contain("dialog.Hide();");
        source.Should().Contain("dialog.ApplyRangeSelection(request.Target, selectedRange.Start);");
        source.Should().Contain("dialog.Show();");
        source.Should().Contain("dialog.Activate();");
    }

    [Fact]
    public void DataTableApplyRangeSelection_UpdatesRequestedInputBox()
    {
        StaTestRunner.Run(() =>
        {
            var sheetId = SheetId.New();
            var range = new GridRange(new CellAddress(sheetId, 2, 2), new CellAddress(sheetId, 8, 5));
            var dialog = new DataTableDialog(sheetId, range);
            dialog.Show();
            try
            {
                var textBoxes = FindVisualChildren<TextBox>(dialog).ToList();

                dialog.ApplyRangeSelection(
                    DataTableRangeSelectionTarget.RowInputCell,
                    new CellAddress(sheetId, 3, 1));
                dialog.ApplyRangeSelection(
                    DataTableRangeSelectionTarget.ColumnInputCell,
                    new CellAddress(sheetId, 1, 6));

                textBoxes[0].Text.Should().Be("A3");
                textBoxes[1].Text.Should().Be("F1");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Theory]
    [InlineData("Select row input cell", DataTableRangeSelectionTarget.RowInputCell, "A1")]
    [InlineData("Select column input cell", DataTableRangeSelectionTarget.ColumnInputCell, "C1")]
    public void DataTableDialogReferencePickers_RaiseRangeSelectionRequest(
        string automationName,
        DataTableRangeSelectionTarget expectedTarget,
        string expectedText)
    {
        StaTestRunner.Run(() =>
        {
            var sheetId = SheetId.New();
            var range = new GridRange(new CellAddress(sheetId, 2, 2), new CellAddress(sheetId, 8, 5));
            var requests = new List<DataTableRangeSelectionRequest>();
            var dialog = new DataTableDialog(sheetId, range, requests.Add);
            dialog.Show();
            try
            {
                var textBoxes = FindVisualChildren<TextBox>(dialog).ToList();
                textBoxes[0].Text = " A1 ";
                textBoxes[1].Text = " C1 ";
                var picker = FindVisualChildren<Button>(dialog)
                    .Single(button => AutomationProperties.GetName(button) == automationName);

                picker.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                requests.Should().Equal(new DataTableRangeSelectionRequest(
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
