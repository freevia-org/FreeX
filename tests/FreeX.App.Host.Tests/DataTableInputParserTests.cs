using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class DataTableInputParserTests
{
    private static readonly SheetId SheetId = SheetId.New();
    private static readonly GridRange Range = new(new CellAddress(SheetId, 3, 2), new CellAddress(SheetId, 8, 5));

    [Fact]
    public void TryParse_ProjectsSharedResultToHostDialogResult()
    {
        DataTableInputParser.TryParse(
                SheetId,
                Range,
                rowInputCellText: "A1",
                columnInputCellText: "C1",
                out var result,
                out var error)
            .Should().BeTrue(error);

        result.Mode.Should().Be(DataTableMode.TwoVariable);
        result.FormulaCell.Should().Be(new CellAddress(SheetId, 3, 2));
        result.RowInputCell.Should().Be(new CellAddress(SheetId, 1, 1));
        result.ColumnInputCell.Should().Be(new CellAddress(SheetId, 1, 3));
    }

    [Theory]
    [InlineData("", "", "Enter either a row input cell or a column input cell.")]
    [InlineData("bad", "", "Enter a valid row input cell.")]
    [InlineData("", "bad", "Enter a valid column input cell.")]
    [InlineData("B3", "", "Row input cell cannot be inside the data table range.")]
    [InlineData("", "C4", "Column input cell cannot be inside the data table range.")]
    [InlineData("A1", "A1", "Row and column input cells must be different.")]
    public void TryParse_LocalizesSharedParserIssues(
        string rowInputCellText,
        string columnInputCellText,
        string expectedError)
    {
        DataTableInputParser.TryParse(
                SheetId,
                Range,
                rowInputCellText,
                columnInputCellText,
                out _,
                out var error)
            .Should().BeFalse();

        error.Should().Be(expectedError);
    }

    [Fact]
    public void Source_DelegatesParsingToPresentationParser()
    {
        var source = DialogSourceTestSupport.ReadHostSources("DataTableInputParser.cs");

        source.Should().Contain("FreeX.App.Presentation.DataTools.DataTableInputParser");
        source.Should().Contain("SharedDataTableInputParser.TryParse");
        source.Should().NotContain("CellReferenceInputParser.TryParseCell");
    }
}
