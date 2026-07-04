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
    public void Toggle_PlainClick_FromCleared_SelectsOnlyClickedItem()
    {
        // H45: a plain click (the default, non-additive) always REPLACES the selection with just the
        // clicked item, matching Excel — it is never additive against the "all selected" default.
        var slicer = Slicer("Region"); // no selection => "all selected"

        var result = SlicerLayoutBuilder.Toggle(slicer, ["North", "South", "East"], "North");

        result.SelectedItems.Should().BeEquivalentTo("North");
        result.IsCleared.Should().BeFalse();
    }

    [Fact]
    public void Toggle_PlainClick_ReplacesExistingSingleSelectionWithClickedItem()
    {
        // H45: North is selected; plain-clicking South must replace the selection with South only,
        // not add South alongside North.
        var slicer = Slicer("Region", "North");

        var result = SlicerLayoutBuilder.Toggle(slicer, ["North", "South", "East"], "South");

        result.SelectedItems.Should().BeEquivalentTo("South");
    }

    [Fact]
    public void Toggle_PlainClick_OnSoleSelectedItem_ClearsFilter()
    {
        // Clicking the only currently-selected tile again clears the filter (Excel: deselect back to "all").
        var slicer = Slicer("Region", "South");

        var result = SlicerLayoutBuilder.Toggle(slicer, ["North", "South", "East"], "South");

        result.SelectedItems.Should().BeEmpty();
        result.IsCleared.Should().BeTrue();
    }

    [Fact]
    public void Toggle_PlainClick_WithMultiSelection_ReplacesWithSingleClickedItem()
    {
        var slicer = Slicer("Region", "North", "South");

        var result = SlicerLayoutBuilder.Toggle(slicer, ["North", "South", "East"], "South");

        result.SelectedItems.Should().BeEquivalentTo("South");
    }

    [Fact]
    public void Toggle_AdditiveClick_FromCleared_SelectsAllExceptToggledItem()
    {
        var slicer = Slicer("Region"); // no selection => "all selected"

        var result = SlicerLayoutBuilder.Toggle(slicer, ["North", "South", "East"], "North", additive: true);

        result.SelectedItems.Should().BeEquivalentTo("South", "East");
        result.IsCleared.Should().BeFalse();
    }

    [Fact]
    public void Toggle_AdditiveClick_AddsUnselectedItem()
    {
        var slicer = Slicer("Region", "North");

        var result = SlicerLayoutBuilder.Toggle(slicer, ["North", "South", "East"], "South", additive: true);

        result.SelectedItems.Should().BeEquivalentTo("North", "South");
    }

    [Fact]
    public void Toggle_AdditiveClick_RemovingItem_FromSelection()
    {
        var slicer = Slicer("Region", "North", "South");

        var result = SlicerLayoutBuilder.Toggle(slicer, ["North", "South", "East"], "South", additive: true);

        result.SelectedItems.Should().BeEquivalentTo("North");
    }

    [Fact]
    public void Toggle_AdditiveClick_SelectingEveryItem_CollapsesToCleared()
    {
        var slicer = Slicer("Region", "North", "South");

        var result = SlicerLayoutBuilder.Toggle(slicer, ["North", "South", "East"], "East", additive: true);

        result.SelectedItems.Should().BeEmpty();
        result.IsCleared.Should().BeTrue();
    }

    [Fact]
    public void HasActiveFilter_TracksSelection()
    {
        SlicerLayoutBuilder.HasActiveFilter(Slicer("Region")).Should().BeFalse();
        SlicerLayoutBuilder.HasActiveFilter(Slicer("Region", "North")).Should().BeTrue();
    }

    // --- BuildFull (multi-column, all items, showCaption) -------------------------------------------

    private static readonly LayoutRect TallBounds = new(100, 50, 200, 300);

    [Fact]
    public void BuildFull_RendersEveryAvailableItem_NotJustAPreview()
    {
        var slicer = new SlicerModel { Name = "S", Caption = "Region" };

        var layout = SlicerLayoutBuilder.BuildFull(slicer, ["A", "B", "C", "D", "E", "F"], TallBounds);

        // No four-item preview cap: all six items get tiles (the box is tall enough here).
        layout.Tiles.Should().HaveCount(6);
        layout.Tiles.Should().NotContain(static t => t.IsAllPreview);
        layout.TotalItemCount.Should().Be(6);
        layout.VisibleItemCount.Should().Be(6);
    }

    [Fact]
    public void BuildFull_TwoColumns_LaysTilesInAGrid()
    {
        // columnCount=2 (file 03's "Category"): item 0/1 on row 0, item 2/3 on row 1.
        var slicer = new SlicerModel { Name = "S", Caption = "Category", ColumnCount = 2 };

        var layout = SlicerLayoutBuilder.BuildFull(slicer, ["Marketing", "Admin", "Sales", "Content"], TallBounds);

        layout.Tiles.Should().HaveCount(4);
        var t0 = layout.Tiles[0];
        var t1 = layout.Tiles[1];
        var t2 = layout.Tiles[2];

        // Two columns: tile 1 sits to the RIGHT of tile 0 on the same row.
        t1.Rect.Top.Should().Be(t0.Rect.Top);
        t1.Rect.Left.Should().BeGreaterThan(t0.Rect.Left);
        // Tile 2 wraps to the next ROW, back at the left column.
        t2.Rect.Left.Should().Be(t0.Rect.Left);
        t2.Rect.Top.Should().BeGreaterThan(t0.Rect.Top);
        // Two columns fit side by side within the body width.
        (t0.Rect.Width + t1.Rect.Width).Should().BeLessThan(TallBounds.Width);
    }

    [Fact]
    public void BuildFull_NoSelection_MarksEveryTileSelected()
    {
        var slicer = new SlicerModel { Name = "S", Caption = "Region" };

        var layout = SlicerLayoutBuilder.BuildFull(slicer, ["A", "B", "C"], TallBounds);

        layout.Tiles.Should().OnlyContain(t => t.IsSelected, "an empty selection is Excel's unfiltered 'all' state");
        layout.HasActiveFilter.Should().BeFalse();
    }

    [Fact]
    public void BuildFull_PartialSelection_FlagsOnlySelectedTiles()
    {
        var slicer = new SlicerModel { Name = "S", Caption = "Region" };
        slicer.SelectedItems.Add("B");

        var layout = SlicerLayoutBuilder.BuildFull(slicer, ["A", "B", "C"], TallBounds);

        layout.Tiles.Single(t => t.Caption == "B").IsSelected.Should().BeTrue();
        layout.Tiles.Single(t => t.Caption == "A").IsSelected.Should().BeFalse();
        layout.Tiles.Single(t => t.Caption == "C").IsSelected.Should().BeFalse();
        layout.HasActiveFilter.Should().BeTrue();
    }

    [Fact]
    public void BuildFull_ShowCaptionTrue_HasHeaderBand_TilesClearIt()
    {
        var slicer = new SlicerModel { Name = "S", Caption = "Region", ShowCaption = true };

        var layout = SlicerLayoutBuilder.BuildFull(slicer, ["A"], TallBounds);

        layout.HeaderRect.Height.Should().Be(22);
        // Tiles start below the 22px caption band.
        layout.Tiles[0].Rect.Top.Should().BeGreaterThan(TallBounds.Top + 22);
    }

    [Fact]
    public void BuildFull_ShowCaptionFalse_OmitsHeaderBand_TilesStartNearTop()
    {
        var slicer = new SlicerModel { Name = "S", Caption = "Region", ShowCaption = false };

        var layout = SlicerLayoutBuilder.BuildFull(slicer, ["A"], TallBounds);

        layout.HeaderRect.Height.Should().Be(0, "showCaption=false drops the caption band");
        // Tiles start near the very top of the box, not below a 22px band.
        layout.Tiles[0].Rect.Top.Should().BeLessThan(TallBounds.Top + 22);
    }

    // --- Header icon chrome (multi-select + clear-filter) -------------------------------------------

    [Fact]
    public void Build_WithCaption_ProducesHeaderIconRectsInHeaderBand()
    {
        var slicer = new SlicerModel { Name = "S", Caption = "Region", ShowCaption = true };

        var layout = SlicerLayoutBuilder.Build(slicer, ["A"], Bounds);

        // Both icon rects must have positive dimensions and sit inside the header band.
        layout.MultiSelectIconRect.Width.Should().BeGreaterThan(0);
        layout.ClearFilterIconRect.Width.Should().BeGreaterThan(0);
        layout.MultiSelectIconRect.Height.Should().BeGreaterThan(0);
        layout.ClearFilterIconRect.Height.Should().BeGreaterThan(0);

        // Both icons must be within the header band vertically.
        layout.MultiSelectIconRect.Top.Should().BeGreaterThanOrEqualTo(layout.HeaderRect.Top);
        layout.ClearFilterIconRect.Top.Should().BeGreaterThanOrEqualTo(layout.HeaderRect.Top);
        layout.MultiSelectIconRect.Bottom.Should().BeLessThanOrEqualTo(layout.HeaderRect.Bottom + 1);
        layout.ClearFilterIconRect.Bottom.Should().BeLessThanOrEqualTo(layout.HeaderRect.Bottom + 1);
    }

    [Fact]
    public void Build_WithCaption_ClearFilterIsRightmostThenMultiSelect()
    {
        var layout = SlicerLayoutBuilder.Build(
            new SlicerModel { Name = "S", Caption = "Region", ShowCaption = true },
            ["A"],
            Bounds);

        // Clear-filter is the rightmost icon; multi-select is to its left.
        layout.ClearFilterIconRect.Left.Should().BeGreaterThan(layout.MultiSelectIconRect.Left,
            because: "clear-filter (×) is the rightmost icon, multi-select (☰) is to its left");
        layout.MultiSelectIconRect.Right.Should().BeLessThanOrEqualTo(layout.ClearFilterIconRect.Left + 1);

        // Both icons fit within the header bounds.
        layout.ClearFilterIconRect.Right.Should().BeLessThanOrEqualTo(layout.HeaderRect.Right + 1);
    }

    [Fact]
    public void Build_WithCaption_CaptionRectStopsBeforeHeaderIcons()
    {
        var layout = SlicerLayoutBuilder.Build(
            new SlicerModel { Name = "S", Caption = "A Very Long Region Caption", ShowCaption = true },
            ["A"],
            Bounds);

        layout.CaptionRect.Left.Should().BeGreaterThan(layout.HeaderRect.Left);
        layout.CaptionRect.Right.Should().BeLessThan(layout.MultiSelectIconRect.Left);
        layout.CaptionRect.Top.Should().Be(layout.HeaderRect.Top);
        layout.CaptionRect.Bottom.Should().Be(layout.HeaderRect.Bottom);
    }

    [Fact]
    public void BuildFull_ShowCaptionFalse_IconRectsAreEmpty()
    {
        var slicer = new SlicerModel { Name = "S", Caption = "Region", ShowCaption = false };

        var layout = SlicerLayoutBuilder.BuildFull(slicer, ["A"], TallBounds);

        layout.MultiSelectIconRect.Width.Should().Be(0, "no header band → no icons");
        layout.ClearFilterIconRect.Width.Should().Be(0, "no header band → no icons");
    }
}
