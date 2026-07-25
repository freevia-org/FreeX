using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>Sets or clears explicit row heights with undo support.</summary>
public sealed class SetRowHeightCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly uint _startRow;
    private readonly uint _endRow;
    private readonly double? _height;
    private Dictionary<uint, double>? _previousHeights;
    private HashSet<uint>? _previousHiddenRows;
    private List<(DrawingShapeModel Shape, double OldHeight)>? _previousShapeHeights;
    private List<(PictureModel Picture, double OldHeight)>? _previousPictureHeights;
    private List<(TextBoxModel TextBox, double OldHeight)>? _previousTextBoxHeights;

    public string Label => _height.HasValue ? "Set Row Height" : "AutoFit Row Height";

    public SetRowHeightCommand(SheetId sheetId, uint startRow, uint endRow, double? height)
    {
        _sheetId = sheetId;
        _startRow = Math.Min(startRow, endRow);
        _endRow = Math.Max(startRow, endRow);
        _height = height;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (!IsValidRowRange(_startRow, _endRow))
            return CommandGuards.RejectRowRangeOutsideWorksheetBounds();
        if (_height is { } height && (!double.IsFinite(height) || height is < 0 or > 409.5))
            return new CommandOutcome(false, "Row height must be from 0 to 409.5.");

        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.FormatRows) is { } protectedOutcome)
            return protectedOutcome;

        _previousHeights = RangeSnapshot.Capture(sheet.RowHeights, _startRow, _endRow);
        _previousHiddenRows = RangeSnapshot.Capture(sheet.HiddenRows, _startRow, _endRow);

        // R90-commands-shape-geometry-5-2: every drawing shape/picture/text box on the sheet is
        // rendered with Excel's default "Move and size with cells" anchor behavior (FreeX has no
        // alternate "Don't move or size with cells" anchor mode yet -- see DrawingAnchorResizeHelper),
        // so an object whose vertical extent overlaps the rows being resized must grow/shrink right
        // along with them. Must run BEFORE the row heights below are mutated, since it measures the
        // objects' current on-sheet extent using the sheet's still-unmutated row heights.
        _previousShapeHeights = DrawingAnchorResizeHelper.ResizeForRowRange(
            sheet.DrawingShapes, sheet, _startRow, _endRow, _height,
            s => s.Anchor, s => s.AnchorOffsetY, s => s.Height, (s, h) => s.Height = h);
        _previousPictureHeights = DrawingAnchorResizeHelper.ResizeForRowRange(
            sheet.Pictures, sheet, _startRow, _endRow, _height,
            p => p.Anchor, p => p.AnchorOffsetY, p => p.Height, (p, h) => p.Height = h);
        _previousTextBoxHeights = DrawingAnchorResizeHelper.ResizeForRowRange(
            sheet.TextBoxes, sheet, _startRow, _endRow, _height,
            t => t.Anchor, t => t.AnchorOffsetY, t => t.Height, (t, h) => t.Height = h);

        for (uint row = _startRow; row <= _endRow; row++)
        {
            if (_height == 0)
            {
                sheet.RowHeights.Remove(row);
                sheet.HiddenRows.Add(row);
            }
            else if (_height.HasValue)
            {
                sheet.RowHeights[row] = _height.Value;
                sheet.HiddenRows.Remove(row);
            }
            else
            {
                sheet.RowHeights.Remove(row);
            }
        }

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousHeights is null) return;
        var sheet = ctx.GetSheet(_sheetId);
        RangeSnapshot.Restore(sheet.RowHeights, _startRow, _endRow, _previousHeights);
        if (_previousHiddenRows is not null)
            RangeSnapshot.Restore(sheet.HiddenRows, _startRow, _endRow, _previousHiddenRows);

        if (_previousShapeHeights is not null)
            foreach (var (shape, oldHeight) in _previousShapeHeights)
                shape.Height = oldHeight;
        if (_previousPictureHeights is not null)
            foreach (var (picture, oldHeight) in _previousPictureHeights)
                picture.Height = oldHeight;
        if (_previousTextBoxHeights is not null)
            foreach (var (textBox, oldHeight) in _previousTextBoxHeights)
                textBox.Height = oldHeight;
    }

    private static bool IsValidRowRange(uint startRow, uint endRow) =>
        startRow >= 1 && endRow <= CellAddress.MaxRow;

}

