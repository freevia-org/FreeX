namespace FreeW.Core.Model.Tests;

public sealed class NoteNumberFormatterTests
{
    [Theory]
    [InlineData(0, NoteNumberFormat.Decimal, "1")]
    [InlineData(49, NoteNumberFormat.LowerRoman, "xlix")]
    [InlineData(944, NoteNumberFormat.UpperRoman, "CMXLIV")]
    [InlineData(26, NoteNumberFormat.LowerLetter, "z")]
    [InlineData(27, NoteNumberFormat.LowerLetter, "aa")]
    [InlineData(53, NoteNumberFormat.UpperLetter, "BA")]
    public void FormatUsesOneNormalizedDecimalRomanAndLetterContract(
        int value,
        NoteNumberFormat format,
        string expected)
    {
        NoteNumberFormatter.Format(value, format).Should().Be(expected);
    }

    [Theory]
    [InlineData(1, "*")]
    [InlineData(2, "\u2020")]
    [InlineData(3, "\u2021")]
    [InlineData(4, "\u00A7")]
    [InlineData(5, "**")]
    [InlineData(8, "\u00A7\u00A7")]
    [InlineData(9, "***")]
    public void FormatUsesTheDocumentedChicagoSymbolCycle(int value, string expected)
    {
        NoteNumberFormatter.Format(value, NoteNumberFormat.Chicago).Should().Be(expected);
    }
}
