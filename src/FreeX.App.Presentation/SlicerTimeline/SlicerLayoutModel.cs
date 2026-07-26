using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.SlicerTimeline;

/// <summary>
/// One button tile inside a slicer's layout: the rectangle to draw in pixel space, the caption
/// text, and the visual state flags the renderers key their fills/colors on. <see cref="ItemIndex"/>
/// is the index into the slicer's full available-item list (or -1 for the synthetic "all" preview
/// tile shown when nothing is selected), so hit-testing can map a clicked tile back to its item.
/// </summary>
public readonly record struct SlicerTileLayout(
    LayoutRect Rect,
    string Caption,
    bool IsSelected,
    bool IsEnabled,
    bool IsAllPreview,
    int ItemIndex);

/// <summary>
/// The portable, framework-free layout of a slicer button-grid inside a bounds rectangle. Carries the
/// header bar rectangle and caption, the body rectangle, the laid-out tiles, and overflow/scroll
/// information for the renderers. The geometry is faithful to the source desktop renderer: a header
/// band capped at 22px, a tile grid starting 26px from the top, and a four-tile preview cap.
/// <para>
/// <see cref="MultiSelectIconRect"/> and <see cref="ClearFilterIconRect"/> are the two header chrome
/// icons Excel shows at the top-right of the header band: a multi-select toggle and a clear-filter
/// (funnel-×) glyph. Both are zero-height when the header is absent (ShowCaption=false).
/// </para>
/// <para>
/// <see cref="MultiSelectModeActive"/> echoes back the transient multi-select mode the caller passed
/// into <see cref="SlicerLayoutBuilder.Build"/>/<see cref="SlicerLayoutBuilder.BuildFull"/> (the mode
/// itself has no persisted home on <c>SlicerModel</c> -- like a held modifier key, it lives only in the
/// shell's own view-state) so a renderer can draw the icon pressed/highlighted while it is active. Use
/// <see cref="SlicerLayoutBuilder.HitTestMultiSelectIcon"/> to detect a click on the icon and flip that
/// shell-side flag before the next layout pass.
/// </para>
/// </summary>
public sealed record SlicerLayoutModel(
    string Name,
    string Caption,
    string? SourceFieldName,
    bool HasActiveFilter,
    LayoutRect Bounds,
    LayoutRect HeaderRect,
    LayoutRect CaptionRect,
    LayoutRect BodyRect,
    IReadOnlyList<SlicerTileLayout> Tiles,
    int TotalItemCount,
    int VisibleItemCount,
    bool HasOverflow,
    LayoutRect MultiSelectIconRect,
    LayoutRect ClearFilterIconRect,
    bool MultiSelectModeActive = false);

/// <summary>
/// The result of toggling a slicer tile: the new selection set ready to hand to the selection command.
/// An empty <see cref="SelectedItems"/> means "no filter" (all items shown), matching the source
/// toggle semantics where selecting every item collapses back to the cleared state.
/// </summary>
public sealed record SlicerToggleResult(string SlicerName, IReadOnlyList<string> SelectedItems)
{
    /// <summary>True when the toggle results in the cleared / unfiltered state (no active filter).</summary>
    public bool IsCleared => SelectedItems.Count == 0;
}

/// <summary>
/// Builds <see cref="SlicerLayoutModel"/> button-grid layouts, performs point hit-testing against the
/// tiles, and computes selection toggles. Pure geometry and set math; the desktop renderers turn the
/// returned rectangles into their own drawing primitives and wire the toggle result into the
/// selection command.
/// </summary>
public static class SlicerLayoutBuilder
{
    // Faithful to the source desktop renderer's slicer math.
    private const double HeaderMaxHeight = 22;
    private const double TileGridTopInset = 26;
    private const double TileHorizontalInset = 6;
    private const double TileBottomPadding = 6;
    private const double TileGap = 3;
    private const double TileMinHeight = 14;
    private const double TileMaxHeight = 22;
    private const int TilePreviewCap = 4;

