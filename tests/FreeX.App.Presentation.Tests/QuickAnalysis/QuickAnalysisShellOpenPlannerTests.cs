using FluentAssertions;
using FreeX.App.Presentation.QuickAnalysis;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.QuickAnalysis;

public sealed class QuickAnalysisShellOpenPlannerTests
{
    [Fact]
    public void Plan_ReadyRequestOpensSharedShellPlan()
    {
        var sheet = CreateSheet();
        for (uint row = 1; row <= 3; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));
        var selection = Range(sheet, 1, 1, 3, 1);
        var request = QuickAnalysisShellRequestPlanner.Build(
            sheet,
            selection,
            QuickAnalysisShellCapabilities.DialogBacked);

        var plan = QuickAnalysisShellOpenPlanner.Plan(request);

        plan.Decision.Should().Be(QuickAnalysisShellOpenDecision.Open);
        plan.CanOpen.Should().BeTrue();
        plan.Selection.Should().Be(selection);
        plan.ShellPlan.Should().Be(request.ShellPlan);
        plan.Issue.Should().BeNull();
    }

    [Theory]
    [InlineData(QuickAnalysisShellRequestStatus.MissingSheet)]
    [InlineData(QuickAnalysisShellRequestStatus.UnsupportedSelection)]
    public void Plan_SelectionIssuesUseSharedSelectRangeDecision(QuickAnalysisShellRequestStatus status)
    {
        var sheet = CreateSheet();
        var selection = Range(sheet, 1, 1, 1, 1);
        var request = QuickAnalysisShellRequestPlan.Empty(status, selection);

        var plan = QuickAnalysisShellOpenPlanner.Plan(request);

        plan.Decision.Should().Be(QuickAnalysisShellOpenDecision.ShowSelectRangeIssue);
        plan.CanOpen.Should().BeFalse();
        plan.Selection.Should().Be(selection);
        plan.ShellPlan.IsEmpty.Should().BeTrue();
        plan.Issue.Should().Be(new QuickAnalysisShellOpenIssuePlan(
            "QuickAnalysis_SelectRangeStatus",
            "TableLoc_QaSelectMoreThanOne",
            RequiresSelectionReference: false));
    }

    [Fact]
    public void Plan_MissingSelectionUsesSharedSelectRangeDecision()
    {
        var request = QuickAnalysisShellRequestPlan.Empty(QuickAnalysisShellRequestStatus.MissingSelection);

        var plan = QuickAnalysisShellOpenPlanner.Plan(request);

        plan.Decision.Should().Be(QuickAnalysisShellOpenDecision.ShowSelectRangeIssue);
        plan.CanOpen.Should().BeFalse();
        plan.Selection.Should().BeNull();
        plan.ShellPlan.IsEmpty.Should().BeTrue();
        plan.Issue.Should().Be(new QuickAnalysisShellOpenIssuePlan(
            "QuickAnalysis_SelectRangeStatus",
            "TableLoc_QaSelectMoreThanOne",
            RequiresSelectionReference: false));
    }

    [Fact]
    public void Plan_NoSuggestionsUsesSharedNoSuggestionsDecision()
    {
        var sheet = CreateSheet();
        var selection = Range(sheet, 1, 1, 3, 1);
        var request = new QuickAnalysisShellRequestPlan(
            QuickAnalysisShellRequestStatus.NoSuggestions,
            selection,
            null,
            QuickAnalysisDisplayModel.Empty,
            QuickAnalysisShellPlan.Empty);

        var plan = QuickAnalysisShellOpenPlanner.Plan(request);

        plan.Decision.Should().Be(QuickAnalysisShellOpenDecision.ShowNoSuggestionsIssue);
        plan.CanOpen.Should().BeFalse();
        plan.Selection.Should().Be(selection);
        plan.ShellPlan.IsEmpty.Should().BeTrue();
        plan.Issue.Should().Be(new QuickAnalysisShellOpenIssuePlan(
            "TableLoc_QaNoSuggestions",
            "TableLoc_QaNoSuggestions",
            RequiresSelectionReference: true));
    }

    [Fact]
    public void FormatIssueText_StatusTargetUsesSharedStatusResourceAndSelectionReference()
    {
        var sheet = CreateSheet();
        var selection = Range(sheet, 1, 1, 3, 2);
        var plan = QuickAnalysisShellOpenPlanner.Plan(new QuickAnalysisShellRequestPlan(
            QuickAnalysisShellRequestStatus.NoSuggestions,
            selection,
            null,
            QuickAnalysisDisplayModel.Empty,
            QuickAnalysisShellPlan.Empty));

        var text = QuickAnalysisShellOpenPlanner.FormatIssueText(
            plan,
            QuickAnalysisShellOpenIssueTextTarget.Status,
            resourceKey => $"get:{resourceKey}",
            (resourceKey, reference) => $"format:{resourceKey}:{reference}",
            FormatRangeForTest);

        text.Should().Be("format:TableLoc_QaNoSuggestions:R1C1:R3C2");
    }

    [Fact]
    public void FormatIssueText_DialogTargetUsesSharedDialogResourceWithoutReferenceWhenNotRequired()
    {
        var plan = QuickAnalysisShellOpenPlanner.Plan(
            QuickAnalysisShellRequestPlan.Empty(QuickAnalysisShellRequestStatus.MissingSelection));

        var text = QuickAnalysisShellOpenPlanner.FormatIssueText(
            plan,
            QuickAnalysisShellOpenIssueTextTarget.Dialog,
            resourceKey => $"get:{resourceKey}",
            (resourceKey, reference) => $"format:{resourceKey}:{reference}",
            FormatRangeForTest);

        text.Should().Be("get:TableLoc_QaSelectMoreThanOne");
    }

    private static Sheet CreateSheet() => new Workbook("Book").AddSheet("Sheet1");

    private static GridRange Range(Sheet sheet, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(new CellAddress(sheet.Id, startRow, startCol), new CellAddress(sheet.Id, endRow, endCol));

    private static string FormatRangeForTest(GridRange range) =>
        $"R{range.Start.Row}C{range.Start.Col}:R{range.End.Row}C{range.End.Col}";
}
