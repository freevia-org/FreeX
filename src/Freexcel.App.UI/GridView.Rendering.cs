using System.Globalization;
using System.Windows;
using System.Windows.Media;

using Freexcel.Core.Model;

using CellHAlign = Freexcel.Core.Model.HorizontalAlignment;
using CellVAlign = Freexcel.Core.Model.VerticalAlignment;

namespace Freexcel.App.UI;

public partial class GridView
{
    // Grid rendering for freeze dividers, selection, headers, cells, borders, and text decorations.
    private void RenderFreezeDivider(DrawingContext dc)
    {
        if (Viewport?.FrozenPanes == null) return;
        var fp = Viewport.FrozenPanes;

        if (fp.Rows > 0)
        {
            var lastFrozenRow = Viewport.RowMetrics.FirstOrDefault(r => r.Row == fp.Rows);
            if (lastFrozenRow != null)
            {
                double y = lastFrozenRow.TopOffset + lastFrozenRow.Height + EffectiveColHeaderHeight;
                dc.DrawLine(FreezePen, new Point(0, y), new Point(ActualWidth, y));
            }
        }

        if (fp.Cols > 0)
        {
            var lastFrozenCol = Viewport.ColMetrics.FirstOrDefault(c => c.Col == fp.Cols);
            if (lastFrozenCol != null)
            {
                double x = lastFrozenCol.LeftOffset + lastFrozenCol.Width + ActualRowHeaderWidth;
                dc.DrawLine(FreezePen, new Point(x, 0), new Point(x, ActualHeight));
            }
        }
    }

    private (double Top, double Left, double Bottom, double Right,
        double MarginLeft, double MarginRight, double MarginTop, double MarginBottom)?
        GetPageMarginGuidePixels(GridRange printArea)
    {
        if (Viewport == null) return null;
        var (top, left, bottom, right) = GetRangePixels(Viewport, printArea);
        if (!top.HasValue || !left.HasValue || !bottom.HasValue || !right.HasValue) return null;

        var guide = WorksheetPageLayout.GetMarginGuideFractions(PaperSize, PageOrientation, PageMargins);
        var width = right.Value - left.Value;
        var height = bottom.Value - top.Value;
        if (width <= 0 || height <= 0) return null;

        return (
            top.Value,
            left.Value,
            bottom.Value,
            right.Value,
            left.Value + width * guide.Left,
            left.Value + width * guide.Right,
            top.Value + height * guide.Top,
            top.Value + height * guide.Bottom);
    }

    // Returns pixel coords for a range, clamped to viewport boundaries.
    private (double? top, double? left, double? bottom, double? right) GetRangePixels(
        ViewportModel vp, GridRange range)
    {
        double? top = null, left = null, bottom = null, right = null;
        foreach (var row in vp.RowMetrics)
        {
            if (row.Row == range.Start.Row) top    = row.TopOffset + EffectiveColHeaderHeight;
            if (row.Row == range.End.Row)   bottom = row.TopOffset + row.Height + EffectiveColHeaderHeight;
        }
        foreach (var col in vp.ColMetrics)
        {
            if (col.Col == range.Start.Col) left  = col.LeftOffset + ActualRowHeaderWidth;
            if (col.Col == range.End.Col)   right = col.LeftOffset + col.Width + ActualRowHeaderWidth;
        }
        return (top, left, bottom, right);
    }

    public static Rect? CalculateVisibleSelectionRect(
        ViewportModel viewport,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight) =>
        SelectionMarqueeLayoutPlanner.CalculateVisibleSelectionRect(viewport, range, rowHeaderWidth, columnHeaderHeight);

    public static Rect? CalculateClipboardMarquee(
        ViewportModel viewport,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight) =>
        SelectionMarqueeLayoutPlanner.CalculateClipboardMarquee(viewport, range, rowHeaderWidth, columnHeaderHeight);

    public static Rect? CalculateQuickAnalysisPreviewRect(
        ViewportModel viewport,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight) =>
        SelectionMarqueeLayoutPlanner.CalculateVisibleSelectionRect(viewport, range, rowHeaderWidth, columnHeaderHeight);

    private void RenderQuickAnalysisPreview(DrawingContext dc)
    {
        if (Viewport == null || QuickAnalysisPreviewRange is not { } range)
            return;

        var rect = CalculateQuickAnalysisPreviewRect(Viewport, range, ActualRowHeaderWidth, EffectiveColHeaderHeight);
        if (rect is null)
            return;

        dc.DrawRectangle(QuickAnalysisPreviewBrush, QuickAnalysisPreviewPen, rect.Value);
    }

