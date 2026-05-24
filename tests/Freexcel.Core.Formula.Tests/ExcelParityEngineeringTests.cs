using Freexcel.Core.Formula;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.Core.Formula.Tests;

public sealed class ExcelParityEngineeringTests
{
    private readonly FormulaEvaluator _eval = new();

    [Theory]
    [InlineData("=BIN2DEC(\"1010\")", 10)]
    [InlineData("=BIN2DEC(1010)", 10)]
    [InlineData("=BIN2DEC(\"1111111111\")", -1)]
    [InlineData("=HEX2DEC(\"ff\")", 255)]
    [InlineData("=HEX2DEC(\"FF\")", 255)]
    [InlineData("=HEX2DEC(\"FFFFFFFFFF\")", -1)]
    [InlineData("=OCT2DEC(17)", 15)]
    [InlineData("=OCT2DEC(\"17\")", 15)]
    [InlineData("=OCT2DEC(\"7777777777\")", -1)]
    public void BaseToDecimalFunctions_UseExcelSignedWidth(string formula, double expected)
    {
        _eval.Evaluate(formula, MakeSheet()).Should().Be(new NumberValue(expected));
    }

    [Theory]
    [InlineData("=DEC2BIN(10)", "1010")]
    [InlineData("=DEC2BIN(10,8)", "00001010")]
    [InlineData("=DEC2BIN(-1)", "1111111111")]
    [InlineData("=DEC2BIN(0,4)", "0000")]
    [InlineData("=DEC2HEX(255)", "FF")]
    [InlineData("=DEC2HEX(255,4)", "00FF")]
    [InlineData("=DEC2HEX(-1)", "FFFFFFFFFF")]
    [InlineData("=DEC2OCT(15)", "17")]
    [InlineData("=DEC2OCT(15,4)", "0017")]
    [InlineData("=DEC2OCT(-1)", "7777777777")]
    [InlineData("=BIN2HEX(\"1010\")", "A")]
    [InlineData("=BIN2OCT(\"1010\")", "12")]
    [InlineData("=HEX2BIN(\"F\",8)", "00001111")]
    [InlineData("=HEX2OCT(\"F\")", "17")]
    [InlineData("=OCT2BIN(\"17\")", "1111")]
    [InlineData("=OCT2HEX(\"17\")", "F")]
    [InlineData("=OCT2HEX(\"17\",4)", "000F")]
    public void BaseConversionFunctions_ReturnExcelText(string formula, string expected)
    {
        _eval.Evaluate(formula, MakeSheet()).Should().Be(new TextValue(expected));
    }

    [Theory]
    [InlineData("=DEC2BIN(10.9)", "1010")]
    [InlineData("=DEC2HEX(31.9,4.9)", "001F")]
    [InlineData("=BIN2HEX(\"101\",4.9)", "0005")]
    public void BaseConversionFunctions_TruncateFractionalNumberAndPlaces(string formula, string expected)
    {
        _eval.Evaluate(formula, MakeSheet()).Should().Be(new TextValue(expected));
    }

    [Fact]
    public void BaseConversionFunctions_RangeArguments_SpillElementwise()
    {
        var values = MakeSheet(
            (1, 1, new NumberValue(10)),
            (2, 1, new NumberValue(15)));

        AssertColumn(_eval.Evaluate("=DEC2BIN(A1:A2)", values), new TextValue("1010"), new TextValue("1111"));
        AssertColumn(_eval.Evaluate("=DEC2HEX(A1:A2)", values), new TextValue("A"), new TextValue("F"));
        AssertColumn(_eval.Evaluate("=DEC2OCT(A1:A2)", values), new TextValue("12"), new TextValue("17"));

        var baseText = MakeSheet(
            (1, 1, new TextValue("1010")),
            (2, 1, new TextValue("1111")),
            (1, 2, new TextValue("A")),
            (2, 2, new TextValue("F")),
            (1, 3, new TextValue("12")),
            (2, 3, new TextValue("17")));

        AssertColumn(_eval.Evaluate("=BIN2DEC(A1:A2)", baseText), new NumberValue(10), new NumberValue(15));
        AssertColumn(_eval.Evaluate("=HEX2DEC(B1:B2)", baseText), new NumberValue(10), new NumberValue(15));
        AssertColumn(_eval.Evaluate("=OCT2DEC(C1:C2)", baseText), new NumberValue(10), new NumberValue(15));

        var places = MakeSheet(
            (1, 1, new NumberValue(4)),
            (2, 1, new NumberValue(5)));

        AssertColumn(_eval.Evaluate("=DEC2BIN(3,A1:A2)", places), new TextValue("0011"), new TextValue("00011"));
        AssertColumn(_eval.Evaluate("=BIN2HEX(\"1010\",A1:A2)", places), new TextValue("000A"), new TextValue("0000A"));
    }

