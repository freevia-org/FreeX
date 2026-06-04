using FluentAssertions;
using FreeX.Core.Commands;

namespace FreeX.Core.Model.Tests;

public sealed partial class FlashFillServiceTests
{
    [Fact]
    public void Fill_EmbeddedTimeExtraction_ExtractsLabeledTwelveHourTime()
    {
        var result = FlashFillService.Fill(
            [
                ("Start: 9:15 AM", "9:15 AM"),
                ("Start: 8:05 PM", "8:05 PM")
            ],
            ["Start: 10:45 PM"]);

        result.Should().BeEquivalentTo(["10:45 PM"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_EmbeddedTimeExtraction_ExtractsTwentyFourHourTime()
    {
        var result = FlashFillService.Fill(
            [
                ("Run at 14:05", "14:05"),
                ("Run at 16:40", "16:40")
            ],
            ["Run at 09:30"]);

        result.Should().BeEquivalentTo(["09:30"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_EmbeddedTimeExtraction_PreservesSeconds()
    {
        var result = FlashFillService.Fill(
            [
                ("Finished 08:15:30", "08:15:30"),
                ("Finished 14:05:09", "14:05:09")
            ],
            ["Finished 23:59:58"]);

        result.Should().BeEquivalentTo(["23:59:58"], o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData("Run at 09:30 and 10:45")]
    [InlineData("Run at soon")]
    public void Fill_EmbeddedTimeExtraction_ReturnsNullForAmbiguousOrNonTimeRows(string remaining)
    {
        var result = FlashFillService.Fill(
            [
                ("Run at 14:05", "14:05"),
                ("Run at 16:40", "16:40")
            ],
            [remaining]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_EmbeddedTimeExtraction_ReturnsNullWhenExamplesContainMultipleTimes()
    {
        var result = FlashFillService.Fill(
            [
                ("Window 14:05 to 16:40", "14:05"),
                ("Window 10:00 to 12:00", "10:00")
            ],
            ["Window 09:30 to 10:45"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_EmbeddedTimeExtraction_DoesNotStealPhoneNormalization()
    {
        var result = FlashFillService.Fill(
            [
                ("425.555.0101", "(425) 555-0101"),
                ("206-555-0199", "(206) 555-0199")
            ],
            ["360 555 0142"]);

        result.Should().BeEquivalentTo(["(360) 555-0142"], o => o.WithStrictOrdering());
    }
}