    // Header icon chrome sizes (matching Excel's slicer header icon dimensions).
    // Two icons sit at the header top-right: [multi-select ☰] [clear-filter ✕]
    // Each icon slot is 16×16 px with a 2px gap between them and a 3px right margin.
    private const double HeaderIconSize = 16;
    private const double HeaderIconGap = 2;
    private const double HeaderIconRightMargin = 3;

    /// <summary>
    /// Builds a button-grid layout for <paramref name="slicer"/> within <paramref name="bounds"/>.
    /// <paramref name="availableItems"/> is the full set of items offered by the slicer's source field;
    /// when empty, the slicer's own selected items are used as the available set (matching the source
    /// fallback). The preview shows up to four tiles; a single "all" tile is shown when nothing is
    /// selected. <paramref name="multiSelectMode"/> is the caller's own transient multi-select-toggle
    /// state (see <see cref="SlicerLayoutModel.MultiSelectModeActive"/>); it is only echoed into the
    /// returned layout for the icon's pressed/highlighted appearance and does not change tile geometry.
    /// </summary>
    public static SlicerLayoutModel Build(
        SlicerModel slicer,
        IEnumerable<string> availableItems,
        LayoutRect bounds,
        bool multiSelectMode = false)
    {
        ArgumentNullException.ThrowIfNull(slicer);
        ArgumentNullException.ThrowIfNull(availableItems);

        var items = OrderAvailableItems(slicer, availableItems);
        var selected = new HashSet<string>(slicer.SelectedItems, StringComparer.CurrentCultureIgnoreCase);
        var hasActiveFilter = slicer.SelectedItems.Count > 0;

        var headerRect = new LayoutRect(
            bounds.X,
            bounds.Y,
            bounds.Width,
            Math.Min(HeaderMaxHeight, bounds.Height));
        var bodyRect = bounds;

        var tiles = BuildTiles(slicer, items, selected, bounds);
        var (multiSelectRect, clearFilterRect) = BuildHeaderIconRects(headerRect, slicer.ShowCaption);
        var captionRect = BuildCaptionRect(headerRect, multiSelectRect);

        return new SlicerLayoutModel(
            Name: slicer.Name,
            Caption: ResolveCaption(slicer),
            SourceFieldName: slicer.SourceFieldName,
            HasActiveFilter: hasActiveFilter,
            Bounds: bounds,
            HeaderRect: headerRect,
            CaptionRect: captionRect,
            BodyRect: bodyRect,
            Tiles: tiles,
            TotalItemCount: items.Count,
            VisibleItemCount: tiles.Count(static tile => !tile.IsAllPreview),
            HasOverflow: items.Count > tiles.Count(static tile => !tile.IsAllPreview),
            MultiSelectIconRect: multiSelectRect,
            ClearFilterIconRect: clearFilterRect,
            MultiSelectModeActive: multiSelectMode);
    }

    /// <summary>
    /// Builds the FULL faithful button-grid for <paramref name="slicer"/>: every available item gets a
    /// tile (no four-item preview cap), laid out in <see cref="SlicerModel.ColumnCount"/> columns, with
    /// each tile flagged selected/unselected. An empty selection renders every tile as selected ("all"),
    /// matching Excel's unfiltered state. The caption band is omitted when
    /// <see cref="SlicerModel.ShowCaption"/> is false, and the tiles start from the top of the box.
    /// This is what the WPF/headless renderer draws; the cross-platform overlay uses it for parity.
    /// </summary>
    public static SlicerLayoutModel BuildFull(
        SlicerModel slicer,
        IEnumerable<string> availableItems,
        LayoutRect bounds,
        bool multiSelectMode = false)
    {
        ArgumentNullException.ThrowIfNull(slicer);
        ArgumentNullException.ThrowIfNull(availableItems);

        var items = OrderAvailableItems(slicer, availableItems);
        var selected = new HashSet<string>(slicer.SelectedItems, StringComparer.CurrentCultureIgnoreCase);
        var hasActiveFilter = slicer.SelectedItems.Count > 0;
        var showCaption = slicer.ShowCaption;

        var headerHeight = showCaption ? Math.Min(HeaderMaxHeight, bounds.Height) : 0;
        var headerRect = new LayoutRect(bounds.X, bounds.Y, bounds.Width, headerHeight);
        var bodyRect = bounds;

        var tiles = BuildFullTiles(slicer, items, selected, bounds, showCaption);
        var visibleCount = tiles.Count;
        var (multiSelectRect, clearFilterRect) = BuildHeaderIconRects(headerRect, showCaption);
        var captionRect = BuildCaptionRect(headerRect, multiSelectRect);

        return new SlicerLayoutModel(
            Name: slicer.Name,
            Caption: ResolveCaption(slicer),
            SourceFieldName: slicer.SourceFieldName,
            HasActiveFilter: hasActiveFilter,
            Bounds: bounds,
            HeaderRect: headerRect,
            CaptionRect: captionRect,
            BodyRect: bodyRect,
            Tiles: tiles,
            TotalItemCount: items.Count,
            VisibleItemCount: visibleCount,
            HasOverflow: items.Count > visibleCount,
            MultiSelectIconRect: multiSelectRect,
            ClearFilterIconRect: clearFilterRect,
            MultiSelectModeActive: multiSelectMode);
    }

