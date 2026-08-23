using Free.Shared.Opc;
using FluentAssertions;

namespace FreeX.Core.IO.Tests;

public sealed class OoxmlOnOffLexicalTests
{
    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("on", true)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("off", false)]
    public void ParseRecognizesTheSixOoxmlTokens(string value, bool expected) =>
        OoxmlOnOffLexical.Parse(value, absentDefault: !expected, invalidDefault: !expected)
            .Should()
            .Be(expected);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ParsePreservesCallerOwnedAbsentAndInvalidDefaults(bool fallback)
    {
        OoxmlOnOffLexical.Parse(null, absentDefault: fallback, invalidDefault: !fallback)
            .Should()
            .Be(fallback);
        OoxmlOnOffLexical.Parse("bogus", absentDefault: !fallback, invalidDefault: fallback)
            .Should()
            .Be(fallback);
    }
}
