using System.Linq;

using FluentAssertions;

using FreeX.App.Avalonia;
using FreeX.App.Presentation.Filtering;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Unit tests for the UI-free <see cref="AutoFilterMenuPlanner"/>: the dropdown menu shape (sort / clear /
/// select-all / checklist), Clear-Filter enablement, and mapping shared checklist items to Avalonia menu
/// items. Shared value extraction, de-duplication, blank handling, and ordering live in presentation tests.
/// </summary>
public sealed class AutoFilterMenuPlannerTests
{
    [Fact]
    public void Build_ProducesSortClearSelectAllThenChecklist()
    {
        var model = AutoFilterMenuPlanner.Build("Region", Items("West", "East"), hasActiveFilter: false);

        model.Header.Should().Be("Region");
        model.Items.Select(i => i.Kind).Take(6).Should().Equal(
            AutoFilterMenuItemKind.SortAscending,
            AutoFilterMenuItemKind.SortDescending,
            AutoFilterMenuItemKind.Separator,
            AutoFilterMenuItemKind.ClearFilter,
            AutoFilterMenuItemKind.Separator,
            AutoFilterMenuItemKind.SelectAll);
        model.Items.Count(i => i.Kind == AutoFilterMenuItemKind.ChecklistItem).Should().Be(2);
    }

    [Fact]
    public void ClearFilter_DisabledWhenNoActiveFilter_EnabledWhenActive()
    {
        AutoFilterMenuPlanner.Build("H", Items("a"), hasActiveFilter: false)
            .Items.Single(i => i.Kind == AutoFilterMenuItemKind.ClearFilter).IsEnabled.Should().BeFalse();

        AutoFilterMenuPlanner.Build("H", Items("a"), hasActiveFilter: true)
            .Items.Single(i => i.Kind == AutoFilterMenuItemKind.ClearFilter).IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void Checklist_UsesSharedItemLabelsAndValues()
    {
        var model = AutoFilterMenuPlanner.Build(
            "H",
            [new AutoFilterChecklistItem("Shown", "stored"), new AutoFilterChecklistItem("(Blanks)", "")],
            hasActiveFilter: false);

        var checklist = model.Items
            .Where(i => i.Kind == AutoFilterMenuItemKind.ChecklistItem)
            .Select(i => (i.Label, i.Value))
            .ToList();

        checklist.Should().Equal(("Shown", "stored"), ("(Blanks)", ""));
    }

    private static AutoFilterChecklistItem[] Items(params string[] values) =>
        values.Select(value => new AutoFilterChecklistItem(value, value)).ToArray();
}