    // Lays out every available item across slicer.ColumnCount columns, capping the visible ROWS to what
    // fits the box (overflow flagged via HasOverflow). Mirrors GridView.DrawNativeSlicerControl's math.
    private static IReadOnlyList<SlicerTileLayout> BuildFullTiles(
        SlicerModel slicer,
        IReadOnlyList<string> items,
        HashSet<string> selected,
        LayoutRect bounds,
        bool showCaption)
    {
        if (items.Count == 0)
            return [];

        var columnCount = Math.Max(1, slicer.ColumnCount);
        var tileTop = bounds.Top + (showCaption ? TileGridTopInset : 4);
        var availableHeight = bounds.Bottom - tileTop - TileBottomPadding;
        if (availableHeight <= 0)
            return [];

        var rowCount = (int)Math.Ceiling(items.Count / (double)columnCount);
        var rowsThatFit = Math.Max(1, (int)(availableHeight / (TileMinHeight + TileGap)));
        var visibleRows = Math.Min(rowCount, rowsThatFit);
        var tileHeight = Math.Max(
            TileMinHeight,
            Math.Min(TileMaxHeight, (availableHeight - (visibleRows - 1) * TileGap) / visibleRows));

        var totalGap = TileGap * (columnCount - 1);
        var tileWidth = Math.Max(1, (bounds.Width - TileHorizontalInset * 2 - totalGap) / columnCount);

        // No active filter => everything is "selected" (the unfiltered/all state).
        var allSelected = selected.Count == 0;
        var visibleTileCount = Math.Min(items.Count, visibleRows * columnCount);
        var tiles = new List<SlicerTileLayout>(visibleTileCount);
        for (var index = 0; index < visibleTileCount; index++)
        {
            var row = index / columnCount;
            var col = index % columnCount;
            var rect = new LayoutRect(
                bounds.Left + TileHorizontalInset + col * (tileWidth + TileGap),
                tileTop + row * (tileHeight + TileGap),
                tileWidth,
                tileHeight);

            var caption = items[index];
            tiles.Add(new SlicerTileLayout(
                rect,
                caption,
                IsSelected: allSelected || selected.Contains(caption),
                IsEnabled: true,
                IsAllPreview: false,
                ItemIndex: index));
        }

        return tiles;
    }

    /// <summary>
    /// Returns the tile at <paramref name="point"/>, or <c>null</c> when the point falls outside every
    /// tile rectangle. Tiles are non-overlapping so the first containing tile is returned.
    /// </summary>
    public static SlicerTileLayout? HitTest(SlicerLayoutModel layout, LayoutPoint point)
    {
        ArgumentNullException.ThrowIfNull(layout);
        foreach (var tile in layout.Tiles)
        {
            if (Contains(tile.Rect, point))
                return tile;
        }

        return null;
    }

