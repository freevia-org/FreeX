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
                DrawLiveResizeHeaderText(dc, FormatRowHeader(++lastRow), headerRect, pixelsPerDip);
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
        _underlinePenCache.Clear();
        _defaultTextLayoutStyleCache.Clear();
        var gridPen = ShowGridLines ? GridPen : null;
        var consumer = new SplitPaneCellRenderConsumer(
            this,
            dc,
            topLeftClip,
            topRightClip,
            bottomLeftClip,
            bottomRightClip,
            pixelsPerDip,
            gridPen);
        SplitPaneCellLayoutPlanner.VisitLayouts(Viewport, MergedRegions, EditingCell, ref consumer);
    }

    private void RenderSplitPaneCell(
        DrawingContext dc,
        SplitPaneCellLayout layout,
        Pen? gridPen,
        double pixelsPerDip)
    {
        var cell = layout.Cell;
        var rect = layout.Rect;
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        var style = cell.Style;
        Brush? fill = WorksheetBackground == null ? Brushes.White : null;
        if (style?.FillColor is { } fillColor)
            fill = BrushForCellColor(fillColor, _brushCache);

        if (fill is not null || gridPen is not null)
            dc.DrawRectangle(fill, gridPen, rect);
        DrawFillPattern(dc, rect, style, _brushCache, _fillPatternPenCache);

        if (style is not null && HasVisibleCellBorder(style))
        {
            DrawBorderEdge(dc, style.BorderTop, new Point(rect.Left, rect.Top), new Point(rect.Right, rect.Top), _brushCache, _borderPenCache);
            DrawBorderEdge(dc, style.BorderBottom, new Point(rect.Left, rect.Bottom), new Point(rect.Right, rect.Bottom), _brushCache, _borderPenCache);
            DrawBorderEdge(dc, style.BorderLeft, new Point(rect.Left, rect.Top), new Point(rect.Left, rect.Bottom), _brushCache, _borderPenCache);
            DrawBorderEdge(dc, style.BorderRight, new Point(rect.Right, rect.Top), new Point(rect.Right, rect.Bottom), _brushCache, _borderPenCache);
        }

        if (cell.HasComment)
            DrawCommentIndicator(dc, rect);

        if (!ShouldDrawCellContent(cell, EditingCell))
            return;

        var textClipRect = layout.TextClipRect;
        if (cell.ConditionalIcon is { } splitIcon)
        {
            var iconLayout = CalculateConditionalIconCellLayout(rect, splitIcon);
            DrawConditionalIcon(dc, splitIcon, iconLayout.IconRect);
            if (!iconLayout.ShouldDrawText || string.IsNullOrEmpty(cell.DisplayText))
                return;

            rect = iconLayout.TextRect;
            textClipRect = AdjustConditionalIconTextClipRect(layout.TextClipRect, rect);
        }

        var hAlign = style?.HorizontalAlignment ?? CellHAlign.General;
        var isNumeric = cell.RawValue is NumberValue or DateTimeValue;
        var wrapText = style?.WrapText == true;
        var fontSize = ToDisplayFontSize((style?.FontSize > 0) ? style!.FontSize : DefaultCellFontSizePoints);
        Brush textBrush = TextBrush;

        var indentPx = (style?.IndentLevel ?? 0) * 8.0;
        if (style?.ShrinkToFit == true && !wrapText)
        {
            var typefaceKey = CreateCellTypefaceKey(style);
            var typeface = CreateCellTypeface(typefaceKey, _typefaceCache);
            var availableWidth = Math.Max(1, rect.Width - 4 - indentPx);
            fontSize = ResolveCachedShrinkFontSize(
                cell.DisplayText,
                typefaceKey,
                typeface,
                fontSize,
                availableWidth,
                ToDisplayFontSize(6),
                pixelsPerDip);
        }

        var useDefaultTextLayout = CanUseDefaultFormattedText(style, wrapText);
        var wrapMaxTextWidth = wrapText ? Math.Max(1, rect.Width - 4) : 0;
        var wrapTextAlignment = TextAlignment.Left;
        var useDefaultWrappedTextLayout = false;
        if (!useDefaultTextLayout && wrapText)
        {
            wrapTextAlignment = hAlign switch
            {
                CellHAlign.Center or CellHAlign.Justify or CellHAlign.Distributed => TextAlignment.Center,
                CellHAlign.Right => TextAlignment.Right,
                _ => TextAlignment.Left
            };
            useDefaultWrappedTextLayout = CanUseDefaultWrappedFormattedText(style);
        }
        FormattedText text;
        if (useDefaultTextLayout)
        {
            text = GetDefaultFormattedText(cell.DisplayText, fontSize, pixelsPerDip);
        }
        else if (useDefaultWrappedTextLayout)
        {
            text = GetDefaultWrappedFormattedText(cell.DisplayText, fontSize, wrapMaxTextWidth, wrapTextAlignment, pixelsPerDip);
        }
        else
        {
            var typefaceKey = CreateCellTypefaceKey(style);
            var typeface = CreateCellTypeface(typefaceKey, _typefaceCache);
            if (style?.FontColor is { } fontColor && !fontColor.IsBlack)
                textBrush = BrushForCellColor(fontColor, _brushCache);
            text = new FormattedText(
                    cell.DisplayText,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    fontSize,
                    textBrush,
                    pixelsPerDip);
        }

        if (!useDefaultTextLayout && !useDefaultWrappedTextLayout && BuildTextDecorations(style) is { } decorations)
            text.SetTextDecorations(decorations);

        if (wrapText && !useDefaultWrappedTextLayout)
        {
            text.MaxTextWidth = wrapMaxTextWidth;
            text.TextAlignment = wrapTextAlignment;
        }

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
        textY = Math.Max(rect.Top, textY);

        var textPoint = new Point(Math.Round(textX), Math.Round(textY));
        var shouldClipText = ShouldClipText(wrapText, textClipRect, text, textPoint);
        if (shouldClipText)
            dc.PushClip(GetCellClipGeometry(textClipRect));

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

    private readonly struct SplitPaneCellRenderConsumer(
        GridView grid,
        DrawingContext dc,
        RectangleGeometry topLeftClip,
        RectangleGeometry topRightClip,
        RectangleGeometry bottomLeftClip,
        RectangleGeometry bottomRightClip,
        double pixelsPerDip,
        Pen? gridPen) : ISplitPaneCellLayoutConsumer
    {
        public void AcceptLayout(SplitPaneCellLayout layout)
        {
            var clipGeometry = GetSplitPaneClipGeometryForRegion(
                layout.Region,
                topLeftClip,
                topRightClip,
                bottomLeftClip,
                bottomRightClip);
            if (clipGeometry.Rect.Width <= 0 || clipGeometry.Rect.Height <= 0)
                return;

            dc.PushClip(clipGeometry);
            grid.RenderSplitPaneCell(dc, layout, gridPen, pixelsPerDip);
            dc.Pop();
        }
    }

    private static Rect AdjustConditionalIconTextClipRect(Rect clipRect, Rect textRect)
    {
        var left = Math.Max(clipRect.Left, textRect.Left);
        return new Rect(
            left,
            textRect.Top,
            Math.Max(0, clipRect.Right - left),
            textRect.Height);
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
        var visibleLeft = rowHeaderWidth;
        var visibleTop = columnHeaderHeight;
        var visibleRight = ActualWidth;
        var visibleBottom = ActualHeight;
        _brushCache.Clear();
        _borderPenCache.Clear();
        _fillPatternPenCache.Clear();
        _typefaceCache.Clear();
        _underlinePenCache.Clear();
        _defaultTextLayoutStyleCache.Clear();
        RenderCellBackgroundBase(dc, rowHeaderWidth, columnHeaderHeight);

        var hasCellSurfaces = styleLookup.Count > 0;
        var hasMergedSurfaces = _mergeLookup.Count > 0;
        if (hasCellSurfaces || hasMergedSurfaces)
        {
            // Pass 1: non-default backgrounds and merged-cell surfaces
            RenderStyledAndMergedCellSurfaces(
                dc,
                styleLookup,
                rowLookupAll,
                colLookupAll,
                hasMergedSurfaces,
                rowHeaderWidth,
                columnHeaderHeight,
                visibleLeft,
                visibleTop,
                visibleRight,
                visibleBottom);
        }

        // Pass 2: explicit cell borders
        foreach (var cell in viewport.Cells)
        {
            if (cell.Style is not { } style || !HasVisibleCellBorder(style)) continue;
            if (!rowLookupAll.TryGetValue(cell.Row, out var rowMetric)) continue;
            if (!colLookupAll.TryGetValue(cell.Col, out var colMetric)) continue;

            double x = colMetric.LeftOffset + rowHeaderWidth;
            double y = rowMetric.TopOffset   + columnHeaderHeight;
            double w = colMetric.Width;
            double h = rowMetric.Height;
            var rect = new Rect(x, y, w, h);
            if (!IntersectsVisibleGrid(rect, visibleLeft, visibleTop, visibleRight, visibleBottom))
                continue;

            DrawBorderEdge(dc, style.BorderTop,    new Point(x,     y),     new Point(x + w, y),     _brushCache, _borderPenCache);
            DrawBorderEdge(dc, style.BorderBottom, new Point(x,     y + h), new Point(x + w, y + h), _brushCache, _borderPenCache);
            DrawBorderEdge(dc, style.BorderLeft,   new Point(x,     y),     new Point(x,     y + h), _brushCache, _borderPenCache);
            DrawBorderEdge(dc, style.BorderRight,  new Point(x + w, y),     new Point(x + w, y + h), _brushCache, _borderPenCache);
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
            if (!IntersectsVisibleGrid(rect, visibleLeft, visibleTop, visibleRight, visibleBottom))
                continue;

            DrawCommentIndicator(dc, rect);
        }

        // Pass 3: text
        var rowLookup = rowLookupAll;
        var colLookup = colLookupAll;
        var hasMergedText = _mergeLookup.Count > 0;

        HashSet<(uint Row, uint Col)>? occupied = null;

        foreach (var cell in viewport.Cells)
        {
            if (!rowLookup.TryGetValue(cell.Row, out var rowMetric)) continue;
            var cellTop = rowMetric.TopOffset + columnHeaderHeight;
            if (cellTop >= visibleBottom) continue;

            if (!colLookup.TryGetValue(cell.Col, out var colMetric)) continue;
            var cellLeft = colMetric.LeftOffset + rowHeaderWidth;
            if (cellLeft >= visibleRight) continue;

            if (!ShouldDrawCellContent(cell, EditingCell)) continue;

            var cellMerge = hasMergedText ? FindMerge(cell.Row, cell.Col) : null;
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

            var rect = new Rect(cellLeft, cellTop, w, h);
            if (rect.Bottom <= visibleTop ||
                rect.Top >= visibleBottom ||
                rect.Left >= visibleRight)
            {
                continue;
            }

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

            // Excel font sizes are typographic points; WPF measures in DIPs (96 DPI).
            // Snap to whole display DIPs so ClearType does not soften 11pt as 14.667 DIP text.
            double fontSize = ToDisplayFontSize((style?.FontSize > 0) ? style!.FontSize : DefaultCellFontSizePoints);

            Brush textBrush = TextBrush;
            double indentPx = (style?.IndentLevel ?? 0) * 8.0;
            if (style?.ShrinkToFit == true && !wrapText)
            {
                var typefaceKey = CreateCellTypefaceKey(style);
                var typeface = CreateCellTypeface(typefaceKey, _typefaceCache);
                var availableWidth = Math.Max(1, rect.Width - 4 - indentPx);
                fontSize = ResolveCachedShrinkFontSize(
                    cell.DisplayText,
                    typefaceKey,
                    typeface,
                    fontSize,
                    availableWidth,
                    ToDisplayFontSize(6),
                    pixelsPerDip);
            }

            var useDefaultTextLayout = CanUseDefaultFormattedText(style, wrapText);
            var wrapMaxTextWidth = wrapText ? Math.Max(1, rect.Width - 4) : 0;
            var wrapTextAlignment = TextAlignment.Left;
            var useDefaultWrappedTextLayout = false;
            if (!useDefaultTextLayout && wrapText)
            {
                wrapTextAlignment = hAlign switch
                {
                    CellHAlign.Center or CellHAlign.Justify or CellHAlign.Distributed => TextAlignment.Center,
                    CellHAlign.Right => TextAlignment.Right,
                    _ => TextAlignment.Left
                };
                useDefaultWrappedTextLayout = CanUseDefaultWrappedFormattedText(style);
            }

            FormattedText text;
            if (useDefaultTextLayout)
            {
                text = GetDefaultFormattedText(cell.DisplayText, fontSize, pixelsPerDip);
            }
            else if (useDefaultWrappedTextLayout)
            {
                text = GetDefaultWrappedFormattedText(cell.DisplayText, fontSize, wrapMaxTextWidth, wrapTextAlignment, pixelsPerDip);
            }
            else
            {
                var typefaceKey = CreateCellTypefaceKey(style);
                var typeface = CreateCellTypeface(typefaceKey, _typefaceCache);
                if (style?.FontColor is { } fc && !fc.IsBlack)
                    textBrush = BrushForCellColor(fc, _brushCache);

                text = new FormattedText(
                        cell.DisplayText,
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        typeface, fontSize, textBrush,
                        pixelsPerDip);
            }

            if (!useDefaultTextLayout && !useDefaultWrappedTextLayout && BuildTextDecorations(style) is { } decorations)
                text.SetTextDecorations(decorations);

            if (wrapText && !useDefaultWrappedTextLayout)
            {
                text.MaxTextWidth = wrapMaxTextWidth;
                text.TextAlignment = wrapTextAlignment;
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
            if (canOverflow && textX + text.Width > rect.Right)
            {
                occupied ??= GetOccupiedCellLookup(viewport, EditingCell);
                uint nextCol = colMetric.Col + 1;
                while (colLookup.TryGetValue(nextCol, out var nextMetric)
                       && !occupied.Contains((cell.Row, nextCol)))
                {
                    renderWidth += nextMetric.Width;
                    nextCol++;
                }

                clipRect = new Rect(rect.Left, rect.Top, renderWidth, rect.Height);
            }

            if (!IntersectsVisibleGrid(clipRect, visibleLeft, visibleTop, visibleRight, visibleBottom))
                continue;

            var textPoint = new Point(Math.Round(textX), Math.Round(textY));
            var shouldClipText = ShouldClipText(wrapText, clipRect, text, textPoint);
            if (shouldClipText)
                dc.PushClip(GetCellClipGeometry(clipRect));

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

    private void RenderStyledAndMergedCellSurfaces(
        DrawingContext dc,
        Dictionary<(uint Row, uint Col), CellStyle> styleLookup,
        Dictionary<uint, RowMetric> rowLookup,
        Dictionary<uint, ColMetric> colLookup,
        bool hasMergedSurfaces,
        double rowHeaderWidth,
        double columnHeaderHeight,
        double visibleLeft,
        double visibleTop,
        double visibleRight,
        double visibleBottom)
    {
        foreach (var entry in styleLookup)
        {
            var row = entry.Key.Row;
            var column = entry.Key.Col;
            if (hasMergedSurfaces && FindMerge(row, column).HasValue)
                continue;
            if (!rowLookup.TryGetValue(row, out var rowMetric)) continue;
            if (!colLookup.TryGetValue(column, out var colMetric)) continue;

            var rect = new Rect(
                colMetric.LeftOffset + rowHeaderWidth,
                rowMetric.TopOffset + columnHeaderHeight,
                colMetric.Width,
                rowMetric.Height);
            DrawCellSurface(dc, rect, entry.Value, isMerged: false, visibleLeft, visibleTop, visibleRight, visibleBottom);
        }

        if (!hasMergedSurfaces)
            return;

        foreach (var entry in _mergeLookup)
        {
            var merge = entry.Value;
            if (entry.Key.Row != merge.Start.Row || entry.Key.Col != merge.Start.Col)
                continue;
            if (!rowLookup.TryGetValue(merge.Start.Row, out var rowMetric)) continue;
            if (!colLookup.TryGetValue(merge.Start.Col, out var colMetric)) continue;

            double width = colMetric.Width;
            double height = rowMetric.Height;
            for (uint column = merge.Start.Col + 1; column <= merge.End.Col; column++)
                if (colLookup.TryGetValue(column, out var mergedColumn)) width += mergedColumn.Width;
            for (uint row = merge.Start.Row + 1; row <= merge.End.Row; row++)
                if (rowLookup.TryGetValue(row, out var mergedRow)) height += mergedRow.Height;

            var rect = new Rect(
                colMetric.LeftOffset + rowHeaderWidth,
                rowMetric.TopOffset + columnHeaderHeight,
                width,
                height);
            styleLookup.TryGetValue((merge.Start.Row, merge.Start.Col), out var bg);
            DrawCellSurface(dc, rect, bg, isMerged: true, visibleLeft, visibleTop, visibleRight, visibleBottom);
        }
    }

    private void DrawCellSurface(
        DrawingContext dc,
        Rect rect,
        CellStyle? bg,
        bool isMerged,
        double visibleLeft,
        double visibleTop,
        double visibleRight,
        double visibleBottom)
    {
        if (!IntersectsVisibleGrid(rect, visibleLeft, visibleTop, visibleRight, visibleBottom))
            return;

        Brush? fill = null;
        if (bg?.FillColor.HasValue == true)
        {
            fill = BrushForCellColor(bg.FillColor.Value, _brushCache);
        }
        else if (WorksheetBackground == null &&
                 (isMerged || bg?.FillPatternStyle is not null and not CellFillPatternStyle.None))
        {
            fill = Brushes.White;
        }

        if (fill is not null || isMerged)
            dc.DrawRectangle(fill, isMerged ? GridPen : null, rect);
        if (bg is not null)
            DrawFillPattern(dc, rect, bg, _brushCache, _fillPatternPenCache);
    }

    private void RenderCellBackgroundBase(DrawingContext dc, double rowHeaderWidth, double columnHeaderHeight)
    {
        if (Viewport is null || Viewport.RowMetrics.Count == 0 || Viewport.ColMetrics.Count == 0)
            return;

        var left = rowHeaderWidth;
        var top = columnHeaderHeight;
        var right = left + Viewport.ColMetrics[^1].LeftOffset + Viewport.ColMetrics[^1].Width;
        var bottom = top + Viewport.RowMetrics[^1].TopOffset + Viewport.RowMetrics[^1].Height;
        var visibleRight = Math.Min(right, ActualWidth);
        var visibleBottom = Math.Min(bottom, ActualHeight);
        var rect = new Rect(left, top, Math.Max(0, visibleRight - left), Math.Max(0, visibleBottom - top));
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        if (WorksheetBackground is null)
            dc.DrawRectangle(Brushes.White, null, rect);

        if (!ShowGridLines)
            return;

        foreach (var row in Viewport.RowMetrics)
        {
            var y = top + row.TopOffset;
            if (y > visibleBottom)
                break;

            dc.DrawLine(GridPen, new Point(left, y), new Point(visibleRight, y));
        }

        if (bottom <= visibleBottom)
            dc.DrawLine(GridPen, new Point(left, bottom), new Point(visibleRight, bottom));

        foreach (var column in Viewport.ColMetrics)
        {
            var x = left + column.LeftOffset;
            if (x > visibleRight)
                break;

            dc.DrawLine(GridPen, new Point(x, top), new Point(x, visibleBottom));
        }

        if (right <= visibleRight)
            dc.DrawLine(GridPen, new Point(right, top), new Point(right, visibleBottom));
    }

    private static bool IntersectsVisibleGrid(
        Rect rect,
        double visibleLeft,
        double visibleTop,
        double visibleRight,
        double visibleBottom) =>
        rect.Right > visibleLeft &&
        rect.Left < visibleRight &&
        rect.Bottom > visibleTop &&
        rect.Top < visibleBottom;

    private static readonly Dictionary<(uint Row, uint Col), CellStyle> EmptyRenderCellStyleLookup = new(0);

    private static Dictionary<(uint Row, uint Col), CellStyle> BuildRenderCellStyleLookup(IReadOnlyList<DisplayCell> cells)
    {
        Dictionary<(uint Row, uint Col), CellStyle>? lookup = null;
        foreach (var cell in cells)
        {
            if (cell.Style is { } style && HasVisibleCellSurface(style))
            {
                lookup ??= new Dictionary<(uint Row, uint Col), CellStyle>(cells.Count);
                lookup.Add((cell.Row, cell.Col), style);
            }
        }

        return lookup ?? EmptyRenderCellStyleLookup;
    }

    private RenderCellLookupCache GetRenderCellLookups(ViewportModel viewport)
    {
        if (_renderCellLookupCache is { } cached &&
            ReferenceEquals(cached.Cells, viewport.Cells) &&
            ReferenceEquals(cached.RowMetrics, viewport.RowMetrics) &&
            ReferenceEquals(cached.ColMetrics, viewport.ColMetrics))
        {
            return cached;
        }

        var metricLookups = GetRenderMetricLookups(viewport);
        var lookups = new RenderCellLookupCache(
            viewport.Cells,
            viewport.RowMetrics,
            viewport.ColMetrics,
            BuildRenderCellStyleLookup(viewport.Cells),
            metricLookups.Rows,
            metricLookups.Columns);
        _renderCellLookupCache = lookups;
        return lookups;
    }

    private RenderMetricLookupCache GetRenderMetricLookups(ViewportModel viewport)
    {
        if (_renderMetricLookupCache is { } cached &&
            ReferenceEquals(cached.RowMetrics, viewport.RowMetrics) &&
            ReferenceEquals(cached.ColMetrics, viewport.ColMetrics))
        {
            return cached;
        }

        var lookups = new RenderMetricLookupCache(
            viewport.RowMetrics,
            viewport.ColMetrics,
            BuildRenderRowMetricLookup(viewport.RowMetrics),
            BuildRenderColumnMetricLookup(viewport.ColMetrics));
        _renderMetricLookupCache = lookups;
        return lookups;
    }

    private HashSet<(uint Row, uint Col)> GetOccupiedCellLookup(ViewportModel viewport, CellAddress? editingCell)
    {
        if (_occupiedCellLookupCache is { } cached &&
            ReferenceEquals(cached.Cells, viewport.Cells) &&
            cached.EditingCell == editingCell)
        {
            return cached.Occupied;
        }

        var occupied = BuildOccupiedCellSet(viewport.Cells, editingCell);
        _occupiedCellLookupCache = new OccupiedCellLookupCache(viewport.Cells, editingCell, occupied);
        return occupied;
    }

    private void ClearRenderLookupCache()
    {
        _renderCellLookupCache = null;
        _renderMetricLookupCache = null;
        _occupiedCellLookupCache = null;
    }

    private const int CellClipGeometryCacheLimit = 16384;
    private const int CommentIndicatorGeometryCacheLimit = 16384;

    private RectangleGeometry GetCellClipGeometry(Rect rect)
    {
        if (_cellClipGeometryCache.TryGetValue(rect, out var cached))
            return cached;

        if (_cellClipGeometryCache.Count >= CellClipGeometryCacheLimit)
            _cellClipGeometryCache.Clear();

        var geometry = new RectangleGeometry(rect);
        geometry.Freeze();
        _cellClipGeometryCache.Add(rect, geometry);
        return geometry;
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

    private void DrawCommentIndicator(DrawingContext dc, Rect rect) =>
        dc.DrawGeometry(Brushes.Red, null, GetCommentIndicatorGeometry(rect));

    private Geometry GetCommentIndicatorGeometry(Rect rect)
    {
        if (_commentIndicatorGeometryCache.TryGetValue(rect, out var cached))
            return cached;

        if (_commentIndicatorGeometryCache.Count >= CommentIndicatorGeometryCacheLimit)
            _commentIndicatorGeometryCache.Clear();

        var geometry = CreateCommentIndicatorGeometry(rect);
        _commentIndicatorGeometryCache.Add(rect, geometry);
        return geometry;
    }

    private static Geometry CreateCommentIndicatorGeometry(Rect rect)
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
        return geometry;
    }

    private static bool ShouldClipText(
        bool wrapText,
        Rect clipRect,
        FormattedText text,
        Point textPoint)
    {
        const double tolerance = 0.5;
        if (wrapText && text.Height > clipRect.Height + tolerance)
            return true;

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
