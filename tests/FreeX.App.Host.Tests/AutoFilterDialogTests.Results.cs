using FluentAssertions;
using FreeX.App.Presentation.Filtering;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class AutoFilterDialogTests
{
    [Fact]
    public void BuildResult_IncludesSortDirectionChecklistValuesSearchAndCriteriaText()
    {
        var items = new[]
        {
            new AutoFilterDialogItem("Apple", "Apple", true),
            new AutoFilterDialogItem("Banana", "Banana", false),
            new AutoFilterDialogItem("(Blanks)", "", true)
        };

        var result = AutoFilterDialogCriteriaPlanner.BuildResult(
            AutoFilterSortDirection.Descending,
            items,
            "a",
            "contains: App");

        result.SortDirection.Should().Be(AutoFilterSortDirection.Descending);
        result.SelectedValues.Should().Equal("Apple", "");
        result.SearchText.Should().Be("a");
        result.CriteriaText.Should().Be("contains: App");
        result.ColorFilter.Should().BeNull();
    }

    [Fact]
    public void BuildResult_WithSearchUsesVisibleMatchesUnlessAddingCurrentSelection()
    {
        var items = new[]
        {
            new AutoFilterDialogItem("Apple", "Apple", true),
            new AutoFilterDialogItem("Apricot", "Apricot", false),
            new AutoFilterDialogItem("Banana", "Banana", true)
        };

        var searchOnly = AutoFilterDialogCriteriaPlanner.BuildResult(
            AutoFilterSortDirection.None,
            items,
            "ap",
            "",
            addCurrentSelectionToFilter: false);
        var addCurrentSelection = AutoFilterDialogCriteriaPlanner.BuildResult(
            AutoFilterSortDirection.None,
            items,
            "ap",
            "",
            addCurrentSelectionToFilter: true);

        searchOnly.SelectedValues.Should().Equal("Apple");
        searchOnly.CriteriaText.Should().BeEmpty();
        addCurrentSelection.SelectedValues.Should().Equal("Apple", "Banana");
        addCurrentSelection.CriteriaText.Should().BeEmpty();
    }

    [Fact]
    public void BuildResult_CarriesOptionalColorFilter()
    {
        var color = new CellColor(33, 115, 70);

        var result = AutoFilterDialogCriteriaPlanner.BuildResult(
            AutoFilterSortDirection.None,
            [new AutoFilterDialogItem("Apple", "Apple", true)],
            "",
            "",
            new AutoFilterColorFilter(AutoFilterColorFilterKind.CellFillColor, color));

        result.ColorFilter.Should().Be(new AutoFilterColorFilter(AutoFilterColorFilterKind.CellFillColor, color));
        result.SelectedValues.Should().Equal("Apple");
        result.CriteriaText.Should().BeEmpty();
    }

    [Fact]
    public void BuildResult_DistinguishesNoFillColorFilterFromNoColorSelection()
    {
        var result = AutoFilterDialogCriteriaPlanner.BuildResult(
            AutoFilterSortDirection.None,
            [new AutoFilterDialogItem("Apple", "Apple", true)],
            "",
            "",
            new AutoFilterColorFilter(AutoFilterColorFilterKind.NoFill, null));

        result.ColorFilter.Should().Be(new AutoFilterColorFilter(AutoFilterColorFilterKind.NoFill, null));
    }

    [Fact]
    public void BuildResult_KeepsChecklistValuesAtomicWithoutCommaSerializing()
    {
        var items = new[]
        {
            new AutoFilterDialogItem("ACME, Inc.", "ACME, Inc.", true),
            new AutoFilterDialogItem("(Blanks)", "", true),
            new AutoFilterDialogItem("Beta", "Beta", false)
        };

        var result = AutoFilterDialogCriteriaPlanner.BuildResult(
            AutoFilterSortDirection.None,
            items,
            "",
            "");

        result.SelectedValues.Should().Equal("ACME, Inc.", "");
        result.CriteriaText.Should().BeEmpty();
    }

    [Fact]
    public void CreateClearFilterResult_RequestsExplicitClearAction()
    {
        AutoFilterDialogCriteriaPlanner.CreateClearFilterResult()
            .Should()
            .Be(new AutoFilterDialogResult(
                AutoFilterSortDirection.None,
                [],
                "",
                "",
                null,
                AutoFilterDialogAction.ClearFilter));
    }
}