    private void RenderSelection(DrawingContext dc)
    {
        if (Viewport == null) return;
        if (SelectedRanges is { Count: > 0 } selectedRanges)
        {
            foreach (var range in selectedRanges)
                RenderSelectionRange(dc, range, drawHandle: false);

            if (SelectedRange is { } activeRange)
                RenderSelectionHandle(dc, activeRange);
            return;
        }

        if (SelectedRange == null) return;

        RenderSelectionRange(dc, SelectedRange.Value, drawHandle: true);
    }

    private void RenderSelectionRange(DrawingContext dc, GridRange range, bool drawHandle)
    {
        if (Viewport == null) return;
        var rows  = Viewport.RowMetrics;
        var cols  = Viewport.ColMetrics;
        if (rows.Count == 0 || cols.Count == 0) return;

        var (top, left, bottom, right) = GetRangePixels(Viewport, range);
        var rect = CalculateVisibleSelectionRect(Viewport, range, ActualRowHeaderWidth, EffectiveColHeaderHeight);
        if (rect is null) return;

        double drawTop    = rect.Value.Top;
        double drawBottom = rect.Value.Bottom;
        double drawLeft   = rect.Value.Left;
        double drawRight  = rect.Value.Right;

        dc.DrawRectangle(SelectionBrush, null, rect.Value);

        if (top.HasValue)    dc.DrawLine(SelectionPen, new Point(drawLeft,  drawTop),    new Point(drawRight, drawTop));
        if (bottom.HasValue) dc.DrawLine(SelectionPen, new Point(drawLeft,  drawBottom), new Point(drawRight, drawBottom));
        if (left.HasValue)   dc.DrawLine(SelectionPen, new Point(drawLeft,  drawTop),    new Point(drawLeft,  drawBottom));
        if (right.HasValue)  dc.DrawLine(SelectionPen, new Point(drawRight, drawTop),    new Point(drawRight, drawBottom));

        if (drawHandle)
            DrawSelectionHandle(dc, right, bottom, drawRight, drawBottom);
    }

    private void RenderSelectionHandle(DrawingContext dc, GridRange range)
    {
        if (Viewport == null) return;
        var (top, left, bottom, right) = GetRangePixels(Viewport, range);
        double drawBottom = bottom ?? ActualHeight;
        double drawRight  = right  ?? ActualWidth;
        DrawSelectionHandle(dc, right, bottom, drawRight, drawBottom);
    }

    private static void DrawSelectionHandle(DrawingContext dc, double? right, double? bottom, double drawRight, double drawBottom)
    {
        if (!right.HasValue || !bottom.HasValue)
            return;

        const double handleSize = 6;
        double hx = drawRight - handleSize / 2;
        double hy = drawBottom - handleSize / 2;
        dc.DrawRectangle(Brushes.White, SelectionPen,
            new Rect(hx, hy, handleSize, handleSize));
        dc.DrawRectangle(
            new SolidColorBrush(Color.FromRgb(33, 115, 70)), null,
            new Rect(hx + 1, hy + 1, handleSize - 2, handleSize - 2));
    }