/// <summary>Sets or clears explicit column widths with undo support.</summary>
public sealed class SetColumnWidthCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly uint _startCol;
    private readonly uint _endCol;
    private readonly double? _width;
    private Dictionary<uint, double>? _previousWidths;
    private HashSet<uint>? _previousHiddenCols;
    private List<(DrawingShapeModel Shape, double OldWidth)>? _previousShapeWidths;
    private List<(PictureModel Picture, double OldWidth)>? _previousPictureWidths;
    private List<(TextBoxModel TextBox, double OldWidth)>? _previousTextBoxWidths;

    public string Label => _width.HasValue ? "Set Column Width" : "AutoFit Column Width";

    public SetColumnWidthCommand(SheetId sheetId, uint startCol, uint endCol, double? width)
    {
        _sheetId = sheetId;
        _startCol = Math.Min(startCol, endCol);
        _endCol = Math.Max(startCol, endCol);
        _width = width;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (!IsValidColumnRange(_startCol, _endCol))
            return CommandGuards.RejectColumnRangeOutsideWorksheetBounds();
        if (_width is { } width && (!double.IsFinite(width) || width is < 0 or > 255))
            return new CommandOutcome(false, "Column width must be from 0 to 255.");

        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.FormatColumns) is { } protectedOutcome)
            return protectedOutcome;

        _previousWidths = RangeSnapshot.Capture(sheet.ColumnWidths, _startCol, _endCol);
        _previousHiddenCols = RangeSnapshot.Capture(sheet.HiddenCols, _startCol, _endCol);

        // R90-commands-shape-geometry-5-2: honor Excel's default "Move and size with cells" anchor
        // behavior -- see the matching comment in SetRowHeightCommand.Apply above. Must run BEFORE
        // the column widths below are mutated.
        _previousShapeWidths = DrawingAnchorResizeHelper.ResizeForColumnRange(
            sheet.DrawingShapes, sheet, _startCol, _endCol, _width,
            s => s.Anchor, s => s.AnchorOffsetX, s => s.Width, (s, w) => s.Width = w);
        _previousPictureWidths = DrawingAnchorResizeHelper.ResizeForColumnRange(
            sheet.Pictures, sheet, _startCol, _endCol, _width,
            p => p.Anchor, p => p.AnchorOffsetX, p => p.Width, (p, w) => p.Width = w);
        _previousTextBoxWidths = DrawingAnchorResizeHelper.ResizeForColumnRange(
            sheet.TextBoxes, sheet, _startCol, _endCol, _width,
            t => t.Anchor, t => t.AnchorOffsetX, t => t.Width, (t, w) => t.Width = w);

        for (uint col = _startCol; col <= _endCol; col++)
        {
            if (_width == 0)
            {
                sheet.ColumnWidths.Remove(col);
                sheet.HiddenCols.Add(col);
            }
            else if (_width.HasValue)
            {
                sheet.ColumnWidths[col] = _width.Value;
                sheet.HiddenCols.Remove(col);
            }
            else
            {
                sheet.ColumnWidths.Remove(col);
            }
        }

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousWidths is null) return;
        var sheet = ctx.GetSheet(_sheetId);
        RangeSnapshot.Restore(sheet.ColumnWidths, _startCol, _endCol, _previousWidths);
        if (_previousHiddenCols is not null)
            RangeSnapshot.Restore(sheet.HiddenCols, _startCol, _endCol, _previousHiddenCols);

        if (_previousShapeWidths is not null)
            foreach (var (shape, oldWidth) in _previousShapeWidths)
                shape.Width = oldWidth;
        if (_previousPictureWidths is not null)
            foreach (var (picture, oldWidth) in _previousPictureWidths)
                picture.Width = oldWidth;
        if (_previousTextBoxWidths is not null)
            foreach (var (textBox, oldWidth) in _previousTextBoxWidths)
                textBox.Width = oldWidth;
    }

    private static bool IsValidColumnRange(uint startCol, uint endCol) =>
        startCol >= 1 && endCol <= CellAddress.MaxCol;

}

