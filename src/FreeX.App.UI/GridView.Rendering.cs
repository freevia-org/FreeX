using System.Globalization;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

using FreeX.Core.Model;

using CellHAlign = FreeX.Core.Model.HorizontalAlignment;
using CellVAlign = FreeX.Core.Model.VerticalAlignment;

namespace FreeX.App.UI;

public partial class GridView
{
    // Grid rendering for freeze dividers, selection, headers, cells, borders, and text decorations.

    private PageMarginGuideLayout? GetPageMarginGuidePixels(GridRange printArea)
    {
        if (Viewport == null) return null;

        return PageMarginGuideLayoutPlanner.CalculateGuide(
            Viewport,
            printArea,
            ActualRowHeaderWidth,
            EffectiveColHeaderHeight,
            PaperSize,
            PageOrientation,
            PageMargins);
    }

    private void RenderGridLines(DrawingContext dc)
    {
        // Grid lines are drawn as cell/header rectangle borders.
    }

    private void RenderLiveResizeContinuation(DrawingContext dc)
    {
        if (Viewport is null)
            return;

        var rowHeaderWidth = ActualRowHeaderWidth;
        var columnHeaderHeight = EffectiveColHeaderHeight;
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var gridLeft = rowHeaderWidth;
        var gridTop = columnHeaderHeight;
        var gridRight = Viewport.ColMetrics.Count > 0
            ? rowHeaderWidth + Viewport.ColMetrics[^1].LeftOffset + Viewport.ColMetrics[^1].Width
            : gridLeft;
        var gridBottom = Viewport.RowMetrics.Count > 0
            ? columnHeaderHeight + Viewport.RowMetrics[^1].TopOffset + Viewport.RowMetrics[^1].Height
            : gridTop;

        if (ActualWidth > gridRight)
            RenderLiveResizeColumnContinuation(dc, gridRight, gridTop, pixelsPerDip);

        if (ActualHeight > gridBottom)
            RenderLiveResizeRowContinuation(dc, gridLeft, gridRight, gridBottom, pixelsPerDip);

        if (ActualWidth > gridRight && ActualHeight > gridBottom)
        {
            DrawLiveResizeHorizontalGridLines(dc, gridRight, ActualWidth, gridBottom);
            DrawLiveResizeVerticalGridLines(dc, gridRight, ActualHeight);
        }
    }

    private void RenderLiveResizeColumnContinuation(
        DrawingContext dc,
        double startX,
        double gridTop,
        double pixelsPerDip)
    {
        if (startX >= ActualWidth)
            return;

        var columnWidth = Viewport!.ColMetrics.Count > 0
            ? Math.Max(1, Viewport.ColMetrics[^1].Width)
            : 64;
        var lastColumn = Viewport.ColMetrics.Count > 0 ? Viewport.ColMetrics[^1].Col : 0;
        var height = Math.Max(0, ActualHeight - gridTop);
        if (height > 0)
            dc.DrawRectangle(Brushes.White, null, new Rect(startX, gridTop, ActualWidth - startX, height));

        for (var x = startX; x < ActualWidth; x += columnWidth)
        {
            var width = Math.Min(columnWidth, ActualWidth - x);
            if (EffectiveColHeaderHeight > 0)
            {
                var headerRect = new Rect(x, 0, width, EffectiveColHeaderHeight);
                dc.DrawRectangle(HeaderBackgroundBrush, GridPen, headerRect);
                DrawLiveResizeHeaderText(dc, FormatColumnHeader(++lastColumn, UseR1C1ReferenceStyle), headerRect, pixelsPerDip);
            }

            if (height > 0)
                dc.DrawLine(GridPen, new Point(x, gridTop), new Point(x, ActualHeight));
        }

        if (height > 0)
            dc.DrawLine(GridPen, new Point(ActualWidth, gridTop), new Point(ActualWidth, ActualHeight));

        DrawLiveResizeHorizontalGridLines(dc, startX, ActualWidth, gridTop);
    }

