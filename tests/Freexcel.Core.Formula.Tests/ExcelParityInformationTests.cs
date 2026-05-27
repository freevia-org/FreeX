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
    [InlineData("=ISERR(1/0)", true)]
    [InlineData("=ISERR(NA())", false)]
    [InlineData("=ISEVEN(4)", true)]
    [InlineData("=ISFORMULA(B1)", true)]
    [InlineData("=ISLOGICAL(TRUE)", true)]
    [InlineData("=ISNA(NA())", true)]
    [InlineData("=ISNONTEXT(42)", true)]
    [InlineData("=ISNONTEXT(\"x\")", false)]
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
    public void IsErrAndIsNonText_SpillOverRanges()
    {
        var sheet = Sheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), ErrorValue.Value);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), ErrorValue.NA);
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("x"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(42));

        AssertColumn(_eval.Evaluate("=ISERR(A1:A4)", sheet), true, false, false, false);
        AssertColumn(_eval.Evaluate("=ISNONTEXT(A1:A4)", sheet), true, true, false, true);
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

    private static void AssertColumn(ScalarValue value, params bool[] expected)
    {
        var range = value.Should().BeOfType<RangeValue>().Subject;
        range.RowCount.Should().Be(expected.Length);
        range.ColCount.Should().Be(1);
        for (int row = 0; row < expected.Length; row++)
            range.At(row + 1, 1).Should().Be(new BoolValue(expected[row]));
    }
}
