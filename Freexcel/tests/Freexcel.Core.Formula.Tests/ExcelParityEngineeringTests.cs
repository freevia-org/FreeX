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

    private static Sheet MakeSheet() => new(SheetId.New(), "S");
}
