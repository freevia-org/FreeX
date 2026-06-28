using FluentAssertions;
using FreeX.App.Presentation.DataTools;

namespace FreeX.App.Presentation.Tests.DataTools;

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
                out var issue)
            .Should().BeTrue(issue.ToString());

        result.GroupColumnOffset.Should().Be(0);
        result.SubtotalColumnOffsets.Should().Equal(1u, 3u);
        result.FunctionNumber.Should().Be(1);
        result.ReplaceCurrentSubtotals.Should().BeTrue();
        result.PageBreakBetweenGroups.Should().BeTrue();
        result.SummaryBelowData.Should().BeFalse();
    }

    [Theory]
    [InlineData("bad", "1", "sum", SubtotalDialogInputParseIssue.InvalidGroupColumnOffset)]
    [InlineData("0", "", "sum", SubtotalDialogInputParseIssue.InvalidSubtotalColumnOffsets)]
    [InlineData("0", "1,bad", "sum", SubtotalDialogInputParseIssue.InvalidSubtotalColumnOffsets)]
    [InlineData("0", "1", "unsupported", SubtotalDialogInputParseIssue.UnsupportedSubtotalFunction)]
    public void TryParse_RejectsInvalidDialogText(
        string groupColumnText,
        string subtotalColumnsText,
        string functionText,
        SubtotalDialogInputParseIssue expectedIssue)
    {
        SubtotalDialogInputParser.TryParse(
                groupColumnText,
                subtotalColumnsText,
                functionText,
                replaceCurrentSubtotals: false,
                pageBreakBetweenGroups: false,
                summaryBelowData: true,
                out _,
                out var issue)
            .Should().BeFalse();

        issue.Should().Be(expectedIssue);
    }

    [Fact]
    public void TryCreateResult_DeduplicatesColumnOffsets()
    {
        SubtotalDialogInputParser.TryCreateResult(
                groupColumnOffset: 0,
                subtotalColumnOffsets: [1u, 3u, 1u],
                functionText: "sum",
                replaceCurrentSubtotals: true,
                pageBreakBetweenGroups: false,
                summaryBelowData: true,
                out var result,
                out var issue)
            .Should().BeTrue(issue.ToString());

        result.SubtotalColumnOffsets.Should().Equal(1u, 3u);
        result.FunctionNumber.Should().Be(9);
    }
}
