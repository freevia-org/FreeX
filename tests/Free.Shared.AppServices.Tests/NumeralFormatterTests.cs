using Free.Shared.IO;

namespace Free.Shared.AppServices.Tests;

/// <summary>
/// <see cref="NumeralFormatter"/> is the single owner of the roman and alphabetic numbering schemes
/// that seven FreeW/FreeP copies used to carry. These pin the boundaries every copy relied on.
/// </summary>
public sealed class NumeralFormatterTests
{
    [Theory]
    [InlineData(1, "I")]
    [InlineData(4, "IV")]
    [InlineData(9, "IX")]
    [InlineData(14, "XIV")]
    [InlineData(40, "XL")]
    [InlineData(90, "XC")]
    [InlineData(400, "CD")]
    [InlineData(1994, "MCMXCIV")]
    [InlineData(2024, "MMXXIV")]
    [InlineData(3999, "MMMCMXCIX")]
    public void TryToRoman_FormatsTheClassicRange(int value, string expected)
    {
        NumeralFormatter.TryToRoman(value, upper: true, out var upper).Should().BeTrue();
        upper.Should().Be(expected);

        NumeralFormatter.TryToRoman(value, upper: false, out var lower).Should().BeTrue();
        lower.Should().Be(expected.ToLowerInvariant());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(4000)]
    [InlineData(32767)]
    [InlineData(int.MaxValue)]
    public void TryToRoman_RejectsOutsideOneToThreeThousandNineHundredNinetyNine(int value)
    {
        NumeralFormatter.TryToRoman(value, upper: true, out var result).Should().BeFalse();
        result.Should().BeEmpty();
    }

    [Theory]
    [InlineData(3999, "MMMCMXCIX")]
    [InlineData(4000, "MMMM")]
    [InlineData(4999, "MMMMCMXCIX")]
    public void TryToExtendedRoman_RepeatsMPerThousandUpToTheWordLimit(int value, string expected)
    {
        NumeralFormatter.TryToExtendedRoman(value, upper: true, out var result).Should().BeTrue();
        result.Should().Be(expected);
        NumeralFormatter.TryToExtendedRoman(value, upper: false, out var lower).Should().BeTrue();
        lower.Should().Be(expected.ToLowerInvariant());
    }

    [Fact]
    public void TryToExtendedRoman_AcceptsTheWordMaximumOfThirtyTwoThousandSevenHundredSixtySeven()
    {
        NumeralFormatter.TryToExtendedRoman(32767, upper: true, out var result).Should().BeTrue();
        result.Should().Be(new string('M', 32) + "DCCLXVII");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(32768)]
    [InlineData(int.MaxValue)]
    public void TryToExtendedRoman_RejectsOutsideItsExplicitBound(int value)
    {
        NumeralFormatter.TryToExtendedRoman(value, upper: true, out var result).Should().BeFalse();
        result.Should().BeEmpty();
    }

    [Theory]
    [InlineData(1, "a")]
    [InlineData(26, "z")]
    [InlineData(27, "aa")]
    [InlineData(28, "ab")]
    [InlineData(52, "az")]
    [InlineData(53, "ba")]
    [InlineData(702, "zz")]
    [InlineData(703, "aaa")]
    public void TryToBijectiveLetters_IsBaseTwentySixWithoutAZeroDigit(int value, string expected)
    {
        NumeralFormatter.TryToBijectiveLetters(value, upper: false, out var lower).Should().BeTrue();
        lower.Should().Be(expected);
        NumeralFormatter.TryToBijectiveLetters(value, upper: true, out var upper).Should().BeTrue();
        upper.Should().Be(expected.ToUpperInvariant());
    }

    [Fact]
    public void TryToBijectiveLetters_IsBoundedForTheLargestInt()
    {
        NumeralFormatter.TryToBijectiveLetters(int.MaxValue, upper: true, out var result).Should().BeTrue();
        result.Should().Be("FXSHRXW");
        result.Length.Should().BeLessThanOrEqualTo(7);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void TryToBijectiveLetters_RejectsZeroAndNegatives(int value)
    {
        NumeralFormatter.TryToBijectiveLetters(value, upper: false, out var result).Should().BeFalse();
        result.Should().BeEmpty();
    }

    [Theory]
    [InlineData(1, "a")]
    [InlineData(26, "z")]
    [InlineData(27, "aa")]
    [InlineData(28, "bb")]
    [InlineData(52, "zz")]
    [InlineData(53, "aaa")]
    public void TryToRepeatedLetters_RepeatsOneLetterPerCycle(int value, string expected)
    {
        NumeralFormatter.TryToRepeatedLetters(value, upper: false, out var lower).Should().BeTrue();
        lower.Should().Be(expected);
        NumeralFormatter.TryToRepeatedLetters(value, upper: true, out var upper).Should().BeTrue();
        upper.Should().Be(expected.ToUpperInvariant());
    }

    [Theory]
    [InlineData(702, 27)]
    [InlineData(780, 30)]
    public void TryToRepeatedLetters_TopOfTheRangeRepeatsZ(int value, int repeats)
    {
        NumeralFormatter.TryToRepeatedLetters(value, upper: false, out var result).Should().BeTrue();
        result.Should().Be(new string('z', repeats));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(781)]
    [InlineData(int.MaxValue)]
    public void TryToRepeatedLetters_RejectsOutsideItsExplicitBound(int value)
    {
        NumeralFormatter.TryToRepeatedLetters(value, upper: false, out var result).Should().BeFalse();
        result.Should().BeEmpty();
    }

    [Fact]
    public void TheTwoLetterSchemesAreDistinct()
    {
        NumeralFormatter.TryToBijectiveLetters(28, upper: false, out var bijective);
        NumeralFormatter.TryToRepeatedLetters(28, upper: false, out var repeated);

        bijective.Should().Be("ab");
        repeated.Should().Be("bb");
    }
}
