using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class WatchWindowMessageFormatterTests
{
    [Theory]
    [InlineData(1, "A1", "1 cell added to Watch Window.")]
    [InlineData(2, "A1:B1", "2 cells added to Watch Window.")]
    [InlineData(0, "A1:B1", "A1:B1 is already watched.")]
    public void FormatAddResult_HandlesSingularPluralAndNoOp(int added, string rangeText, string expected)
    {
        WatchWindowMessageFormatter.FormatAddResult(added, rangeText).Should().Be(expected);
    }

    [Theory]
    [InlineData(1, "A1", "1 cell removed from Watch Window.")]
    [InlineData(2, "A1:B1", "2 cells removed from Watch Window.")]
    [InlineData(0, "A1:B1", "A1:B1 is not watched.")]
    public void FormatRemoveResult_HandlesSingularPluralAndNoOp(int removed, string rangeText, string expected)
    {
        WatchWindowMessageFormatter.FormatRemoveResult(removed, rangeText).Should().Be(expected);
    }
}
