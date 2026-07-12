using System.Globalization;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Calc;
using FreeX.Core.Model;

using CellHAlign = FreeX.Core.Model.HorizontalAlignment;
using CellVAlign = FreeX.Core.Model.VerticalAlignment;

namespace FreeX.App.UI;

public partial class GridView
{
    // Grid rendering for freeze dividers, selection, headers, cells, borders, and text decorations.

    // Mirrors the active sheet's Sheet.IsRightToLeft flag (Excel's sheetView rightToLeft="1") so cell
    // text can be mirrored the same way the Avalonia shell already does via
    // CellTextOrientationLayoutPlanner.ResolveIsEffectivelyRightToLeft. Host code (e.g. MainWindow.Viewport.cs)
    // is expected to bind this to the active sheet whenever it changes, the same way ActiveSheetId is set.
    public static readonly DependencyProperty IsSheetRightToLeftProperty =
        DependencyProperty.Register(nameof(IsSheetRightToLeft), typeof(bool), typeof(GridView),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public bool IsSheetRightToLeft
    {
        get => (bool)GetValue(IsSheetRightToLeftProperty);
        set => SetValue(IsSheetRightToLeftProperty, value);
    }

    private FreeX.App.Presentation.PageLayout.PageMarginGuideLayout? GetPageMarginGuidePixels(GridRange printArea)
    {
        if (Viewport == null) return null;

        return FreeX.App.Presentation.PageLayout.PageMarginGuideLayoutPlanner.CalculateGuide(
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

    private void RenderViewportContinuation(DrawingContext dc)
    {
        if (Viewport is null)
            return;

        var rowHeaderWidth = ActualRowHeaderWidth;
        var columnHeaderHeight = EffectiveColHeaderHeight;
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var viewportWidth = GetLogicalViewportWidth();
        var viewportHeight = GetLogicalViewportHeight();
        var gridLeft = rowHeaderWidth;
        var gridTop = columnHeaderHeight;
        var gridRight = Viewport.ColMetrics.Count > 0
            ? rowHeaderWidth + Viewport.ColMetrics[^1].LeftOffset + Viewport.ColMetrics[^1].Width
            : gridLeft;
        var gridBottom = Viewport.RowMetrics.Count > 0
            ? columnHeaderHeight + Viewport.RowMetrics[^1].TopOffset + Viewport.RowMetrics[^1].Height
            : gridTop;

        if (viewportWidth > gridRight)
            RenderViewportColumnContinuation(dc, gridRight, gridTop, viewportWidth, viewportHeight, pixelsPerDip);

        if (viewportHeight > gridBottom)
            RenderViewportRowContinuation(dc, gridLeft, gridRight, gridBottom, viewportHeight, pixelsPerDip);

        if (viewportWidth > gridRight && viewportHeight > gridBottom)
        {
            DrawViewportContinuationHorizontalGridLines(dc, gridRight, viewportWidth, gridBottom, viewportHeight);
            DrawViewportContinuationVerticalGridLines(dc, gridRight, viewportHeight);
        }
    }

    private void RenderViewportColumnContinuation(
        DrawingContext dc,
        double startX,
        double gridTop,
        double viewportWidth,
        double viewportHeight,
        double pixelsPerDip)
    {
        if (startX >= viewportWidth)
            return;

        var columnWidth = Viewport!.ColMetrics.Count > 0
            ? Math.Max(1, Viewport.ColMetrics[^1].Width)
            : 64;
        var lastColumn = Viewport.ColMetrics.Count > 0 ? Viewport.ColMetrics[^1].Col : 0;
        var height = Math.Max(0, viewportHeight - gridTop);
        if (height > 0)
            dc.DrawRectangle(Brushes.White, null, new Rect(startX, gridTop, viewportWidth - startX, height));

        for (var x = startX; x < viewportWidth; x += columnWidth)
        {
            var width = Math.Min(columnWidth, viewportWidth - x);
            if (EffectiveColHeaderHeight > 0)
            {
                var headerRect = new Rect(x, 0, width, EffectiveColHeaderHeight);
                dc.DrawRectangle(HeaderBackgroundBrush, GridPen, headerRect);
                DrawLiveResizeHeaderText(dc, FormatColumnHeader(++lastColumn, UseR1C1ReferenceStyle), headerRect, pixelsPerDip);
            }

            if (height > 0)
                dc.DrawLine(GridPen, new Point(x, gridTop), new Point(x, viewportHeight));
        }

        if (height > 0)
            dc.DrawLine(GridPen, new Point(viewportWidth, gridTop), new Point(viewportWidth, viewportHeight));

        DrawViewportContinuationHorizontalGridLines(dc, startX, viewportWidth, gridTop, viewportHeight);
    }

    private void RenderViewportRowContinuation(
        DrawingContext dc,
        double gridLeft,
        double gridRight,
        double startY,
        double viewportHeight,
        double pixelsPerDip)
    {
        if (startY >= viewportHeight)
            return;

        var rowHeight = Viewport!.RowMetrics.Count > 0
            ? Math.Max(1, Viewport.RowMetrics[^1].Height)
            : 20;
        var lastRow = Viewport.RowMetrics.Count > 0 ? Viewport.RowMetrics[^1].Row : 0;
        var width = Math.Max(0, gridRight - gridLeft);
        if (width > 0)
            dc.DrawRectangle(Brushes.White, null, new Rect(gridLeft, startY, width, viewportHeight - startY));

        for (var y = startY; y < viewportHeight; y += rowHeight)
        {
            var height = Math.Min(rowHeight, viewportHeight - y);
            if (ActualRowHeaderWidth > 0)
            {
                var headerRect = new Rect(0, y, ActualRowHeaderWidth, height);
                dc.DrawRectangle(HeaderBackgroundBrush, GridPen, headerRect);
                lastRow++;
                if (height >= rowHeight)
                    DrawLiveResizeHeaderText(dc, FormatRowHeader(lastRow), headerRect, pixelsPerDip);
            }
            else
            {
                lastRow++;
            }

            if (width > 0)
                dc.DrawLine(GridPen, new Point(gridLeft, y), new Point(gridRight, y));
        }

        if (width > 0)
            dc.DrawLine(GridPen, new Point(gridLeft, viewportHeight), new Point(gridRight, viewportHeight));

        DrawViewportContinuationVerticalGridLines(dc, gridLeft, viewportHeight);
    }

    private void DrawViewportContinuationHorizontalGridLines(
        DrawingContext dc,
        double startX,
        double endX,
        double startY,
        double viewportHeight)
    {
        if (endX <= startX || startY >= viewportHeight)
            return;

        var rowHeight = Viewport!.RowMetrics.Count > 0
            ? Math.Max(1, Viewport.RowMetrics[^1].Height)
            : 20;
        for (var y = startY; y < viewportHeight; y += rowHeight)
            dc.DrawLine(GridPen, new Point(startX, y), new Point(endX, y));

        dc.DrawLine(GridPen, new Point(startX, viewportHeight), new Point(endX, viewportHeight));
    }

    private void DrawViewportContinuationVerticalGridLines(DrawingContext dc, double startX, double endY)
    {
        var viewportWidth = GetLogicalViewportWidth();
        if (startX >= viewportWidth || endY <= EffectiveColHeaderHeight)
            return;

        var columnWidth = Viewport!.ColMetrics.Count > 0
            ? Math.Max(1, Viewport.ColMetrics[^1].Width)
            : 64;
        for (var x = startX; x < viewportWidth; x += columnWidth)
            dc.DrawLine(GridPen, new Point(x, EffectiveColHeaderHeight), new Point(x, endY));

        dc.DrawLine(GridPen, new Point(viewportWidth, EffectiveColHeaderHeight), new Point(viewportWidth, endY));
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

        var clips = CalculateSplitPaneClipRects(Viewport, GetLogicalViewportWidth(), GetLogicalViewportHeight());
        var topLeftClip = FrozenClipGeometry(clips.TopLeft);
        var topRightClip = FrozenClipGeometry(clips.TopRight);
        var bottomLeftClip = FrozenClipGeometry(clips.BottomLeft);
        var bottomRightClip = FrozenClipGeometry(clips.BottomRight);
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        TrimRenderCachesIfOversized();
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
        if (style?.GradientFill is { } cellGradFill)
        {
            fill = BuildCellGradientBrush(cellGradFill);
        }
        else if (style?.FillColor is { } fillColor)
        {
            fill = BrushForCellColor(fillColor, _brushCache);
        }

        if (fill is not null || gridPen is not null)
            dc.DrawRectangle(fill, gridPen, rect);
        if (style?.GradientFill is null)
            DrawFillPattern(dc, rect, style, _brushCache, _fillPatternPenCache);
        if (cell.ConditionalDataBar is { } splitDataBar)
            DrawConditionalDataBar(dc, splitDataBar, rect, _brushCache);

        if (style is not null && HasVisibleCellBorder(style))
        {
            DrawBorderEdge(dc, style.BorderTop, new Point(rect.Left, rect.Top), new Point(rect.Right, rect.Top), _brushCache, _borderPenCache);
            DrawBorderEdge(dc, style.BorderBottom, new Point(rect.Left, rect.Bottom), new Point(rect.Right, rect.Bottom), _brushCache, _borderPenCache);
            DrawBorderEdge(dc, style.BorderLeft, new Point(rect.Left, rect.Top), new Point(rect.Left, rect.Bottom), _brushCache, _borderPenCache);
            DrawBorderEdge(dc, style.BorderRight, new Point(rect.Right, rect.Top), new Point(rect.Right, rect.Bottom), _brushCache, _borderPenCache);
            // Diagonal borders: drawn across cell interior (not edge-aligned), so no pen cache — these are rare
            if (style.BorderDiagonalDown.Style != BorderStyle.None)
                DrawBorderEdge(dc, style.BorderDiagonalDown, new Point(rect.Left, rect.Top), new Point(rect.Right, rect.Bottom), _brushCache);
            if (style.BorderDiagonalUp.Style != BorderStyle.None)
                DrawBorderEdge(dc, style.BorderDiagonalUp, new Point(rect.Left, rect.Bottom), new Point(rect.Right, rect.Top), _brushCache);
        }

        if (cell.HasComment)
            DrawCommentIndicator(dc, rect, cell.CommentDisplay?.Kind ?? CellCommentDisplayKind.Note);

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
        var textRotation = style?.TextRotation ?? 0;
        var renderText = PrepareCellDisplayTextForRender(cell.DisplayText, textRotation);
        var isEffectivelyRightToLeft = CellTextOrientationLayoutPlanner.ResolveIsEffectivelyRightToLeft(
            style?.ReadingOrder ?? CellReadingOrder.Context, IsSheetRightToLeft);
        var flowDirection = isEffectivelyRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        var fontSize = ToDisplayFontSize((style?.FontSize > 0) ? style!.FontSize : DefaultCellFontSizePoints);
        Brush textBrush = TextBrush;

        var indentPx = (style?.IndentLevel ?? 0) * 8.0 + GetPivotRowLabelAdornmentTextPadding(cell.Row, cell.Col);
        if (style?.ShrinkToFit == true && !wrapText)
        {
            var typefaceKey = CreateCellTypefaceKeyWithTheme(style);
            var typeface = CreateCellTypeface(typefaceKey, _typefaceCache);
            var availableWidth = Math.Max(1, rect.Width - 4 - indentPx);
            fontSize = ResolveCachedShrinkFontSize(
                renderText,
                typefaceKey,
                typeface,
                fontSize,
                availableWidth,
                ToDisplayFontSize(6),
                pixelsPerDip);
        }

        ResolveSuperSubFontAdjustment(style, fontSize, out fontSize, out double splitSuperSubBaselineOffsetPx);

        // Pre-resolve rich runs (split-pane path).  Same cache-bypass logic as the main pass:
        // cells with rich runs get a fresh FormattedText so ApplyRichRunFormatting can mutate it.
        // Use CellStyle.Default when cell.Style is null so null run props inherit sensible defaults.
        IReadOnlyList<ResolvedCellTextRun>? splitRichRuns = null;
        if (SheetRichTextRuns is { } richTextMapSplit)
        {
            var cellAddrSplit = new CellAddress(ActiveSheetId, cell.Row, cell.Col);
            if (richTextMapSplit.TryGetValue(cellAddrSplit, out var rawRunsSplit) && rawRunsSplit is { Count: > 0 })
                splitRichRuns = CellRichRunLayoutPlanner.Resolve(rawRunsSplit, style ?? CellStyle.Default);
        }
        var hasSplitRichRuns = splitRichRuns is { Count: > 0 };

        // The cached default-layout fast paths below always build FlowDirection.LeftToRight text keyed
        // without regard to reading order, so an effectively-RTL cell must bypass them and take the
        // uncached branch, which honors flowDirection.
        var useDefaultTextLayout = !hasSplitRichRuns && !isEffectivelyRightToLeft && CanUseDefaultFormattedText(style, wrapText);
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
            useDefaultWrappedTextLayout = !hasSplitRichRuns && !isEffectivelyRightToLeft && CanUseDefaultWrappedFormattedText(style);
        }
        FormattedText text;
        if (useDefaultTextLayout)
        {
            text = GetDefaultFormattedText(renderText, fontSize, pixelsPerDip);
        }
        else if (useDefaultWrappedTextLayout)
        {
            text = GetDefaultWrappedFormattedText(renderText, fontSize, wrapMaxTextWidth, wrapTextAlignment, pixelsPerDip);
        }
        else
        {
            var typefaceKey = CreateCellTypefaceKeyWithTheme(style);
            var typeface = CreateCellTypeface(typefaceKey, _typefaceCache);
            if (style?.FontColor is { } fontColor && !fontColor.IsBlack)
                textBrush = BrushForCellColor(fontColor, _brushCache);
            text = new FormattedText(
                    renderText,
                    CultureInfo.CurrentCulture,
                    flowDirection,
                    typeface,
                    fontSize,
                    textBrush,
                    pixelsPerDip);
        }

        if (!useDefaultTextLayout && !useDefaultWrappedTextLayout && BuildTextDecorations(style) is { } decorations)
            text.SetTextDecorations(decorations);

        // Per-run rich text (split-pane path).
        if (hasSplitRichRuns)
            ApplyRichRunFormatting(text, splitRichRuns!, _brushCache);

        if (wrapText && !useDefaultWrappedTextLayout)
        {
            text.MaxTextWidth = wrapMaxTextWidth;
            text.TextAlignment = wrapTextAlignment;
        }

        var textLayout = CalculateCellTextRenderLayout(
            rect,
            text.Width,
            text.Height,
            hAlign,
            style?.VerticalAlignment,
            isNumeric,
            indentPx,
            textRotation,
            isEffectivelyRightToLeft);

        // Fill alignment: repeat text horizontally to fill the cell width, clipped to textClipRect.
        if (hAlign == CellHAlign.Fill && text.Width > 0 && rect.Width > 0)
        {
            dc.PushClip(GetCellClipGeometry(textClipRect));
            var fillX = rect.Left + 2;
            var fillY = textLayout.TextPoint.Y;
            while (fillX < textClipRect.Right)
            {
                dc.DrawText(text, new Point(fillX, fillY));
                fillX += text.Width;
            }
            dc.Pop();
            return;
        }

        var shouldClipText = ShouldClipText(wrapText, textClipRect, text, textLayout);
        if (shouldClipText)
            dc.PushClip(GetCellClipGeometry(textClipRect));

        DrawCellText(dc, text, textLayout, style, textBrush, _underlinePenCache, splitSuperSubBaselineOffsetPx);

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
        var visibleRight = GetLogicalViewportWidth();
        var visibleBottom = GetLogicalViewportHeight();
        // Caches are persisted across frames and between split-pane/main render passes.
        // TrimRenderCachesIfOversized() is called once per paint cycle from RenderSplitPaneCells
        // (or here if there are no split panes) to bound memory use.
        if (Viewport?.SplitPanes is null)
            TrimRenderCachesIfOversized();
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
            if (style.BorderDiagonalDown.Style != BorderStyle.None)
                DrawBorderEdge(dc, style.BorderDiagonalDown, new Point(x, y), new Point(x + w, y + h), _brushCache);
            if (style.BorderDiagonalUp.Style != BorderStyle.None)
                DrawBorderEdge(dc, style.BorderDiagonalUp, new Point(x, y + h), new Point(x + w, y), _brushCache);
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

            DrawCommentIndicator(dc, rect, cell.CommentDisplay?.Kind ?? CellCommentDisplayKind.Note);
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
            if (cell.ConditionalDataBar is { } dataBar)
                DrawConditionalDataBar(dc, dataBar, rect, _brushCache);

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
            var textRotation = style?.TextRotation ?? 0;
            var renderText = PrepareCellDisplayTextForRender(cell.DisplayText, textRotation);
            var isEffectivelyRightToLeft = CellTextOrientationLayoutPlanner.ResolveIsEffectivelyRightToLeft(
                style?.ReadingOrder ?? CellReadingOrder.Context, IsSheetRightToLeft);
            var flowDirection = isEffectivelyRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

            bool canOverflow = CanOverflowCellText(style, cell.RawValue, cell.DisplayText, cellMerge);

            double fontSize = ToDisplayFontSize((style?.FontSize > 0) ? style!.FontSize : DefaultCellFontSizePoints);

            Brush textBrush = TextBrush;
            double indentPx = (style?.IndentLevel ?? 0) * 8.0 + GetPivotRowLabelAdornmentTextPadding(cell.Row, cell.Col);
            if (style?.ShrinkToFit == true && !wrapText)
            {
                var typefaceKey = CreateCellTypefaceKeyWithTheme(style);
                var typeface = CreateCellTypeface(typefaceKey, _typefaceCache);
                var availableWidth = Math.Max(1, rect.Width - 4 - indentPx);
                fontSize = ResolveCachedShrinkFontSize(
                    renderText,
                    typefaceKey,
                    typeface,
                    fontSize,
                    availableWidth,
                    ToDisplayFontSize(6),
                    pixelsPerDip);
            }

            // Super/subscript: scale font to ~58% and apply a vertical baseline offset.
            ResolveSuperSubFontAdjustment(style, fontSize, out fontSize, out double superSubBaselineOffsetPx);

            // Pre-resolve rich runs so we know whether to bypass the shared FormattedText cache.
            // The cache must NOT be modified in-place (it is shared across cells), so when this cell
            // has per-run rich text the default-layout fast-path is suppressed and a fresh
            // FormattedText is created so that ApplyRichRunFormatting can safely mutate it.
            // Use CellStyle.Default when cell.Style is null (plain cell, no explicit styling) so
            // that null run properties inherit sensible defaults (Calibri, 11pt, black).
            IReadOnlyList<ResolvedCellTextRun>? cellRichRuns = null;
            if (SheetRichTextRuns is { } richTextMap)
            {
                var cellAddr = new CellAddress(ActiveSheetId, cell.Row, cell.Col);
                if (richTextMap.TryGetValue(cellAddr, out var rawRuns) && rawRuns is { Count: > 0 })
                    cellRichRuns = CellRichRunLayoutPlanner.Resolve(rawRuns, style ?? CellStyle.Default);
            }
            var hasRichRuns = cellRichRuns is { Count: > 0 };

            // When the cell has per-run rich text, force the full (non-cached) FormattedText path so
            // ApplyRichRunFormatting can mutate font/color ranges without corrupting the shared cache.
            // Effectively-RTL cells must also take this path: the cached default-layout fast paths
            // always build FlowDirection.LeftToRight text regardless of reading order.
            var useDefaultTextLayout = !hasRichRuns && !isEffectivelyRightToLeft && CanUseDefaultFormattedText(style, wrapText);
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
                useDefaultWrappedTextLayout = !hasRichRuns && !isEffectivelyRightToLeft && CanUseDefaultWrappedFormattedText(style);
            }

            FormattedText text;
            if (useDefaultTextLayout)
            {
                text = GetDefaultFormattedText(renderText, fontSize, pixelsPerDip);
            }
            else if (useDefaultWrappedTextLayout)
            {
                text = GetDefaultWrappedFormattedText(renderText, fontSize, wrapMaxTextWidth, wrapTextAlignment, pixelsPerDip);
            }
            else
            {
                var typefaceKey = CreateCellTypefaceKeyWithTheme(style);
                var typeface = CreateCellTypeface(typefaceKey, _typefaceCache);
                if (style?.FontColor is { } fc && !fc.IsBlack)
                    textBrush = BrushForCellColor(fc, _brushCache);

                text = new FormattedText(
                        renderText,
                        CultureInfo.CurrentCulture,
                        flowDirection,
                        typeface, fontSize, textBrush,
                        pixelsPerDip);
            }

            if (!useDefaultTextLayout && !useDefaultWrappedTextLayout && BuildTextDecorations(style) is { } decorations)
                text.SetTextDecorations(decorations);

            // Per-run rich text: apply per-character-range formatting.
            // cellRichRuns is pre-resolved above; the formattedText is guaranteed to be a fresh
            // (non-cached) instance when hasRichRuns == true.
            if (hasRichRuns)
                ApplyRichRunFormatting(text, cellRichRuns!, _brushCache);

            if (wrapText && !useDefaultWrappedTextLayout)
            {
                text.MaxTextWidth = wrapMaxTextWidth;
                text.TextAlignment = wrapTextAlignment;
            }

            var textLayout = CalculateCellTextRenderLayout(
                rect,
                text.Width,
                text.Height,
                hAlign,
                style?.VerticalAlignment,
                isNumeric,
                indentPx,
                textRotation,
                isEffectivelyRightToLeft);

            double clipLeft = rect.Left;
            if (canOverflow && textLayout.Bounds.Right > rect.Right)
            {
                occupied ??= GetOccupiedCellLookup(viewport, EditingCell);
                uint nextCol = colMetric.Col + 1;
                while (colLookup.TryGetValue(nextCol, out var nextMetric)
                       && !occupied.Contains((cell.Row, nextCol)))
                {
                    renderWidth += nextMetric.Width;
                    nextCol++;
                }
            }

            // Right/Center-aligned text can overflow leftward into empty cells (mirrors Excel,
            // which slides right-aligned/centered overflow text over blank cells to its left).
            if (canOverflow && textLayout.Bounds.Left < rect.Left && colMetric.Col > 1)
            {
                occupied ??= GetOccupiedCellLookup(viewport, EditingCell);
                uint prevCol = colMetric.Col - 1;
                while (colLookup.TryGetValue(prevCol, out var prevMetric)
                       && !occupied.Contains((cell.Row, prevCol)))
                {
                    clipLeft -= prevMetric.Width;
                    renderWidth += prevMetric.Width;
                    if (prevCol == 1)
                        break;
                    prevCol--;
                }
            }

            var clipRect = new Rect(clipLeft, rect.Top, renderWidth, rect.Height);

            if (!IntersectsVisibleGrid(clipRect, visibleLeft, visibleTop, visibleRight, visibleBottom))
                continue;

            // Fill alignment: repeat text horizontally to fill the cell width.
            if (hAlign == CellHAlign.Fill && text.Width > 0 && rect.Width > 0)
            {
                dc.PushClip(GetCellClipGeometry(clipRect));
                var fillX = rect.Left + 2;
                var fillY = textLayout.TextPoint.Y;
                while (fillX < clipRect.Right)
                {
                    dc.DrawText(text, new Point(fillX, fillY));
                    fillX += text.Width;
                }
                dc.Pop();
                continue;
            }

            var shouldClipText = ShouldClipText(wrapText, clipRect, text, textLayout);
            if (shouldClipText)
                dc.PushClip(GetCellClipGeometry(clipRect));

            DrawCellText(dc, text, textLayout, style, textBrush, _underlinePenCache, superSubBaselineOffsetPx);

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
        if (bg?.GradientFill is { } gradFill)
        {
            fill = BuildCellGradientBrush(gradFill);
        }
        else if (bg?.FillColor.HasValue == true)
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
        if (bg is not null && bg.GradientFill is null)
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
        var visibleRight = Math.Min(right, GetLogicalViewportWidth());
        var visibleBottom = Math.Min(bottom, GetLogicalViewportHeight());
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

    // Maximum number of entries allowed in each render cache before it is evicted.
    // In practice a viewport has ~50–200 unique colours/typefaces; 512 gives generous
    // headroom while bounding worst-case memory on highly-varied workbooks.
    private const int RenderCacheSizeLimit = 512;

    /// <summary>
    /// Evicts render caches that have grown beyond <see cref="RenderCacheSizeLimit"/>.
    /// Called once per paint cycle (at the start of the split-pane pass if present,
    /// otherwise at the start of the main-grid pass). This preserves warm entries
    /// across frames and between split-pane/main render passes within the same frame,
    /// eliminating the per-frame allocation spike from unconditional Clear() calls.
    /// </summary>
    /// <remarks>
    /// Invalidation analysis — none of these caches need theme or zoom invalidation:
    /// <list type="bullet">
    /// <item><description><c>_brushCache</c>: keyed by <c>CellColor</c> (ARGB value type). Color IS the key,
    ///   so a different color yields a different entry. Theme changes alter which colors appear
    ///   in the spreadsheet but do not make existing brush entries stale.</description></item>
    /// <item><description><c>_borderPenCache</c>: keyed by <c>CellBorder</c> (style + color value type).
    ///   Same argument — the key fully determines the pen.</description></item>
    /// <item><description><c>_fillPatternPenCache</c>: keyed by <c>CellColor</c>. Same as brush cache.</description></item>
    /// <item><description><c>_typefaceCache</c>: keyed by <c>CellTypefaceKey</c> (font name/weight/style).
    ///   Zoom only affects <c>fontSize</c> which is passed to <c>FormattedText</c>, not to <c>Typeface</c>.
    ///   Font resolution is therefore zoom-independent.</description></item>
    /// <item><description><c>_underlinePenCache</c>: keyed by the frozen <c>Brush</c> reference.
    ///   The brush objects themselves are stable frozen instances.</description></item>
    /// <item><description><c>_defaultTextLayoutStyleCache</c>: keyed by <c>CellStyle</c> reference equality.
    ///   Entries record whether a style can use the fast default text path. Style objects are
    ///   immutable; a new style revision yields a new reference, so stale entries are never hit.</description></item>
    /// </list>
    /// All six caches are accessed only from the WPF UI thread (OnRender), so no
    /// synchronisation is needed.
    /// </remarks>
    private void TrimRenderCachesIfOversized()
    {
        if (_brushCache.Count >= RenderCacheSizeLimit)
            _brushCache.Clear();
        if (_borderPenCache.Count >= RenderCacheSizeLimit)
            _borderPenCache.Clear();
        if (_fillPatternPenCache.Count >= RenderCacheSizeLimit)
            _fillPatternPenCache.Clear();
        if (_typefaceCache.Count >= RenderCacheSizeLimit)
            _typefaceCache.Clear();
        if (_underlinePenCache.Count >= RenderCacheSizeLimit)
            _underlinePenCache.Clear();
        if (_defaultTextLayoutStyleCache.Count >= RenderCacheSizeLimit)
            _defaultTextLayoutStyleCache.Clear();
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

    private void DrawCommentIndicator(DrawingContext dc, Rect rect, CellCommentDisplayKind kind) =>
        dc.DrawGeometry(CommentIndicatorBrush(kind), null, GetCommentIndicatorGeometry(rect));

    /// <summary>
    /// Returns the frozen brush for a comment indicator triangle.
    /// Note (legacy)      → red   (Excel classic, confirmed parity).
    /// ThreadedComment     → purple/magenta #7C379E — sampled from Excel 365 threaded-comment
    ///                       corner markers; the hue sits between the purple comment bubble
    ///                       icon and the magenta @mention highlight used in the same UI.
    /// Mixed (note + thread in one cell) → purple, matching Excel which shows the threaded
    ///                       indicator when both kinds coexist (the note red is suppressed).
    /// </summary>
    internal static Brush CommentIndicatorBrush(CellCommentDisplayKind kind)
    {
        // ThreadedComment purple: RGB(124, 55, 158) / #7C379E.
        // Frozen once; allocation happens only at first call per kind.
        if (kind == CellCommentDisplayKind.Note)
            return Brushes.Red;

        // ThreadedComment and Mixed both use the threaded-comment purple.
        if (s_threadedCommentBrush is null)
        {
            var b = new SolidColorBrush(Color.FromRgb(0x7C, 0x37, 0x9E));
            b.Freeze();
            s_threadedCommentBrush = b;
        }
        return s_threadedCommentBrush;
    }

    private static SolidColorBrush? s_threadedCommentBrush;

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

    internal readonly record struct CellTextRenderLayout(Point TextPoint, Rect Bounds, double TransformAngle)
    {
        public bool IsRotated => Math.Abs(TransformAngle) > 0.001;
    }

    internal static bool HasCellTextOrientation(int textRotation) =>
        CellTextOrientationLayoutPlanner.HasTextOrientation(textRotation);

    internal static bool IsStackedCellTextRotation(int textRotation) =>
        CellTextOrientationLayoutPlanner.IsStackedTextRotation(textRotation);

    internal static int NormalizeCellTextRotationForDisplay(int textRotation) =>
        CellTextOrientationLayoutPlanner.NormalizeRotationForDisplay(textRotation);

    internal static string PrepareCellDisplayTextForRender(string text, int textRotation) =>
        CellTextOrientationLayoutPlanner.PrepareDisplayText(text, textRotation);

    internal static CellTextRenderLayout CalculateCellTextRenderLayout(
        Rect rect,
        double textWidth,
        double textHeight,
        CellHAlign hAlign,
        CellVAlign? vAlign,
        bool isNumeric,
        double indentPx,
        int textRotation,
        bool isEffectivelyRightToLeft = false)
    {
        var layout = CellTextOrientationLayoutPlanner.CalculateLayout(
            new CellTextLayoutRect(rect.Left, rect.Top, rect.Width, rect.Height),
            textWidth,
            textHeight,
            hAlign,
            vAlign,
            isNumeric,
            indentPx,
            textRotation,
            isEffectivelyRightToLeft);
        return ToWpfLayout(layout);
    }

    private static void DrawCellText(
        DrawingContext dc,
        FormattedText text,
        CellTextRenderLayout textLayout,
        CellStyle? style,
        Brush textBrush,
        Dictionary<Brush, Pen> underlinePenCache,
        double baselineOffsetPx = 0)
    {
        var drawPoint = baselineOffsetPx == 0
            ? textLayout.TextPoint
            : new Point(textLayout.TextPoint.X, textLayout.TextPoint.Y + baselineOffsetPx);

        if (textLayout.IsRotated)
            dc.PushTransform(new RotateTransform(textLayout.TransformAngle, drawPoint.X, drawPoint.Y));

        dc.DrawText(text, drawPoint);

        if (style?.DoubleUnderline == true)
        {
            double uY = drawPoint.Y + text.Height + 1;
            var underlinePen = UnderlinePenForTextBrush(textBrush, underlinePenCache);
            dc.DrawLine(underlinePen, new Point(drawPoint.X, uY), new Point(drawPoint.X + text.Width, uY));
            dc.DrawLine(underlinePen, new Point(drawPoint.X, uY + 2), new Point(drawPoint.X + text.Width, uY + 2));
        }

        if (textLayout.IsRotated)
            dc.Pop();
    }

    private static bool ShouldClipText(
        bool wrapText,
        Rect clipRect,
        FormattedText text,
        CellTextRenderLayout textLayout)
    {
        return CellTextOrientationLayoutPlanner.ShouldClip(
            wrapText,
            new CellTextLayoutRect(clipRect.Left, clipRect.Top, clipRect.Width, clipRect.Height),
            text.Height,
            new CellTextOrientationLayout(
                new CellTextLayoutPoint(textLayout.TextPoint.X, textLayout.TextPoint.Y),
                new CellTextLayoutRect(textLayout.Bounds.Left, textLayout.Bounds.Top, textLayout.Bounds.Width, textLayout.Bounds.Height),
                textLayout.TransformAngle));
    }

    private static CellTextRenderLayout ToWpfLayout(CellTextOrientationLayout layout) =>
        new(
            new Point(layout.TextPoint.X, layout.TextPoint.Y),
            new Rect(layout.Bounds.Left, layout.Bounds.Top, layout.Bounds.Width, layout.Bounds.Height),
            layout.TransformAngle);

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