    private void RenderHeaders(DrawingContext dc)
    {
        if (!ShowHeaders) return;

        var selectedRanges = SelectedRanges;
        var selRange = SelectedRange;

        // Column Headers (A, B, Câ€¦)
        foreach (var col in Viewport!.ColMetrics)
        {
            bool inSel = selectedRanges is { Count: > 0 }
                ? selectedRanges.Any(r => col.Col >= r.Start.Col && col.Col <= r.End.Col)
                : selRange.HasValue
                    && col.Col >= selRange.Value.Start.Col
                    && col.Col <= selRange.Value.End.Col;

            var bg   = inSel ? HeaderHighlightBrush : HeaderBackgroundBrush;
            var rect = new Rect(col.LeftOffset + ActualRowHeaderWidth, 0, col.Width, EffectiveColHeaderHeight);
            dc.DrawRectangle(bg, GridPen, rect);

            var text = new FormattedText(
                FormatColumnHeader(col.Col, UseR1C1ReferenceStyle),
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                DefaultTypeface, 11, TextBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            dc.DrawText(text, new Point(
                rect.Left + (rect.Width  - text.Width)  / 2,
                rect.Top  + (rect.Height - text.Height) / 2));
        }

        // Row Headers (1, 2, 3â€¦)
        foreach (var row in Viewport!.RowMetrics)
        {
            bool inSel = selectedRanges is { Count: > 0 }
                ? selectedRanges.Any(r => row.Row >= r.Start.Row && row.Row <= r.End.Row)
                : selRange.HasValue
                    && row.Row >= selRange.Value.Start.Row
                    && row.Row <= selRange.Value.End.Row;

            var bg   = inSel ? HeaderHighlightBrush : HeaderBackgroundBrush;
            var rect = new Rect(0, row.TopOffset + EffectiveColHeaderHeight, ActualRowHeaderWidth, row.Height);
            dc.DrawRectangle(bg, GridPen, rect);

            var text = new FormattedText(
                row.Row.ToString(),
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                DefaultTypeface, 11, TextBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            dc.DrawText(text, new Point(
                rect.Left + (rect.Width  - text.Width)  / 2,
                rect.Top  + (rect.Height - text.Height) / 2));
        }

        // Top-left corner
        dc.DrawRectangle(HeaderBackgroundBrush, GridPen,
            new Rect(0, 0, ActualRowHeaderWidth, EffectiveColHeaderHeight));
    }

    internal static string FormatColumnHeader(uint column, bool useR1C1ReferenceStyle) =>
        useR1C1ReferenceStyle
            ? column.ToString(CultureInfo.InvariantCulture)
            : CellAddress.NumberToColumnName(column);

    private void RenderGridLines(DrawingContext dc)
    {
        // Grid lines are drawn as cell/header rectangle borders.
    }

    private void RenderSplitPaneCells(DrawingContext dc)
    {
        if (Viewport?.SplitPanes?.Cells is not { Count: > 0 }) return;

        var clips = CalculateSplitPaneClipRects(Viewport, ActualWidth, ActualHeight);
        foreach (var layout in CalculateSplitPaneCellLayouts(Viewport, MergedRegions))
        {
            var cell = layout.Cell;
            var rect = layout.Rect;
            var style = cell.Style;
            var clipRect = GetSplitPaneClipRectForCell(Viewport, cell, clips);
            dc.PushClip(new RectangleGeometry(clipRect));

            Brush? fill = WorksheetBackground == null ? Brushes.White : null;
            if (style?.FillColor is { } fillColor)
                fill = BrushForCellColor(fillColor);

            dc.DrawRectangle(fill, GridPen, rect);
            DrawFillPattern(dc, rect, style);

            if (style is not null)
            {
                DrawBorderEdge(dc, style.BorderTop, new Point(rect.Left, rect.Top), new Point(rect.Right, rect.Top));
                DrawBorderEdge(dc, style.BorderBottom, new Point(rect.Left, rect.Bottom), new Point(rect.Right, rect.Bottom));
                DrawBorderEdge(dc, style.BorderLeft, new Point(rect.Left, rect.Top), new Point(rect.Left, rect.Bottom));
                DrawBorderEdge(dc, style.BorderRight, new Point(rect.Right, rect.Top), new Point(rect.Right, rect.Bottom));
            }

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
            var typeface = (style?.Bold == true, style?.Italic == true) switch
            {
                (true,  true)  => new Typeface(new FontFamily("Calibri"), FontStyles.Italic,  FontWeights.Bold,   FontStretches.Normal),
                (true,  false) => new Typeface(new FontFamily("Calibri"), FontStyles.Normal,  FontWeights.Bold,   FontStretches.Normal),
                (false, true)  => new Typeface(new FontFamily("Calibri"), FontStyles.Italic,  FontWeights.Normal, FontStretches.Normal),
                _              => DefaultTypeface
            };
            var fontSize = ToDisplayFontSize((style?.FontSize > 0) ? style!.FontSize : DefaultCellFontSizePoints);
            Brush textBrush = TextBrush;
            if (style?.FontColor is { } fontColor && !fontColor.IsBlack)
                textBrush = new SolidColorBrush(Color.FromRgb(fontColor.R, fontColor.G, fontColor.B));

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
                        VisualTreeHelper.GetDpi(this).PixelsPerDip).Width,
                    ToDisplayFontSize(6));
            }

            var text = new FormattedText(
                cell.DisplayText,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                textBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

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

    private GridRange? FindMerge(uint row, uint col)
    {
        return _mergeLookup.TryGetValue((row, col), out var r) ? r : null;
    }

    private void RenderCells(DrawingContext dc)
    {
        var styleLookup = Viewport!.Cells
            .Where(c => c.Style != null)
            .ToDictionary(c => (c.Row, c.Col), c => c.Style!);

        var rowLookupAll = Viewport.RowMetrics.ToDictionary(r => r.Row);
        var colLookupAll = Viewport.ColMetrics.ToDictionary(c => c.Col);


        // Pass 1: backgrounds
        foreach (var rowMetric in Viewport.RowMetrics)
        {
            foreach (var colMetric in Viewport.ColMetrics)
            {
                var merge = FindMerge(rowMetric.Row, colMetric.Col);
                if (merge.HasValue && (rowMetric.Row != merge.Value.Start.Row || colMetric.Col != merge.Value.Start.Col))
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
                    colMetric.LeftOffset + ActualRowHeaderWidth, rowMetric.TopOffset + EffectiveColHeaderHeight, w, h);

                Brush? fill = WorksheetBackground == null ? Brushes.White : null;
                if (styleLookup.TryGetValue((rowMetric.Row, colMetric.Col), out var bg)
                    && bg.FillColor.HasValue)
                {
                    fill = BrushForCellColor(bg.FillColor.Value);
                }

                dc.DrawRectangle(fill, GridPen, rect);
                if (bg is not null)
                    DrawFillPattern(dc, rect, bg);
            }
        }

        // Pass 2: explicit cell borders
        foreach (var cell in Viewport.Cells)
        {
            if (cell.Style == null) continue;
            var rowMetric = Viewport.RowMetrics.FirstOrDefault(r => r.Row == cell.Row);
            var colMetric = Viewport.ColMetrics.FirstOrDefault(c => c.Col == cell.Col);
            if (rowMetric is null || colMetric is null) continue;

            double x = colMetric.LeftOffset + ActualRowHeaderWidth;
            double y = rowMetric.TopOffset   + EffectiveColHeaderHeight;
            double w = colMetric.Width;
            double h = rowMetric.Height;

            DrawBorderEdge(dc, cell.Style.BorderTop,    new Point(x,     y),     new Point(x + w, y));
            DrawBorderEdge(dc, cell.Style.BorderBottom, new Point(x,     y + h), new Point(x + w, y + h));
            DrawBorderEdge(dc, cell.Style.BorderLeft,   new Point(x,     y),     new Point(x,     y + h));
            DrawBorderEdge(dc, cell.Style.BorderRight,  new Point(x + w, y),     new Point(x + w, y + h));
        }

        // Pass 3: text
        var rowLookup = rowLookupAll;
        var colLookup = colLookupAll;

        var occupied = new HashSet<(uint, uint)>(
            Viewport.Cells
                .Where(c => !string.IsNullOrEmpty(c.DisplayText) || c.ConditionalIcon is not null)
                .Select(c => (c.Row, c.Col)));

        foreach (var cell in Viewport.Cells)
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
                colMetric.LeftOffset + ActualRowHeaderWidth, rowMetric.TopOffset + EffectiveColHeaderHeight, w, h);
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

            var typeface = (style?.Bold == true, style?.Italic == true) switch
            {
                (true,  true)  => new Typeface(new FontFamily("Calibri"), FontStyles.Italic,  FontWeights.Bold,   FontStretches.Normal),
                (true,  false) => new Typeface(new FontFamily("Calibri"), FontStyles.Normal,  FontWeights.Bold,   FontStretches.Normal),
                (false, true)  => new Typeface(new FontFamily("Calibri"), FontStyles.Italic,  FontWeights.Normal, FontStretches.Normal),
                _              => DefaultTypeface
            };

            // Excel font sizes are typographic points; WPF measures in DIPs (96 DPI).
            // Snap to whole display DIPs so ClearType does not soften 11pt as 14.667 DIP text.
            double fontSize = ToDisplayFontSize((style?.FontSize > 0) ? style!.FontSize : DefaultCellFontSizePoints);

            Brush textBrush = TextBrush;
            if (style?.FontColor is { } fc && !fc.IsBlack)
                textBrush = new SolidColorBrush(Color.FromRgb(fc.R, fc.G, fc.B));

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
                        VisualTreeHelper.GetDpi(this).PixelsPerDip).Width,
                    ToDisplayFontSize(6));
            }

            var text = new FormattedText(
                cell.DisplayText,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface, fontSize, textBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

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
            dc.PushClip(new RectangleGeometry(clipRect));
            dc.DrawText(text, new Point(Math.Round(textX), Math.Round(textY)));

            if (style?.DoubleUnderline == true)
            {
                double uY = textY + text.Height + 1;
                dc.DrawLine(new Pen(textBrush, 1), new Point(textX, uY), new Point(textX + text.Width, uY));
                dc.DrawLine(new Pen(textBrush, 1), new Point(textX, uY + 2), new Point(textX + text.Width, uY + 2));
            }
            dc.Pop();
        }
    }

    private static void DrawBorderEdge(DrawingContext dc, CellBorder border, Point p1, Point p2)
    {
        if (border.Style == BorderStyle.None) return;

        double thickness = border.Style switch
        {
            BorderStyle.Thin   => 0.5,
            BorderStyle.Medium => 1.5,
            BorderStyle.Thick  => 2.5,
            _                  => 0.5
        };

        DashStyle dash = border.Style switch
        {
            BorderStyle.Dashed => DashStyles.Dash,
            BorderStyle.Dotted => DashStyles.Dot,
            _                  => DashStyles.Solid
        };

        var pen = new Pen(
            new SolidColorBrush(Color.FromRgb(border.Color.R, border.Color.G, border.Color.B)),
            thickness) { DashStyle = dash };

        dc.DrawLine(pen, p1, p2);
    }

    private static SolidColorBrush BrushForCellColor(CellColor color) =>
        new(Color.FromRgb(color.R, color.G, color.B));

    private static void DrawFillPattern(DrawingContext dc, Rect rect, CellStyle? style)
    {
        if (style is null || style.FillPatternStyle is CellFillPatternStyle.None or CellFillPatternStyle.Solid)
            return;

        var color = style.FillPatternColor ?? CellColor.Black;
        var pen = new Pen(BrushForCellColor(color), 0.75);
        const double step = 6;

        dc.PushClip(new RectangleGeometry(rect));
        switch (style.FillPatternStyle)
        {
            case CellFillPatternStyle.Gray0625:
            case CellFillPatternStyle.Gray125:
            case CellFillPatternStyle.LightGray:
            case CellFillPatternStyle.MediumGray:
            case CellFillPatternStyle.DarkGray:
                var opacity = style.FillPatternStyle switch
                {
                    CellFillPatternStyle.Gray0625 => 0.12,
                    CellFillPatternStyle.Gray125 => 0.18,
                    CellFillPatternStyle.LightGray => 0.28,
                    CellFillPatternStyle.MediumGray => 0.45,
                    _ => 0.62
                };
                dc.DrawRectangle(MakeBrushAlpha((byte)(opacity * 255), color.R, color.G, color.B), null, rect);
                break;
            case CellFillPatternStyle.LightHorizontal:
            case CellFillPatternStyle.DarkHorizontal:
                for (var y = rect.Top + step; y < rect.Bottom; y += step)
                    dc.DrawLine(pen, new Point(rect.Left, y), new Point(rect.Right, y));
                break;
            case CellFillPatternStyle.LightVertical:
            case CellFillPatternStyle.DarkVertical:
                for (var x = rect.Left + step; x < rect.Right; x += step)
                    dc.DrawLine(pen, new Point(x, rect.Top), new Point(x, rect.Bottom));
                break;
            case CellFillPatternStyle.LightGrid:
            case CellFillPatternStyle.DarkGrid:
                DrawFillPattern(dc, rect, new CellStyle { FillPatternStyle = CellFillPatternStyle.LightHorizontal, FillPatternColor = color });
                DrawFillPattern(dc, rect, new CellStyle { FillPatternStyle = CellFillPatternStyle.LightVertical, FillPatternColor = color });
                break;
            case CellFillPatternStyle.LightDown:
            case CellFillPatternStyle.DarkDown:
                DrawDiagonalPattern(dc, rect, pen, descending: true);
                break;
            case CellFillPatternStyle.LightUp:
            case CellFillPatternStyle.DarkUp:
                DrawDiagonalPattern(dc, rect, pen, descending: false);
                break;
            case CellFillPatternStyle.LightTrellis:
            case CellFillPatternStyle.DarkTrellis:
                DrawDiagonalPattern(dc, rect, pen, descending: true);
                DrawDiagonalPattern(dc, rect, pen, descending: false);
                break;
        }
        dc.Pop();
    }

    private static void DrawDiagonalPattern(DrawingContext dc, Rect rect, Pen pen, bool descending)
    {
        const double step = 8;
        for (var offset = -rect.Height; offset < rect.Width; offset += step)
        {
            var start = descending
                ? new Point(rect.Left + offset, rect.Top)
                : new Point(rect.Left + offset, rect.Bottom);
            var end = descending
                ? new Point(rect.Left + offset + rect.Height, rect.Bottom)
                : new Point(rect.Left + offset + rect.Height, rect.Top);
            dc.DrawLine(pen, start, end);
        }
    }

    public static TextDecorationCollection? BuildTextDecorations(CellStyle? style) =>
        CellTextDecorationPlanner.Build(style);
}
