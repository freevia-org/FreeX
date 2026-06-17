using System.Linq;

using FluentAssertions;

using FreeX.App.Avalonia;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Unit tests for the UI-free <see cref="AutoFilterMenuPlanner"/>: the dropdown menu shape (sort / clear /
/// select-all / checklist), the Clear-Filter enablement, value de-duplication, blank handling, and the
/// numbers-then-dates-then-text checklist ordering. No running shell required.
/// </summary>
public sealed class AutoFilterMenuPlannerTests
{
    [Fact]
    public void Build_ProducesSortClearSelectAllThenChecklist()
    {
        var model = AutoFilterMenuPlanner.Build("Region", new[] { "West", "East" }, hasActiveFilter: false);

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
        AutoFilterMenuPlanner.Build("H", new[] { "a" }, hasActiveFilter: false)
            .Items.Single(i => i.Kind == AutoFilterMenuItemKind.ClearFilter).IsEnabled.Should().BeFalse();

        AutoFilterMenuPlanner.Build("H", new[] { "a" }, hasActiveFilter: true)
            .Items.Single(i => i.Kind == AutoFilterMenuItemKind.ClearFilter).IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void Checklist_DeduplicatesValues()
    {
        var model = AutoFilterMenuPlanner.Build("H", new[] { "x", "x", "y" }, hasActiveFilter: false);

        model.Items.Count(i => i.Kind == AutoFilterMenuItemKind.ChecklistItem).Should().Be(2);
    }

    [Fact]
    public void Checklist_OrdersNumbersThenTextThenBlanks()
    {
        var model = AutoFilterMenuPlanner.Build("H", new[] { "banana", "10", "2", "apple", "" }, hasActiveFilter: false);

        var checklist = model.Items
            .Where(i => i.Kind == AutoFilterMenuItemKind.ChecklistItem)
            .Select(i => i.Value)
            .ToList();

        // Numbers (ascending) first, then text (alpha), then the blank last.
        checklist.Should().Equal("2", "10", "apple", "banana", "");
    }

    [Fact]
    public void Checklist_BlankValue_ShowsBlankLabel_KeepsEmptyValue()
    {
        var model = AutoFilterMenuPlanner.Build("H", new[] { "" }, hasActiveFilter: false);

        var blank = model.Items.Single(i => i.Kind == AutoFilterMenuItemKind.ChecklistItem);
        blank.Label.Should().Be(AutoFilterMenuPlanner.BlankDisplayText);
        blank.Value.Should().BeEmpty();
    }
}
