using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.SlicerTimeline;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.SlicerTimeline;

/// <summary>
/// R88-app-slicer-timeline-interaction-5-3: the slicer header's multi-select toggle icon
/// (<see cref="SlicerLayoutModel.MultiSelectIconRect"/>) was drawn but had no hit-test or state
/// anywhere in the portable layout layer, so a shell had nothing to bind a click on it to. This adds
/// the missing affordances: a hit-test helper for the icon rect, and a transient
/// <see cref="SlicerLayoutModel.MultiSelectModeActive"/> flag threaded through
/// <see cref="SlicerLayoutBuilder.Build"/>/<see cref="SlicerLayoutBuilder.BuildFull"/> so a shell can
/// echo its own held-mode state back into the layout for rendering.
/// </summary>
public sealed class R88_SlicerMultiSelectToggleTests
{
    private static readonly LayoutRect Bounds = new(100, 50, 160, 120);

    private static SlicerModel Slicer(string caption, params string[] selected)
    {
        var slicer = new SlicerModel { Name = "Slicer1", Caption = caption, SourceFieldName = "Region" };
        foreach (var item in selected)
            slicer.SelectedItems.Add(item);
        return slicer;
    }

    [Fact]
    public void HitTestMultiSelectIcon_ReturnsTrue_ForAPointInsideTheIconRect()
    {
        var layout = SlicerLayoutBuilder.Build(Slicer("Region"), ["North", "South"], Bounds);

        var iconCenter = new LayoutPoint(
            layout.MultiSelectIconRect.Left + layout.MultiSelectIconRect.Width / 2,
            layout.MultiSelectIconRect.Top + layout.MultiSelectIconRect.Height / 2);

        SlicerLayoutBuilder.HitTestMultiSelectIcon(layout, iconCenter).Should().BeTrue();
    }

    [Fact]
    public void Build_ThreadsMultiSelectMode_IntoTheLayout()
    {
        var slicer = Slicer("Region");

        var inactive = SlicerLayoutBuilder.Build(slicer, ["North"], Bounds);
        var active = SlicerLayoutBuilder.Build(slicer, ["North"], Bounds, multiSelectMode: true);

        inactive.MultiSelectModeActive.Should().BeFalse();
        active.MultiSelectModeActive.Should().BeTrue();
    }

    // No-regression sibling: a point that misses the icon rect entirely (e.g. deep inside the tile
    // body) must not be reported as a hit, and the header-hidden case (zero-size rect) must not throw
    // or accidentally report a hit either.
    [Fact]
    public void HitTestMultiSelectIcon_ReturnsFalse_ForPointsOutsideTheIcon()
    {
        var layout = SlicerLayoutBuilder.Build(Slicer("Region"), ["North", "South"], Bounds);
        var farAwayPoint = new LayoutPoint(Bounds.Left + 5, Bounds.Top + 60);
        SlicerLayoutBuilder.HitTestMultiSelectIcon(layout, farAwayPoint).Should().BeFalse();

        var slicer = new SlicerModel { Name = "Slicer1", Caption = "Region", SourceFieldName = "Region", ShowCaption = false };
        var noHeaderLayout = SlicerLayoutBuilder.Build(slicer, ["North"], Bounds);
        noHeaderLayout.MultiSelectIconRect.Width.Should().Be(0);
        SlicerLayoutBuilder.HitTestMultiSelectIcon(noHeaderLayout, new LayoutPoint(noHeaderLayout.MultiSelectIconRect.Left, noHeaderLayout.MultiSelectIconRect.Top))
            .Should().BeFalse();
    }
}
