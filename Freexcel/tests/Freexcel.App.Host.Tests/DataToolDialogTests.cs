using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class DataToolDialogTests
{
    [Theory]
    [InlineData(TextToColumnsDelimiterKind.Comma, null, ",")]
    [InlineData(TextToColumnsDelimiterKind.Semicolon, null, ";")]
    [InlineData(TextToColumnsDelimiterKind.Tab, null, "\t")]
    [InlineData(TextToColumnsDelimiterKind.Space, null, " ")]
    [InlineData(TextToColumnsDelimiterKind.Custom, "|", "|")]
    public void TextToColumnsResult_MapsDelimiterChoiceToDelimiterString(
        TextToColumnsDelimiterKind kind,
        string? customDelimiter,
        string expectedDelimiter)
    {
        var result = TextToColumnsDialog.CreateResult(kind, customDelimiter);

        result.Delimiter.Should().Be(expectedDelimiter);
    }

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
    }

    [Fact]
    public void AdvancedFilterDialog_ParsesRangesAndOptionalCopyToCellOnCurrentSheet()
    {
        var sheetId = SheetId.New();

        var parsed = AdvancedFilterDialog.TryParse(
            sheetId,
            listRangeText: "A1:D20",
            criteriaRangeText: "F1:G2",
            copyToCellText: "J1",
            uniqueRecordsOnly: true,
            out var result,
            out var error);

        parsed.Should().BeTrue(error);
        result.ListRange.Should().Be(new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 20, 4)));
        result.CriteriaRange.Should().Be(new GridRange(new CellAddress(sheetId, 1, 6), new CellAddress(sheetId, 2, 7)));
        result.CopyToCell.Should().Be(new CellAddress(sheetId, 1, 10));
        result.UniqueRecordsOnly.Should().BeTrue();
    }

    [Fact]
    public void AdvancedFilterDialog_AcceptsSingleCellRangesOnCurrentSheet()
    {
        var sheetId = SheetId.New();

        var parsed = AdvancedFilterDialog.TryParse(
            sheetId,
            listRangeText: "A1",
            criteriaRangeText: "C3",
            copyToCellText: "",
            uniqueRecordsOnly: false,
            out var result,
            out var error);

        parsed.Should().BeTrue(error);
        result.ListRange.Should().Be(new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 1, 1)));
        result.CriteriaRange.Should().Be(new GridRange(new CellAddress(sheetId, 3, 3), new CellAddress(sheetId, 3, 3)));
        result.CopyToCell.Should().BeNull();
        result.UniqueRecordsOnly.Should().BeFalse();
    }

    [Fact]
    public void AdvancedFilterDialog_RejectsInvalidCopyToCell()
    {
        var sheetId = SheetId.New();

        var parsed = AdvancedFilterDialog.TryParse(
            sheetId,
            listRangeText: "A1:D20",
            criteriaRangeText: "F1:G2",
            copyToCellText: "NotACell",
            uniqueRecordsOnly: false,
            out _,
            out var error);

        parsed.Should().BeFalse();
        error.Should().Be("Enter a valid copy-to cell.");
    }

    [Fact]
    public void ConsolidateDialog_ValidatesSameSizeSourceRanges()
    {
        var sheetId = SheetId.New();
        var first = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2));
        var second = new GridRange(new CellAddress(sheetId, 5, 4), new CellAddress(sheetId, 7, 5));
        var different = new GridRange(new CellAddress(sheetId, 10, 1), new CellAddress(sheetId, 12, 3));

        ConsolidateDialog.HaveSameSize([first, second]).Should().BeTrue();
        ConsolidateDialog.HaveSameSize([first, different]).Should().BeFalse();

        var result = ConsolidateDialog.CreateResult([first, second], new CellAddress(sheetId, 9, 1));
        result.SourceRanges.Should().Equal(first, second);
        result.DestinationCell.Should().Be(new CellAddress(sheetId, 9, 1));
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
    }

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

    [Fact]
    public void DataTableDialog_ParsesOneAndTwoVariableInputs()
    {
        var sheetId = SheetId.New();

        var oneVariableParsed = DataTableDialog.TryParse(
            sheetId,
            DataTableMode.OneVariable,
            formulaCellText: "B2",
            rowInputCellText: "",
            columnInputCellText: "C1",
            out var oneVariable,
            out var oneVariableError);

        oneVariableParsed.Should().BeTrue(oneVariableError);
        oneVariable.Mode.Should().Be(DataTableMode.OneVariable);
        oneVariable.FormulaCell.Should().Be(new CellAddress(sheetId, 2, 2));
        oneVariable.RowInputCell.Should().BeNull();
        oneVariable.ColumnInputCell.Should().Be(new CellAddress(sheetId, 1, 3));

        var twoVariableParsed = DataTableDialog.TryParse(
            sheetId,
            DataTableMode.TwoVariable,
            formulaCellText: "B2",
            rowInputCellText: "A1",
            columnInputCellText: "C1",
            out var twoVariable,
            out var twoVariableError);

        twoVariableParsed.Should().BeTrue(twoVariableError);
        twoVariable.RowInputCell.Should().Be(new CellAddress(sheetId, 1, 1));
        twoVariable.ColumnInputCell.Should().Be(new CellAddress(sheetId, 1, 3));
    }

    [Fact]
    public void DataTableDialog_RejectsInvalidFormulaCell()
    {
        var sheetId = SheetId.New();

        var parsed = DataTableDialog.TryParse(
            sheetId,
            DataTableMode.OneVariable,
            formulaCellText: "not-a-cell",
            rowInputCellText: "",
            columnInputCellText: "C1",
            out _,
            out var error);

        parsed.Should().BeFalse();
        error.Should().Be("Enter a valid formula cell.");
    }

    [Fact]
    public void DataTableDialog_RejectsInvalidOptionalInputCell()
    {
        var sheetId = SheetId.New();

        var parsed = DataTableDialog.TryParse(
            sheetId,
            DataTableMode.OneVariable,
            formulaCellText: "B2",
            rowInputCellText: "",
            columnInputCellText: "not-a-cell",
            out _,
            out var error);

        parsed.Should().BeFalse();
        error.Should().Be("Enter a valid column input cell.");
    }
}
