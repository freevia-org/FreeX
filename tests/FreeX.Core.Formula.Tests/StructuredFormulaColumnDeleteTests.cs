using FluentAssertions;
using FreeX.Core.Formula;

namespace FreeX.Core.Formula.Tests;

public sealed class StructuredFormulaColumnDeleteTests
{
    [Theory]
    [InlineData("SUM(SalesTable[Cost])", "SUM(#REF!)")]
    [InlineData("SUM(SalesTable[[#Data],[Cost]])", "SUM(#REF!)")]
    [InlineData("SUM(SalesTable[[Sales]:[Cost]])", "SUM(#REF!)")]
    [InlineData("SUM(SalesTable[[Cost]:[Profit]])", "SUM(#REF!)")]
    [InlineData("SUM(SalesTable[[Sales]:[Profit]])", "SUM(SalesTable[[Sales]:[Profit]])")]
    [InlineData("SUM(Alias[Cost])", "SUM(#REF!)")]
    [InlineData("SUM([@Cost])", "SUM(#REF!)")]
    [InlineData("SUM(SalesTable[Other])", "SUM(SalesTable[Other])")]
    public void DeletedColumnInvalidatesOnlySelectedColumnOrRangeEndpoint(string formula, string expected)
    {
        StructuredFormulaColumnDelete.TryRewrite(formula, "SalesTable", "Alias", "SalesTable", ["Cost"], false, out var actual).Should().BeTrue();
        actual.Should().Be(expected);
    }

    [Theory]
    [InlineData("SUM(SalesTable[X'#Y])", "X#Y")]
    [InlineData("SUM(SalesTable[[#Data],[X'#Y]])", "X#Y")]
    [InlineData("SUM(SalesTable[Sales Amount])", "Sales Amount")]
    [InlineData("SUM(SalesTable[A]]B])", "A]B")]
    public void EscapedAndSpacedColumnNamesAreRecognized(string formula, string column)
    {
        StructuredFormulaColumnDelete.TryRewrite(formula, "SalesTable", null, null, [column], false, out var result).Should().BeTrue();
        result.Should().Be("SUM(#REF!)");
    }

    [Fact]
    public void UnqualifiedReferencesUseOwnerAndLeaveOtherTablesUntouched()
    {
        StructuredFormulaColumnDelete.TryRewrite("SUM([@Cost])+SUM(Other[Cost])", "SalesTable", "Alias", "Other", ["Cost"], false, out var result).Should().BeTrue();
        result.Should().Be("SUM([@Cost])+SUM(Other[Cost])");
        StructuredFormulaColumnDelete.TryRewrite("SUM([@Cost])", "SalesTable", "Alias", null, ["Cost"], false, out _).Should().BeFalse();
    }

    [Fact]
    public void DeletedColumnMatchingInternalMarker_IsStillInvalidated()
    {
        StructuredFormulaColumnDelete.TryRewrite("SUM(SalesTable[FreexDeletedColumnMarker])", "SalesTable", null, null,
            ["FreexDeletedColumnMarker"], false, out var result).Should().BeTrue();
        result.Should().Be("SUM(#REF!)");
    }

    [Fact]
    public void WholeTableDeletionInvalidatesWholeTableSelector()
    {
        StructuredFormulaColumnDelete.TryRewrite("SUM(Alias[#All])", "SalesTable", "Alias", null, [], true, out var result).Should().BeTrue();
        result.Should().Be("SUM(#REF!)");
    }
}
