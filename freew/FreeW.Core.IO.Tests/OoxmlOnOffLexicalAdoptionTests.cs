namespace FreeW.Core.IO.Tests;

public sealed class OoxmlOnOffLexicalAdoptionTests
{
    [Theory]
    [InlineData(null, false, false)]
    [InlineData(null, true, true)]
    [InlineData("1", false, true)]
    [InlineData("true", false, true)]
    [InlineData("on", false, true)]
    [InlineData("0", true, false)]
    [InlineData("false", true, false)]
    [InlineData("off", true, false)]
    [InlineData("bogus", true, false)]
    [InlineData("TRUE", true, false)]
    [InlineData(" true ", true, false)]
    public void WordprocessingReaderPreservesAbsentAndInvalidDefaults(
        string? value,
        bool defaultValue,
        bool expected) =>
        Ooxml.ReadOnOffValue(value, defaultValue).Should().Be(expected);
}