/// <summary>
/// R90-commands-shape-geometry-5-2: resizes drawing shapes/pictures/text boxes so they grow or
/// shrink when the column(s)/row(s) they're anchored over are resized, matching Excel's default
/// "Move and size with cells" anchor behavior. FreeX does not yet model an alternate "Don't move or
/// size with cells" anchor mode for these objects (see the editAs-preserve investigation note on
/// <c>XlsxDrawingAnchorApplier</c>), so every drawing object is treated as the default.
/// </summary>
internal static class DrawingAnchorResizeHelper
{
    /// <summary>Matches the character-width-to-DIP-pixel conversion used when a column-anchored
    /// object's width is first derived from its span at load time (see
    /// <c>XlsxDrawingAnchorApplier.SumColumnPixels</c>), so a live resize keeps the same units.</summary>
    private const double ColumnCharWidthToPixelFactor = 8;

    private static double ColumnPixelWidth(Sheet sheet, uint col) =>
        sheet.IsColEffectivelyHidden(col) ? 0 : sheet.ColumnWidths.GetValueOrDefault(col, sheet.DefaultColumnWidth) * ColumnCharWidthToPixelFactor;

    private static double RowPixelHeight(Sheet sheet, uint row) =>
        sheet.IsRowEffectivelyHidden(row) ? 0 : sheet.RowHeights.GetValueOrDefault(row, sheet.DefaultRowHeight);

    private static double EffectiveNewColumnWidth(Sheet sheet, uint col, double? width)
    {
        if (width == 0)
            return 0;
        if (width.HasValue)
            return width.Value * ColumnCharWidthToPixelFactor;
        // A null width clears the explicit override without touching hidden state, matching the
        // mutation loop in SetColumnWidthCommand.Apply.
        return sheet.IsColEffectivelyHidden(col) ? 0 : sheet.DefaultColumnWidth * ColumnCharWidthToPixelFactor;
    }

    private static double EffectiveNewRowHeight(Sheet sheet, uint row, double? height)
    {
        if (height == 0)
            return 0;
        if (height.HasValue)
            return height.Value;
        return sheet.IsRowEffectivelyHidden(row) ? 0 : sheet.DefaultRowHeight;
    }

