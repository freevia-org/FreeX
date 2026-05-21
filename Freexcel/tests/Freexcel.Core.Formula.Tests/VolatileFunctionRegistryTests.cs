using Freexcel.Core.Formula;
using FluentAssertions;

namespace Freexcel.Core.Formula.Tests;

public sealed class VolatileFunctionRegistryTests
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
    public void IsVolatile_ReturnsTrueForExcelVolatileFunctions(string functionName)
    {
        BuiltInFunctions.IsVolatile(functionName).Should().BeTrue();
    }

    [Theory]
    [InlineData("SUM")]
    [InlineData("XLOOKUP")]
    [InlineData("SEQUENCE")]
    [InlineData("FILTER")]
    [InlineData("SORT")]
    [InlineData("LET")]
    [InlineData("LAMBDA")]
    public void IsVolatile_ReturnsFalseForNonVolatileFunctions(string functionName)
    {
        BuiltInFunctions.IsVolatile(functionName).Should().BeFalse();
    }
}