    private void RenderLiveResizeRowContinuation(
        DrawingContext dc,
        double gridLeft,
        double gridRight,
        double startY,
        double pixelsPerDip)
    {
        if (startY >= ActualHeight)
            return;

        var rowHeight = Viewport!.RowMetrics.Count > 0
            ? Math.Max(1, Viewport.RowMetrics[^1].Height)
            : 20;
        var lastRow = Viewport.RowMetrics.Count > 0 ? Viewport.RowMetrics[^1].Row : 0;
        var width = Math.Max(0, gridRight - gridLeft);
        if (width > 0)
            dc.DrawRectangle(Brushes.White, null, new Rect(gridLeft, startY, width, ActualHeight - startY));

        for (var y = startY; y < ActualHeight; y += rowHeight)
        {
            var height = Math.Min(rowHeight, ActualHeight - y);
            if (ActualRowHeaderWidth > 0)
            {
                var headerRect = new Rect(0, y, ActualRowHeaderWidth, height);
                dc.DrawRectangle(HeaderBackgroundBrush, GridPen, headerRect);
                DrawLiveResizeHeaderText(dc, (++lastRow).ToString(CultureInfo.InvariantCulture), headerRect, pixelsPerDip);
            }

            if (width > 0)
                dc.DrawLine(GridPen, new Point(gridLeft, y), new Point(gridRight, y));
        }

        if (width > 0)
            dc.DrawLine(GridPen, new Point(gridLeft, ActualHeight), new Point(gridRight, ActualHeight));

        DrawLiveResizeVerticalGridLines(dc, gridLeft, ActualHeight);
    }

    private void DrawLiveResizeHorizontalGridLines(DrawingContext dc, double startX, double endX, double startY)
    {
        if (endX <= startX || startY >= ActualHeight)
            return;

        var rowHeight = Viewport!.RowMetrics.Count > 0
            ? Math.Max(1, Viewport.RowMetrics[^1].Height)
            : 20;
        for (var y = startY; y < ActualHeight; y += rowHeight)
            dc.DrawLine(GridPen, new Point(startX, y), new Point(endX, y));

        dc.DrawLine(GridPen, new Point(startX, ActualHeight), new Point(endX, ActualHeight));
    }

    private void DrawLiveResizeVerticalGridLines(DrawingContext dc, double startX, double endY)
    {
        if (startX >= ActualWidth || endY <= EffectiveColHeaderHeight)
            return;

        var columnWidth = Viewport!.ColMetrics.Count > 0
            ? Math.Max(1, Viewport.ColMetrics[^1].Width)
            : 64;
        for (var x = startX; x < ActualWidth; x += columnWidth)
            dc.DrawLine(GridPen, new Point(x, EffectiveColHeaderHeight), new Point(x, endY));

        dc.DrawLine(GridPen, new Point(ActualWidth, EffectiveColHeaderHeight), new Point(ActualWidth, endY));
    }

    private void DrawLiveResizeHeaderText(DrawingContext dc, string text, Rect rect, double pixelsPerDip)
    {
        if (string.IsNullOrWhiteSpace(text) || rect.Width <= 4 || rect.Height <= 4)
            return;

        var formatted = GetDefaultFormattedText(text, 11, pixelsPerDip);

        dc.DrawText(formatted, new Point(
            rect.Left + Math.Max(2, (rect.Width - formatted.Width) / 2),
            rect.Top + Math.Max(1, (rect.Height - formatted.Height) / 2)));
    }

