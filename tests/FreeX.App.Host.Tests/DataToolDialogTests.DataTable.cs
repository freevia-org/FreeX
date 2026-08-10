using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FluentAssertions;
using FreeX.App.Presentation.DataTools;
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
            out var oneVariableIssue);

        oneVariableParsed.Should().BeTrue(oneVariableIssue.ToString());
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
            out var rowInputIssue);

        rowInputParsed.Should().BeTrue(rowInputIssue.ToString());
        rowInput.Mode.Should().Be(DataTableMode.OneVariable);
        rowInput.Orientation.Should().Be(DataTableInputOrientation.Row);
        rowInput.FormulaCell.Should().Be(new CellAddress(sheetId, 3, 2));

        var twoVariableParsed = DataTableDialog.TryParse(
            sheetId,
            range,
            rowInputCellText: "A1",
            columnInputCellText: "C1",
            out var twoVariable,
            out var twoVariableIssue);

        twoVariableParsed.Should().BeTrue(twoVariableIssue.ToString());
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
            out var issue);

        parsed.Should().BeTrue(issue.ToString());
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
            out var issue);

        parsed.Should().BeFalse();
        issue.Should().Be(DataTableInputParseIssue.MissingInputCell);
        DataTableDialog.DescribeIssue(issue).Should().Be("Enter either a row input cell or a column input cell.");
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
            out var issue);

        parsed.Should().BeFalse();
        issue.Should().Be(DataTableInputParseIssue.InvalidColumnInputCell);
        DataTableDialog.DescribeIssue(issue).Should().Be("Enter a valid column input cell.");
    }

    [Theory]
    [InlineData("B2", "", DataTableInputParseIssue.RowInputCellInsideTableRange, "Row input cell cannot be inside the data table range.")]
    [InlineData("", "C3", DataTableInputParseIssue.ColumnInputCellInsideTableRange, "Column input cell cannot be inside the data table range.")]
    public void DataTableDialog_RejectsInputCellInsideTableRange(
        string rowInputCellText,
        string columnInputCellText,
        DataTableInputParseIssue expectedIssue,
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
            out var issue);

        parsed.Should().BeFalse();
        issue.Should().Be(expectedIssue);
        DataTableDialog.DescribeIssue(issue).Should().Be(expectedError);
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
            out var issue);

        parsed.Should().BeFalse();
        issue.Should().Be(DataTableInputParseIssue.InputCellsMustBeDifferent);
        DataTableDialog.DescribeIssue(issue).Should().Be("Row and column input cells must be different.");
    }

    [Fact]
    public void DataTableDialog_ExposesReferencePickersForCellInputs()
    {
        var source = DialogSourceTestSupport.ReadHostSources("DataTableDialog.cs");

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
        source.Should().Contain("SharedDataTableInputParser.TryParse(");
        File.Exists(Path.Combine(
                WorkspaceFileLocator.FindWorkspaceRoot(),
                "src",
                "FreeX.App.Host",
                "DataTableInputParser.cs"))
            .Should().BeFalse("WPF should consume the shared parser directly");
    }

    [Fact]
    public void DataTableDialog_CellInputEditorsExposeAutomationMetadata()
    {
        var source = DialogSourceTestSupport.ReadHostSources("DataTableDialog.cs");

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
                    var textBox = WpfTestTree.FindVisualDescendants<TextBox>(dialog)
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
        var source = DialogSourceTestSupport.ReadHostSources("DataTableDialog.cs");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("FocusRangeSelectionInput(_rowInputBox);");
    }

    [Fact]
    public void DataTableDialogInvalidInput_RefocusesInvalidCellEntry()
    {
        var source = DialogSourceTestSupport.ReadHostSources("DataTableDialog.cs");

        source.Should().Contain("FocusInvalidInput(issue);");
        source.Should().Contain("private void FocusInvalidInput(DataTableInputParseIssue issue)");
        source.Should().Contain("SharedDataTableInputParser.GetErrorFocusTarget(issue)");
        source.Should().Contain("DialogFocus.FocusAndSelect(GetInputBox(target));");
        source.Should().NotContain("StringComparison.Ordinal");
    }

    [Fact]
    public void DataTableDialogRangePicker_RefocusesSelectedInputAfterRequest()
    {
        var source = DialogSourceTestSupport.ReadHostSources("DataTableDialog.cs");
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
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.DataCommands.cs");

        source.Should().Contain("new DataTableDialog(");
        source.Should().Contain("request => ApplyDataTableRangeSelection(dialog, request)");
        source.Should().Contain("private void ApplyDataTableRangeSelection(");
        source.Should().Contain("DataTableRangeSelectionRequest request");
        source.Should().Contain("BeginDialogRangeSelection(");
        source.Should().Contain("request.CollapseDialog");
        source.Should().Contain("selectedRange => dialog.ApplyRangeSelection(request.Target, selectedRange.Start)");
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
                var textBoxes = WpfTestTree.FindVisualDescendants<TextBox>(dialog).ToList();

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
                var textBoxes = WpfTestTree.FindVisualDescendants<TextBox>(dialog).ToList();
                textBoxes[0].Text = " A1 ";
                textBoxes[1].Text = " C1 ";
                var picker = WpfTestTree.FindVisualDescendants<Button>(dialog)
                    .Single(button => AutomationProperties.GetName(button) == automationName);

                DialogSourceTestSupport.ClickButton(picker);

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
