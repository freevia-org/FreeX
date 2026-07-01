using FluentAssertions;
using FreeX.App.Presentation;
using FreeX.App.Presentation.DataTools;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.DataTools;

public sealed class DataTableInputParserTests
{
    private static readonly SheetId SheetId = SheetId.New();
    private static readonly GridRange Range = new(new CellAddress(SheetId, 3, 2), new CellAddress(SheetId, 8, 5));

    [Theory]
    [InlineData("two", true)]
    [InlineData("2", true)]
    [InlineData("one", false)]
    [InlineData("1", false)]
    [InlineData("anything", false)]
    public void IsTwoVariableMode_RecognizesTwoVariableAliases(string input, bool expected)
    {
        DataTableInputParser.IsTwoVariableMode(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(false, 3, 3)]
    [InlineData(true, 3, 2)]
    public void GetDefaultFormulaCell_UsesExcelAdjacentFormulaDefaults(
        bool twoVariable,
        uint expectedRow,
        uint expectedCol)
    {
        DataTableInputParser.GetDefaultFormulaCell(Range, twoVariable)
            .Should().Be(new CellAddress(SheetId, expectedRow, expectedCol));
    }

    [Theory]
    [InlineData(" C5 ", true, 5, 3)]
    [InlineData("$C$5", true, 5, 3)]
    [InlineData("C$5", true, 5, 3)]
    [InlineData("$C5", true, 5, 3)]
    [InlineData("R5C3", true, 5, 3)]
    [InlineData("bad", false, 0, 0)]
    [InlineData("$C", false, 0, 0)]
    [InlineData("R0C3", false, 0, 0)]
    public void TryParseCell_ParsesTrimmedCellAddress(string input, bool expected, uint row, uint col)
    {
        var result = DataTableInputParser.TryParseCell(input, SheetId, out var address);

        result.Should().Be(expected);
        if (expected)
            address.Should().Be(new CellAddress(SheetId, row, col));
    }

    [Fact]
    public void TryParse_BuildsOneAndTwoVariableResults()
    {
        DataTableInputParser.TryParse(
                SheetId,
                Range,
                rowInputCellText: "",
                columnInputCellText: "C1",
                out var oneVariable,
                out var oneVariableIssue)
            .Should().BeTrue(oneVariableIssue.ToString());

        oneVariable.Mode.Should().Be(DataTableMode.OneVariable);
        oneVariable.Orientation.Should().Be(DataTableInputOrientation.Column);
        oneVariable.FormulaCell.Should().Be(new CellAddress(SheetId, 3, 3));
        oneVariable.RowInputCell.Should().BeNull();
        oneVariable.ColumnInputCell.Should().Be(new CellAddress(SheetId, 1, 3));

        DataTableInputParser.TryParse(
                SheetId,
                Range,
                rowInputCellText: "A1",
                columnInputCellText: "C1",
                out var twoVariable,
                out var twoVariableIssue)
            .Should().BeTrue(twoVariableIssue.ToString());

        twoVariable.Mode.Should().Be(DataTableMode.TwoVariable);
        twoVariable.Orientation.Should().Be(DataTableInputOrientation.Column);
        twoVariable.FormulaCell.Should().Be(new CellAddress(SheetId, 3, 2));
        twoVariable.RowInputCell.Should().Be(new CellAddress(SheetId, 1, 1));
        twoVariable.ColumnInputCell.Should().Be(new CellAddress(SheetId, 1, 3));
    }

    [Theory]
    [InlineData("", "", DataTableInputParseIssue.MissingInputCell)]
    [InlineData("bad", "", DataTableInputParseIssue.InvalidRowInputCell)]
    [InlineData("", "bad", DataTableInputParseIssue.InvalidColumnInputCell)]
    [InlineData("B3", "", DataTableInputParseIssue.RowInputCellInsideTableRange)]
    [InlineData("", "C4", DataTableInputParseIssue.ColumnInputCellInsideTableRange)]
    [InlineData("A1", "A1", DataTableInputParseIssue.InputCellsMustBeDifferent)]
    public void TryParse_RejectsInvalidInputCells(
        string rowInputCellText,
        string columnInputCellText,
        DataTableInputParseIssue expectedIssue)
    {
        DataTableInputParser.TryParse(
                SheetId,
                Range,
                rowInputCellText,
                columnInputCellText,
                out _,
                out var issue)
            .Should().BeFalse();

        issue.Should().Be(expectedIssue);
    }

    [Fact]
    public void CreateRangeSelectionRequest_TrimsCurrentTextAndCollapsesDialog()
    {
        DataTableInputParser.CreateRangeSelectionRequest(DataTableRangeSelectionTarget.ColumnInputCell, " $C$1 ")
            .Should()
            .Be(new DataTableRangeSelectionRequest(
                DataTableRangeSelectionTarget.ColumnInputCell,
                "$C$1",
                CollapseDialog: true));
    }

    [Theory]
    [InlineData(DataTableInputParseIssue.InvalidRowInputCell, DataTableRangeSelectionTarget.RowInputCell)]
    [InlineData(DataTableInputParseIssue.MissingInputCell, DataTableRangeSelectionTarget.RowInputCell)]
    [InlineData(DataTableInputParseIssue.RowInputCellInsideTableRange, DataTableRangeSelectionTarget.RowInputCell)]
    [InlineData(DataTableInputParseIssue.InvalidColumnInputCell, DataTableRangeSelectionTarget.ColumnInputCell)]
    [InlineData(DataTableInputParseIssue.ColumnInputCellInsideTableRange, DataTableRangeSelectionTarget.ColumnInputCell)]
    [InlineData(DataTableInputParseIssue.InputCellsMustBeDifferent, DataTableRangeSelectionTarget.ColumnInputCell)]
    public void GetErrorFocusTarget_MapsParserIssuesToInputTargets(
        DataTableInputParseIssue issue,
        DataTableRangeSelectionTarget expectedTarget)
    {
        DataTableInputParser.GetErrorFocusTarget(issue).Should().Be(expectedTarget);
    }
}
