using System.Linq;

using FluentAssertions;

using FreeX.App.Avalonia;
using FreeX.App.Presentation.Filtering;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Unit tests for the Avalonia adapter over the shared AutoFilter menu plan. Shared menu shape, value
/// extraction, active-filter detection, and ordering live in presentation tests.
/// </summary>
public sealed class AutoFilterMenuPlannerTests
{
    [Fact]
    public void Build_FromSharedPlan_PreservesEntryKindsLabelsValuesAndEnablement()
    {
        var plan = new AutoFilterMenuPlan(
            "Region",
            AutoFilterMenuFilterKind.Text,
            [
                new("Sort A to Z", AutoFilterMenuEntryKind.SortAscending),
                new("Clear Filter from Region", AutoFilterMenuEntryKind.ClearFilter, isEnabled: false),
                new("Search", AutoFilterMenuEntryKind.Search),
                new("Select All", AutoFilterMenuEntryKind.SelectAll, isChecked: null),
                new("Text Filters", AutoFilterMenuEntryKind.FilterFamily, ["contains:", "blank"], "Text Filters"),
                new(new AutoFilterChecklistItem("West", "west", IsChecked: false))
            ],
            ColorOptions:
            [
                new("#217346", AutoFilterColorFilterKind.CellFillColor, new CellColor(0x21, 0x73, 0x46))
            ]);

        var model = AutoFilterMenuPlanner.Build(plan);

        model.Header.Should().Be("Region");
        model.FilterKind.Should().Be(AutoFilterMenuFilterKind.Text);
        model.Items.Select(item => item.Kind).Should().Equal(
            AutoFilterMenuItemKind.SortAscending,
            AutoFilterMenuItemKind.ClearFilter,
            AutoFilterMenuItemKind.Search,
            AutoFilterMenuItemKind.SelectAll,
            AutoFilterMenuItemKind.FilterFamily,
            AutoFilterMenuItemKind.ChecklistItem);
        model.Items.Single(item => item.Kind == AutoFilterMenuItemKind.ClearFilter).IsEnabled.Should().BeFalse();
        model.Items.Single(item => item.Kind == AutoFilterMenuItemKind.ChecklistItem).Value.Should().Be("west");
        model.Items.Single(item => item.Kind == AutoFilterMenuItemKind.SelectAll).IsChecked.Should().BeNull();
        model.Items.Single(item => item.Kind == AutoFilterMenuItemKind.ChecklistItem).IsChecked.Should().BeFalse();
        model.CriteriaSuggestions.Should().Equal("contains:", "blank");
        model.CriteriaOptions.Should().Contain(option => option.CriteriaPrefix == "contains:");
        model.ColorOptions.Should().ContainSingle(option => option.Kind == AutoFilterColorFilterKind.CellFillColor);
    }

    [Fact]
    public void CreateDialogItems_ProjectsChecklistRowsForSearchAndSelection()
    {
        var plan = new AutoFilterMenuPlan(
            "Region",
            AutoFilterMenuFilterKind.Text,
            [
                new(new AutoFilterChecklistItem("North", "north", IsChecked: true)),
                new(new AutoFilterChecklistItem("West", "west", IsChecked: false))
            ]);
        var model = AutoFilterMenuPlanner.Build(plan);

        var items = AutoFilterMenuPlanner.CreateDialogItems(model);
        var filtered = AutoFilterMenuPlanner.FilterItems(items, "we");
        var updated = AutoFilterMenuPlanner.SetSelectionForSearch(items, "we", isSelected: true);

        items.Should().BeEquivalentTo(
        [
            new AutoFilterDialogItem("North", "north", true),
            new AutoFilterDialogItem("West", "west", false)
        ]);
        filtered.Select(item => item.Value).Should().Equal("west");
        updated.Single(item => item.Value == "west").IsSelected.Should().BeTrue();
        AutoFilterMenuPlanner.SelectAllState(updated).Should().BeTrue();
    }

    [Fact]
    public void BuildResult_UsesSharedSearchAndCriteriaSemantics()
    {
        var items = new[]
        {
            new AutoFilterDialogItem("North", "north", true),
            new AutoFilterDialogItem("West", "west", true),
        };
        var option = AutoFilterMenuPlanner.Build(new AutoFilterMenuPlan(
            "Region",
            AutoFilterMenuFilterKind.Text,
            [])).CriteriaOptions.Single(criteria => criteria.CriteriaPrefix == "contains:");

        var result = AutoFilterMenuPlanner.BuildResult(
            items,
            searchText: "we",
            AutoFilterMenuPlanner.BuildCriteriaText(option, "st"));

        result.SearchText.Should().Be("we");
        result.SelectedValues.Should().Equal("west");
        result.CriteriaText.Should().Be("contains:st");
    }
}
