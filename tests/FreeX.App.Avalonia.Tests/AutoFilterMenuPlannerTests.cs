using System.Linq;

using FluentAssertions;

using FreeX.App.Avalonia;
using FreeX.App.Presentation.Filtering;

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
                new("Text Filters", AutoFilterMenuEntryKind.FilterFamily, ["contains:"], "Text Filters"),
                new(new AutoFilterChecklistItem("West", "west", IsChecked: false))
            ]);

        var model = AutoFilterMenuPlanner.Build(plan);

        model.Header.Should().Be("Region");
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
    }
}
