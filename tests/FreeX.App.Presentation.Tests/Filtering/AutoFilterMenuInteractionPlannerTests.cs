using FluentAssertions;
using FreeX.App.Presentation.Filtering;

namespace FreeX.App.Presentation.Tests.Filtering;

public sealed class AutoFilterMenuInteractionPlannerTests
{
    [Fact]
    public void Build_ProjectsLocalizedCriteriaAndChecklistState()
    {
        var plan = new AutoFilterMenuPlan(
            "Amount",
            AutoFilterMenuFilterKind.Number,
            [
                new("Sort", AutoFilterMenuEntryKind.SortAscending),
                new(new AutoFilterChecklistItem("10", "10", IsChecked: true)),
                new(new AutoFilterChecklistItem("20", "20", IsChecked: false))
            ]);

        var model = AutoFilterMenuPlanner.Build(plan, PrefixTextProvider.Instance);

        model.CriteriaOptions.Should().Contain(option =>
            option.Label == "localized:AutoFilter_Criteria_Between" &&
            option.CriteriaPrefix == "between:");
        var items = AutoFilterMenuPlanner.CreateDialogItems(model);
        AutoFilterMenuPlanner.SelectAllState(items).Should().BeNull();
        AutoFilterMenuPlanner.SetSelectionForSearch(items, "20", isSelected: true)
            .Should().OnlyContain(item => item.IsSelected);
    }

    [Theory]
    [InlineData(WorksheetFilterMutationKind.ApplyFilter, "ShellLoc_AppliedFilter", "ShellLoc_FilterFailed")]
    [InlineData(WorksheetFilterMutationKind.ClearFilter, "ShellLoc_ClearedFilter", "ShellLoc_FilterFailed")]
    [InlineData(WorksheetFilterMutationKind.SortAscending, "ShellLoc_SortedAToZ", "ShellLoc_SortFailed")]
    [InlineData(WorksheetFilterMutationKind.SortDescending, "ShellLoc_SortedZToA", "ShellLoc_SortFailed")]
    [InlineData(WorksheetFilterMutationKind.SortByColor, "ShellLoc_SortedByColor", "ShellLoc_SortFailed")]
    public void MessagePlanner_MapsMutationOutcomeResources(
        WorksheetFilterMutationKind kind,
        string successKey,
        string failureKey)
    {
        WorksheetFilterMessagePlanner.GetSuccessResourceKey(kind).Should().Be(successKey);
        WorksheetFilterMessagePlanner.GetCommandFailureResourceKey(kind).Should().Be(failureKey);
    }

    [Theory]
    [InlineData(FilterPromptPlanError.TopBottomSyntax, "FilterPrompt_ErrorTopBottomSyntax")]
    [InlineData(FilterPromptPlanError.DateBetweenSyntax, "FilterPrompt_ErrorDateBetweenSyntax")]
    [InlineData(FilterPromptPlanError.ComparisonNumber, "FilterPrompt_ErrorComparisonNumber")]
    [InlineData(FilterPromptPlanError.None, "MainWindowMessage_FilterUnsupportedCriterion")]
    public void MessagePlanner_MapsPromptErrors(FilterPromptPlanError error, string resourceKey)
    {
        WorksheetFilterMessagePlanner.GetPromptErrorResourceKey(error).Should().Be(resourceKey);
    }

    private sealed class PrefixTextProvider : IAutoFilterMenuTextProvider
    {
        public static PrefixTextProvider Instance { get; } = new();

        public string Get(string resourceKey) => $"localized:{resourceKey}";

        public string Format(string resourceKey, string value) => $"localized:{resourceKey}:{value}";
    }
}
