using System.Globalization;

namespace FreeW.Core.Model.Tests;

public sealed class WordFieldDateTimeFormatterTests
{
    private static readonly DateTime Moment = new(2026, 8, 6, 14, 5, 9);

    [Theory]
    [InlineData(" DATE \\@ \"MMMM d, yyyy\" ", "August 6, 2026")]
    [InlineData(" TIME \\@ \"h:mm AM/PM\" ", "2:05 PM")]
    [InlineData(" TIME \\@ \"m\" ", "5")]
    public void TryFormat_AppliesCalibratedWordPicture(string instruction, string expected)
    {
        WordFieldDateTimeFormatter.TryFormat(
                Moment,
                instruction,
                CultureInfo.GetCultureInfo("en-US"),
                out var result)
            .Should().BeTrue();
        result.Should().Be(expected);
    }

    [Fact]
    public void TryParseAndFormat_UsesRequestedCultureForParsingAndNames()
    {
        WordFieldDateTimeFormatter.TryParseAndFormat(
                "06.08.2026",
                " DATE \\@ \"dddd, d. MMMM yyyy\" ",
                CultureInfo.GetCultureInfo("de-DE"),
                out var result)
            .Should().BeTrue();
        result.Should().Be("Donnerstag, 6. August 2026");
    }

    [Theory]
    [InlineData(" DATE ")]
    [InlineData(" DATE \\@ \"broken token\" ")]
    public void TryFormat_MissingOrMalformedPictureDoesNotInventResult(string instruction)
    {
        WordFieldDateTimeFormatter.TryFormat(
                Moment,
                instruction,
                CultureInfo.InvariantCulture,
                out var result)
            .Should().BeFalse();
        result.Should().BeEmpty();
    }
}