    private void RenderSplitPaneCells(DrawingContext dc)
    {
        if (Viewport?.SplitPanes?.Cells is not { Count: > 0 }) return;

        var clips = CalculateSplitPaneClipRects(Viewport, ActualWidth, ActualHeight);
        var topLeftClip = FrozenClipGeometry(clips.TopLeft);
        var topRightClip = FrozenClipGeometry(clips.TopRight);
        var bottomLeftClip = FrozenClipGeometry(clips.BottomLeft);
        var bottomRightClip = FrozenClipGeometry(clips.BottomRight);
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        _brushCache.Clear();
        _borderPenCache.Clear();
        _fillPatternPenCache.Clear();
        _typefaceCache.Clear();
        foreach (var layout in CalculateSplitPaneCellLayouts(Viewport, MergedRegions))
        {
            var cell = layout.Cell;
            var rect = layout.Rect;
            var style = cell.Style;
            var clipGeometry = GetSplitPaneClipGeometryForRegion(
                layout.Region,
                topLeftClip,
                topRightClip,
                bottomLeftClip,
                bottomRightClip);
            dc.PushClip(clipGeometry);

            Brush? fill = WorksheetBackground == null ? Brushes.White : null;
            if (style?.FillColor is { } fillColor)
                fill = BrushForCellColor(fillColor, _brushCache);

            dc.DrawRectangle(fill, GridPen, rect);
            DrawFillPattern(dc, rect, style, _brushCache, _fillPatternPenCache);

            if (style is not null)
            {
                DrawBorderEdge(dc, style.BorderTop, new Point(rect.Left, rect.Top), new Point(rect.Right, rect.Top), _brushCache, _borderPenCache);
                DrawBorderEdge(dc, style.BorderBottom, new Point(rect.Left, rect.Bottom), new Point(rect.Right, rect.Bottom), _brushCache, _borderPenCache);
                DrawBorderEdge(dc, style.BorderLeft, new Point(rect.Left, rect.Top), new Point(rect.Left, rect.Bottom), _brushCache, _borderPenCache);
                DrawBorderEdge(dc, style.BorderRight, new Point(rect.Right, rect.Top), new Point(rect.Right, rect.Bottom), _brushCache, _borderPenCache);
            }

            if (cell.HasComment)
                DrawCommentIndicator(dc, rect);

            if (!ShouldDrawCellContent(cell, EditingCell))
            {
                dc.Pop();
                continue;
            }

            if (cell.ConditionalIcon is { } splitIcon)
            {
                var iconLayout = CalculateConditionalIconCellLayout(rect, splitIcon);
                DrawConditionalIcon(dc, splitIcon, iconLayout.IconRect);
                if (!iconLayout.ShouldDrawText || string.IsNullOrEmpty(cell.DisplayText))
                {
                    dc.Pop();
                    continue;
                }

                rect = iconLayout.TextRect;
            }

            var hAlign = style?.HorizontalAlignment ?? CellHAlign.General;
            var isNumeric = cell.RawValue is NumberValue or DateTimeValue;
            var typeface = CreateCellTypeface(style, _typefaceCache);
            var fontSize = ToDisplayFontSize((style?.FontSize > 0) ? style!.FontSize : DefaultCellFontSizePoints);
            Brush textBrush = TextBrush;
            if (style?.FontColor is { } fontColor && !fontColor.IsBlack)
                textBrush = BrushForCellColor(fontColor, _brushCache);

            var indentPx = (style?.IndentLevel ?? 0) * 8.0;
            if (style?.ShrinkToFit == true && style.WrapText != true)
            {
                var availableWidth = Math.Max(1, rect.Width - 4 - indentPx);
                fontSize = ResolveShrinkFontSize(
                    fontSize,
                    availableWidth,
                    size => new FormattedText(
                        cell.DisplayText,
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        typeface,
                        size,
                        textBrush,
                        pixelsPerDip).Width,
                    ToDisplayFontSize(6));
            }

            var text = new FormattedText(
                cell.DisplayText,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                textBrush,
                pixelsPerDip);

            if (BuildTextDecorations(style) is { } decorations)
                text.SetTextDecorations(decorations);

            var textX = hAlign switch
            {
                CellHAlign.Right => rect.Right - Math.Min(text.Width, rect.Width - 2) - 2,
                CellHAlign.Justify or CellHAlign.Distributed => rect.Left + (rect.Width - text.Width) / 2,
                CellHAlign.Center => rect.Left + (rect.Width - text.Width) / 2,
                CellHAlign.General when isNumeric => rect.Right - Math.Min(text.Width, rect.Width - 2) - 2,
                _ => rect.Left + 2 + indentPx
            };
            var textY = style?.VerticalAlignment switch
            {
                CellVAlign.Top => rect.Top + 1,
                CellVAlign.Center => rect.Top + (rect.Height - text.Height) / 2,
                CellVAlign.Bottom => rect.Bottom - text.Height - 1,
                _ => rect.Top + (rect.Height - text.Height) / 2
            };

            dc.PushClip(new RectangleGeometry(layout.TextClipRect));
            dc.DrawText(text, new Point(Math.Round(textX), Math.Round(Math.Max(rect.Top, textY))));
            dc.Pop();
            dc.Pop();
        }
    }

