using System.Globalization;

namespace FreeW.Core.Model.Tests;

public class DateTimeFormatsTests
{
    // A fixed moment so the formatted strings are deterministic regardless of when the test runs.
    private static readonly DateTime Moment = new(2026, 6, 17, 14, 5, 9);
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    [Fact]
    public void Build_ReturnsExpectedLabelsInOrder()
    {
        var formats = DateTimeFormats.Build(Moment, Invariant);

        formats.Select(f => f.Label).Should().Equal(
            "Short date", "Long date", "Short time", "Long time", "Date and time");
    }

    [Fact]
    public void Build_FormatsEachOptionForTheGivenMoment()
    {
        var formats = DateTimeFormats.Build(Moment, Invariant);

        // Standard format specifiers under the invariant culture.
        formats[0].Text.Should().Be(Moment.ToString("d", Invariant)); // short date: 06/17/2026
        formats[1].Text.Should().Be(Moment.ToString("D", Invariant)); // long date
        formats[2].Text.Should().Be(Moment.ToString("t", Invariant)); // short time
        formats[3].Text.Should().Be(Moment.ToString("T", Invariant)); // long time
        formats[4].Text.Should().Be(Moment.ToString("f", Invariant)); // date + short time
    }

    [Fact]
    public void Build_ShortDate_MatchesExpectedInvariantString()
    {
        var formats = DateTimeFormats.Build(Moment, Invariant);

        formats[0].Text.Should().Be("06/17/2026");
    }

    [Fact]
    public void Build_DefaultsToCurrentCultureWhenNoneSupplied()
    {
        var expected = Moment.ToString("d", CultureInfo.CurrentCulture);

        var formats = DateTimeFormats.Build(Moment);

        formats[0].Text.Should().Be(expected);
    }
}
