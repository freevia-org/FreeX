using FluentAssertions;
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

        var result = AutoFilterDialog.BuildResult(
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

        var searchOnly = AutoFilterDialog.BuildResult(
            AutoFilterSortDirection.None,
            items,
            "ap",
            "",
            addCurrentSelectionToFilter: false);
        var addCurrentSelection = AutoFilterDialog.BuildResult(
            AutoFilterSortDirection.None,
            items,
            "ap",
            "",
            addCurrentSelectionToFilter: true);

        searchOnly.SelectedValues.Should().Equal("Apple");
        searchOnly.CriteriaText.Should().Be("Apple");
        addCurrentSelection.SelectedValues.Should().Equal("Apple", "Banana");
        addCurrentSelection.CriteriaText.Should().Be("Apple, Banana");
    }

    [Fact]
    public void BuildResult_CarriesOptionalColorFilter()
    {
        var color = new CellColor(33, 115, 70);

        var result = AutoFilterDialog.BuildResult(
            AutoFilterSortDirection.None,
            [new AutoFilterDialogItem("Apple", "Apple", true)],
            "",
            "",
            new AutoFilterColorFilter(AutoFilterColorFilterKind.CellFillColor, color));

        result.ColorFilter.Should().Be(new AutoFilterColorFilter(AutoFilterColorFilterKind.CellFillColor, color));
        result.CriteriaText.Should().Be("Apple");
    }

    [Fact]
    public void BuildResult_DistinguishesNoFillColorFilterFromNoColorSelection()
    {
        var result = AutoFilterDialog.BuildResult(
            AutoFilterSortDirection.None,
            [new AutoFilterDialogItem("Apple", "Apple", true)],
            "",
            "",
            new AutoFilterColorFilter(AutoFilterColorFilterKind.NoFill, null));

        result.ColorFilter.Should().Be(new AutoFilterColorFilter(AutoFilterColorFilterKind.NoFill, null));
    }

    [Fact]
    public void CreateClearFilterResult_RequestsExplicitClearAction()
    {
        AutoFilterDialog.CreateClearFilterResult()
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