    private static RectangleGeometry FrozenClipGeometry(Rect rect)
    {
        var geometry = new RectangleGeometry(rect);
        geometry.Freeze();
        return geometry;
    }

    private static RectangleGeometry GetSplitPaneClipGeometryForRegion(
        SplitPaneRegion region,
        RectangleGeometry topLeft,
        RectangleGeometry topRight,
        RectangleGeometry bottomLeft,
        RectangleGeometry bottomRight) =>
        region switch
        {
            SplitPaneRegion.TopLeft => topLeft,
            SplitPaneRegion.TopRight => topRight,
            SplitPaneRegion.BottomLeft => bottomLeft,
            _ => bottomRight
        };

    private GridRange? FindMerge(uint row, uint col)
    {
        return _mergeLookup.TryGetValue((row, col), out var r) ? r : null;
    }

    private void RenderCells(DrawingContext dc)
    {
        var viewport = Viewport!;
        var lookups = GetRenderCellLookups(viewport);
        var styleLookup = lookups.Styles;
        var rowLookupAll = lookups.Rows;
        var colLookupAll = lookups.Columns;
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var rowHeaderWidth = ActualRowHeaderWidth;
        var columnHeaderHeight = EffectiveColHeaderHeight;
        _brushCache.Clear();
        _borderPenCache.Clear();
        _fillPatternPenCache.Clear();
        _typefaceCache.Clear();
        _underlinePenCache.Clear();
        RenderCellBackgroundBase(dc, rowHeaderWidth, columnHeaderHeight);

        // Pass 1: non-default backgrounds and merged-cell surfaces
        foreach (var rowMetric in viewport.RowMetrics)
        {
            foreach (var colMetric in viewport.ColMetrics)
            {
                var merge = FindMerge(rowMetric.Row, colMetric.Col);
                if (merge.HasValue && (rowMetric.Row != merge.Value.Start.Row || colMetric.Col != merge.Value.Start.Col))
                    continue;
                styleLookup.TryGetValue((rowMetric.Row, colMetric.Col), out var bg);
                if (bg is null && !merge.HasValue)
                    continue;

                double w = colMetric.Width;
                double h = rowMetric.Height;

                if (merge.HasValue)
                {
                    for (uint c2 = merge.Value.Start.Col + 1; c2 <= merge.Value.End.Col; c2++)
                        if (colLookupAll.TryGetValue(c2, out var cm2)) w += cm2.Width;
                    for (uint r2 = merge.Value.Start.Row + 1; r2 <= merge.Value.End.Row; r2++)
                        if (rowLookupAll.TryGetValue(r2, out var rm2)) h += rm2.Height;
                }

                var rect = new Rect(
                    colMetric.LeftOffset + rowHeaderWidth, rowMetric.TopOffset + columnHeaderHeight, w, h);

                Brush? fill = null;
                if (bg?.FillColor.HasValue == true)
                {
                    fill = BrushForCellColor(bg.FillColor.Value, _brushCache);
                }
                else if (WorksheetBackground == null &&
                         (merge.HasValue || bg?.FillPatternStyle is not null and not CellFillPatternStyle.None))
                {
                    fill = Brushes.White;
                }

                if (fill is not null || merge.HasValue)
                    dc.DrawRectangle(fill, merge.HasValue ? GridPen : null, rect);
                if (bg is not null)
                    DrawFillPattern(dc, rect, bg, _brushCache, _fillPatternPenCache);
            }
        }

        // Pass 2: explicit cell borders
        foreach (var cell in viewport.Cells)
        {
            if (cell.Style == null) continue;
            if (!rowLookupAll.TryGetValue(cell.Row, out var rowMetric)) continue;
            if (!colLookupAll.TryGetValue(cell.Col, out var colMetric)) continue;

            double x = colMetric.LeftOffset + rowHeaderWidth;
            double y = rowMetric.TopOffset   + columnHeaderHeight;
            double w = colMetric.Width;
            double h = rowMetric.Height;

            DrawBorderEdge(dc, cell.Style.BorderTop,    new Point(x,     y),     new Point(x + w, y),     _brushCache, _borderPenCache);
            DrawBorderEdge(dc, cell.Style.BorderBottom, new Point(x,     y + h), new Point(x + w, y + h), _brushCache, _borderPenCache);
            DrawBorderEdge(dc, cell.Style.BorderLeft,   new Point(x,     y),     new Point(x,     y + h), _brushCache, _borderPenCache);
            DrawBorderEdge(dc, cell.Style.BorderRight,  new Point(x + w, y),     new Point(x + w, y + h), _brushCache, _borderPenCache);
        }

        // Pass 2b: comment/note indicators
        foreach (var cell in viewport.Cells)
        {
            if (!cell.HasComment) continue;
            if (!rowLookupAll.TryGetValue(cell.Row, out var rowMetric)) continue;
            if (!colLookupAll.TryGetValue(cell.Col, out var colMetric)) continue;

            var rect = new Rect(
                colMetric.LeftOffset + rowHeaderWidth,
                rowMetric.TopOffset + columnHeaderHeight,
                colMetric.Width,
                rowMetric.Height);
            DrawCommentIndicator(dc, rect);
        }

        // Pass 3: text
        var rowLookup = rowLookupAll;
        var colLookup = colLookupAll;

        var occupied = GetOccupiedCellLookup(viewport, EditingCell);

        foreach (var cell in viewport.Cells)
        {
            if (!rowLookup.TryGetValue(cell.Row, out var rowMetric)) continue;
            if (!colLookup.TryGetValue(cell.Col, out var colMetric)) continue;
            if (!ShouldDrawCellContent(cell, EditingCell)) continue;

            var cellMerge = FindMerge(cell.Row, cell.Col);
            if (cellMerge.HasValue && (cell.Row != cellMerge.Value.Start.Row || cell.Col != cellMerge.Value.Start.Col))
                continue;

            var style = cell.Style;
            double w = colMetric.Width;
            double h = rowMetric.Height;

            if (cellMerge.HasValue)
            {
                for (uint c2 = cellMerge.Value.Start.Col + 1; c2 <= cellMerge.Value.End.Col; c2++)
                    if (colLookup.TryGetValue(c2, out var cm2)) w += cm2.Width;
                for (uint r2 = cellMerge.Value.Start.Row + 1; r2 <= cellMerge.Value.End.Row; r2++)
                    if (rowLookup.TryGetValue(r2, out var rm2)) h += rm2.Height;
            }

            var rect = new Rect(
                colMetric.LeftOffset + rowHeaderWidth, rowMetric.TopOffset + columnHeaderHeight, w, h);
            double renderWidth = w;

            if (cell.ConditionalIcon is { } icon)
            {
                var iconLayout = CalculateConditionalIconCellLayout(rect, icon);
                DrawConditionalIcon(dc, icon, iconLayout.IconRect);
                if (!iconLayout.ShouldDrawText || string.IsNullOrEmpty(cell.DisplayText))
                    continue;
                rect = iconLayout.TextRect;
                renderWidth = rect.Width;
            }

            var hAlign   = style?.HorizontalAlignment ?? CellHAlign.General;
            bool isNumeric = cell.RawValue is NumberValue or DateTimeValue;
            bool wrapText  = style?.WrapText == true;

            bool canOverflow = CanOverflowCellText(style, cell.RawValue, cell.DisplayText, cellMerge);

            if (canOverflow)
            {
                uint nextCol = colMetric.Col + 1;
                while (colLookup.TryGetValue(nextCol, out var nextMetric)
                       && !occupied.Contains((cell.Row, nextCol)))
                {
                    renderWidth += nextMetric.Width;
                    nextCol++;
                }
            }

            var typeface = CreateCellTypeface(style, _typefaceCache);

            // Excel font sizes are typographic points; WPF measures in DIPs (96 DPI).
            // Snap to whole display DIPs so ClearType does not soften 11pt as 14.667 DIP text.
            double fontSize = ToDisplayFontSize((style?.FontSize > 0) ? style!.FontSize : DefaultCellFontSizePoints);

            Brush textBrush = TextBrush;
            if (style?.FontColor is { } fc && !fc.IsBlack)
                textBrush = BrushForCellColor(fc, _brushCache);

            double indentPx = (style?.IndentLevel ?? 0) * 8.0;
            if (style?.ShrinkToFit == true && !wrapText)
            {
                var availableWidth = Math.Max(1, rect.Width - 4 - indentPx);
                fontSize = ResolveShrinkFontSize(
                    fontSize,
                    availableWidth,
                    size => new FormattedText(
                        cell.DisplayText,
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        typeface,
                        size,
                        textBrush,
                        pixelsPerDip).Width,
                    ToDisplayFontSize(6));
            }

            var text = style is null
                ? GetDefaultFormattedText(cell.DisplayText, fontSize, pixelsPerDip)
                : new FormattedText(
                    cell.DisplayText,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface, fontSize, textBrush,
                    pixelsPerDip);

            if (BuildTextDecorations(style) is { } decorations)
                text.SetTextDecorations(decorations);

            if (wrapText)
            {
                text.MaxTextWidth  = Math.Max(1, rect.Width - 4);
                text.TextAlignment = hAlign switch
                {
                    CellHAlign.Center or CellHAlign.Justify or CellHAlign.Distributed => System.Windows.TextAlignment.Center,
                    CellHAlign.Right => System.Windows.TextAlignment.Right,
                    _ => System.Windows.TextAlignment.Left
                };
            }

            double textX = hAlign switch
            {
                CellHAlign.Right  => rect.Right - Math.Min(text.Width, rect.Width - 2) - 2,
                CellHAlign.Justify or CellHAlign.Distributed => rect.Left + (rect.Width - text.Width) / 2,
                CellHAlign.Center => rect.Left  + (rect.Width - text.Width) / 2,
                CellHAlign.General when isNumeric
                                  => rect.Right - Math.Min(text.Width, rect.Width - 2) - 2,
                _                 => rect.Left + 2 + indentPx
            };

            double textY = style?.VerticalAlignment switch
            {
                CellVAlign.Top    => rect.Top + 1,
                CellVAlign.Center => rect.Top + (rect.Height - text.Height) / 2,
                CellVAlign.Bottom => rect.Bottom - text.Height - 1,
                _                 => rect.Top  + (rect.Height - text.Height) / 2
            };
            textY = Math.Max(rect.Top, textY);

            var clipRect = new Rect(rect.Left, rect.Top, renderWidth, rect.Height);
            var textPoint = new Point(Math.Round(textX), Math.Round(textY));
            var shouldClipText = ShouldClipText(style, wrapText, clipRect, text, textPoint);
            if (shouldClipText)
                dc.PushClip(new RectangleGeometry(clipRect));

            dc.DrawText(text, textPoint);

            if (style?.DoubleUnderline == true)
            {
                double uY = textY + text.Height + 1;
                var underlinePen = UnderlinePenForTextBrush(textBrush, _underlinePenCache);
                dc.DrawLine(underlinePen, new Point(textX, uY), new Point(textX + text.Width, uY));
                dc.DrawLine(underlinePen, new Point(textX, uY + 2), new Point(textX + text.Width, uY + 2));
            }

            if (shouldClipText)
                dc.Pop();
        }
    }

