using Freexcel.Core.Formula;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.Core.Formula.Tests;

public sealed class ExcelParityInformationTests
{
    private readonly FormulaEvaluator _eval = new();

    [Theory]
    [InlineData("=ISBLANK(A1)", true)]
    [InlineData("=ISERROR(1/0)", true)]
    [InlineData("=ISEVEN(4)", true)]
    [InlineData("=ISFORMULA(B1)", true)]
    [InlineData("=ISLOGICAL(TRUE)", true)]
    [InlineData("=ISNA(NA())", true)]
    [InlineData("=ISNUMBER(42)", true)]
    [InlineData("=ISODD(3)", true)]
    [InlineData("=ISREF(A1)", true)]
    [InlineData("=ISTEXT(\"x\")", true)]
    public void InformationPredicates_MatchExcelBooleanResults(string formula, bool expected)
    {
        var sheet = Sheet();
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 2), "1+1");

        _eval.Evaluate(formula, sheet).Should().Be(new BoolValue(expected));
    }

    [Fact]
    public void ErrorType_ReturnsExcelErrorCodes()
    {
        _eval.Evaluate("=ERROR.TYPE(NA())", Sheet()).Should().Be(new NumberValue(7));
        _eval.Evaluate("=ERROR.TYPE(1/0)", Sheet()).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Na_Type_N_Cell_Info_ReturnExcelCompatibleScalarValues()
    {
        _eval.Evaluate("=NA()", Sheet()).Should().Be(ErrorValue.NA);
        _eval.Evaluate("=TYPE(\"x\")", Sheet()).Should().Be(new NumberValue(2));
        _eval.Evaluate("=N(TRUE)", Sheet()).Should().Be(new NumberValue(1));
        _eval.Evaluate("=INFO(\"system\")", Sheet()).Should().BeOfType<TextValue>();
        _eval.Evaluate("=CELL(\"address\",B2)", Sheet()).Should().Be(new TextValue("$B$2"));
    }

    private static Sheet Sheet() => new(SheetId.New(), "S");
}
