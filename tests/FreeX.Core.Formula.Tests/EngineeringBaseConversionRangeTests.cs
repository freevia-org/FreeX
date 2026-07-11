using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regression coverage for R27-engineering-functions-deep-2: OCT2BIN/HEX2BIN/HEX2OCT (the
/// BaseToBase family) must reject a parsed source value that doesn't fit the TARGET base's
/// positive representable range, matching the bound already enforced by the DEC2xxx family.
/// </summary>
public sealed class EngineeringBaseConversionRangeTests
{
    private readonly FormulaEvaluator _eval = new();

    [Theory]
    // Bug case: value fits the source base's width but overflows the target's positive range.
    [InlineData("=OCT2BIN(\"1000\")")]   // octal 1000 = 512, bin max positive is 511
    [InlineData("=HEX2BIN(\"7FF\")")]    // hex 7FF = 2047, bin max positive is 511
    [InlineData("=HEX2OCT(\"7FFFFFFF\")")] // hex 7FFFFFFF = 2147483647, oct max positive is 536870911
    public void BaseToBaseFunctions_TargetOverflow_ReturnsNum(string formula)
    {
        _eval.Evaluate(formula, MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Theory]
    // Sibling already-working cases: value fits both the source and the target range.
    [InlineData("=OCT2BIN(\"17\")", "1111")]
    [InlineData("=HEX2BIN(\"F\")", "1111")]
    [InlineData("=HEX2OCT(\"F\")", "17")]
    [InlineData("=BIN2HEX(\"1010\")", "A")]
    [InlineData("=BIN2OCT(\"1010\")", "12")]
    [InlineData("=OCT2HEX(\"17\")", "F")]
    // Negative (two's-complement) source values must still round-trip unaffected by the new bound.
    [InlineData("=OCT2BIN(\"7777777777\")", "1111111111")]
    [InlineData("=HEX2BIN(\"FFFFFFFFFF\")", "1111111111")]
    public void BaseToBaseFunctions_InRangeValues_StillConvert(string formula, string expected)
    {
        _eval.Evaluate(formula, MakeSheet()).Should().Be(new TextValue(expected));
    }

    private static Sheet MakeSheet(params (uint Row, uint Col, ScalarValue Value)[] values)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (row, col, value) in values)
            sheet.SetCell(new CellAddress(sheet.Id, row, col), value);
        return sheet;
    }
}