    private void RenderCellBackgroundBase(DrawingContext dc, double rowHeaderWidth, double columnHeaderHeight)
    {
        if (Viewport is null || Viewport.RowMetrics.Count == 0 || Viewport.ColMetrics.Count == 0)
            return;

        var left = rowHeaderWidth;
        var top = columnHeaderHeight;
        var right = left + Viewport.ColMetrics[^1].LeftOffset + Viewport.ColMetrics[^1].Width;
        var bottom = top + Viewport.RowMetrics[^1].TopOffset + Viewport.RowMetrics[^1].Height;
        var rect = new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        if (WorksheetBackground is null)
            dc.DrawRectangle(Brushes.White, null, rect);

        if (!ShowGridLines)
            return;

        foreach (var row in Viewport.RowMetrics)
        {
            var y = top + row.TopOffset;
            dc.DrawLine(GridPen, new Point(left, y), new Point(right, y));
        }

        dc.DrawLine(GridPen, new Point(left, bottom), new Point(right, bottom));

        foreach (var column in Viewport.ColMetrics)
        {
            var x = left + column.LeftOffset;
            dc.DrawLine(GridPen, new Point(x, top), new Point(x, bottom));
        }

        dc.DrawLine(GridPen, new Point(right, top), new Point(right, bottom));
    }