    [Theory]
    [InlineData("=BIN2DEC(\"102\")")]
    [InlineData("=BIN2DEC(\"10101010101\")")]
    [InlineData("=DEC2BIN(512)")]
    [InlineData("=DEC2BIN(10,2)")]
    [InlineData("=DEC2HEX(255,-1)")]
    [InlineData("=DEC2OCT(64,1)")]
    [InlineData("=HEX2DEC(\"10000000000\")")]
    [InlineData("=DEC2HEX(549755813888)")]
    [InlineData("=OCT2DEC(\"10000000000\")")]
    [InlineData("=DEC2OCT(536870912)")]
    public void BaseConversionDomainErrors_ReturnNum(string formula)
    {
        _eval.Evaluate(formula, MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Theory]
    [InlineData("=BITAND(13,25)", 9)]
    [InlineData("=BITOR(13,25)", 29)]
    [InlineData("=BITXOR(13,25)", 20)]
    [InlineData("=BITLSHIFT(4,2)", 16)]
    [InlineData("=BITLSHIFT(16,-2)", 4)]
    [InlineData("=BITRSHIFT(16,2)", 4)]
    [InlineData("=BITRSHIFT(4,-2)", 16)]
    public void BitFunctions_ReturnExcelIntegerResults(string formula, double expected)
    {
        _eval.Evaluate(formula, MakeSheet()).Should().Be(new NumberValue(expected));
    }

    [Fact]
    public void BitFunctions_RangeArguments_SpillElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(12)),
            (2, 1, new NumberValue(10)));

        AssertColumn(_eval.Evaluate("=BITAND(A1:A2,10)", sheet), new NumberValue(8), new NumberValue(10));
        AssertColumn(_eval.Evaluate("=BITOR(A1:A2,5)", sheet), new NumberValue(13), new NumberValue(15));
        AssertColumn(_eval.Evaluate("=BITXOR(A1:A2,5)", sheet), new NumberValue(9), new NumberValue(15));
        AssertColumn(_eval.Evaluate("=BITLSHIFT(A1:A2,1)", sheet), new NumberValue(24), new NumberValue(20));
        AssertColumn(_eval.Evaluate("=BITRSHIFT(A1:A2,1)", sheet), new NumberValue(6), new NumberValue(5));

        var shifts = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(2)));
        AssertColumn(_eval.Evaluate("=BITAND(12,A1:A2)", shifts), new NumberValue(0), new NumberValue(0));
        AssertColumn(_eval.Evaluate("=BITLSHIFT(4,A1:A2)", shifts), new NumberValue(8), new NumberValue(16));
        AssertColumn(_eval.Evaluate("=BITRSHIFT(16,A1:A2)", shifts), new NumberValue(8), new NumberValue(4));
    }

    [Theory]
    [InlineData("=BITLSHIFT(1,2.9)", 4)]
    [InlineData("=BITRSHIFT(8,1.9)", 4)]
    public void BitShiftFunctions_TruncateFractionalShiftAmount(string formula, double expected)
    {
        _eval.Evaluate(formula, MakeSheet()).Should().Be(new NumberValue(expected));
    }

    [Theory]
    [InlineData("=BITAND(-1,1)")]
    [InlineData("=BITOR(1.5,1)")]
    [InlineData("=BITLSHIFT(1,54)")]
    [InlineData("=BITLSHIFT(281474976710655,1)")]
    [InlineData("=BITRSHIFT(281474976710656,1)")]
    public void BitFunctions_InvalidArguments_ReturnNum(string formula)
    {
        _eval.Evaluate(formula, MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Theory]
    [InlineData("=BIN2DEC(NA())")]
    [InlineData("=DEC2BIN(NA())")]
    [InlineData("=BITAND(NA(),1)")]
    [InlineData("=BITOR(1,NA())")]
    [InlineData("=BITLSHIFT(NA(),1)")]
    [InlineData("=BITRSHIFT(1,NA())")]
    [InlineData("=DEC2BIN(10,NA())")]
    [InlineData("=DEC2HEX(255,NA())")]
    [InlineData("=DEC2OCT(15,NA())")]
    [InlineData("=BIN2HEX(\"1010\",NA())")]
    [InlineData("=HEX2BIN(\"F\",NA())")]
    [InlineData("=OCT2HEX(\"17\",NA())")]
    public void EngineeringFunctions_PropagateExcelErrors(string formula)
    {
        _eval.Evaluate(formula, MakeSheet()).Should().Be(ErrorValue.NA);
    }

    private static void AssertColumn(ScalarValue value, params ScalarValue[] expected)
    {
        var range = value.Should().BeOfType<RangeValue>().Subject;
        range.RowCount.Should().Be(expected.Length);
        range.ColCount.Should().Be(1);
        for (int row = 0; row < expected.Length; row++)
            range.Cells[row, 0].Should().Be(expected[row]);
    }

    private static Sheet MakeSheet(params (uint Row, uint Col, ScalarValue Value)[] values)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (row, col, value) in values)
            sheet.SetCell(new CellAddress(sheet.Id, row, col), value);
        return sheet;
    }
}
