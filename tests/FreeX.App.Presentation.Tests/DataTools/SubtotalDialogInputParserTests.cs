using FluentAssertions;
using FreeX.App.Presentation.DataTools;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.DataTools;

public sealed class SubtotalDialogInputParserTests
{
    [Fact]
    public void BuildColumnChoices_UsesHeadersFallbackLabelsAndDefaultSelection()
    {
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Total"));

        var choices = SubtotalDialogPlanner.BuildColumnChoices(
            sheet,
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 3)));

        choices.Select(static choice => choice.Header).Should().Equal("Region", "Column B", "Total");
        choices.Select(static choice => choice.IsSelected).Should().Equal(false, true, true);
    }

    [Fact]
    public void CreateFunctionChoices_UsesSharedFunctionTokens()
    {
        var choices = SubtotalDialogPlanner.CreateFunctionChoices();

        choices.Select(static choice => choice.Label).Should().Equal(
            "Sum",
            "Count",
            "Average",
            "Max",
            "Min",
            "Product",
            "Count Numbers",
            "StdDev",
            "StdDevp",
            "Var",
            "Varp");
        choices.Single(static choice => choice.Label == "Count Numbers").FunctionText.Should().Be("CountA");
    }

    [Fact]
    public void FindFunctionChoice_MapsPersistedFunctionNumberToLocalizedChoice()
    {
        SubtotalDialogPlanner.FindFunctionChoice(1)!
            .FunctionText.Should().Be("Average");
        SubtotalDialogPlanner.FindFunctionChoice(999).Should().BeNull();
    }

    [Fact]
    public void TryCreateResult_CreatesApplyPlanAndInputOptions()
    {
        SubtotalDialogPlanner.TryCreateResult(
                groupColumnOffset: 0,
                subtotalColumnOffsets: [1u, 3u, 1u],
                functionText: "sum",
                replaceCurrentSubtotals: true,
                pageBreakBetweenGroups: false,
                summaryBelowData: true,
                out var result,
                out var issue)
            .Should().BeTrue(issue.ToString());

        result.Action.Should().Be(SubtotalDialogPlanAction.Apply);
        result.SubtotalColumnOffsets.Should().Equal(1u, 3u);
        result.ToInputOptions().FunctionNumber.Should().Be(9);
    }

    [Fact]
    public void CreateRemoveAllResult_UsesSharedDefaultPolicy()
    {
        var result = SubtotalDialogPlanner.CreateRemoveAllResult();

        result.Action.Should().Be(SubtotalDialogPlanAction.RemoveAll);
        result.GroupColumnOffset.Should().Be(0);
        result.SubtotalColumnOffsets.Should().BeEmpty();
        result.FunctionNumber.Should().Be(9);
        result.ReplaceCurrentSubtotals.Should().BeFalse();
        result.PageBreakBetweenGroups.Should().BeFalse();
        result.SummaryBelowData.Should().BeTrue();
    }

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
