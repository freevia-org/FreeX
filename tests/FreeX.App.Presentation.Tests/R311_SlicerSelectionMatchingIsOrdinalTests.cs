using FluentAssertions;
using FreeX.App.Presentation.SlicerTimeline;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests;

/// <summary>
/// r311: a slicer's selection is file data, so matching it must use the file's rules.
///
/// <para>The IO layer decides which slicer items are the same item ordinally --
/// <c>XlsxPivotSlicerCacheData</c> and <c>XlsxSlicerTimelineStateRewriter</c> both key selections
/// with <c>OrdinalIgnoreCase</c>, as does the filter engine. The presentation layer matched the same
/// persisted strings with <c>CurrentCultureIgnoreCase</c>, which is a different question: ICU
/// collation ignores characters like a soft hyphen, so two values the file keeps distinct compared
/// equal. The visible cost is a tile shown as selected that the filter does not include, or two
/// distinct source values collapsing into one tile.</para>
///
/// <para>The first test is a platform probe rather than a product assertion. r286 taught the lesson:
/// the behavioural tests here would have passed against the unfixed code had I not first proved the
/// two comparers actually disagree.</para>
/// </summary>
public sealed class R311_SlicerSelectionMatchingIsOrdinalTests
{
    private const string Plain = "TotalRevenue";
    private const string WithIgnorable = "Total­Revenue";

    [Fact]
    public void TheTwoComparersDisagreeOnDataThatReachesThisCode()
    {
        StringComparer.CurrentCultureIgnoreCase.Equals(Plain, WithIgnorable)
            .Should().BeTrue("ICU collation skips ignorable characters, merging these two values");
        StringComparer.OrdinalIgnoreCase.Equals(Plain, WithIgnorable)
            .Should().BeFalse("the IO layer keeps them distinct, and it owns what the file means");
    }

    private static SlicerModel SlicerSelecting(params string[] selected)
    {
        var slicer = new SlicerModel { Name = "Slicer1" };
        slicer.SelectedItems.AddRange(selected);
        return slicer;
    }

    [Fact]
    public void ATileIsSelectedOnlyWhenTheFileSaysThatExactItemIs()
    {
        var tiles = SlicerTimelinePanePlanner.BuildSlicerTiles(
            SlicerSelecting(Plain), [Plain, WithIgnorable]);

        tiles.Should().HaveCount(2, "the two values are distinct to the file, so both get a tile");
        tiles.Single(tile => tile.Caption == Plain).IsSelected.Should().BeTrue();
        tiles.Single(tile => tile.Caption == WithIgnorable).IsSelected
            .Should().BeFalse("this value was not selected; only culture-aware matching says otherwise");
    }

    [Fact]
    public void TwoDistinctSourceValuesDoNotCollapseIntoOneTile()
    {
        var tiles = SlicerTimelinePanePlanner.BuildSlicerTiles(
            SlicerSelecting(), [Plain, WithIgnorable]);

        tiles.Select(tile => tile.Caption).Should().BeEquivalentTo([Plain, WithIgnorable]);
    }

    /// <summary>
    /// Case-insensitivity itself must survive the change: ordinal-IGNORE-case still matches "east"
    /// to "EAST", which is what the file layer does and what the user expects.
    /// </summary>
    [Fact]
    public void MatchingRemainsCaseInsensitive()
    {
        var tiles = SlicerTimelinePanePlanner.BuildSlicerTiles(SlicerSelecting("east"), ["EAST"]);

        tiles.Should().ContainSingle().Which.IsSelected.Should().BeTrue();
    }

    [Fact]
    public void TogglingASelectionTreatsDistinctValuesAsDistinct()
    {
        // Three items, deliberately: with only two, selecting both means "everything", which the
        // planner correctly reports as no filter at all -- that would pass whatever the comparer did.
        var result = SlicerTimelinePanePlanner.ToggleSlicerSelection(
            allItems: [Plain, WithIgnorable, "West"],
            selectedItems: [Plain],
            caption: WithIgnorable);

        result.Should().BeEquivalentTo([Plain, WithIgnorable],
            "toggling the second value on must add it rather than remove the first");
    }

    [Fact]
    public void APlainClickOnADifferentValueDoesNotReadAsClearingTheFilter()
    {
        SlicerTimelinePanePlanner.ReplaceSlicerSelection([Plain], WithIgnorable)
            .Should().BeEquivalentTo([WithIgnorable],
                "only clicking the already-sole selection clears the filter");

        SlicerTimelinePanePlanner.ReplaceSlicerSelection([Plain], Plain)
            .Should().BeEmpty("clicking the sole selection still clears, so the test above is not vacuous");
    }

    /// <summary>
    /// Display order is a separate question from identity and stays in the user's locale, so the
    /// ordinal change must not have made the list order ordinal (where lowercase sorts after "Z").
    /// </summary>
    [Fact]
    public void DisplayOrderStillFollowsTheUsersLocaleNotByteOrder()
    {
        var captions = SlicerTimelinePanePlanner
            .BuildSlicerTiles(SlicerSelecting(), ["banana", "Apple", "cherry"])
            .Select(tile => tile.Caption)
            .ToList();

        captions.Should().Equal(["Apple", "banana", "cherry"],
            "an ordinal sort would put every capital before every lowercase letter");
    }
}