    /// <summary>
    /// Returns true when <paramref name="point"/> falls inside <see cref="SlicerLayoutModel.MultiSelectIconRect"/>
    /// -- the header's multi-select toggle icon. A shell should call this BEFORE its tile hit-test (the
    /// same ordering <see cref="SlicerTimelineInteractionPlanner.BuildSlicerClearFilterCommand"/> already
    /// uses for the clear-filter icon) so a click on the icon flips the shell's own multi-select mode
    /// flag instead of falling through to whatever tile happens to sit underneath it. The rect is
    /// zero-sized when the header is hidden (<see cref="SlicerModel.ShowCaption"/> = false), so this
    /// always returns false in that case.
    /// </summary>
    public static bool HitTestMultiSelectIcon(SlicerLayoutModel layout, LayoutPoint point)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var rect = layout.MultiSelectIconRect;
        return rect.Width > 0 && rect.Height > 0 && Contains(rect, point);
    }

    /// <summary>
    /// Computes the new selection set after clicking <paramref name="caption"/> against the slicer's
    /// current selection, matching Excel's slicer click semantics.
    /// <para>
    /// A plain click (<paramref name="additive"/> = false, the default) REPLACES the whole selection
    /// with just the clicked item — unless that item is already the sole selected item, in which case
    /// clicking it again clears the filter back to "everything selected" (Excel deselects a lone active
    /// tile on a second plain click).
    /// </para>
    /// <para>
    /// A Ctrl+click (<paramref name="additive"/> = true) is additive: an empty current selection is
    /// treated as "everything selected"; toggling the item adds or removes it from the existing
    /// selection; and selecting every available item collapses back to the cleared (unfiltered) state.
    /// </para>
    /// </summary>
    public static SlicerToggleResult Toggle(
        SlicerModel slicer,
        IEnumerable<string> availableItems,
        string caption,
        bool additive = false)
    {
        ArgumentNullException.ThrowIfNull(slicer);
        ArgumentNullException.ThrowIfNull(availableItems);
        ArgumentNullException.ThrowIfNull(caption);

        if (!additive)
        {
            // Plain click: replace the selection with just this item, unless it is already the lone
            // selected item — in which case Excel treats a second plain click as clearing the filter.
            var isSoleSelection = slicer.SelectedItems.Count == 1 &&
                string.Equals(slicer.SelectedItems[0], caption, StringComparison.CurrentCultureIgnoreCase);
            return new SlicerToggleResult(slicer.Name, isSoleSelection ? [] : [caption]);
        }

        var allItems = OrderAvailableItems(slicer, availableItems);
        var selected = slicer.SelectedItems.Count == 0
            ? new HashSet<string>(allItems, StringComparer.CurrentCultureIgnoreCase)
            : new HashSet<string>(slicer.SelectedItems, StringComparer.CurrentCultureIgnoreCase);

        if (!selected.Remove(caption))
            selected.Add(caption);
        if (selected.Count == allItems.Count)
            selected.Clear();

        return new SlicerToggleResult(slicer.Name, selected.ToList());
    }

    /// <summary>True when the slicer has at least one explicitly selected item (an active filter).</summary>
    public static bool HasActiveFilter(SlicerModel slicer)
    {
        ArgumentNullException.ThrowIfNull(slicer);
        return slicer.SelectedItems.Count > 0;
    }

    private static IReadOnlyList<SlicerTileLayout> BuildTiles(
        SlicerModel slicer,
        IReadOnlyList<string> items,
        HashSet<string> selected,
        LayoutRect bounds)
    {
        var selectedCount = slicer.SelectedItems.Count;
        var tileCount = selectedCount == 0 ? 1 : Math.Min(TilePreviewCap, selectedCount);

        var tileTop = bounds.Top + TileGridTopInset;
        var tileHeight = Math.Max(
            TileMinHeight,
            Math.Min(TileMaxHeight, (bounds.Bottom - tileTop - TileBottomPadding) / tileCount));
        var tileWidth = Math.Max(1, bounds.Width - (TileHorizontalInset * 2));

        var tiles = new List<SlicerTileLayout>(tileCount);
        for (var index = 0; index < tileCount; index++)
        {
            var rect = new LayoutRect(
                bounds.Left + TileHorizontalInset,
                tileTop + (index * (tileHeight + TileGap)),
                tileWidth,
                tileHeight);

            if (selectedCount == 0)
            {
                var caption = slicer.SourceFieldName ?? NullIfEmpty(slicer.CacheName) ?? "All";
                tiles.Add(new SlicerTileLayout(rect, caption, IsSelected: true, IsEnabled: true, IsAllPreview: true, ItemIndex: -1));
                continue;
            }

            var itemCaption = slicer.SelectedItems[index];
            var itemIndex = IndexOf(items, itemCaption);
            tiles.Add(new SlicerTileLayout(
                rect,
                itemCaption,
                IsSelected: selected.Contains(itemCaption),
                IsEnabled: true,
                IsAllPreview: false,
                ItemIndex: itemIndex));
        }

        return tiles;
    }

    private static IReadOnlyList<string> OrderAvailableItems(SlicerModel slicer, IEnumerable<string> availableItems)
    {
        var items = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var item in availableItems)
            items.Add(item);

        if (items.Count == 0)
        {
            foreach (var item in slicer.SelectedItems)
                items.Add(item);
        }

        return items.ToList();
    }

    private static int IndexOf(IReadOnlyList<string> items, string caption)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (string.Equals(items[index], caption, StringComparison.CurrentCultureIgnoreCase))
                return index;
        }

        return -1;
    }

    private static string ResolveCaption(SlicerModel slicer)
    {
        if (!string.IsNullOrWhiteSpace(slicer.Caption))
            return slicer.Caption.Trim();
        if (!string.IsNullOrWhiteSpace(slicer.Name))
            return slicer.Name.Trim();
        return string.IsNullOrWhiteSpace(slicer.DrawingShapeName) ? "Filter" : slicer.DrawingShapeName.Trim();
    }

    // Computes the two header-chrome icon slots matching Excel's slicer header layout:
    //   Right edge → [rightMargin] [clear-filter icon] [gap] [multi-select icon] [rightMargin] → caption
    // Both rects collapse to zero height when the header is absent (showCaption=false).
    private static (LayoutRect MultiSelect, LayoutRect ClearFilter) BuildHeaderIconRects(
        LayoutRect headerRect,
        bool showCaption)
    {
        if (!showCaption || headerRect.Height <= 0)
        {
            var empty = new LayoutRect(headerRect.Right, headerRect.Top, 0, 0);
            return (empty, empty);
        }

        var iconY = headerRect.Top + (headerRect.Height - HeaderIconSize) / 2;
        // Clear-filter (✕ funnel) is the rightmost icon.
        var clearFilterLeft = headerRect.Right - HeaderIconRightMargin - HeaderIconSize;
        var clearFilterRect = new LayoutRect(clearFilterLeft, iconY, HeaderIconSize, HeaderIconSize);
        // Multi-select (☰) is to the left of clear-filter, with a gap.
        var multiSelectLeft = clearFilterLeft - HeaderIconGap - HeaderIconSize;
        var multiSelectRect = new LayoutRect(multiSelectLeft, iconY, HeaderIconSize, HeaderIconSize);
        return (multiSelectRect, clearFilterRect);
    }

    private static LayoutRect BuildCaptionRect(LayoutRect headerRect, LayoutRect firstIconRect)
    {
        if (headerRect.Height <= 0 || headerRect.Width <= 0)
            return new LayoutRect(headerRect.X, headerRect.Y, 0, 0);

        const double CaptionPaddingLeft = 6;
        const double CaptionIconGap = 4;
        var right = firstIconRect.Width > 0
            ? Math.Max(headerRect.Left + CaptionPaddingLeft, firstIconRect.Left - CaptionIconGap)
            : headerRect.Right - CaptionPaddingLeft;

        return new LayoutRect(
            headerRect.Left + CaptionPaddingLeft,
            headerRect.Top,
            Math.Max(0, right - headerRect.Left - CaptionPaddingLeft),
            headerRect.Height);
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static bool Contains(LayoutRect rect, LayoutPoint point) =>
        point.X >= rect.Left && point.X <= rect.Right &&
        point.Y >= rect.Top && point.Y <= rect.Bottom;
}
