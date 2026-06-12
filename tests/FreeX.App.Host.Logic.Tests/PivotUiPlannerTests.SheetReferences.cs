using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class PivotUiPlannerTests
{
    [Theory]
    [InlineData("Sheet1", "Sheet1")]
    [InlineData("'Sales Q1'", "Sales Q1")]
    [InlineData("'Bob''s Sheet'", "Bob's Sheet")]
    public void UnquoteSheetName_RemovesExcelQuotes(string input, string expected)
    {
        PivotUiPlanner.UnquoteSheetName(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("Sheet1", "Sheet1")]
    [InlineData("Sales_Q1", "Sales_Q1")]
    [InlineData("Sales Q1", "'Sales Q1'")]
    [InlineData("Bob's Sheet", "'Bob''s Sheet'")]
    public void QuoteSheetNameForReference_QuotesOnlyWhenNeeded(string input, string expected)
    {
        PivotUiPlanner.QuoteSheetNameForReference(input).Should().Be(expected);
    }
}
