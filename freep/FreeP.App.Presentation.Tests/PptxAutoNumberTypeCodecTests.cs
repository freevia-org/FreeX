using FreeP.Core.IO;

namespace FreeP.App.Compositor.Tests;

public sealed class PptxAutoNumberTypeCodecTests
{
    [Theory]
    [InlineData(AutoNumType.ArabicPeriod, "arabicPeriod")]
    [InlineData(AutoNumType.ArabicParenR, "arabicParenR")]
    [InlineData(AutoNumType.ArabicParenBoth, "arabicParenBoth")]
    [InlineData(AutoNumType.RomanUcPeriod, "romanUcPeriod")]
    [InlineData(AutoNumType.RomanLcPeriod, "romanLcPeriod")]
    [InlineData(AutoNumType.RomanUcParenR, "romanUcParenR")]
    [InlineData(AutoNumType.RomanLcParenR, "romanLcParenR")]
    [InlineData(AutoNumType.AlphaUcPeriod, "alphaUcPeriod")]
    [InlineData(AutoNumType.AlphaLcPeriod, "alphaLcPeriod")]
    [InlineData(AutoNumType.AlphaUcParenR, "alphaUcParenR")]
    [InlineData(AutoNumType.AlphaLcParenR, "alphaLcParenR")]
    [InlineData(AutoNumType.AlphaUcParenBoth, "alphaUcParenBoth")]
    [InlineData(AutoNumType.AlphaLcParenBoth, "alphaLcParenBoth")]
    public void KnownTypes_RoundTripThroughTheirExactDrawingMlTokens(AutoNumType type, string token)
    {
        PptxAutoNumberTypeCodec.Format(type).Should().Be(token);
        PptxAutoNumberTypeCodec.Parse(token).Should().Be(type);
    }

    [Fact]
    public void EveryDefinedType_HasAUniqueRoundTripToken()
    {
        var types = Enum.GetValues<AutoNumType>();
        var tokens = types.Select(PptxAutoNumberTypeCodec.Format).ToArray();

        tokens.Should().OnlyHaveUniqueItems();
        types.Zip(tokens).Should().OnlyContain(pair =>
            PptxAutoNumberTypeCodec.Parse(pair.Second) == pair.First);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("ARABICPARENR")]
    [InlineData(" arabicParenR ")]
    public void Parse_UnknownOrNonExactTokens_FallBackToArabicPeriod(string? token) =>
        PptxAutoNumberTypeCodec.Parse(token).Should().Be(AutoNumType.ArabicPeriod);

    [Theory]
    [InlineData(-1)]
    [InlineData(13)]
    [InlineData(int.MaxValue)]
    public void Format_UndefinedValues_FallBackToArabicPeriod(int value) =>
        PptxAutoNumberTypeCodec.Format((AutoNumType)value).Should().Be("arabicPeriod");

    [Fact]
    public void PackageReaderAndWriter_UseTheCanonicalCodecAtEveryAutoNumberTokenSite()
    {
        var reader = TestWorkspaceFileLocator.ReadAllText(
            "freep", "FreeP.Core.IO", "PptxPackageReader.cs");
        var writer = TestWorkspaceFileLocator.ReadAllText(
            "freep", "FreeP.Core.IO", "PptxPackageWriter.cs");

        CountOccurrences(reader, "PptxAutoNumberTypeCodec.Parse(").Should().Be(2);
        CountOccurrences(writer, "PptxAutoNumberTypeCodec.Format(").Should().Be(2);
        reader.Should().NotContain("ParseAutoNumType");
        writer.Should().NotContain("AutoNumType.ArabicParenR");
    }

    private static int CountOccurrences(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;
}
