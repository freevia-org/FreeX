using FluentAssertions;
using Freexcel.Core.Commands;

namespace Freexcel.App.Host.Tests;

public sealed class GoToSpecialInputParserTests
{
    [Theory]
    [InlineData("blanks", GoToSpecialKind.Blanks)]
    [InlineData("constant", GoToSpecialKind.Constants)]
    [InlineData("constants", GoToSpecialKind.Constants)]
    [InlineData("formula", GoToSpecialKind.Formulas)]
    [InlineData("formulas", GoToSpecialKind.Formulas)]
    [InlineData("comment", GoToSpecialKind.Comments)]
    [InlineData("comments", GoToSpecialKind.Comments)]
    [InlineData("validation", GoToSpecialKind.DataValidation)]
    [InlineData("data validation", GoToSpecialKind.DataValidation)]
    [InlineData("visible", GoToSpecialKind.VisibleCellsOnly)]
    [InlineData("visible cells", GoToSpecialKind.VisibleCellsOnly)]
    [InlineData("row differences", GoToSpecialKind.RowDifferences)]
    [InlineData("column differences", GoToSpecialKind.ColumnDifferences)]
    [InlineData("current region", GoToSpecialKind.CurrentRegion)]
    [InlineData("last cell", GoToSpecialKind.LastCell)]
    [InlineData("conditional formats", GoToSpecialKind.ConditionalFormats)]
    [InlineData("unknown", GoToSpecialKind.Blanks)]
    public void Parse_MapsPromptTextToGoToSpecialKind(string input, GoToSpecialKind expected)
    {
        GoToSpecialInputParser.Parse(input).Should().Be(expected);
    }
}
