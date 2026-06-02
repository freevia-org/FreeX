using System.Globalization;
using System.Windows;
using System.Windows.Media;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public partial class GridView
{
    // Floating drawing objects, pictures, charts, and worksheet background rendering.

    private static readonly Brush ObjectPlaceholderFill = MakeBrushAlpha(48, 255, 255, 255);
    private static readonly Brush ObjectPlaceholderTextBrush = MakeBrush(89, 89, 89);
    private static readonly Pen ObjectPlaceholderPen = CreateFrozenPen(MakeBrush(120, 120, 120), 1);
    private static readonly Brush NativeControlHeaderBrush = MakeBrush(91, 155, 213);
    private static readonly Brush NativeControlBorderBrush = MakeBrush(68, 114, 196);
    private static readonly Brush NativeControlBodyBrush = MakeBrush(245, 248, 252);
    private static readonly Brush NativeControlTileBrush = MakeBrush(225, 235, 247);
    private static readonly Brush NativeControlSelectedTileBrush = MakeBrush(198, 224, 180);
    private static readonly Brush NativeControlMutedTextBrush = MakeBrush(89, 89, 89);
    private static readonly Pen NativeControlBorderPen = CreateFrozenPen(NativeControlBorderBrush, 1);

    private const int DrawingObjectBrushCacheLimit = 256;
    private const int DrawingObjectPenCacheLimit = 256;
    private const int DrawingObjectGradientBrushCacheLimit = 256;
    private const int DrawingObjectClipGeometryCacheLimit = 4096;
    private const int DrawingObjectTextLayoutCacheLimit = 4096;

    private readonly Dictionary<DrawingObjectBrushKey, Brush> _drawingObjectBrushCache = new();
    private readonly Dictionary<DrawingObjectPenKey, Pen> _drawingObjectPenCache = new();
    private readonly Dictionary<DrawingObjectGradientBrushKey, Brush> _drawingObjectGradientBrushCache = new();
    private readonly Dictionary<Rect, RectangleGeometry> _drawingObjectClipGeometryCache = new();
    private readonly Dictionary<DrawingObjectTextLayoutKey, FormattedText> _drawingObjectTextLayoutCache = new();

    private double GetDrawingViewportRight()
    {
        var zoom = ZoomFactor > 0 ? ZoomFactor : 1.0;
        return Math.Max(0, ActualWidth / zoom);
    }

    private double GetDrawingViewportBottom()
    {
        var zoom = ZoomFactor > 0 ? ZoomFactor : 1.0;
        return Math.Max(0, ActualHeight / zoom);
    }

    private (uint LastRow, uint LastColumn) GetRenderableDrawingAnchorBounds(double visibleRight, double visibleBottom)
    {
        var viewport = Viewport!;
        return (
            FindLastRenderableDrawingRow(viewport.RowMetrics, EffectiveColHeaderHeight, visibleBottom),
            FindLastRenderableDrawingColumn(viewport.ColMetrics, ActualRowHeaderWidth, visibleRight));
    }

    private static uint FindLastRenderableDrawingRow(
        IReadOnlyList<RowMetric> rows,
        double columnHeaderHeight,
        double visibleBottom)
    {
        uint lastRow = 0;
        foreach (var row in rows)
            if (columnHeaderHeight + row.TopOffset < visibleBottom && row.Row > lastRow)
                lastRow = row.Row;

        return lastRow;
    }

    private static uint FindLastRenderableDrawingColumn(
        IReadOnlyList<ColMetric> columns,
        double rowHeaderWidth,
        double visibleRight)
    {
        uint lastColumn = 0;
        foreach (var column in columns)
            if (rowHeaderWidth + column.LeftOffset < visibleRight && column.Col > lastColumn)
                lastColumn = column.Col;

        return lastColumn;
    }

    private static bool CanAnchoredObjectReachDrawingViewport(
        CellAddress anchor,
        uint lastRenderableRow,
        uint lastRenderableColumn) =>
        lastRenderableRow > 0 &&
        lastRenderableColumn > 0 &&
        anchor.Row <= lastRenderableRow &&
        anchor.Col <= lastRenderableColumn;

    private static bool NeedsDrawingViewportCull(
        Rect rect,
        double rotationDegrees,
        double visibleRight,
        double visibleBottom) =>
        Math.Abs(rotationDegrees % 360) > 0.0001 ||
        rect.Left < 0 ||
        rect.Top < 0 ||
        rect.Left >= visibleRight ||
        rect.Top >= visibleBottom;

    private bool IntersectsDrawingViewport(Rect rect) =>
        IntersectsDrawingViewport(rect, 0);

    private bool IntersectsDrawingViewport(Rect rect, double rotationDegrees)
    {
        return IntersectsDrawingViewport(
            rect,
            rotationDegrees,
            GetDrawingViewportRight(),
            GetDrawingViewportBottom());
    }

    private static bool IntersectsDrawingViewport(
        Rect rect,
        double rotationDegrees,
        double visibleRight,
        double visibleBottom)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            return false;

        if (visibleRight <= 0 || visibleBottom <= 0)
            return false;

        var cullRect = Math.Abs(rotationDegrees % 360) <= 0.0001
            ? rect
            : CalculateRotatedBounds(rect, rotationDegrees);
        return IntersectsVisibleGrid(cullRect, 0, 0, visibleRight, visibleBottom);
    }

    private static Rect CalculateRotatedBounds(Rect rect, double rotationDegrees)
    {
        var radians = rotationDegrees * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var centerX = rect.Left + rect.Width / 2.0;
        var centerY = rect.Top + rect.Height / 2.0;

        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var maxY = double.NegativeInfinity;

        IncludeRotatedCorner(rect.Left, rect.Top);
        IncludeRotatedCorner(rect.Right, rect.Top);
        IncludeRotatedCorner(rect.Right, rect.Bottom);
        IncludeRotatedCorner(rect.Left, rect.Bottom);

        return new Rect(new Point(minX, minY), new Point(maxX, maxY));

        void IncludeRotatedCorner(double x, double y)
        {
            var dx = x - centerX;
            var dy = y - centerY;
            var rotatedX = centerX + dx * cos - dy * sin;
            var rotatedY = centerY + dx * sin + dy * cos;
            minX = Math.Min(minX, rotatedX);
            minY = Math.Min(minY, rotatedY);
            maxX = Math.Max(maxX, rotatedX);
            maxY = Math.Max(maxY, rotatedY);
        }
    }

    private void RenderCharts(DrawingContext dc)
    {
        if (Charts == null || Viewport == null) return;
        var visibleRight = GetDrawingViewportRight();
        var visibleBottom = GetDrawingViewportBottom();
        foreach (var chart in Charts)
        {
            if (!chart.IsVisible) continue;
            var rect = new Rect(
                chart.Left + ActualRowHeaderWidth, chart.Top + EffectiveColHeaderHeight,
                chart.Width, chart.Height);
            if (!IntersectsDrawingViewport(rect, 0, visibleRight, visibleBottom))
                continue;

            var img = GetCachedChartImage(chart, Viewport, WorkbookTheme);
            if (img == null) continue;
            dc.DrawImage(img, rect);
        }
    }

    private void RenderTextBoxes(DrawingContext dc)
    {
        if (TextBoxes == null || Viewport == null) return;

        var themeEffect = WorkbookThemeEffectStyle.FromTheme(WorkbookTheme);
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var visibleRight = GetDrawingViewportRight();
        var visibleBottom = GetDrawingViewportBottom();
        var (lastRenderableRow, lastRenderableColumn) = GetRenderableDrawingAnchorBounds(visibleRight, visibleBottom);
        foreach (var textBox in TextBoxes)
        {
            if (!textBox.IsVisible) continue;
            if (!CanAnchoredObjectReachDrawingViewport(textBox.Anchor, lastRenderableRow, lastRenderableColumn))
                continue;
            if (!TryCreateAnchoredObjectRect(textBox.Anchor,
                    textBox.Width,
                    textBox.Height,
                    MinimumTextBoxObjectWidth,
                    MinimumTextBoxObjectHeight,
                    out var rect))
                continue;
            if (NeedsDrawingViewportCull(rect, textBox.RotationDegrees, visibleRight, visibleBottom) &&
                !IntersectsDrawingViewport(rect, textBox.RotationDegrees, visibleRight, visibleBottom))
                continue;

            var rotationPushed = PushRotation(dc, textBox.RotationDegrees, rect);
            var colors = ResolveTextBoxColors(textBox, WorkbookTheme);
            DrawTextBoxThemeEffect(dc, rect, themeEffect);
            var fillBrush = GetDrawingObjectBrush(242, colors.Fill);
            var borderPen = GetDrawingObjectPen(255, colors.Outline, 1);
            dc.DrawRectangle(fillBrush, borderPen, rect);

            var textWidth = Math.Max(1, rect.Width - 8);
            var textHeight = Math.Max(1, rect.Height - 8);
            var text = GetDrawingObjectText(textBox.Text, TextBrush, 12, textWidth, textHeight, pixelsPerDip);

            dc.PushClip(GetDrawingObjectClipGeometry(new Rect(rect.Left + 4, rect.Top + 4, textWidth, textHeight)));
            dc.DrawText(text, new Point(rect.Left + 4, rect.Top + 4));
            dc.Pop();
            if (rotationPushed) dc.Pop();
        }
    }

    private void RenderDrawingShapes(DrawingContext dc)
    {
        if (DrawingShapes == null || Viewport == null) return;

        var themeEffect = WorkbookThemeEffectStyle.FromTheme(WorkbookTheme);
        var visibleRight = GetDrawingViewportRight();
        var visibleBottom = GetDrawingViewportBottom();
        var (lastRenderableRow, lastRenderableColumn) = GetRenderableDrawingAnchorBounds(visibleRight, visibleBottom);
        foreach (var shape in DrawingShapes)
        {
            if (!shape.IsVisible) continue;
            if (!CanAnchoredObjectReachDrawingViewport(shape.Anchor, lastRenderableRow, lastRenderableColumn))
                continue;
            if (!TryCreateAnchoredObjectRect(shape.Anchor,
                    shape.Width,
                    shape.Height,
                    MinimumShapeObjectWidth,
                    MinimumShapeObjectHeight,
                    out var rect))
                continue;
            if (NeedsDrawingViewportCull(rect, shape.RotationDegrees, visibleRight, visibleBottom) &&
                !IntersectsDrawingViewport(rect, shape.RotationDegrees, visibleRight, visibleBottom))
                continue;

            var rotationPushed = PushRotation(dc, shape.RotationDegrees, rect);
            var colors = ResolveDrawingShapeColors(shape, WorkbookTheme);
            DrawShapeThemeEffect(dc, shape.Kind, rect, themeEffect);
            DrawShapeAuthoredEffect(dc, shape.Kind, rect, shape);
            var pen = GetDrawingObjectPen(255, colors.Outline, 1.5);
            var fill = CreateDrawingShapeFill(shape, colors.Fill);
            switch (shape.Kind)
            {
                case DrawingShapeKind.Rectangle:
                    dc.DrawRectangle(fill, pen, rect);
                    break;
                case DrawingShapeKind.Ellipse:
                    dc.DrawEllipse(fill, pen, new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2), rect.Width / 2, rect.Height / 2);
                    break;
                case DrawingShapeKind.Line:
                    dc.DrawLine(pen, rect.TopLeft, rect.BottomRight);
                    break;
            }
            if (rotationPushed) dc.Pop();
        }
    }

    private void RenderNativeSlicerTimelineControls(DrawingContext dc)
    {
        if (Viewport == null ||
            (NativeSlicers is not { Count: > 0 } && NativeTimelines is not { Count: > 0 }))
            return;

        var visibleRight = GetDrawingViewportRight();
        var visibleBottom = GetDrawingViewportBottom();
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (NativeSlicers is not null)
        {
            foreach (var slicer in NativeSlicers)
            {
                if (slicer.DrawingAnchor is not { } anchor ||
                    !TryCreateDrawingAnchorRect(Viewport, anchor, ActualRowHeaderWidth, EffectiveColHeaderHeight, out var rect))
                    continue;

                var controlRect = EnsureMinimumControlRect(rect);
                if (!IntersectsDrawingViewport(controlRect, 0, visibleRight, visibleBottom))
                    continue;

                DrawNativeSlicerControl(dc, controlRect, slicer, pixelsPerDip);
            }
        }

        if (NativeTimelines is not null)
        {
            foreach (var timeline in NativeTimelines)
            {
                if (timeline.DrawingAnchor is not { } anchor ||
                    !TryCreateDrawingAnchorRect(Viewport, anchor, ActualRowHeaderWidth, EffectiveColHeaderHeight, out var rect))
                    continue;

                var controlRect = EnsureMinimumControlRect(rect);
                if (!IntersectsDrawingViewport(controlRect, 0, visibleRight, visibleBottom))
                    continue;

                DrawNativeTimelineControl(dc, controlRect, timeline, pixelsPerDip);
            }
        }
    }

    public static bool TryCreateDrawingAnchorRect(
        ViewportModel? viewport,
        DrawingAnchorRange anchor,
        double rowHeaderWidth,
        double columnHeaderHeight,
        out Rect rect) =>
        GridDrawingObjectPlanner.TryCreateDrawingAnchorRect(viewport, anchor, rowHeaderWidth, columnHeaderHeight, out rect);

    private static Rect EnsureMinimumControlRect(Rect rect) =>
        GridDrawingObjectPlanner.EnsureMinimumControlRect(rect);

    private void DrawNativeSlicerControl(DrawingContext dc, Rect rect, SlicerModel slicer, double pixelsPerDip)
    {
        DrawNativeControlFrame(dc, rect, GetNativeControlCaption(slicer.Caption, slicer.Name, slicer.DrawingShapeName), pixelsPerDip);

        var selectedItemCount = slicer.SelectedItems.Count;
        var tileCount = selectedItemCount == 0 ? 1 : Math.Min(4, selectedItemCount);
        var tileTop = rect.Top + 26;
        var tileHeight = Math.Max(14, Math.Min(22, (rect.Bottom - tileTop - 6) / tileCount));
        for (var index = 0; index < tileCount; index++)
        {
            var tileRect = new Rect(rect.Left + 6, tileTop + index * (tileHeight + 3), Math.Max(1, rect.Width - 12), tileHeight);
            dc.DrawRoundedRectangle(
                slicer.SelectedItems.Count == 0 ? NativeControlSelectedTileBrush : NativeControlTileBrush,
                null,
                tileRect,
                2,
                2);
            var tileText = selectedItemCount == 0
                ? slicer.SourceFieldName ?? slicer.CacheName ?? "All"
                : slicer.SelectedItems[index];
            DrawClippedText(dc, tileText, tileRect, NativeControlMutedTextBrush, 10, verticalPadding: 1, pixelsPerDip);
        }
    }

    private void DrawNativeTimelineControl(DrawingContext dc, Rect rect, TimelineModel timeline, double pixelsPerDip)
    {
        DrawNativeControlFrame(dc, rect, GetNativeControlCaption(timeline.Caption, timeline.Name, timeline.DrawingShapeName), pixelsPerDip);

        var label = FormatTimelineRange(timeline);
        var barRect = new Rect(rect.Left + 8, rect.Top + 34, Math.Max(1, rect.Width - 16), Math.Max(6, Math.Min(14, rect.Height - 42)));
        dc.DrawRoundedRectangle(NativeControlTileBrush, null, barRect, 3, 3);
        var selectedRect = new Rect(
            barRect.Left + barRect.Width * 0.18,
            barRect.Top,
            Math.Max(6, barRect.Width * 0.56),
            barRect.Height);
        dc.DrawRoundedRectangle(NativeControlSelectedTileBrush, null, selectedRect, 3, 3);
        DrawClippedText(dc, label, new Rect(rect.Left + 6, rect.Top + 22, Math.Max(1, rect.Width - 12), 12), NativeControlMutedTextBrush, 9, verticalPadding: 0, pixelsPerDip);
    }

    private void DrawNativeControlFrame(DrawingContext dc, Rect rect, string caption, double pixelsPerDip)
    {
        dc.DrawRectangle(NativeControlBodyBrush, NativeControlBorderPen, rect);
        var headerRect = new Rect(rect.Left, rect.Top, rect.Width, Math.Min(22, rect.Height));
        dc.DrawRectangle(NativeControlHeaderBrush, null, headerRect);
        DrawClippedText(dc, caption, new Rect(headerRect.Left + 5, headerRect.Top + 2, Math.Max(1, headerRect.Width - 10), Math.Max(1, headerRect.Height - 4)), Brushes.White, 11, verticalPadding: 0, pixelsPerDip);
    }

    private void DrawClippedText(DrawingContext dc, string textValue, Rect rect, Brush brush, double fontSize, double verticalPadding, double pixelsPerDip)
    {
        var text = GetDrawingObjectText(
            string.IsNullOrWhiteSpace(textValue) ? " " : textValue,
            brush,
            fontSize,
            Math.Max(1, rect.Width),
            Math.Max(1, rect.Height),
            pixelsPerDip,
            TextTrimming.CharacterEllipsis);

        dc.PushClip(GetDrawingObjectClipGeometry(rect));
        dc.DrawText(text, new Point(rect.Left, rect.Top + verticalPadding));
        dc.Pop();
    }

    private static string GetNativeControlCaption(string? caption, string name, string? shapeName)
        => GridDrawingObjectPlanner.GetNativeControlCaption(caption, name, shapeName);

    private static string FormatTimelineRange(TimelineModel timeline)
        => GridDrawingObjectPlanner.FormatTimelineRange(timeline);

    private Brush CreateDrawingShapeFill(DrawingShapeModel shape, CellColor startColor)
    {
        if (shape.GradientFillEndColor is { } endColor && shape.Kind != DrawingShapeKind.Line)
            return GetDrawingObjectGradientBrush(startColor, endColor, shape.GetEffectiveGradientFillDirection());

        return GetDrawingObjectBrush(32, startColor);
    }

    private void DrawShapeAuthoredEffect(DrawingContext dc, DrawingShapeKind kind, Rect rect, DrawingShapeModel shape)
    {
        switch (shape.GetEffectiveEffectPreset())
        {
            case DrawingShapeEffectPreset.Shadow:
                DrawShapeShadowEffect(dc, kind, rect, offsetX: 3, offsetY: 3, alpha: 58);
                break;
            case DrawingShapeEffectPreset.Glow:
                DrawShapeOutlineEffect(dc, kind, rect, alpha: 96, r: 91, g: 155, b: 213, thickness: 6, inflate: 3);
                break;
            case DrawingShapeEffectPreset.SoftEdges:
                DrawShapeOutlineEffect(dc, kind, rect, alpha: 54, r: 128, g: 128, b: 128, thickness: 8, inflate: 2);
                break;
        }
    }

    private void DrawShapeShadowEffect(
        DrawingContext dc,
        DrawingShapeKind kind,
        Rect rect,
        double offsetX,
        double offsetY,
        byte alpha)
    {
        var shadowRect = rect;
        shadowRect.Offset(offsetX, offsetY);
        var shadowBrush = GetDrawingObjectBrush(alpha, 0, 0, 0);
        var shadowPen = GetDrawingObjectPen(alpha, 0, 0, 0, 2);

        switch (kind)
        {
            case DrawingShapeKind.Rectangle:
                dc.DrawRectangle(shadowBrush, null, shadowRect);
                break;
            case DrawingShapeKind.Ellipse:
                dc.DrawEllipse(shadowBrush, null, new Point(shadowRect.Left + shadowRect.Width / 2, shadowRect.Top + shadowRect.Height / 2), shadowRect.Width / 2, shadowRect.Height / 2);
                break;
            case DrawingShapeKind.Line:
                dc.DrawLine(shadowPen, shadowRect.TopLeft, shadowRect.BottomRight);
                break;
        }
    }

    private void DrawShapeOutlineEffect(
        DrawingContext dc,
        DrawingShapeKind kind,
        Rect rect,
        byte alpha,
        byte r,
        byte g,
        byte b,
        double thickness,
        double inflate)
    {
        var effectRect = rect;
        effectRect.Inflate(inflate, inflate);
        var pen = GetDrawingObjectPen(alpha, r, g, b, thickness);

        switch (kind)
        {
            case DrawingShapeKind.Rectangle:
                dc.DrawRectangle(null, pen, effectRect);
                break;
            case DrawingShapeKind.Ellipse:
                dc.DrawEllipse(null, pen, new Point(effectRect.Left + effectRect.Width / 2, effectRect.Top + effectRect.Height / 2), effectRect.Width / 2, effectRect.Height / 2);
                break;
            case DrawingShapeKind.Line:
                dc.DrawLine(pen, effectRect.TopLeft, effectRect.BottomRight);
                break;
        }
    }

    private void DrawTextBoxThemeEffect(DrawingContext dc, Rect rect, WorkbookThemeEffectStyle effect)
    {
        if (!effect.HasShadow)
            return;

        var shadowRect = rect;
        shadowRect.Offset(effect.ShadowOffsetX, effect.ShadowOffsetY);
        var alpha = (byte)Math.Clamp(Math.Round(255 * effect.ShadowOpacity), 0, 255);
        dc.DrawRectangle(GetDrawingObjectBrush(alpha, 0, 0, 0), null, shadowRect);
    }

    private void DrawShapeThemeEffect(DrawingContext dc, DrawingShapeKind kind, Rect rect, WorkbookThemeEffectStyle effect)
    {
        if (!effect.HasShadow)
            return;

        var shadowRect = rect;
        shadowRect.Offset(effect.ShadowOffsetX, effect.ShadowOffsetY);
        var alpha = (byte)Math.Clamp(Math.Round(255 * effect.ShadowOpacity), 0, 255);
        var shadowBrush = GetDrawingObjectBrush(alpha, 0, 0, 0);
        var shadowPen = GetDrawingObjectPen(alpha, 0, 0, 0, 2);

        switch (kind)
        {
            case DrawingShapeKind.Rectangle:
                dc.DrawRectangle(shadowBrush, null, shadowRect);
                break;
            case DrawingShapeKind.Ellipse:
                dc.DrawEllipse(shadowBrush, null, new Point(shadowRect.Left + shadowRect.Width / 2, shadowRect.Top + shadowRect.Height / 2), shadowRect.Width / 2, shadowRect.Height / 2);
                break;
            case DrawingShapeKind.Line:
                dc.DrawLine(shadowPen, shadowRect.TopLeft, shadowRect.BottomRight);
                break;
        }
    }

    public static DrawingObjectColors ResolveDrawingShapeColors(DrawingShapeModel shape, WorkbookTheme theme) =>
        GridDrawingObjectPlanner.ResolveDrawingShapeColors(shape, theme);

    public static DrawingObjectColors ResolveTextBoxColors(TextBoxModel textBox, WorkbookTheme theme) =>
        GridDrawingObjectPlanner.ResolveTextBoxColors(textBox, theme);

    private static bool PushRotation(DrawingContext dc, double rotationDegrees, Rect rect)
    {
        if (Math.Abs(rotationDegrees % 360) <= 0.0001)
            return false;

        dc.PushTransform(new RotateTransform(
            rotationDegrees,
            rect.Left + rect.Width / 2,
            rect.Top + rect.Height / 2));
        return true;
    }

    private void RenderObjectPlaceholders(DrawingContext dc)
    {
        if (Viewport == null) return;

        var visibleRight = GetDrawingViewportRight();
        var visibleBottom = GetDrawingViewportBottom();
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (Charts is not null)
        {
            var index = 1;
            foreach (var chart in Charts)
            {
                if (chart.IsVisible)
                {
                    var rect = new Rect(
                        chart.Left + ActualRowHeaderWidth,
                        chart.Top + EffectiveColHeaderHeight,
                        Math.Max(24, chart.Width),
                        Math.Max(18, chart.Height));
                    if (IntersectsDrawingViewport(rect, 0, visibleRight, visibleBottom))
                        DrawObjectPlaceholder(dc, rect, CreateObjectPlaceholderLabel("Chart", chart.Name, index), pixelsPerDip);
                }
                index++;
            }
        }

        var (lastRenderableRow, lastRenderableColumn) = GetRenderableDrawingAnchorBounds(visibleRight, visibleBottom);
        if (DrawingShapes is not null)
        {
            var index = 1;
            foreach (var shape in DrawingShapes)
            {
                if (shape.IsVisible &&
                    CanAnchoredObjectReachDrawingViewport(shape.Anchor, lastRenderableRow, lastRenderableColumn) &&
                    TryCreateAnchoredObjectRect(shape.Anchor,
                        shape.Width,
                        shape.Height,
                        MinimumShapeObjectWidth,
                        MinimumShapeObjectHeight,
                        out var rect) &&
                    (!NeedsDrawingViewportCull(rect, shape.RotationDegrees, visibleRight, visibleBottom) ||
                        IntersectsDrawingViewport(rect, shape.RotationDegrees, visibleRight, visibleBottom)))
                    DrawObjectPlaceholder(dc, rect, CreateObjectPlaceholderLabel("Shape", shape.Name, index), pixelsPerDip);
                index++;
            }
        }

        if (Pictures is not null)
        {
            var index = 1;
            foreach (var picture in Pictures)
            {
                if (picture.IsVisible &&
                    CanAnchoredObjectReachDrawingViewport(picture.Anchor, lastRenderableRow, lastRenderableColumn) &&
                    TryCreateAnchoredObjectRect(picture.Anchor,
                        picture.Width,
                        picture.Height,
                        MinimumPictureObjectWidth,
                        MinimumPictureObjectHeight,
                        out var rect) &&
                    (!NeedsDrawingViewportCull(rect, picture.RotationDegrees, visibleRight, visibleBottom) ||
                        IntersectsDrawingViewport(rect, picture.RotationDegrees, visibleRight, visibleBottom)))
                    DrawObjectPlaceholder(dc, rect, CreateObjectPlaceholderLabel("Picture", picture.Name, index), pixelsPerDip);
                index++;
            }
        }

        if (TextBoxes is not null)
        {
            var index = 1;
            foreach (var textBox in TextBoxes)
            {
                if (textBox.IsVisible &&
                    CanAnchoredObjectReachDrawingViewport(textBox.Anchor, lastRenderableRow, lastRenderableColumn) &&
                    TryCreateAnchoredObjectRect(textBox.Anchor,
                        textBox.Width,
                        textBox.Height,
                        MinimumTextBoxObjectWidth,
                        MinimumTextBoxObjectHeight,
                        out var rect) &&
                    (!NeedsDrawingViewportCull(rect, textBox.RotationDegrees, visibleRight, visibleBottom) ||
                        IntersectsDrawingViewport(rect, textBox.RotationDegrees, visibleRight, visibleBottom)))
                    DrawObjectPlaceholder(dc, rect, CreateObjectPlaceholderLabel("Text Box", textBox.Name, index), pixelsPerDip);
                index++;
            }
        }

        if (NativeSlicers is not null)
        {
            var index = 1;
            foreach (var slicer in NativeSlicers)
            {
                if (slicer.DrawingAnchor is { } anchor &&
                    TryCreateDrawingAnchorRect(Viewport, anchor, ActualRowHeaderWidth, EffectiveColHeaderHeight, out var rect))
                {
                    var controlRect = EnsureMinimumControlRect(rect);
                    if (IntersectsDrawingViewport(controlRect, 0, visibleRight, visibleBottom))
                        DrawObjectPlaceholder(dc, controlRect, CreateObjectPlaceholderLabel("Slicer", slicer.DrawingShapeName ?? slicer.Caption ?? slicer.Name, index), pixelsPerDip);
                }
                index++;
            }
        }

        if (NativeTimelines is not null)
        {
            var index = 1;
            foreach (var timeline in NativeTimelines)
            {
                if (timeline.DrawingAnchor is { } anchor &&
                    TryCreateDrawingAnchorRect(Viewport, anchor, ActualRowHeaderWidth, EffectiveColHeaderHeight, out var rect))
                {
                    var controlRect = EnsureMinimumControlRect(rect);
                    if (IntersectsDrawingViewport(controlRect, 0, visibleRight, visibleBottom))
                        DrawObjectPlaceholder(dc, controlRect, CreateObjectPlaceholderLabel("Timeline", timeline.DrawingShapeName ?? timeline.Caption ?? timeline.Name, index), pixelsPerDip);
                }
                index++;
            }
        }
    }

    public static string CreateObjectPlaceholderLabel(string objectType, string? objectName, int index)
        => GridDrawingObjectPlanner.CreateObjectPlaceholderLabel(objectType, objectName, index);

    public bool TryCreateAnchoredObjectRect(
        CellAddress anchor,
        double width,
        double height,
        double minimumWidth,
        double minimumHeight,
        out Rect rect) =>
        GridDrawingObjectPlanner.TryCreateAnchoredObjectRect(
            Viewport,
            anchor,
            ActualRowHeaderWidth,
            EffectiveColHeaderHeight,
            width,
            height,
            minimumWidth,
            minimumHeight,
            out rect);

    private void DrawObjectPlaceholder(DrawingContext dc, Rect rect, string label, double pixelsPerDip)
    {
        dc.DrawRectangle(ObjectPlaceholderFill, ObjectPlaceholderPen, rect);
        DrawPlaceholderDiagonals(dc, rect);

        var text = GetDrawingObjectText(
            label,
            ObjectPlaceholderTextBrush,
            11,
            Math.Max(1, rect.Width - 8),
            Math.Max(1, rect.Height - 8),
            pixelsPerDip,
            TextTrimming.CharacterEllipsis);

        var textPoint = new Point(
            rect.Left + Math.Max(4, (rect.Width - text.Width) / 2),
            rect.Top + Math.Max(4, (rect.Height - text.Height) / 2));
        dc.PushClip(GetDrawingObjectClipGeometry(new Rect(rect.Left + 4, rect.Top + 4, Math.Max(1, rect.Width - 8), Math.Max(1, rect.Height - 8))));
        dc.DrawText(text, textPoint);
        dc.Pop();
    }

    private static void DrawPlaceholderDiagonals(DrawingContext dc, Rect rect)
    {
        dc.DrawLine(ObjectPlaceholderPen, rect.TopLeft, rect.BottomRight);
        dc.DrawLine(ObjectPlaceholderPen, rect.TopRight, rect.BottomLeft);
    }

    private static Pen CreateFrozenPen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }

    private Brush GetDrawingObjectBrush(byte alpha, CellColor color) =>
        GetDrawingObjectBrush(alpha, color.R, color.G, color.B);

    private Brush GetDrawingObjectBrush(byte alpha, byte r, byte g, byte b)
    {
        var key = new DrawingObjectBrushKey(alpha, r, g, b);
        if (_drawingObjectBrushCache.TryGetValue(key, out var cached))
            return cached;

        if (_drawingObjectBrushCache.Count >= DrawingObjectBrushCacheLimit)
            _drawingObjectBrushCache.Clear();

        var brush = MakeBrushAlpha(alpha, r, g, b);
        _drawingObjectBrushCache.Add(key, brush);
        return brush;
    }

    private Pen GetDrawingObjectPen(byte alpha, CellColor color, double thickness) =>
        GetDrawingObjectPen(alpha, color.R, color.G, color.B, thickness);

    private Pen GetDrawingObjectPen(byte alpha, byte r, byte g, byte b, double thickness)
    {
        var key = new DrawingObjectPenKey(alpha, r, g, b, thickness);
        if (_drawingObjectPenCache.TryGetValue(key, out var cached))
            return cached;

        if (_drawingObjectPenCache.Count >= DrawingObjectPenCacheLimit)
            _drawingObjectPenCache.Clear();

        var pen = CreateFrozenPen(GetDrawingObjectBrush(alpha, r, g, b), thickness);
        _drawingObjectPenCache.Add(key, pen);
        return pen;
    }

    private Brush GetDrawingObjectGradientBrush(
        CellColor startColor,
        CellColor endColor,
        DrawingShapeGradientDirection direction)
    {
        var effectiveDirection = Enum.IsDefined(direction)
            ? direction
            : DrawingShapeGradientDirection.DiagonalDown;
        var key = new DrawingObjectGradientBrushKey(startColor, endColor, effectiveDirection);
        if (_drawingObjectGradientBrushCache.TryGetValue(key, out var cached))
            return cached;

        if (_drawingObjectGradientBrushCache.Count >= DrawingObjectGradientBrushCacheLimit)
            _drawingObjectGradientBrushCache.Clear();

        var (startPoint, endPoint) = GetDrawingObjectGradientPoints(effectiveDirection);
        var brush = new LinearGradientBrush(
            Color.FromArgb(72, startColor.R, startColor.G, startColor.B),
            Color.FromArgb(72, endColor.R, endColor.G, endColor.B),
            startPoint,
            endPoint);
        brush.Freeze();
        _drawingObjectGradientBrushCache.Add(key, brush);
        return brush;
    }

    private static (Point Start, Point End) GetDrawingObjectGradientPoints(DrawingShapeGradientDirection direction) =>
        direction switch
        {
            DrawingShapeGradientDirection.Horizontal => (new Point(0, 0.5), new Point(1, 0.5)),
            DrawingShapeGradientDirection.Vertical => (new Point(0.5, 0), new Point(0.5, 1)),
            DrawingShapeGradientDirection.DiagonalUp => (new Point(0, 1), new Point(1, 0)),
            _ => (new Point(0, 0), new Point(1, 1))
        };

    private RectangleGeometry GetDrawingObjectClipGeometry(Rect rect)
    {
        if (_drawingObjectClipGeometryCache.TryGetValue(rect, out var cached))
            return cached;

        if (_drawingObjectClipGeometryCache.Count >= DrawingObjectClipGeometryCacheLimit)
            _drawingObjectClipGeometryCache.Clear();

        var geometry = new RectangleGeometry(rect);
        geometry.Freeze();
        _drawingObjectClipGeometryCache.Add(rect, geometry);
        return geometry;
    }

    private FormattedText GetDrawingObjectText(
        string textValue,
        Brush brush,
        double fontSize,
        double maxTextWidth,
        double maxTextHeight,
        double pixelsPerDip,
        TextTrimming trimming = TextTrimming.None)
    {
        var key = new DrawingObjectTextLayoutKey(
            textValue,
            CultureInfo.CurrentCulture.Name,
            brush,
            fontSize,
            maxTextWidth,
            maxTextHeight,
            pixelsPerDip,
            trimming);
        if (_drawingObjectTextLayoutCache.TryGetValue(key, out var cached))
            return cached;

        if (_drawingObjectTextLayoutCache.Count >= DrawingObjectTextLayoutCacheLimit)
            _drawingObjectTextLayoutCache.Clear();

        var formatted = new FormattedText(
            textValue,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            DefaultTypeface,
            fontSize,
            brush,
            pixelsPerDip)
        {
            MaxTextWidth = maxTextWidth,
            MaxTextHeight = maxTextHeight,
            Trimming = trimming
        };
        _drawingObjectTextLayoutCache.Add(key, formatted);
        return formatted;
    }

    private readonly record struct DrawingObjectBrushKey(byte Alpha, byte R, byte G, byte B);

    private readonly record struct DrawingObjectPenKey(byte Alpha, byte R, byte G, byte B, double Thickness);

    private readonly record struct DrawingObjectGradientBrushKey(
        CellColor StartColor,
        CellColor EndColor,
        DrawingShapeGradientDirection Direction);

    private readonly record struct DrawingObjectTextLayoutKey(
        string Text,
        string CultureName,
        Brush Brush,
        double FontSize,
        double MaxTextWidth,
        double MaxTextHeight,
        double PixelsPerDip,
        TextTrimming Trimming);

}

public readonly record struct DrawingObjectColors(CellColor Fill, CellColor Outline);
