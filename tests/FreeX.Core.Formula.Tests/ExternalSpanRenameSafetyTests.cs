using FluentAssertions;
using FreeX.Core.Formula;

namespace FreeX.Core.Formula.Tests;

public class ExternalSpanRenameSafetyTests
{
    [Fact]
    public void RenameLocalSheet_PreservesOrdinaryExternalReferenceInSameExpression()
    {
        FormulaRewriter.Rewrite("SUM(Data!A1,'[1]Data'!B2)", new RenameSheetOp("Data", "New Data"), "Host")
            .Should().Be("SUM('New Data'!A1,'[1]Data'!B2)");
    }

    [Theory]
    [InlineData("'[1]First:Data'!A1")]
    [InlineData("'C:\\books\\[Book.xlsx]First:Data'!A1")]
    public void RenameLocalSheet_InMixedExpression_PreservesExternalSpan(string external)
    {
        var input = $"SUM(Data!A1,{external},\"[literal]\",TABLE1[Column])";
        FormulaRewriter.Rewrite(input, new RenameSheetOp("Data", "New Data"), "Host")
            .Should().Be($"SUM('New Data'!A1,{external},\"[literal]\",TABLE1[Column])");
    }

    [Theory]
    [InlineData("'[1]First:Data'!A1")]
    [InlineData("'C:\\books\\[Book.xlsx]First:Data'!A1")]
    public void DeleteLocalSheet_DoesNotAlterExternalSpan(string external)
    {
        FormulaRewriter.Rewrite($"SUM({external})", new DeleteSheetOp("Data"), "Host")
            .Should().BeNull();
    }
}
