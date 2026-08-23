using FluentAssertions;
using FreeX.Core.Formula;

namespace FreeX.Core.Calc.Tests;

public sealed class FormulaVolatilityPolicyTests
{
    [Theory]
    [InlineData("NOW")]
    [InlineData("TODAY")]
    [InlineData("RAND")]
    [InlineData("RANDBETWEEN")]
    [InlineData("RANDARRAY")]
    [InlineData("INDIRECT")]
    [InlineData("OFFSET")]
    [InlineData("CELL")]
    [InlineData("INFO")]
    public void VolatileCatalog_RemainsExact(string name) =>
        FormulaVolatilityPolicy.IsVolatileFunctionName(name).Should().BeTrue();

    [Theory]
    [InlineData("CELL", "width")]
    [InlineData("CELL", " WIDTH ")]
    [InlineData("INFO", "directory")]
    [InlineData("INFO", "NUMFILE")]
    [InlineData("INFO", "origin")]
    [InlineData("INFO", "osversion")]
    [InlineData("INFO", "recalc")]
    [InlineData("INFO", "release")]
    [InlineData("INFO", "system")]
    public void ConstantCellAndInfoExceptions_AreNotVolatile(string name, string argument)
    {
        var call = new FunctionCallNode(name, [new StringNode(argument)]);
        FormulaVolatilityPolicy.IsVolatileCall(call).Should().BeFalse();
        FormulaVolatilityPolicy.IsCurrentCellSensitiveCall(call).Should().BeFalse();
    }

    [Fact]
    public void DynamicExceptionsAndZeroArgumentRowColumnRemainSensitive()
    {
        FormulaVolatilityPolicy.IsVolatileCall(new FunctionCallNode("CELL", [new CellRefNode("A", 1)]))
            .Should().BeTrue();
        FormulaVolatilityPolicy.IsVolatileCall(new FunctionCallNode("INFO", [])).Should().BeTrue();
        FormulaVolatilityPolicy.IsCurrentCellSensitiveCall(new FunctionCallNode("ROW", [])).Should().BeTrue();
        FormulaVolatilityPolicy.IsCurrentCellSensitiveCall(new FunctionCallNode("COLUMN", [new NumberNode(1)]))
            .Should().BeFalse();
        FormulaVolatilityPolicy.IsVolatileFunctionName("now").Should().BeFalse();
    }

    [Fact]
    public void CalcConsumers_UseSharedVolatilityPolicy()
    {
        CalcSourceTestSupport.ReadCalcSource("RecalcEngine.cs").Should().Contain("FormulaVolatilityPolicy.IsVolatileCall(");
        CalcSourceTestSupport.ReadCalcSource("ViewportConditionalFormatEvaluator.Thresholds.cs")
            .Should().Contain("FormulaVolatilityPolicy.IsCurrentCellSensitiveCall(");
    }
}
