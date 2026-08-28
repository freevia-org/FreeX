namespace FreeW.Core.Model.Tests;

/// <summary>
/// r164 remediation, unbounded declared quantity. The FreeX half of this round fixed commands that
/// expanded an unbounded SELECTION; in FreeW the same shape arrives as an unbounded declared COUNT.
/// Both numeral schemes here emit one symbol per unit once they run out of larger symbols, so their
/// output grows linearly with the value -- and a .docx declares the starting note number through
/// <c>w:numStart</c>, which DocxReader accepts as long as it is >= 1.
///
/// Measured before the fix: <c>Chicago(2_000_000_000)</c> built a 500,000,000-character string (1 GB
/// of UTF-16) in 2.6s, and <c>UpperRoman(2_000_000_000)</c> a 2,000,000-character one. Three other
/// copies of this helper in the suite (ComplexFieldEngine, HtmlFileAdapter's ToRoman, and FreeP's
/// PresentationListMarkerPlanner) already clamped at 3999; these two never got it.
/// </summary>
public sealed class R164_NoteNumeralGrowthTests
{
    [Theory]
    [InlineData(NoteNumberFormat.Chicago)]
    [InlineData(NoteNumberFormat.UpperRoman)]
    [InlineData(NoteNumberFormat.LowerRoman)]
    public void Format_AbsurdStartingNumber_DegradesToDecimalInsteadOfGrowingUnbounded(NoteNumberFormat format)
    {
        var formatted = NoteNumberFormatter.Format(2_000_000_000, format);

        formatted.Should().Be("2000000000");
    }

    [Theory]
    [InlineData(1, "*")]
    [InlineData(2, "†")]
    [InlineData(5, "**")]
    [InlineData(9, "***")]
    public void Format_OrdinaryChicagoNotes_AreUnchanged(int value, string expected)
    {
        // Sibling/no-regression: the cap sits far above any realistic note count, so ordinary
        // documents still get their symbolic numeral.
        NoteNumberFormatter.Format(value, NoteNumberFormat.Chicago).Should().Be(expected);
    }

    [Theory]
    [InlineData(4, "IV")]
    [InlineData(1987, "MCMLXXXVII")]
    [InlineData(3999, "MMMCMXCIX")]
    public void Format_OrdinaryRomanNotes_AreUnchanged(int value, string expected)
    {
        NoteNumberFormatter.Format(value, NoteNumberFormat.UpperRoman).Should().Be(expected);
    }
}