    /// <summary>
    /// Resizes every item in <paramref name="items"/> whose horizontal extent overlaps the
    /// [<paramref name="startCol"/>, <paramref name="endCol"/>] column range about to be resized to
    /// <paramref name="width"/> (matching <c>SetColumnWidthCommand</c>'s own null/zero/value
    /// semantics). Must be called BEFORE the column widths are actually mutated on <paramref
    /// name="sheet"/> -- it measures each item's current pixel span using the sheet's still-live old
    /// widths. An item that only partially overlaps a resized column is scaled by the fraction of
    /// that column's old width it actually covers, so a shape anchored mid-column keeps its
    /// unaffected portion accurate. Returns the (item, old width) pairs actually changed, for undo.
    /// </summary>
    internal static List<(T Item, double OldExtent)> ResizeForColumnRange<T>(
        IReadOnlyList<T> items,
        Sheet sheet,
        uint startCol,
        uint endCol,
        double? width,
        Func<T, CellAddress> getAnchor,
        Func<T, double> getOffsetX,
        Func<T, double> getWidth,
        Action<T, double> setWidth)
    {
        var changed = new List<(T, double)>();
        if (items.Count == 0)
            return changed;

        // Pre-mutation left-edge pixel position of every column from 1..endCol, in the coordinate
        // space the objects are currently rendered in.
        var columnLeft = new double[endCol + 1];
        var oldColumnWidth = new double[endCol + 1];
        double running = 0;
        for (uint col = 1; col <= endCol; col++)
        {
            columnLeft[col] = running;
            var w = ColumnPixelWidth(sheet, col);
            oldColumnWidth[col] = w;
            running += w;
        }

        foreach (var item in items)
        {
            var anchor = getAnchor(item);
            if (anchor.Col < 1 || anchor.Col > endCol)
                continue; // anchored at/after the resize range's right edge -- unaffected

            var itemLeft = columnLeft[anchor.Col] + getOffsetX(item);
            var itemWidth = getWidth(item);
            var itemRight = itemLeft + itemWidth;

            double totalDelta = 0;
            for (var col = Math.Max(startCol, 1u); col <= endCol; col++)
            {
                var oldWidth = oldColumnWidth[col];
                if (oldWidth <= 0)
                    continue;

                var newWidth = EffectiveNewColumnWidth(sheet, col, width);
                var delta = newWidth - oldWidth;
                if (delta == 0)
                    continue;

                var colLeft = columnLeft[col];
                var colRight = colLeft + oldWidth;
                var overlap = Math.Min(itemRight, colRight) - Math.Max(itemLeft, colLeft);
                if (overlap <= 0)
                    continue;

                totalDelta += delta * (overlap / oldWidth);
            }

            if (totalDelta == 0)
                continue;

            changed.Add((item, itemWidth));
            setWidth(item, Math.Max(0, itemWidth + totalDelta));
        }

        return changed;
    }

    /// <summary>Row-axis counterpart of <see cref="ResizeForColumnRange{T}"/>; see its remarks.</summary>
    internal static List<(T Item, double OldExtent)> ResizeForRowRange<T>(
        IReadOnlyList<T> items,
        Sheet sheet,
        uint startRow,
        uint endRow,
        double? height,
        Func<T, CellAddress> getAnchor,
        Func<T, double> getOffsetY,
        Func<T, double> getHeight,
        Action<T, double> setHeight)
    {
        var changed = new List<(T, double)>();
        if (items.Count == 0)
            return changed;

        var rowTop = new double[endRow + 1];
        var oldRowHeight = new double[endRow + 1];
        double running = 0;
        for (uint row = 1; row <= endRow; row++)
        {
            rowTop[row] = running;
            var h = RowPixelHeight(sheet, row);
            oldRowHeight[row] = h;
            running += h;
        }

        foreach (var item in items)
        {
            var anchor = getAnchor(item);
            if (anchor.Row < 1 || anchor.Row > endRow)
                continue;

            var itemTop = rowTop[anchor.Row] + getOffsetY(item);
            var itemHeight = getHeight(item);
            var itemBottom = itemTop + itemHeight;

            double totalDelta = 0;
            for (var row = Math.Max(startRow, 1u); row <= endRow; row++)
            {
                var oldHeight = oldRowHeight[row];
                if (oldHeight <= 0)
                    continue;

                var newHeight = EffectiveNewRowHeight(sheet, row, height);
                var delta = newHeight - oldHeight;
                if (delta == 0)
                    continue;

                var rowTopPx = rowTop[row];
                var rowBottomPx = rowTopPx + oldHeight;
                var overlap = Math.Min(itemBottom, rowBottomPx) - Math.Max(itemTop, rowTopPx);
                if (overlap <= 0)
                    continue;

                totalDelta += delta * (overlap / oldHeight);
            }

            if (totalDelta == 0)
                continue;

            changed.Add((item, itemHeight));
            setHeight(item, Math.Max(0, itemHeight + totalDelta));
        }

        return changed;
    }
}
