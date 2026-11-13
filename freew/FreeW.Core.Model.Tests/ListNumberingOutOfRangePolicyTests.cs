namespace FreeW.Core.Model.Tests;

/// <summary>
/// The roman/alphabetic conversion is shared (<c>Free.Shared.IO.NumeralFormatter</c>); each FreeW
/// caller keeps its own out-of-range policy. These pin that policy per caller, including the
/// multilevel-list guard that used to be missing (an uncapped roman numeral grows one "M" per
/// thousand, so a huge <c>w:start</c> built a multi-megabyte list marker).
/// </summary>
public sealed class ListNumberingOutOfRangePolicyTests
{
    [Theory]
    [InlineData(3999, "MMMCMXCIX", "mmmcmxcix")]
    [InlineData(4000, "4000", "4000")]
    [InlineData(2_000_000_000, "2000000000", "2000000000")]
    public void MultiLevelList_RomanFallsBackToTheDecimalNumberPastThreeThousandNineHundredNinetyNine(
        int value, string upper, string lower)
    {
        MultiLevelListMarkerFormatter.FormatNumber(value, ListNumberFormat.UpperRoman).Should().Be(upper);
        MultiLevelListMarkerFormatter.FormatNumber(value, ListNumberFormat.LowerRoman).Should().Be(lower);
    }

    [Fact]
    public void MultiLevelList_AHugeStartDoesNotBuildAHugeMarker()
    {
        var marker = MultiLevelListMarkerFormatter.FormatSingleLevelMarker(
            "%1.", int.MaxValue, ListNumberFormat.UpperRoman);

        marker.Length.Should().BeLessThan(32);
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(-3, "-3")]
    public void MultiLevelList_ZeroAndNegativeStayDecimalForEveryFormat(int value, string expected)
    {
        foreach (var format in Enum.GetValues<ListNumberFormat>())
            MultiLevelListMarkerFormatter.FormatNumber(value, format).Should().Be(expected);
    }

    [Theory]
    [InlineData("\\* ROMAN", 1999, "MCMXCIX")]
    [InlineData("\\* roman", 1999, "mcmxcix")]
    [InlineData("\\* ROMAN", 0, "0")]
    [InlineData("\\* ROMAN", -4, "-4")]
    [InlineData("\\* ROMAN", 4000, "4000")]
    [InlineData("\\* ALPHABETIC", 27, "AA")]
    [InlineData("\\* alphabetic", 28, "ab")]
    [InlineData("\\* ALPHABETIC", 0, "0")]
    [InlineData("\\* alphabetic", -1, "-1")]
    public void FieldIntegerPictures_KeepTheirDecimalFallbackOutsideTheRange(string picture, int value, string expected)
    {
        ComplexFieldEngine.FormatIntegerFieldValue(value, "SEQ Figure " + picture).Should().Be(expected);
    }

    [Theory]
    [InlineData(NoteNumberFormat.UpperRoman, 3999, "MMMCMXCIX")]
    [InlineData(NoteNumberFormat.UpperRoman, 4000, "4000")]
    [InlineData(NoteNumberFormat.LowerRoman, 0, "i")]
    [InlineData(NoteNumberFormat.LowerLetter, 703, "aaa")]
    [InlineData(NoteNumberFormat.UpperLetter, -9, "A")]
    [InlineData(NoteNumberFormat.UpperLetter, 52, "AZ")]
    public void NoteNumbers_KeepTheirClampToOneAndDecimalFallback(NoteNumberFormat format, int value, string expected)
    {
        NoteNumberFormatter.Format(value, format).Should().Be(expected);
    }
}
