using Freexcel.Core.Commands;
using FluentAssertions;

namespace Freexcel.Core.Model.Tests;

public sealed class FormulaInsertionServiceTests
{
    [Theory]
    [InlineData("", 0, "Totals", "=Totals", 7)]
    [InlineData("=SUM()", 5, "Rates", "=SUM(Rates)", 10)]
    [InlineData("=A1+", 4, "TaxRate", "=A1+TaxRate", 11)]
    [InlineData("A1+", 3, "TaxRate", "=A1+TaxRate", 11)]
    public void InsertDefinedName_InsertsNameIntoFormulaText(string text, int caretIndex, string name, string expectedText, int expectedCaret)
    {
        var result = FormulaInsertionService.InsertDefinedName(text, caretIndex, name);

        result.Text.Should().Be(expectedText);
        result.CaretIndex.Should().Be(expectedCaret);
    }
}
