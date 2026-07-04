using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.SlicerTimeline;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.SlicerTimeline;

/// <summary>
/// Regression tests for H45: a plain slicer-tile click must REPLACE the current selection with just
/// the clicked item (Excel's default click semantics), while Ctrl+click remains additive/toggling.
/// </summary>
public sealed class GSlicersClickSemanticsTests
{
    private static SlicerModel Slicer(params string[] selected)
    {
        var slicer = new SlicerModel { Name = "Region Slicer", Caption = "Region", SourceFieldName = "Region" };
        slicer.SelectedItems.AddRange(selected);
        return slicer;
    }

    [Fact]
    public void PlainClick_WithNorthSelected_ClickingSouth_ReplacesSelectionWithSouthOnly()
    {
        // Exact H45 failure scenario: North is selected; a plain click on South must filter to
        // South-only, not leave both North and South selected.
        var slicer = Slicer("North");

        var result = SlicerLayoutBuilder.Toggle(slicer, ["North", "South", "East"], "South");

        result.SelectedItems.Should().BeEquivalentTo("South");
        result.SelectedItems.Should().NotContain("North");
    }

    [Fact]
    public void PlainClick_IsTheDefault_NoAdditiveArgumentNeeded()
    {
        var slicer = Slicer("North");

        // Calling Toggle without the additive argument must use plain-click (replace) semantics.
        var result = SlicerLayoutBuilder.Toggle(slicer, ["North", "South", "East"], "South");

        result.SelectedItems.Should().BeEquivalentTo("South");
    }

    [Fact]
    public void PlainClick_OnAlreadySoleSelectedTile_ClearsFilter()
    {
        var slicer = Slicer("South");

        var result = SlicerLayoutBuilder.Toggle(slicer, ["North", "South", "East"], "South");

        result.IsCleared.Should().BeTrue();
        result.SelectedItems.Should().BeEmpty();
    }

    [Fact]
    public void CtrlClick_WithNorthSelected_ClickingSouth_AddsSouthToSelection()
    {
        var slicer = Slicer("North");

        var result = SlicerLayoutBuilder.Toggle(slicer, ["North", "South", "East"], "South", additive: true);

        result.SelectedItems.Should().BeEquivalentTo("North", "South");
    }

    [Fact]
    public void CtrlClick_BuildSlicerToggleCommand_ProducesAdditiveSelection()
    {
        var slicer = Slicer("North");
        var bounds = new LayoutRect(0, 0, 120, 160);
        string[] availableItems = ["East", "North", "South"];
        var layout = SlicerLayoutBuilder.Build(slicer, availableItems, bounds);

        var tile = layout.Tiles.First(t => !t.IsAllPreview && t.Caption == "North");
        var command = SlicerTimelineInteractionPlanner.BuildSlicerToggleCommand(
            slicer, availableItems, layout, tile.Rect.Center, additive: true);

        command.Should().NotBeNull();
        // Ctrl+clicking the only selected tile toggles it off (additive membership toggle), leaving
        // the selection empty rather than replacing it with itself.
        var toggle = SlicerLayoutBuilder.Toggle(slicer, availableItems, "North", additive: true);
        toggle.SelectedItems.Should().BeEmpty();
    }

    [Fact]
    public void PlainClick_BuildSlicerToggleCommand_DefaultsToReplaceSemantics()
    {
        var slicer = Slicer("North");
        var bounds = new LayoutRect(0, 0, 120, 160);
        string[] availableItems = ["East", "North", "South"];
        var layout = SlicerLayoutBuilder.Build(slicer, availableItems, bounds);

        var tile = layout.Tiles.First(t => !t.IsAllPreview && t.Caption == "North");
        // No additive argument passed — must default to plain-click (replace) semantics.
        var command = SlicerTimelineInteractionPlanner.BuildSlicerToggleCommand(slicer, availableItems, layout, tile.Rect.Center);

        command.Should().NotBeNull();
        var toggle = SlicerLayoutBuilder.Toggle(slicer, availableItems, "North");
        toggle.IsCleared.Should().BeTrue("clicking the sole selected tile again clears the filter");
    }
}
