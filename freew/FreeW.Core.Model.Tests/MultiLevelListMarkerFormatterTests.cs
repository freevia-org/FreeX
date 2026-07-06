namespace FreeW.Core.Model.Tests;

public sealed class MultiLevelListMarkerFormatterTests
{
    [Fact]
    public void MarkerSequence_UsesDecimalByDefault()
    {
        var markers = MultiLevelListMarkerFormatter.MarkerSequence([0, 1, 2, 1, 0]);

        markers.Should().Equal("1.", "1.1.", "1.1.1.", "1.2.", "2.");
    }

    [Fact]
    public void MarkerSequence_UsesPerLevelNumberFormats()
    {
        var markers = MultiLevelListMarkerFormatter.MarkerSequence(
            [0, 1, 2, 1, 0],
            MultiLevelListFormat.DecimalLowerLetterLowerRomanNumberFormats);

        markers.Should().Equal("1.", "1.a.", "1.a.i.", "1.b.", "2.");
    }

    [Fact]
    public void FormatNumber_SupportsLettersAndRomans()
    {
        MultiLevelListMarkerFormatter.FormatNumber(27, ListNumberFormat.LowerLetter).Should().Be("aa");
        MultiLevelListMarkerFormatter.FormatNumber(4, ListNumberFormat.UpperRoman).Should().Be("IV");
        MultiLevelListMarkerFormatter.ToOoxmlToken(ListNumberFormat.LowerRoman).Should().Be("lowerRoman");
        MultiLevelListMarkerFormatter.FromOoxmlToken("upperLetter").Should().Be(ListNumberFormat.UpperLetter);
    }
}
