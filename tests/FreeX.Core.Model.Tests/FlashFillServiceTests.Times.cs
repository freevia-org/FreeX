using FluentAssertions;
using FreeX.Core.Commands;

namespace FreeX.Core.Model.Tests;

public sealed partial class FlashFillServiceTests
{
    [Fact]
    public void Fill_TimeComponentExtraction_ExtractsDisplayedHourWithoutPadding()
    {
        var result = FlashFillService.Fill(
            [
                ("9:15 AM", "9"),
                ("08:05 pm", "8")
            ],
            ["07:30:09", "14:05", "12:00 AM"]);

        result.Should().BeEquivalentTo(["7", "14", "12"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_TimeComponentExtraction_ExtractsMinute()
    {
        var result = FlashFillService.Fill(
            [
                ("9:15 AM", "15"),
                ("08:05 pm", "05")
            ],
            ["14:07", "07:30:09"]);

        result.Should().BeEquivalentTo(["07", "30"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_TimeComponentExtraction_ExtractsSecond()
    {
        var result = FlashFillService.Fill(
            [
                ("07:30:09", "09"),
                ("14:05:58", "58")
            ],
            ["23:59:01"]);

        result.Should().BeEquivalentTo(["01"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_TimeComponentExtraction_ExtractsMeridiemWithRemainingRowCasing()
    {
        var result = FlashFillService.Fill(
            [
                ("9:15 AM", "AM"),
                ("10:05 AM", "AM")
            ],
            ["08:05 pm", "7:30 Pm"]);

        result.Should().BeEquivalentTo(["pm", "Pm"], o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData("24:05")]
    [InlineData("9:60 AM")]
    public void Fill_TimeComponentExtraction_ReturnsNullForInvalidRemainingRows(string remaining)
    {
        var result = FlashFillService.Fill(
            [
                ("9:15 AM", "9"),
                ("08:05 pm", "8")
            ],
            [remaining]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_TimeComponentExtraction_ReturnsNullWhenSecondIsMissingFromRemainingRow()
    {
        var result = FlashFillService.Fill(
            [
                ("07:30:09", "09"),
                ("14:05:58", "58")
            ],
            ["23:59"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_TimeComponentExtraction_ReturnsNullForAmbiguousComponentExamples()
    {
        var result = FlashFillService.Fill(
            [
                ("08:08 pm", "08"),
                ("09:09 AM", "09")
            ],
            ["10:10 AM"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_TimeComponentExtraction_ReturnsNullForMixedComponentExamples()
    {
        var result = FlashFillService.Fill(
            [
                ("9:15 AM", "9"),
                ("08:05 pm", "05")
            ],
            ["10:45 PM"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_EmbeddedTimeComponentExtraction_ExtractsDisplayedHour()
    {
        var result = FlashFillService.Fill(
            [
                ("Start: 9:15 AM", "9"),
                ("End 07:30 PM", "07")
            ],
            ["Due 08:05 pm", "Run at 14:05"]);

        result.Should().BeEquivalentTo(["08", "14"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_EmbeddedTimeComponentExtraction_ExtractsMinute()
    {
        var result = FlashFillService.Fill(
            [
                ("Start: 9:15 AM", "15"),
                ("Due 08:05 pm", "05")
            ],
            ["Run at 14:07", "Finished 07:30:09"]);

        result.Should().BeEquivalentTo(["07", "30"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_EmbeddedTimeComponentExtraction_ExtractsSecond()
    {
        var result = FlashFillService.Fill(
            [
                ("Finished 07:30:09", "09"),
                ("Closed 14:05:58", "58")
            ],
            ["Done 23:59:01"]);

        result.Should().BeEquivalentTo(["01"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_EmbeddedTimeComponentExtraction_ExtractsMeridiemWithRemainingRowCasing()
    {
        var result = FlashFillService.Fill(
            [
                ("Start: 9:15 AM", "AM"),
                ("Opens 10:05 AM", "AM")
            ],
            ["Due 08:05 pm", "Window closes 7:30 Pm"]);

        result.Should().BeEquivalentTo(["pm", "Pm"], o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData("Run at 24:05")]
    [InlineData("Run at 9:60 AM")]
    [InlineData("Run at soon")]
    [InlineData("Run at 09:30 and 10:45")]
    public void Fill_EmbeddedTimeComponentExtraction_ReturnsNullForInvalidAmbiguousOrMissingRemainingRows(
        string remaining)
    {
        var result = FlashFillService.Fill(
            [
                ("Start: 9:15 AM", "9"),
                ("End 07:30 PM", "07")
            ],
            [remaining]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_EmbeddedTimeComponentExtraction_ReturnsNullForAmbiguousComponentExamples()
    {
        var result = FlashFillService.Fill(
            [
                ("Due 08:08 pm", "08"),
                ("Done 09:09 AM", "09")
            ],
            ["Next 10:10 AM"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_EmbeddedTimeComponentExtraction_ReturnsNullForMixedComponentExamples()
    {
        var result = FlashFillService.Fill(
            [
                ("Start: 9:15 AM", "9"),
                ("Due 08:05 pm", "05")
            ],
            ["Next 10:45 PM"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_EmbeddedTimeComponentExtraction_ReturnsNullWhenExamplesContainMultipleTimes()
    {
        var result = FlashFillService.Fill(
            [
                ("Window 14:05 to 16:40", "14"),
                ("Window 10:00 to 12:00", "10")
            ],
            ["Window 09:30 to 10:45"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_EmbeddedTimeRangeEndpointExtraction_ExtractsFirstEndpoint()
    {
        var result = FlashFillService.Fill(
            [
                ("Window 9:15 AM to 10:45 AM", "9:15 AM"),
                ("Shift 08:05 pm - 09:30 pm", "08:05 pm")
            ],
            ["Run 07:30 to 08:45"]);

        result.Should().BeEquivalentTo(["07:30"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_EmbeddedTimeRangeEndpointExtraction_ExtractsSecondEndpointPreservingSourceStyle()
    {
        var result = FlashFillService.Fill(
            [
                ("Window 9:15 AM to 10:45 AM", "10:45 AM"),
                ("Run 07:30 to 08:45", "08:45")
            ],
            ["Shift 08:05 pm - 09:30 Pm"]);

        result.Should().BeEquivalentTo(["09:30 Pm"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_EmbeddedTimeRangeEndpointExtraction_PreservesSelectedEndpointSeconds()
    {
        var result = FlashFillService.Fill(
            [
                ("Window 9:15 AM to 10:45 AM", "9:15 AM"),
                ("Shift 08:05 pm - 09:30 pm", "08:05 pm")
            ],
            ["Run 07:30:09 to 08:00:00"]);

        result.Should().BeEquivalentTo(["07:30:09"], o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData("Run soon")]
    [InlineData("Run 07:30")]
    [InlineData("Run 07:30 to 08:45 to 09:30")]
    [InlineData("Run 07:30 to 24:05")]
    public void Fill_EmbeddedTimeRangeEndpointExtraction_ReturnsNullForInvalidOrNonRangeRemainingRows(
        string remaining)
    {
        var result = FlashFillService.Fill(
            [
                ("Window 9:15 AM to 10:45 AM", "9:15 AM"),
                ("Shift 08:05 pm - 09:30 pm", "08:05 pm")
            ],
            [remaining]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_EmbeddedTimeRangeEndpointExtraction_ReturnsNullForAmbiguousEndpointExamples()
    {
        var result = FlashFillService.Fill(
            [
                ("Window 09:30 to 09:30", "09:30"),
                ("Shift 08:05 to 09:30", "08:05")
            ],
            ["Run 07:30 to 08:45"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_EmbeddedTimeRangeEndpointExtraction_ReturnsNullForMixedEndpointExamples()
    {
        var result = FlashFillService.Fill(
            [
                ("Window 09:30 to 10:45", "09:30"),
                ("Shift 08:05 to 09:30", "09:30")
            ],
            ["Run 07:30 to 08:45"]);

        result.Should().BeNull();
    }

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
