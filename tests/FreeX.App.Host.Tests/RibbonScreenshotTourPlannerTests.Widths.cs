using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class RibbonScreenshotTourPlannerTests
{
    [Fact]
    public void ParseWidths_ReturnsRepresentativeWidthsWhenNoFilterIsProvided()
    {
        RibbonScreenshotTourPlanner.ParseWidths(null)
            .Should()
            .Equal(RibbonScreenshotTourPlanner.DefaultWidths);

        RibbonScreenshotTourPlanner.ParseWidths("  ")
            .Should()
            .Equal(RibbonScreenshotTourPlanner.DefaultWidths);
    }

    [Fact]
    public void ParseWidths_UsesInvariantCultureAndAcceptsMax()
    {
        RibbonScreenshotTourPlanner.ParseWidths("max, 1100, 900.5, 750")
            .Should()
            .Equal(
            [
                new("max", null),
                new("1100", 1100),
                new("900.5", 900.5),
                new("750", 750)
            ]);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("Infinity")]
    [InlineData("NaN")]
    [InlineData("nope")]
    public void ParseWidths_RejectsInvalidNonPositiveOrNonFiniteValues(string requestedWidths)
    {
        var act = () => RibbonScreenshotTourPlanner.ParseWidths(requestedWidths);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*invalid width(s): {requestedWidths}*");
    }

    [Fact]
    public void ParseWidths_RejectsMissingWidthEntries()
    {
        var act = () => RibbonScreenshotTourPlanner.ParseWidths("1100,,750");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*width list contains empty entry*position(s): 2*");
    }
}
