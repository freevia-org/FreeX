using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.SlicerTimeline;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.SlicerTimeline;

public sealed class SlicerLayoutBuilderTests
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
    public void Build_NoSelection_ShowsSingleAllPreviewTile()
    {
        var slicer = Slicer("Region");

        var layout = SlicerLayoutBuilder.Build(slicer, ["North", "South", "East"], Bounds);

        layout.Tiles.Should().HaveCount(1);
        var tile = layout.Tiles[0];
        tile.IsAllPreview.Should().BeTrue();
        tile.IsSelected.Should().BeTrue();
        tile.Caption.Should().Be("Region");
        tile.ItemIndex.Should().Be(-1);
        layout.HasActiveFilter.Should().BeFalse();
        layout.TotalItemCount.Should().Be(3);
    }

    [Fact]
    public void Build_HeaderRect_CappedAt22px()
    {
        var layout = SlicerLayoutBuilder.Build(Slicer("R"), ["A"], Bounds);

        layout.HeaderRect.Height.Should().Be(22);
        layout.HeaderRect.Width.Should().Be(Bounds.Width);
        layout.HeaderRect.Top.Should().Be(Bounds.Top);
    }

    [Fact]
    public void Build_TileGeometry_MatchesSourceMath()
    {
        var slicer = Slicer("Region", "North", "South");

        var layout = SlicerLayoutBuilder.Build(slicer, ["North", "South", "East"], Bounds);

        // tileCount = 2; tileTop = top+26 = 76; tileHeight = max(14, min(22, (bottom-76-6)/2))
        // bottom = 170; (170-76-6)/2 = 44 -> clamped to 22.
        layout.Tiles.Should().HaveCount(2);
        var first = layout.Tiles[0];
        first.Rect.Left.Should().Be(Bounds.Left + 6);
        first.Rect.Top.Should().Be(Bounds.Top + 26);
        first.Rect.Width.Should().Be(Bounds.Width - 12);
        first.Rect.Height.Should().Be(22);
        // second tile offset by tileHeight + gap (3)
        layout.Tiles[1].Rect.Top.Should().Be(Bounds.Top + 26 + 22 + 3);
    }

    [Fact]
    public void Build_CapsPreviewAtFourTiles()
    {
        var slicer = Slicer("Region", "A", "B", "C", "D", "E", "F");

        var layout = SlicerLayoutBuilder.Build(slicer, ["A", "B", "C", "D", "E", "F"], Bounds);

        layout.Tiles.Should().HaveCount(4);
        layout.HasOverflow.Should().BeTrue();
        layout.VisibleItemCount.Should().Be(4);
    }

    [Fact]
    public void Build_NoAvailableItems_FallsBackToSelectedItems()
    {
        var slicer = Slicer("Region", "North", "South");

        var layout = SlicerLayoutBuilder.Build(slicer, [], Bounds);

        layout.TotalItemCount.Should().Be(2);
        layout.Tiles.Should().HaveCount(2);
    }

    [Fact]
    public void Build_ResolvesCaptionFallbacks()
    {
        var noCaption = new SlicerModel { Name = "MySlicer" };
        SlicerLayoutBuilder.Build(noCaption, ["A"], Bounds).Caption.Should().Be("MySlicer");

        var noNameNoCaption = new SlicerModel { DrawingShapeName = "Shape 3" };
        SlicerLayoutBuilder.Build(noNameNoCaption, ["A"], Bounds).Caption.Should().Be("Shape 3");
    }

    [Fact]
    public void HitTest_PointInsideTile_ReturnsTile()
    {
        var slicer = Slicer("Region", "North", "South");
        var layout = SlicerLayoutBuilder.Build(slicer, ["North", "South"], Bounds);
        var tile = layout.Tiles[1];

        var hit = SlicerLayoutBuilder.HitTest(layout, tile.Rect.Center);

        hit.Should().NotBeNull();
        hit!.Value.Caption.Should().Be("South");
    }

    [Fact]
    public void HitTest_PointOutsideAllTiles_ReturnsNull()
    {
        var layout = SlicerLayoutBuilder.Build(Slicer("Region", "A"), ["A"], Bounds);

        SlicerLayoutBuilder.HitTest(layout, new LayoutPoint(0, 0)).Should().BeNull();
    }

    [Fact]
    public void Toggle_FromCleared_SelectsAllExceptToggledItem()
    {
        var slicer = Slicer("Region"); // no selection => "all selected"

        var result = SlicerLayoutBuilder.Toggle(slicer, ["North", "South", "East"], "North");

        result.SelectedItems.Should().BeEquivalentTo("South", "East");
        result.IsCleared.Should().BeFalse();
    }

    [Fact]
    public void Toggle_AddsUnselectedItem()
    {
        var slicer = Slicer("Region", "North");

        var result = SlicerLayoutBuilder.Toggle(slicer, ["North", "South", "East"], "South");

        result.SelectedItems.Should().BeEquivalentTo("North", "South");
    }

    [Fact]
    public void Toggle_RemovingItem_FromSelection()
    {
        var slicer = Slicer("Region", "North", "South");

        var result = SlicerLayoutBuilder.Toggle(slicer, ["North", "South", "East"], "South");

        result.SelectedItems.Should().BeEquivalentTo("North");
    }

    [Fact]
    public void Toggle_SelectingEveryItem_CollapsesToCleared()
    {
        var slicer = Slicer("Region", "North", "South");

        var result = SlicerLayoutBuilder.Toggle(slicer, ["North", "South", "East"], "East");

        result.SelectedItems.Should().BeEmpty();
        result.IsCleared.Should().BeTrue();
    }

    [Fact]
    public void HasActiveFilter_TracksSelection()
    {
        SlicerLayoutBuilder.HasActiveFilter(Slicer("Region")).Should().BeFalse();
        SlicerLayoutBuilder.HasActiveFilter(Slicer("Region", "North")).Should().BeTrue();
    }
}
