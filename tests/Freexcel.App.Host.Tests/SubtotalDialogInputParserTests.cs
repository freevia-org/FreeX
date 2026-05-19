using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class SubtotalDialogInputParserTests
{
    [Fact]
    public void TryParse_CreatesDialogResultFromZeroBasedOffsets()
    {
        SubtotalDialogInputParser.TryParse(
                groupColumnText: "0",
                subtotalColumnsText: "1, 3, 1",
                functionText: "average",
                replaceCurrentSubtotals: true,
                pageBreakBetweenGroups: true,
                summaryBelowData: false,
                out var result,
                out var error)
            .Should().BeTrue(error);

        result.GroupColumnOffset.Should().Be(0);
        result.SubtotalColumnOffsets.Should().Equal(1u, 3u);
        result.FunctionNumber.Should().Be(1);
        result.ReplaceCurrentSubtotals.Should().BeTrue();
        result.PageBreakBetweenGroups.Should().BeTrue();
        result.SummaryBelowData.Should().BeFalse();
    }

    [Theory]
    [InlineData("bad", "1", "sum", "Enter a valid group column offset.")]
    [InlineData("0", "", "sum", "Enter one or more valid subtotal column offsets.")]
    [InlineData("0", "1,bad", "sum", "Enter one or more valid subtotal column offsets.")]
    [InlineData("0", "1", "unsupported", "Unsupported SUBTOTAL function.")]
    public void TryParse_RejectsInvalidDialogText(
        string groupColumnText,
        string subtotalColumnsText,
        string functionText,
        string expectedError)
    {
        SubtotalDialogInputParser.TryParse(
                groupColumnText,
                subtotalColumnsText,
                functionText,
                replaceCurrentSubtotals: false,
                pageBreakBetweenGroups: false,
                summaryBelowData: true,
                out _,
                out var error)
            .Should().BeFalse();

        error.Should().Be(expectedError);
    }
}