    private static Dictionary<(uint Row, uint Col), CellStyle> BuildRenderCellStyleLookup(IReadOnlyList<DisplayCell> cells)
    {
        var lookup = new Dictionary<(uint Row, uint Col), CellStyle>();
        foreach (var cell in cells)
        {
            if (cell.Style is { } style)
                lookup.Add((cell.Row, cell.Col), style);
        }

        return lookup;
    }

    private RenderCellLookupCache GetRenderCellLookups(ViewportModel viewport)
    {
        if (_renderCellLookupCache is { } cached && ReferenceEquals(cached.Viewport, viewport))
            return cached;

        var lookups = new RenderCellLookupCache(
            viewport,
            BuildRenderCellStyleLookup(viewport.Cells),
            BuildRenderRowMetricLookup(viewport.RowMetrics),
            BuildRenderColumnMetricLookup(viewport.ColMetrics));
        _renderCellLookupCache = lookups;
        return lookups;
    }

    private HashSet<(uint Row, uint Col)> GetOccupiedCellLookup(ViewportModel viewport, CellAddress? editingCell)
    {
        if (_occupiedCellLookupCache is { } cached &&
            ReferenceEquals(cached.Viewport, viewport) &&
            cached.EditingCell == editingCell)
        {
            return cached.Occupied;
        }

        var occupied = BuildOccupiedCellSet(viewport.Cells, editingCell);
        _occupiedCellLookupCache = new OccupiedCellLookupCache(viewport, editingCell, occupied);
        return occupied;
    }

