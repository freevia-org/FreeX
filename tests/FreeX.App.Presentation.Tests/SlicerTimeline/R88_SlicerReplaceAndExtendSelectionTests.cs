using FluentAssertions;
using FreeX.App.Presentation.SlicerTimeline;

namespace FreeX.App.Presentation.Tests.SlicerTimeline;

/// <summary>
/// R88-app-slicer-timeline-interaction-5-2: the WPF slicer side-pane's <c>SlicerTileButton_Click</c>
/// routed every click (plain or Ctrl) through <see cref="SlicerTimelinePanePlanner.ToggleSlicerSelection"/>,
/// which is purely additive -- a plain click on "West" with "East" already selected left the pivot
/// filtered to East+West instead of replacing the filter with West-only, the opposite of Excel and of
/// the native on-grid slicer overlay (which already applies plain-click REPLACE semantics via
/// <c>SlicerLayoutBuilder.Toggle(additive: false)</c>). These tests cover the new
/// <see cref="SlicerTimelinePanePlanner.ReplaceSlicerSelection"/> (plain click) and
/// <see cref="SlicerTimelinePanePlanner.ExtendSlicerSelection"/> (Shift+click) planner functions the
/// pane's click handler now dispatches to.
/// </summary>
public sealed class R88_SlicerReplaceAndExtendSelectionTests
{
    [Fact]
    public void ReplaceSlicerSelection_NarrowsAMultiItemSelectionDownToJustTheClickedItem()
    {
        // Exact failure scenario from the finding: East+West both selected, plain click on West
        // must narrow the filter to West-only -- something the additive ToggleSlicerSelection can
        // never do (it can only add to or remove from the existing set).
        var result = SlicerTimelinePanePlanner.ReplaceSlicerSelection(["East", "West"], "West");

        result.Should().Equal("West");
    }

    [Fact]
    public void ReplaceSlicerSelection_ClickingTheLoneSelectedItemClearsTheFilter()
    {
        // A second plain click on the only already-selected item clears the filter back to
        // "everything selected", matching Excel and the native on-grid overlay.
        var result = SlicerTimelinePanePlanner.ReplaceSlicerSelection(["West"], "West");

        result.Should().BeEmpty();
    }

    [Fact]
    public void ReplaceSlicerSelection_FromNoSelection_SelectsJustTheClickedItem()
    {
        var result = SlicerTimelinePanePlanner.ReplaceSlicerSelection([], "West");

        result.Should().Equal("West");
    }

    [Fact]
    public void ExtendSlicerSelection_SelectsTheContiguousRangeFromTheAnchorThroughTheClickedItem()
    {
        var allItems = new[] { "East", "North", "South", "West" };

        var result = SlicerTimelinePanePlanner.ExtendSlicerSelection(allItems, ["North"], "West");

        result.Should().BeEquivalentTo(["North", "South", "West"]);
    }

    [Fact]
    public void ExtendSlicerSelection_WorksBackwardWhenTheClickedItemPrecedesTheAnchor()
    {
        var allItems = new[] { "East", "North", "South", "West" };

        var result = SlicerTimelinePanePlanner.ExtendSlicerSelection(allItems, ["West"], "East");

        result.Should().BeEquivalentTo(["East", "North", "South", "West"]);
    }

    [Fact]
    public void ExtendSlicerSelection_FromNoSelection_SelectsJustTheClickedItem()
    {
        var allItems = new[] { "East", "North", "South", "West" };

        var result = SlicerTimelinePanePlanner.ExtendSlicerSelection(allItems, [], "South");

        result.Should().Equal("South");
    }

    // No-regression sibling: Ctrl+click's additive ToggleSlicerSelection must keep its existing
    // contract (add-or-remove-from-the-current-set) unchanged by the new plain-click/shift-click paths.
    [Fact]
    public void ToggleSlicerSelection_RemainsAdditiveAndUnaffectedByTheNewReplaceAndExtendPaths()
    {
        // Adding "West" to an existing {"East"} selection selects everything, which the existing
        // additive-toggle contract normalizes back to "no filter" (empty) -- unchanged by this fix.
        SlicerTimelinePanePlanner.ToggleSlicerSelection(["East", "West"], ["East"], "West")
            .Should()
            .BeEmpty();

        // Removing "West" from an {"East","West"} selection leaves just "East".
        SlicerTimelinePanePlanner.ToggleSlicerSelection(["East", "West"], ["East", "West"], "West")
            .Should()
            .Equal("East");
    }
}