    private void ClearRenderLookupCache()
    {
        _renderCellLookupCache = null;
        _occupiedCellLookupCache = null;
    }

    private static Dictionary<uint, RowMetric> BuildRenderRowMetricLookup(IReadOnlyList<RowMetric> rows)
    {
        var lookup = new Dictionary<uint, RowMetric>(rows.Count);
        foreach (var row in rows)
            lookup.Add(row.Row, row);

        return lookup;
    }

    private static Dictionary<uint, ColMetric> BuildRenderColumnMetricLookup(IReadOnlyList<ColMetric> columns)
    {
        var lookup = new Dictionary<uint, ColMetric>(columns.Count);
        foreach (var column in columns)
            lookup.Add(column.Col, column);

        return lookup;
    }

    private static void DrawCommentIndicator(DrawingContext dc, Rect rect)
    {
        const double size = 7;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(rect.Right - size, rect.Top), isFilled: true, isClosed: true);
            context.LineTo(new Point(rect.Right, rect.Top), isStroked: true, isSmoothJoin: false);
            context.LineTo(new Point(rect.Right, rect.Top + size), isStroked: true, isSmoothJoin: false);
        }
        geometry.Freeze();
        dc.DrawGeometry(Brushes.Red, null, geometry);
    }

    private static bool ShouldClipText(
        CellStyle? style,
        bool wrapText,
        Rect clipRect,
        FormattedText text,
        Point textPoint)
    {
        if (style is not null || wrapText)
            return true;

        const double tolerance = 0.5;
        return textPoint.X < clipRect.Left - tolerance ||
            textPoint.Y < clipRect.Top - tolerance ||
            textPoint.X + text.Width > clipRect.Right + tolerance ||
            textPoint.Y + text.Height > clipRect.Bottom + tolerance;
    }

    private static Pen UnderlinePenForTextBrush(Brush textBrush, Dictionary<Brush, Pen> underlinePenCache)
    {
        if (underlinePenCache.TryGetValue(textBrush, out var pen))
            return pen;

        pen = new Pen(textBrush, 1);
        pen.Freeze();
        underlinePenCache[textBrush] = pen;
        return pen;
    }

}
