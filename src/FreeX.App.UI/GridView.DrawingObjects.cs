using System.Globalization;
using System.Windows;
using System.Windows.Media;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.DrawingUI;
using FreeX.App.Presentation.Shapes;
using FreeX.App.Presentation.SlicerTimeline;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public partial class GridView
{
    // Floating drawing objects, pictures, charts, and worksheet background rendering.

    private static readonly Brush ObjectPlaceholderFill = MakeBrushAlpha(48, 255, 255, 255);
    private static readonly Brush ObjectPlaceholderTextBrush = MakeBrush(89, 89, 89);
    private static readonly Pen ObjectPlaceholderPen = CreateFrozenPen(MakeBrush(120, 120, 120), 1);
    private static readonly Brush ChartObjectBackgroundBrush = MakeBrush(255, 255, 255);
    private static readonly Pen ChartObjectBorderPen = CreateFrozenPen(MakeBrush(166, 166, 166), 1);
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
        var bounds = DrawingObjectViewportPlanner.GetRenderableAnchorBounds(
            viewport,
            ActualRowHeaderWidth,
            EffectiveColHeaderHeight,
            visibleRight,
            visibleBottom);
        return (bounds.LastRow, bounds.LastColumn);
    }

    private static bool CanAnchoredObjectReachDrawingViewport(
        CellAddress anchor,
        uint lastRenderableRow,
        uint lastRenderableColumn) =>
        DrawingObjectViewportPlanner.CanAnchoredObjectReachViewport(
            anchor,
            new DrawingViewportAnchorBounds(lastRenderableRow, lastRenderableColumn));

    private static bool CanAnchoredObjectReachDrawingViewport(
        DrawingAnchorRange anchor,
        uint lastRenderableRow,
        uint lastRenderableColumn) =>
        DrawingObjectViewportPlanner.CanAnchorRangeReachViewport(
            anchor,
                new DrawingViewportAnchorBounds(lastRenderableRow, lastRenderableColumn));

    private static bool ShouldDisplayAnchoredDrawingObject(
        bool isVisible,
        CellAddress anchor,
        uint lastRenderableRow,
        uint lastRenderableColumn) =>
        GridDrawingObjectPlanner.ShouldDisplayAnchoredObject(
            isVisible,
            anchor,
            lastRenderableRow,
            lastRenderableColumn);

    private static bool ShouldDisplayDrawingAnchorRange(
        DrawingAnchorRange anchor,
        uint lastRenderableRow,
        uint lastRenderableColumn) =>
        GridDrawingObjectPlanner.ShouldDisplayAnchorRange(
            anchor,
            lastRenderableRow,
            lastRenderableColumn);

    private static bool NeedsDrawingViewportCull(
        Rect rect,
        double rotationDegrees,
        double visibleRight,
        double visibleBottom) =>
        GridDrawingObjectPlanner.NeedsViewportCull(rect, rotationDegrees, visibleRight, visibleBottom);

    private static bool ShouldDisplayDrawingObjectRect(
        Rect rect,
        double rotationDegrees,
        double visibleRight,
        double visibleBottom) =>
        GridDrawingObjectPlanner.ShouldDisplayObjectRect(rect, rotationDegrees, visibleRight, visibleBottom);

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
        => GridDrawingObjectPlanner.IntersectsViewport(rect, rotationDegrees, visibleRight, visibleBottom);

    private static Rect CalculateRotatedBounds(Rect rect, double rotationDegrees) =>
        GridDrawingObjectPlanner.CalculateRotatedBounds(rect, rotationDegrees);

    private void RenderCharts(DrawingContext dc)
    {
        if (Charts == null || Viewport == null) return;
        var visibleRight = GetDrawingViewportRight();
        var visibleBottom = GetDrawingViewportBottom();
        var dpi = VisualTreeHelper.GetDpi(this);
        var zoom = ZoomFactor > 0 ? ZoomFactor : 1.0;
        var renderScale = Math.Clamp(Math.Max(dpi.DpiScaleX, dpi.DpiScaleY) * zoom, 0.25, 4.0);
        foreach (var chart in Charts)
            RenderChart(dc, chart, visibleRight, visibleBottom, renderScale);
    }

    // R60-render-drawing-shapes-6-1: extracted so a single chart can be drawn in its recorded
    // z-order position (see RenderDrawingObjectsByZOrder) instead of only ever being drawable as
    // part of the unconditional "all charts first" pass.
    private void RenderChart(DrawingContext dc, ChartModel chart, double visibleRight, double visibleBottom, double renderScale)
    {
        if (!chart.IsVisible || Viewport == null) return;
        var rect = CreateChartRect(chart);
        if (TryResolveLiveObjectTransform(
                chart.Id,
                ObjectKind.Chart,
                rect,
                committedRotationDegrees: 0,
                committedFlipHorizontal: false,
                committedFlipVertical: false,
                out var previewRect,
                out _,
                out _,
                out _))
        {
            rect = previewRect;
        }

        if (!ShouldDisplayDrawingObjectRect(rect, 0, visibleRight, visibleBottom))
            return;

        DrawChartObjectBackground(dc, chart, rect);
        var img = GetCachedChartImage(chart, Viewport, WorkbookTheme, renderScale);
        if (img is not null)
            dc.DrawImage(img, rect);
        DrawChartObjectBorder(dc, chart, rect);
    }

    private void DrawChartObjectBackground(DrawingContext dc, ChartModel chart, Rect rect)
    {
        // R44-meta-1: "No Fill" is an explicit user choice distinct from "nothing set" -- paint
        // nothing (transparent) instead of falling back to the opaque default chart-area brush.
        if (chart.IsChartAreaFillSuppressed)
            return;

        var fill = chart.ResolveChartAreaFillColor(WorkbookTheme) is { } fillColor
            ? GetDrawingObjectBrush(255, fillColor)
            : ChartObjectBackgroundBrush;
        dc.DrawRectangle(fill, null, rect);
    }

    private void DrawChartObjectBorder(DrawingContext dc, ChartModel chart, Rect rect)
    {
        // R44-meta-1: "No Line" is an explicit user choice -- draw no border at all rather than
        // falling back to the default chart-area border pen.
        if (chart.IsChartAreaLineSuppressed)
            return;

        var borderThickness = chart.ChartAreaBorderThickness is { } thickness &&
            double.IsFinite(thickness) &&
            thickness > 0
                ? thickness
                : 1.0;
        var border = chart.ResolveChartAreaBorderColor(WorkbookTheme) is { } borderColor
            ? GetDrawingObjectPen(255, borderColor, borderThickness)
            : ChartObjectBorderPen;
        dc.DrawRectangle(null, border, rect);
    }

    private void RenderTextBoxes(DrawingContext dc)
    {
        if (TextBoxes == null || Viewport == null) return;

        var themeEffect = WorkbookThemeEffectStyle.FromTheme(WorkbookTheme);
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var visibleRight = GetDrawingViewportRight();
        var visibleBottom = GetDrawingViewportBottom();
        var (lastRenderableRow, lastRenderableColumn) = GetRenderableDrawingAnchorBounds(visibleRight, visibleBottom);
        var metricLookups = GetRenderMetricLookups(Viewport);
        foreach (var textBox in TextBoxes)
            RenderTextBox(dc, metricLookups, textBox, themeEffect, pixelsPerDip, visibleRight, visibleBottom, lastRenderableRow, lastRenderableColumn);
    }

    private void RenderTextBox(
        DrawingContext dc,
        RenderMetricLookupCache metricLookups,
        TextBoxModel textBox,
        WorkbookThemeEffectStyle themeEffect,
        double pixelsPerDip,
        double visibleRight,
        double visibleBottom,
        uint lastRenderableRow,
        uint lastRenderableColumn)
    {
        if (!ShouldDisplayAnchoredDrawingObject(textBox.IsVisible, textBox.Anchor, lastRenderableRow, lastRenderableColumn))
            return;
        if (!TryCreateAnchoredObjectRect(
                metricLookups,
                textBox.Anchor,
                textBox.Width,
                textBox.Height,
                MinimumTextBoxObjectWidth,
                MinimumTextBoxObjectHeight,
                out var rect,
                textBox.AnchorOffsetX,
                textBox.AnchorOffsetY))
            return;

        var metadata = ResolveTextBoxRenderMetadata(textBox, WorkbookTheme);
        var rotationDegrees = metadata.Transform.RotationDegrees;
        var flipHorizontal = metadata.Transform.FlipHorizontal;
        var flipVertical = metadata.Transform.FlipVertical;
        if (TryResolveLiveObjectTransform(
                textBox.Id,
                ObjectKind.TextBox,
                rect,
                rotationDegrees,
                flipHorizontal,
                flipVertical,
                out var previewRect,
                out var previewRotationDegrees,
                out var previewFlipHorizontal,
                out var previewFlipVertical))
        {
            rect = previewRect;
            rotationDegrees = previewRotationDegrees;
            flipHorizontal = previewFlipHorizontal;
            flipVertical = previewFlipVertical;
        }

        if (!ShouldDisplayDrawingObjectRect(rect, rotationDegrees, visibleRight, visibleBottom))
            return;

        var transformState = PushDrawingObjectTransform(dc, rotationDegrees, flipHorizontal, flipVertical, rect);
        var colors = new DrawingObjectColors(metadata.Paint.Fill, metadata.Paint.Outline);
        DrawTextBoxThemeEffect(dc, rect, themeEffect);
        var fillBrush = metadata.Paint.HasFill ? GetDrawingObjectBrush(242, colors.Fill) : null;
        // R91-commands-insert-object-5-1: a text box's line can be explicitly suppressed
        // (TextBoxModel.OutlineHasNoFill, e.g. Excel's Insert > Text Box default) -- draw no
        // border pen at all rather than always forcing one, mirroring RenderDrawingShape's
        // GetDrawingShapeOutlinePen null-for-no-outline behavior.
        var borderPen = metadata.Paint.HasOutline ? GetDrawingObjectPen(255, colors.Outline, 1) : null;
        dc.DrawRectangle(fillBrush, borderPen, rect);
        DrawTextBoxThemeInnerShadow(dc, rect, themeEffect);
        if (EditingTextBoxId is { } editingTextBoxId && editingTextBoxId == textBox.Id)
        {
            PopDrawingObjectTransform(dc, transformState);
            return;
        }

        var textWidth = Math.Max(1, rect.Width - 8);
        var textHeight = Math.Max(1, rect.Height - 8);
        var textClipRect = new Rect(rect.Left + 4, rect.Top + 4, textWidth, textHeight);
        var text = GetDrawingObjectText(textBox.Text, TextBrush, 12, textWidth, textHeight, pixelsPerDip);

        // Draw text with the flip's ScaleTransform popped (rotation, if any, stays active):
        // Excel mirrors a flipped text box's outline but never its text (same as shapes).
        PopDrawingObjectFlipTransform(dc, ref transformState);
        dc.PushClip(GetDrawingObjectClipGeometry(textClipRect));
        dc.DrawText(text, new Point(rect.Left + 4, rect.Top + 4));
        dc.Pop();
        PopDrawingObjectTransform(dc, transformState);
    }

    private void RenderDrawingShapes(DrawingContext dc)
    {
        if (DrawingShapes == null || Viewport == null) return;

        var themeEffect = WorkbookThemeEffectStyle.FromTheme(WorkbookTheme);
        var visibleRight = GetDrawingViewportRight();
        var visibleBottom = GetDrawingViewportBottom();
        var (lastRenderableRow, lastRenderableColumn) = GetRenderableDrawingAnchorBounds(visibleRight, visibleBottom);
        var metricLookups = GetRenderMetricLookups(Viewport);
        foreach (var shape in DrawingShapes)
            RenderDrawingShape(dc, metricLookups, shape, themeEffect, visibleRight, visibleBottom, lastRenderableRow, lastRenderableColumn);
    }

    private void RenderDrawingShape(
        DrawingContext dc,
        RenderMetricLookupCache metricLookups,
        DrawingShapeModel shape,
        WorkbookThemeEffectStyle themeEffect,
        double visibleRight,
        double visibleBottom,
        uint lastRenderableRow,
        uint lastRenderableColumn)
    {
        if (!ShouldDisplayAnchoredDrawingObject(shape.IsVisible, shape.Anchor, lastRenderableRow, lastRenderableColumn))
            return;
        if (!TryCreateAnchoredObjectRect(
                metricLookups,
                shape.Anchor,
                shape.Width,
                shape.Height,
                MinimumShapeObjectWidth,
                MinimumShapeObjectHeight,
                out var rect,
                shape.AnchorOffsetX,
                shape.AnchorOffsetY))
            return;

        var metadata = ResolveDrawingShapeRenderMetadata(shape, WorkbookTheme);
        var rotationDegrees = metadata.Transform.RotationDegrees;
        var flipHorizontal = metadata.Transform.FlipHorizontal;
        var flipVertical = metadata.Transform.FlipVertical;
        if (TryResolveLiveObjectTransform(
                shape.Id,
                ObjectKind.Shape,
                rect,
                rotationDegrees,
                flipHorizontal,
                flipVertical,
                out var previewRect,
                out var previewRotationDegrees,
                out var previewFlipHorizontal,
                out var previewFlipVertical))
        {
            rect = previewRect;
            rotationDegrees = previewRotationDegrees;
            flipHorizontal = previewFlipHorizontal;
            flipVertical = previewFlipVertical;
        }

        if (!ShouldDisplayDrawingObjectRect(rect, rotationDegrees, visibleRight, visibleBottom))
            return;

        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var transformState = PushDrawingObjectTransform(dc, rotationDegrees, flipHorizontal, flipVertical, rect);
        var colors = new DrawingObjectColors(metadata.Paint.Fill, metadata.Paint.Outline);
        var shapeThemeEffect = ResolveDrawingShapeThemeEffect(metadata, themeEffect);
        var pen = GetDrawingShapeOutlinePen(colors.Outline, metadata.Outline);
        // Render the body fill when the shape has an authored fill (HasFill=true).
        // WordArt with no body fill (HasFill=false) correctly produces null here.
        // WordArt WITH an authored body fill still renders the box behind the styled text,
        // matching Excel which shows the filled box and the styled run text together.
        var bodyFill = metadata.Paint.HasFill ? CreateDrawingShapeFill(metadata) : null;
        DrawShapeThemeEffect(dc, metadata.Kind, rect, shapeThemeEffect, colors);
        DrawShapeAuthoredEffect(dc, metadata.Kind, rect, metadata.AuthoredEffect, colors);
        DrawShapeGeometry(dc, metadata.Kind, rect, metadata.IsLineLike ? null : bodyFill, pen);
        if (metadata.IsLineLike)
            DrawShapeArrowheads(dc, shape, rect, flipHorizontal, flipVertical, colors.Outline, metadata.Outline.ThicknessDip);
        DrawShapeAuthoredBevelEffect(dc, metadata.Kind, rect, metadata.AuthoredEffect);
        DrawShapeThemeBevelEffect(dc, metadata.Kind, rect, shapeThemeEffect);
        DrawShapeAuthoredInnerShadow(dc, metadata.Kind, rect, metadata.AuthoredEffect);
        DrawShapeThemeInnerShadow(dc, metadata.Kind, rect, shapeThemeEffect);
        if (metadata.HasShapeText)
        {
            // Draw text with the flip's ScaleTransform popped (rotation, if any, stays active):
            // Excel mirrors a flipped shape's geometry but never its text.
            PopDrawingObjectFlipTransform(dc, ref transformState);
            DrawShapeText(dc, shape, rect, pixelsPerDip);
        }
        PopDrawingObjectTransform(dc, transformState);
    }

    private bool HasExplicitDrawingObjectZOrder() =>
        GridDrawingObjectPlanner.HasExplicitDrawingObjectZOrder(DrawingObjectZOrder);

    private void RenderDrawingObjectsByZOrder(DrawingContext dc)
    {
        if (Viewport == null) return;

        var order = GetNormalizedDrawingObjectZOrder();
        if (order.Count == 0)
            return;

        var themeEffect = WorkbookThemeEffectStyle.FromTheme(WorkbookTheme);
        var dpi = VisualTreeHelper.GetDpi(this);
        var pixelsPerDip = dpi.PixelsPerDip;
        var visibleRight = GetDrawingViewportRight();
        var visibleBottom = GetDrawingViewportBottom();
        var (lastRenderableRow, lastRenderableColumn) = GetRenderableDrawingAnchorBounds(visibleRight, visibleBottom);
        var metricLookups = GetRenderMetricLookups(Viewport);
        var zoom = ZoomFactor > 0 ? ZoomFactor : 1.0;
        var chartRenderScale = Math.Clamp(Math.Max(dpi.DpiScaleX, dpi.DpiScaleY) * zoom, 0.25, 4.0);
        foreach (var entry in order)
        {
            switch (entry.Kind)
            {
                case SelectionPaneObjectKind.Shape when FindDrawingShape(entry.Id) is { } shape:
                    RenderDrawingShape(dc, metricLookups, shape, themeEffect, visibleRight, visibleBottom, lastRenderableRow, lastRenderableColumn);
                    break;
                case SelectionPaneObjectKind.Picture when FindPicture(entry.Id) is { } picture:
                    RenderPicture(dc, metricLookups, picture, Brushes.White, pixelsPerDip, visibleRight, visibleBottom, lastRenderableRow, lastRenderableColumn);
                    break;
                case SelectionPaneObjectKind.TextBox when FindTextBox(entry.Id) is { } textBox:
                    RenderTextBox(dc, metricLookups, textBox, themeEffect, pixelsPerDip, visibleRight, visibleBottom, lastRenderableRow, lastRenderableColumn);
                    break;
                case SelectionPaneObjectKind.Chart when FindChart(entry.Id) is { } chart:
                    // R60-render-drawing-shapes-6-1: charts now draw in their recorded z-order slot
                    // instead of always being forced behind every shape/picture/textbox.
                    RenderChart(dc, chart, visibleRight, visibleBottom, chartRenderScale);
                    break;
            }
        }
    }

    private IReadOnlyList<DrawingObjectZOrderEntry> GetNormalizedDrawingObjectZOrder()
    {
        var normalized = GridDrawingObjectPlanner.NormalizeDrawingObjectZOrder(
            DrawingShapes,
            Pictures,
            TextBoxes,
            DrawingObjectZOrder);

        return Charts is { Count: > 0 } charts
            ? MergeChartsIntoDrawingObjectZOrder(normalized, charts, DrawingObjectZOrder)
            : normalized;
    }

    // R60-render-drawing-shapes-6-1: GridDrawingObjectPlanner.NormalizeDrawingObjectZOrder (shared
    // with the OOXML load-time normalizer) only knows about shapes/pictures/textboxes, so charts are
    // merged back in here at the GridView render layer: any chart position recorded in the raw
    // DrawingObjectZOrder list is preserved relative to the other objects, and any chart with no
    // recorded position is appended at the end -- mirroring the "missing object" fallback the shared
    // normalizer already uses for shapes/pictures/textboxes.
    private static IReadOnlyList<DrawingObjectZOrderEntry> MergeChartsIntoDrawingObjectZOrder(
        IReadOnlyList<DrawingObjectZOrderEntry> normalized,
        IReadOnlyList<ChartModel> charts,
        IReadOnlyList<DrawingObjectZOrderEntry>? rawOrder)
    {
        var placedChartIds = new HashSet<Guid>();
        var merged = new List<DrawingObjectZOrderEntry>(normalized.Count + charts.Count);

        if (rawOrder is { Count: > 0 })
        {
            var normalizedIndex = 0;
            foreach (var entry in rawOrder)
            {
                if (entry.Kind == SelectionPaneObjectKind.Chart)
                {
                    if (placedChartIds.Add(entry.Id) && ChartExists(charts, entry.Id))
                        merged.Add(entry);
                    continue;
                }

                if (normalizedIndex < normalized.Count &&
                    normalized[normalizedIndex].Kind == entry.Kind &&
                    normalized[normalizedIndex].Id == entry.Id)
                {
                    merged.Add(normalized[normalizedIndex]);
                    normalizedIndex++;
                }
            }

            while (normalizedIndex < normalized.Count)
                merged.Add(normalized[normalizedIndex++]);
        }
        else
        {
            merged.AddRange(normalized);
        }

        foreach (var chart in charts)
        {
            if (placedChartIds.Add(chart.Id))
                merged.Add(new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Chart, chart.Id));
        }

        return merged;
    }

    private static bool ChartExists(IReadOnlyList<ChartModel> charts, Guid id)
    {
        foreach (var chart in charts)
        {
            if (chart.Id == id)
                return true;
        }

        return false;
    }

    private DrawingShapeModel? FindDrawingShape(Guid id)
    {
        if (DrawingShapes is null)
            return null;

        foreach (var shape in DrawingShapes)
        {
            if (shape.Id == id)
                return shape;
        }

        return null;
    }

    private PictureModel? FindPicture(Guid id)
    {
        if (Pictures is null)
            return null;

        foreach (var picture in Pictures)
        {
            if (picture.Id == id)
                return picture;
        }

        return null;
    }

    private TextBoxModel? FindTextBox(Guid id)
    {
        if (TextBoxes is null)
            return null;

        foreach (var textBox in TextBoxes)
        {
            if (textBox.Id == id)
                return textBox;
        }

        return null;
    }

    private ChartModel? FindChart(Guid id)
    {
        if (Charts is null)
            return null;

        foreach (var chart in Charts)
        {
            if (chart.Id == id)
                return chart;
        }

        return null;
    }

    private void RenderNativeSlicerTimelineControls(DrawingContext dc)
    {
        if (Viewport == null ||
            (NativeSlicers is not { Count: > 0 } && NativeTimelines is not { Count: > 0 }))
            return;

        var visibleRight = GetDrawingViewportRight();
        var visibleBottom = GetDrawingViewportBottom();
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var (lastRenderableRow, lastRenderableColumn) = GetRenderableDrawingAnchorBounds(visibleRight, visibleBottom);
        var metricLookups = GetRenderMetricLookups(Viewport);
        if (NativeSlicers is not null)
        {
            foreach (var slicer in NativeSlicers)
            {
                if (slicer.DrawingAnchor is not { } anchor ||
                    !ShouldDisplayDrawingAnchorRange(anchor, lastRenderableRow, lastRenderableColumn) ||
                    !TryCreateDrawingAnchorRect(metricLookups, anchor, out var rect))
                    continue;

                var controlRect = EnsureMinimumControlRect(rect);
                if (!ShouldDisplayDrawingObjectRect(controlRect, 0, visibleRight, visibleBottom))
                    continue;

                DrawNativeSlicerControl(dc, controlRect, slicer, pixelsPerDip);
            }
        }

        if (NativeTimelines is not null)
        {
            foreach (var timeline in NativeTimelines)
            {
                if (timeline.DrawingAnchor is not { } anchor ||
                    !ShouldDisplayDrawingAnchorRange(anchor, lastRenderableRow, lastRenderableColumn) ||
                    !TryCreateDrawingAnchorRect(metricLookups, anchor, out var rect))
                    continue;

                var controlRect = EnsureMinimumControlRect(rect);
                if (!ShouldDisplayDrawingObjectRect(controlRect, 0, visibleRight, visibleBottom))
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
        // Theme the box from the slicer's built-in style (SlicerStyleLight1..6) against the workbook theme.
        var style = SlicerStyleColors.Resolve(slicer.StyleName, WorkbookTheme);
        var bodyBrush = GetDrawingObjectBrush(255, style.Body);
        var borderPen = GetDrawingObjectPen(255, style.Border, 1);
        var tileBrush = GetDrawingObjectBrush(255, style.Tile);
        var selectedTileBrush = GetDrawingObjectBrush(255, style.SelectedTile);
        var itemTextBrush = GetDrawingObjectBrush(255, style.ItemText);

        // Light2–6 use a white header with a bold dark caption; Light1 uses the gray-filled header.
        // Detect Light2–6 by checking if the header is white (neutral gray header = Light1/default).
        var isAccentStyle = style.Header == CellColor.White;
        // Unselected tiles in Light2–6 get a thin light-gray border (~RGB 191,191,191) matching Excel.
        // Light1 tiles are also bordered the same way for consistency with Excel's default look.
        var tileBorderPen = GetDrawingObjectPen(255, new CellColor(191, 191, 191), 0.75);

        // showCaption="0" => omit the caption header band and start the tiles at the top of the box.
        var hasHeader = slicer.ShowCaption;
        DrawNativeControlFrame(
            dc,
            rect,
            GetNativeControlCaption(slicer.Caption, slicer.Name, slicer.DrawingShapeName),
            pixelsPerDip,
            bodyBrush,
            borderPen,
            GetDrawingObjectBrush(255, style.Header),
            GetDrawingObjectBrush(255, style.HeaderText),
            hasHeader,
            boldHeader: isAccentStyle);

        // Header chrome: multi-select toggle + clear-filter icons in the top-right of the header band.
        // Geometry mirrors SlicerLayoutBuilder.BuildHeaderIconRects.
        if (hasHeader)
        {
            var headerRect = new Rect(rect.Left, rect.Top, rect.Width, Math.Min(22, rect.Height));
            DrawSlicerHeaderIcons(dc, headerRect, slicer.SelectedItems.Count > 0, GetDrawingObjectBrush(255, style.HeaderText), pixelsPerDip);
        }

        // Lay out the slicer's item buttons: prefer the resolved available items (table-column distinct
        // values / pivot cache shared items); fall back to the slicer's selected items, then a single
        // field-name tile, matching the source desktop renderer. Honor the slicer's columnCount.
        var items = ResolveSlicerTileItems(slicer, out var fallbackAllTile);
        // Slicer captions are workbook data, not UI text — compare ordinally so a tile's selected
        // state doesn't depend on the user's locale (e.g. Turkish I/i case-folding).
        var selected = new HashSet<string>(slicer.SelectedItems, StringComparer.OrdinalIgnoreCase);
        var columnCount = Math.Max(1, slicer.ColumnCount);

        // With no caption band the tiles start near the top (small inset); with one they clear the 22px band.
        var tileTop = rect.Top + (hasHeader ? 26 : 4);
        var availableHeight = rect.Bottom - tileTop - 6;
        if (availableHeight <= 0 || items.Count == 0)
            return;

        // Cap the previewed rows so a long list still fits the box.
        var rowCount = (int)Math.Ceiling(items.Count / (double)columnCount);
        var rowsThatFit = Math.Max(1, (int)(availableHeight / (14 + 3)));
        var visibleRows = Math.Min(rowCount, rowsThatFit);
        var tileHeight = Math.Max(14, Math.Min(22, (availableHeight - (visibleRows - 1) * 3) / visibleRows));

        const double horizontalInset = 6;
        const double columnGap = 3;
        var totalGap = columnGap * (columnCount - 1);
        var tileWidth = Math.Max(1, (rect.Width - horizontalInset * 2 - totalGap) / columnCount);

        var visibleTileCount = Math.Min(items.Count, visibleRows * columnCount);
        for (var index = 0; index < visibleTileCount; index++)
        {
            var row = index / columnCount;
            var col = index % columnCount;
            var tileRect = new Rect(
                rect.Left + horizontalInset + col * (tileWidth + columnGap),
                tileTop + row * (tileHeight + 3),
                tileWidth,
                tileHeight);

            var caption = items[index];
            // Empty selection means "all selected" (no active filter) in Excel.
            var isSelected = fallbackAllTile || selected.Count == 0 || selected.Contains(caption);
            // Unselected tiles get a thin light-gray border matching Excel's slicer tile chrome.
            // Selected tiles use no border — the filled accent tint is sufficient visual distinction.
            dc.DrawRoundedRectangle(
                isSelected ? selectedTileBrush : tileBrush,
                isSelected ? null : tileBorderPen,
                tileRect,
                2,
                2);
            DrawClippedText(dc, caption, tileRect, itemTextBrush, 10, verticalPadding: 1, pixelsPerDip);
        }
    }

    // Draws the two slicer header chrome icons at the top-right of the header band.
    // Geometry matches SlicerLayoutBuilder.BuildHeaderIconRects:
    //   Right edge → [3px margin] [clear-filter ×] [2px gap] [multi-select ☰] → caption
    // The multi-select icon is always shown; clear-filter is rendered at full opacity only when
    // hasActiveFilter is true (grayed-out when no filter is active, matching Excel's behavior).
    private void DrawSlicerHeaderIcons(
        DrawingContext dc,
        Rect headerRect,
        bool hasActiveFilter,
        Brush iconBrush,
        double pixelsPerDip)
    {
        const double iconSize = 16;
        const double iconGap = 2;
        const double rightMargin = 3;
        const double iconFontSize = 8;

        var iconY = headerRect.Top + (headerRect.Height - iconSize) / 2;

        // Clear-filter icon (× glyph) — rightmost slot.
        var clearFilterLeft = headerRect.Right - rightMargin - iconSize;
        if (clearFilterLeft > headerRect.Left + 5)
        {
            var clearFilterRect = new Rect(clearFilterLeft, iconY, iconSize, iconSize);
            // Use a semi-transparent brush when inactive, fully opaque when filter is active.
            var clearBrush = hasActiveFilter ? iconBrush : GetDrawingObjectBrush(128, new CellColor(255, 255, 255));
            DrawClippedText(dc, "×", clearFilterRect, clearBrush, iconFontSize, verticalPadding: 0, pixelsPerDip);
        }

        // Multi-select icon (☰ glyph) — to the left of clear-filter.
        var multiSelectLeft = clearFilterLeft - iconGap - iconSize;
        if (multiSelectLeft > headerRect.Left + 5)
        {
            var multiSelectRect = new Rect(multiSelectLeft, iconY, iconSize, iconSize);
            DrawClippedText(dc, "☰", multiSelectRect, iconBrush, iconFontSize, verticalPadding: 0, pixelsPerDip);
        }
    }

    // Resolves the ordered tile captions for a slicer: resolved available items first, then selected items,
    // then a single synthetic field-name tile (the legacy preview) when neither is present.
    private static IReadOnlyList<string> ResolveSlicerTileItems(SlicerModel slicer, out bool fallbackAllTile)
    {
        fallbackAllTile = false;
        if (slicer.AvailableItems.Count > 0)
            return slicer.AvailableItems;

        if (slicer.SelectedItems.Count > 0)
            return slicer.SelectedItems;

        fallbackAllTile = true;
        var caption = !string.IsNullOrWhiteSpace(slicer.SourceFieldName)
            ? slicer.SourceFieldName!
            : !string.IsNullOrWhiteSpace(slicer.CacheName) ? slicer.CacheName : "All";
        return [caption];
    }

    private void DrawNativeTimelineControl(DrawingContext dc, Rect rect, TimelineModel timeline, double pixelsPerDip)
    {
        // Resolve colors from the timeline's built-in style (TimeSlicerStyleLight1..6) and workbook theme.
        var style = TimelineStyleColors.Resolve(timeline.StyleName, WorkbookTheme);
        var bodyBrush = GetDrawingObjectBrush(255, style.Body);
        var borderPen = GetDrawingObjectPen(255, style.Border, 1);
        var trackBrush = GetDrawingObjectBrush(255, style.Track);
        var selectionBandBrush = GetDrawingObjectBrush(255, style.SelectionBand);
        var summaryLabelBrush = GetDrawingObjectBrush(255, style.SummaryLabel);
        var headerBrush = GetDrawingObjectBrush(255, style.Header);
        var headerTextBrush = GetDrawingObjectBrush(255, style.HeaderText);

        // Muted dark brush for year banner and tick labels (Excel uses a dark-grey, ~RGB 64,64,64).
        var tickLabelBrush = GetDrawingObjectBrush(255, new CellColor(64, 64, 64));
        // Scrollbar colors: light grey fill with darker arrow boxes, matching Excel.
        var scrollbarFillBrush = GetDrawingObjectBrush(255, new CellColor(230, 230, 230));
        var scrollbarArrowBrush = GetDrawingObjectBrush(255, new CellColor(198, 198, 198));

        // Light2–6 use a white header with a bold dark caption; Light1 uses the grey-filled header.
        var isAccentStyle = style.Header == CellColor.White;

        var layout = TimelineLayoutBuilder.Build(
            timeline,
            new LayoutRect(rect.Left, rect.Top, rect.Width, rect.Height),
            SlicerTimelineGranularity.Resolve(timeline));

        DrawNativeControlFrame(
            dc,
            rect,
            layout.Caption,
            pixelsPerDip,
            bodyBrush,
            borderPen,
            headerBrush,
            headerTextBrush,
            hasHeader: true,
            boldHeader: isAccentStyle);

        // Granularity dropdown label (e.g. "MONTHS ▾") — use the layout's rect so geometry
        // is shared with the Avalonia renderer via TimelineLayoutBuilder.
        if (layout.GranularityDropdownRect.Width > 0)
            DrawClippedText(dc, layout.GranularityLabel, ToRect(layout.GranularityDropdownRect), headerTextBrush, 7.5, verticalPadding: 0, pixelsPerDip);

        // Clear-filter (×) glyph — draw from the layout's shared rect when the filter is active.
        if (layout.HasActiveFilter && layout.ClearFilterIconRect.Width > 0)
            DrawClippedText(dc, layout.ClearFilterGlyph, ToRect(layout.ClearFilterIconRect), headerTextBrush, 9, verticalPadding: 0, pixelsPerDip);

        // Summary date label — accent color and bold so it reads clearly against the white body.
        DrawClippedText(dc, layout.DateLabel, ToRect(layout.DateLabelRect), summaryLabelBrush, 9, verticalPadding: 0, pixelsPerDip, isBold: isAccentStyle);

        // Year banner — show year label(s) above the tick row, left-aligned at each year's start x.
        if (layout.YearBannerRect.Height > 0)
            DrawTimelineYearBanner(dc, layout, tickLabelBrush, pixelsPerDip);

        // Period tick labels — one per period (month/year/quarter/day) across the track.
        if (layout.TickLabelRect.Height > 0)
            DrawTimelineTickLabels(dc, layout, tickLabelBrush, pixelsPerDip);

        dc.DrawRoundedRectangle(trackBrush, null, ToRect(layout.TrackRect), 3, 3);
        dc.DrawRoundedRectangle(selectionBandBrush, null, ToRect(layout.SelectionRect), 3, 3);
        DrawTimelineHandle(dc, layout.StartHandle);
        DrawTimelineHandle(dc, layout.EndHandle);

        // Scrollbar strip at the bottom.
        if (layout.ScrollbarRect.Height > 0)
            DrawTimelineScrollbar(dc, layout, scrollbarFillBrush, scrollbarArrowBrush);
    }

    // Renders the year banner row — "2026" left-aligned at the start of each year's track span.
    private void DrawTimelineYearBanner(
        DrawingContext dc,
        TimelineLayoutModel layout,
        Brush brush,
        double pixelsPerDip)
    {
        var spans = TimelineLayoutBuilder.GetYearBannerSpans(layout);
        foreach (var (year, startX, spanWidth) in spans)
        {
            if (spanWidth < 4)
                continue;
            var labelRect = new Rect(startX, layout.YearBannerRect.Top, spanWidth, layout.YearBannerRect.Height);
            DrawClippedText(dc, year.ToString(CultureInfo.InvariantCulture), labelRect, brush, 9, verticalPadding: 0, pixelsPerDip, isBold: true);
        }
    }

    // Renders period tick labels (JAN, FEB, … or Q1, Q2, … or year numbers) centered at each period.
    private void DrawTimelineTickLabels(
        DrawingContext dc,
        TimelineLayoutModel layout,
        Brush brush,
        double pixelsPerDip)
    {
        var ticks = TimelineLayoutBuilder.GetTickLabels(layout);
        // Estimate label width to avoid overdrawing when ticks are dense.
        // Each tick label gets at most (trackWidth / tickCount) px, capped to 40px.
        var maxLabelWidth = ticks.Count > 0
            ? Math.Min(40, layout.TrackRect.Width / ticks.Count)
            : 40;
        if (maxLabelWidth < 6)
            return;

        foreach (var (label, centerX) in ticks)
        {
            var labelLeft = centerX - maxLabelWidth / 2;
            var labelRect = new Rect(labelLeft, layout.TickLabelRect.Top, maxLabelWidth, layout.TickLabelRect.Height);
            DrawClippedText(dc, label, labelRect, brush, 8, verticalPadding: 0, pixelsPerDip);
        }
    }

    // Renders the horizontal scrollbar at the bottom of the timeline widget, with a thumb that
    // reflects the current scroll position (ScrollThumbLeftRatio/ScrollThumbWidthRatio).
    private static void DrawTimelineScrollbar(
        DrawingContext dc,
        TimelineLayoutModel layout,
        Brush fillBrush,
        Brush arrowBrush)
    {
        var sbRect = ToRect(layout.ScrollbarRect);
        // Full strip background
        dc.DrawRectangle(fillBrush, null, sbRect);

        const double arrowBoxWidth = 14;

        if (sbRect.Width > arrowBoxWidth * 2 + 4)
        {
            var leftArrowRect = new Rect(sbRect.Left, sbRect.Top, arrowBoxWidth, sbRect.Height);
            dc.DrawRectangle(arrowBrush, null, leftArrowRect);

            var rightArrowRect = new Rect(sbRect.Right - arrowBoxWidth, sbRect.Top, arrowBoxWidth, sbRect.Height);
            dc.DrawRectangle(arrowBrush, null, rightArrowRect);
        }
    }

    private void DrawTimelineHandle(DrawingContext dc, TimelineHandleLayout handle)
    {
        var rect = ToRect(handle.Rect);
        dc.DrawRoundedRectangle(Brushes.White, NativeControlBorderPen, rect, 1, 1);
    }

    // Timeline path: default-themed frame, always with a caption band.
    private void DrawNativeControlFrame(DrawingContext dc, Rect rect, string caption, double pixelsPerDip) =>
        DrawNativeControlFrame(
            dc,
            rect,
            caption,
            pixelsPerDip,
            NativeControlBodyBrush,
            NativeControlBorderPen,
            NativeControlHeaderBrush,
            Brushes.White,
            hasHeader: true);

    private void DrawNativeControlFrame(
        DrawingContext dc,
        Rect rect,
        string caption,
        double pixelsPerDip,
        Brush bodyBrush,
        Pen borderPen,
        Brush headerBrush,
        Brush headerTextBrush,
        bool hasHeader,
        bool boldHeader = false)
    {
        dc.DrawRectangle(bodyBrush, borderPen, rect);
        if (!hasHeader)
            return;

        var headerRect = new Rect(rect.Left, rect.Top, rect.Width, Math.Min(22, rect.Height));
        dc.DrawRectangle(headerBrush, null, headerRect);
        DrawClippedText(dc, caption, new Rect(headerRect.Left + 5, headerRect.Top + 2, Math.Max(1, headerRect.Width - 10), Math.Max(1, headerRect.Height - 4)), headerTextBrush, 11, verticalPadding: 0, pixelsPerDip, isBold: boldHeader);
    }

    private void DrawClippedText(DrawingContext dc, string textValue, Rect rect, Brush brush, double fontSize, double verticalPadding, double pixelsPerDip, bool isBold = false)
    {
        var text = GetDrawingObjectText(
            string.IsNullOrWhiteSpace(textValue) ? " " : textValue,
            brush,
            fontSize,
            Math.Max(1, rect.Width),
            Math.Max(1, rect.Height),
            pixelsPerDip,
            TextTrimming.CharacterEllipsis,
            isBold);

        dc.PushClip(GetDrawingObjectClipGeometry(rect));
        dc.DrawText(text, new Point(rect.Left, rect.Top + verticalPadding));
        dc.Pop();
    }

    private static string GetNativeControlCaption(string? caption, string name, string? shapeName)
        => GridDrawingObjectPlanner.GetNativeControlCaption(caption, name, shapeName);

    private static Rect ToRect(LayoutRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    private static string FormatTimelineRange(TimelineModel timeline)
        => GridDrawingObjectPlanner.FormatTimelineRange(timeline);

    private Brush CreateDrawingShapeFill(DrawingShapeRenderMetadata metadata)
    {
        if (metadata.FillGradient is { } gradient)
            return GetDrawingObjectGradientBrush(metadata.Paint.Fill, gradient.EndColor, gradient.Direction);

        return GetDrawingObjectBrush(255, metadata.Paint.Fill);
    }

    private static WorkbookThemeEffectStyle ResolveDrawingShapeThemeEffect(
        DrawingShapeRenderMetadata metadata,
        WorkbookThemeEffectStyle themeEffect) =>
        metadata.UsesThemeEffects ? themeEffect : default;

    private static void DrawShapeGeometry(
        DrawingContext dc,
        DrawingShapeKind kind,
        Rect rect,
        Brush? brush,
        Pen? pen) =>
        dc.DrawGeometry(brush, pen, ShapeGeometryWpfAdapter.Create(kind, rect));

    /// <summary>
    /// Draws filled arrowheads at the start (headEnd) and/or end (tailEnd) of a line/connector.
    /// Uses <see cref="ArrowheadGeometry"/> for the polygon math and fills with the line color.
    /// </summary>
    private void DrawShapeArrowheads(
        DrawingContext dc,
        DrawingShapeModel shape,
        Rect rect,
        bool flipHorizontal,
        bool flipVertical,
        CellColor lineColor,
        double strokeDip)
    {
        var headArrow = shape.HeadArrowhead;
        var tailArrow = shape.TailArrowhead;
        if ((headArrow is null || !headArrow.IsPresent) &&
            (tailArrow is null || !tailArrow.IsPresent))
            return;

        // Pass flipHorizontal/flipVertical as false here: PushDrawingObjectTransform already pushed
        // a ScaleTransform onto the DrawingContext that flips the entire drawing context, including
        // these arrowheads. Passing the flip flags to LineEndpoints would mirror the endpoints a
        // second time, landing them at the wrong corners and pointing the wrong way.
        var (startPt, endPt, dirStartToEnd) = ArrowheadGeometry.LineEndpoints(
            rect.Left, rect.Top, rect.Width, rect.Height,
            flipHorizontal: false, flipVertical: false, shape.Kind);

        var arrowBrush = GetDrawingObjectBrush(255, lineColor);

        // HeadEnd = arrowhead at the START of the line (points back toward start)
        if (headArrow is not null && headArrow.IsPresent)
            DrawArrowheadWpf(dc, headArrow, startPt, dirStartToEnd + Math.PI, strokeDip, arrowBrush);

        // TailEnd = arrowhead at the END of the line (points in the forward direction)
        if (tailArrow is not null && tailArrow.IsPresent)
            DrawArrowheadWpf(dc, tailArrow, endPt, dirStartToEnd, strokeDip, arrowBrush);
    }

    private static Point ArrowWpfPointFromLayout(LayoutPoint p) =>
        new(p.X, p.Y);

    private void DrawArrowheadWpf(
        DrawingContext dc,
        DrawingArrowhead arrowhead,
        LayoutPoint tip,
        double directionRadians,
        double strokeWidth,
        Brush brush)
    {
        if (arrowhead.Type == DrawingArrowheadType.Oval)
        {
            var (center, radius) = ArrowheadGeometry.OvalCenter(arrowhead, tip, directionRadians, strokeWidth);
            dc.DrawEllipse(brush, null, ArrowWpfPointFromLayout(center), radius, radius);
            return;
        }

        var pts = ArrowheadGeometry.PolygonPoints(arrowhead, tip, directionRadians, strokeWidth);
        if (pts.Length < 3)
            return;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(ArrowWpfPointFromLayout(pts[0]), isFilled: true, isClosed: true);
            for (var i = 1; i < pts.Length; i++)
                ctx.LineTo(ArrowWpfPointFromLayout(pts[i]), isStroked: false, isSmoothJoin: false);
        }
        geometry.Freeze();
        dc.DrawGeometry(brush, null, geometry);
    }

    private void DrawShapeAuthoredEffect(
        DrawingContext dc,
        DrawingShapeKind kind,
        Rect rect,
        DrawingShapeEffectPreset effectPreset,
        DrawingObjectColors colors)
    {
        switch (effectPreset)
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
            case DrawingShapeEffectPreset.Reflection:
                DrawShapeReflectionEffect(dc, kind, rect, colors);
                break;
            case DrawingShapeEffectPreset.ThreeDRotation:
                DrawShapeThreeDRotationEffect(dc, kind, rect, colors);
                break;
        }
    }

    private void DrawShapeThreeDRotationEffect(
        DrawingContext dc,
        DrawingShapeKind kind,
        Rect rect,
        DrawingObjectColors colors)
    {
        var offsetX = Math.Clamp(rect.Width * 0.14, 4, 10);
        var offsetY = -Math.Clamp(rect.Height * 0.12, 3, 8);
        var rearRect = rect;
        rearRect.Offset(offsetX, offsetY);
        var pen = GetDrawingObjectPen(130, colors.Outline, 1.25);
        var faceBrush = GetDrawingObjectBrush(26, colors.Fill);

        if (!DrawingShapeKindSupport.IsLineLike(kind))
        {
            DrawPerspectiveFace(dc, faceBrush, pen, rect.TopLeft, rect.TopRight, rearRect.TopRight, rearRect.TopLeft);
            DrawPerspectiveFace(dc, faceBrush, pen, rect.TopRight, rect.BottomRight, rearRect.BottomRight, rearRect.TopRight);
        }

        DrawShapeGeometry(dc, kind, rearRect, DrawingShapeKindSupport.IsLineLike(kind) ? null : faceBrush, pen);
        dc.DrawLine(pen, rect.TopLeft, rearRect.TopLeft);
        dc.DrawLine(pen, rect.BottomRight, rearRect.BottomRight);
    }

    private static void DrawPerspectiveFace(
        DrawingContext dc,
        Brush brush,
        Pen pen,
        Point first,
        Point second,
        Point third,
        Point fourth)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(first, isFilled: true, isClosed: true);
            context.LineTo(second, isStroked: true, isSmoothJoin: false);
            context.LineTo(third, isStroked: true, isSmoothJoin: false);
            context.LineTo(fourth, isStroked: true, isSmoothJoin: false);
        }

        geometry.Freeze();
        dc.DrawGeometry(brush, pen, geometry);
    }

    private void DrawShapeAuthoredBevelEffect(
        DrawingContext dc,
        DrawingShapeKind kind,
        Rect rect,
        DrawingShapeEffectPreset effectPreset)
    {
        if (effectPreset != DrawingShapeEffectPreset.Bevel)
            return;

        DrawShapeBevelEffect(dc, kind, rect);
    }

    private void DrawShapeThemeBevelEffect(DrawingContext dc, DrawingShapeKind kind, Rect rect, WorkbookThemeEffectStyle effect)
    {
        if (!effect.HasBevel)
            return;

        DrawShapeBevelEffect(dc, kind, rect);
    }

    private void DrawShapeBevelEffect(DrawingContext dc, DrawingShapeKind kind, Rect rect)
    {
        var thickness = Math.Clamp(Math.Min(rect.Width, rect.Height) / 12, 2, 5);
        var inset = thickness / 2;
        var bevelRect = new Rect(
            rect.Left + inset,
            rect.Top + inset,
            Math.Max(1, rect.Width - thickness),
            Math.Max(1, rect.Height - thickness));
        var highlightPen = GetDrawingObjectPen(170, 255, 255, 255, thickness);
        var shadowPen = GetDrawingObjectPen(118, 0, 0, 0, thickness);
        var highlightRect = bevelRect;
        highlightRect.Offset(-inset / 3, -inset / 3);
        var shadowRect = bevelRect;
        shadowRect.Offset(inset / 3, inset / 3);

        DrawShapeGeometry(dc, kind, highlightRect, null, highlightPen);
        DrawShapeGeometry(dc, kind, shadowRect, null, shadowPen);
    }

    private void DrawShapeAuthoredInnerShadow(
        DrawingContext dc,
        DrawingShapeKind kind,
        Rect rect,
        DrawingShapeEffectPreset effectPreset)
    {
        if (effectPreset != DrawingShapeEffectPreset.InnerShadow)
            return;

        var thickness = GetInnerShadowThickness(4);
        var shadowRect = GetInnerShadowRect(rect, thickness, offsetX: 1.5, offsetY: 1.5);
        var pen = GetDrawingObjectPen(alpha: 112, r: 0, g: 0, b: 0, thickness);

        DrawShapeGeometry(dc, kind, shadowRect, null, pen);
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

        DrawShapeGeometry(dc, kind, shadowRect, DrawingShapeKindSupport.IsLineLike(kind) ? null : shadowBrush, shadowPen);
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

        DrawShapeGeometry(dc, kind, effectRect, null, pen);
    }

    private void DrawShapeReflectionEffect(
        DrawingContext dc,
        DrawingShapeKind kind,
        Rect rect,
        DrawingObjectColors colors)
    {
        var reflectionRect = GetReflectionRect(rect);
        var fill = GetDrawingObjectBrush(36, colors.Fill);
        var pen = GetDrawingObjectPen(70, colors.Outline, 1);

        DrawShapeGeometry(dc, kind, reflectionRect, DrawingShapeKindSupport.IsLineLike(kind) ? null : fill, pen);
    }

    private static Rect GetReflectionRect(Rect rect)
    {
        var gap = Math.Clamp(rect.Height * 0.08, 2, 6);
        var height = Math.Max(1, rect.Height * 0.45);
        return new Rect(rect.Left, rect.Bottom + gap, rect.Width, height);
    }

    private void DrawTextBoxThemeEffect(DrawingContext dc, Rect rect, WorkbookThemeEffectStyle effect)
    {
        if (!effect.HasShadow && !effect.HasGlow && !effect.HasSoftEdge)
            return;

        if (effect.HasShadow)
        {
            var shadowRect = rect;
            shadowRect.Offset(effect.ShadowOffsetX, effect.ShadowOffsetY);
            var alpha = (byte)Math.Clamp(Math.Round(255 * effect.ShadowOpacity), 0, 255);
            dc.DrawRectangle(GetDrawingObjectBrush(alpha, 0, 0, 0), null, shadowRect);
        }

        if (effect.HasGlow)
        {
            var glowColor = effect.GlowColor ?? new CellColor(91, 155, 213);
            var glowRect = rect;
            glowRect.Inflate(effect.GlowRadius, effect.GlowRadius);
            var alpha = (byte)Math.Clamp(Math.Round(255 * effect.GlowOpacity), 0, 255);
            var thickness = Math.Max(2, effect.GlowRadius);
            dc.DrawRectangle(null, GetDrawingObjectPen(alpha, glowColor, thickness), glowRect);
        }

        if (effect.HasSoftEdge)
        {
            var softEdgeRect = rect;
            var inflate = GetSoftEdgeInflate(effect.SoftEdgeRadius);
            var thickness = GetSoftEdgeThickness(effect.SoftEdgeRadius);
            softEdgeRect.Inflate(inflate, inflate);
            dc.DrawRectangle(null, GetDrawingObjectPen(54, 128, 128, 128, thickness), softEdgeRect);
        }
    }

    private void DrawTextBoxThemeInnerShadow(DrawingContext dc, Rect rect, WorkbookThemeEffectStyle effect)
    {
        if (!effect.HasInnerShadow)
            return;

        var alpha = GetInnerShadowAlpha(effect.InnerShadowOpacity);
        var thickness = GetInnerShadowThickness(effect.InnerShadowBlurRadius);
        var shadowRect = GetInnerShadowRect(rect, thickness, effect.InnerShadowOffsetX, effect.InnerShadowOffsetY);

        dc.PushClip(GetDrawingObjectClipGeometry(rect));
        dc.DrawRectangle(null, GetDrawingObjectPen(alpha, 0, 0, 0, thickness), shadowRect);
        dc.Pop();
    }

    private void DrawShapeThemeEffect(
        DrawingContext dc,
        DrawingShapeKind kind,
        Rect rect,
        WorkbookThemeEffectStyle effect,
        DrawingObjectColors colors)
    {
        if (!effect.HasShadow && !effect.HasGlow && !effect.HasSoftEdge && !effect.HasThreeDRotation)
            return;

        if (effect.HasThreeDRotation)
        {
            DrawShapeThreeDRotationEffect(dc, kind, rect, colors);
        }

        if (effect.HasShadow)
        {
            var shadowRect = rect;
            shadowRect.Offset(effect.ShadowOffsetX, effect.ShadowOffsetY);
            var alpha = (byte)Math.Clamp(Math.Round(255 * effect.ShadowOpacity), 0, 255);
            var shadowBrush = GetDrawingObjectBrush(alpha, 0, 0, 0);
            var shadowPen = GetDrawingObjectPen(alpha, 0, 0, 0, 2);
            DrawShapeGeometry(dc, kind, shadowRect, DrawingShapeKindSupport.IsLineLike(kind) ? null : shadowBrush, shadowPen);
        }

        if (effect.HasGlow)
        {
            var glowColor = effect.GlowColor ?? new CellColor(91, 155, 213);
            var alpha = (byte)Math.Clamp(Math.Round(255 * effect.GlowOpacity), 0, 255);
            var thickness = Math.Max(2, effect.GlowRadius);
            DrawShapeOutlineEffect(
                dc,
                kind,
                rect,
                alpha,
                glowColor.R,
                glowColor.G,
                glowColor.B,
                thickness,
                effect.GlowRadius);
        }

        if (effect.HasSoftEdge)
        {
            DrawShapeOutlineEffect(
                dc,
                kind,
                rect,
                alpha: 54,
                r: 128,
                g: 128,
                b: 128,
                thickness: GetSoftEdgeThickness(effect.SoftEdgeRadius),
                inflate: GetSoftEdgeInflate(effect.SoftEdgeRadius));
        }
    }

    private void DrawShapeThemeInnerShadow(
        DrawingContext dc,
        DrawingShapeKind kind,
        Rect rect,
        WorkbookThemeEffectStyle effect)
    {
        if (!effect.HasInnerShadow)
            return;

        var alpha = GetInnerShadowAlpha(effect.InnerShadowOpacity);
        var thickness = GetInnerShadowThickness(effect.InnerShadowBlurRadius);
        var shadowRect = GetInnerShadowRect(rect, thickness, effect.InnerShadowOffsetX, effect.InnerShadowOffsetY);
        var pen = GetDrawingObjectPen(alpha, 0, 0, 0, thickness);
        DrawShapeGeometry(dc, kind, shadowRect, null, pen);
    }

    private static byte GetInnerShadowAlpha(double opacity) =>
        (byte)Math.Clamp(Math.Round(255 * opacity * 0.7), 1, 255);

    private static double GetInnerShadowThickness(double blurRadius) =>
        Math.Clamp(Math.Max(2, blurRadius), 2, 12);

    private static Rect GetInnerShadowRect(Rect rect, double thickness, double offsetX, double offsetY)
    {
        var insetX = Math.Min(Math.Max(1, thickness / 2), Math.Max(0, rect.Width / 2 - 0.5));
        var insetY = Math.Min(Math.Max(1, thickness / 2), Math.Max(0, rect.Height / 2 - 0.5));
        var shadowRect = new Rect(
            rect.Left + insetX,
            rect.Top + insetY,
            Math.Max(1, rect.Width - insetX * 2),
            Math.Max(1, rect.Height - insetY * 2));

        shadowRect.Offset(
            Math.Clamp(offsetX / 2, -insetX, insetX),
            Math.Clamp(offsetY / 2, -insetY, insetY));
        return shadowRect;
    }

    private static double GetSoftEdgeThickness(double radius) =>
        Math.Max(2, radius * 2);

    private static double GetSoftEdgeInflate(double radius) =>
        Math.Max(1, radius / 2);

    public static DrawingObjectColors ResolveDrawingShapeColors(DrawingShapeModel shape, WorkbookTheme theme) =>
        GridDrawingObjectPlanner.ResolveDrawingShapeColors(shape, theme);

    public static DrawingObjectColors ResolveTextBoxColors(TextBoxModel textBox, WorkbookTheme theme) =>
        GridDrawingObjectPlanner.ResolveTextBoxColors(textBox, theme);

    public static DrawingShapeRenderMetadata ResolveDrawingShapeRenderMetadata(DrawingShapeModel shape, WorkbookTheme theme) =>
        GridDrawingObjectPlanner.ResolveDrawingShapeRenderMetadata(shape, theme);

    public static TextBoxRenderMetadata ResolveTextBoxRenderMetadata(TextBoxModel textBox, WorkbookTheme theme) =>
        GridDrawingObjectPlanner.ResolveTextBoxRenderMetadata(textBox, theme);

    /// <summary>
    /// Tracks which of the two transforms <see cref="PushDrawingObjectTransform"/> pushed, so a
    /// caller can pop just the flip (<see cref="PopDrawingObjectFlipTransform"/>) before drawing
    /// shape/text-box text -- Excel mirrors a flipped shape's outline geometry but keeps its text
    /// body upright and readable, so the flip's <see cref="ScaleTransform"/> must not be active
    /// while text glyphs are drawn, while the rotation should still apply to the text like Excel.
    /// </summary>
    private readonly record struct DrawingObjectTransformState(bool HasRotation, bool HasFlip);

    private static DrawingObjectTransformState PushDrawingObjectTransform(
        DrawingContext dc,
        double rotationDegrees,
        bool flipHorizontal,
        bool flipVertical,
        Rect rect)
    {
        var hasRotation = false;
        if (Math.Abs(rotationDegrees % 360) > 0.0001)
        {
            dc.PushTransform(new RotateTransform(
                rotationDegrees,
                rect.Left + rect.Width / 2,
                rect.Top + rect.Height / 2));
            hasRotation = true;
        }

        var hasFlip = false;
        if (flipHorizontal || flipVertical)
        {
            dc.PushTransform(new ScaleTransform(
                flipHorizontal ? -1 : 1,
                flipVertical ? -1 : 1,
                rect.Left + rect.Width / 2,
                rect.Top + rect.Height / 2));
            hasFlip = true;
        }

        return new DrawingObjectTransformState(hasRotation, hasFlip);
    }

    /// <summary>
    /// Pops just the flip <see cref="ScaleTransform"/> (if one was pushed), leaving any rotation
    /// transform active. Call before drawing shape/text-box text so the text stays upright/mirror-free
    /// under a flip while still following the shape's rotation, matching Excel.
    /// </summary>
    private static void PopDrawingObjectFlipTransform(DrawingContext dc, ref DrawingObjectTransformState state)
    {
        if (!state.HasFlip)
            return;

        dc.Pop();
        state = state with { HasFlip = false };
    }

    private static void PopDrawingObjectTransform(DrawingContext dc, DrawingObjectTransformState state)
    {
        if (state.HasFlip)
            dc.Pop();
        if (state.HasRotation)
            dc.Pop();
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
                    if (ShouldDisplayDrawingObjectRect(rect, 0, visibleRight, visibleBottom))
                        DrawObjectPlaceholder(dc, rect, CreateObjectPlaceholderMetadata("Chart", chart.Name, index).Label, pixelsPerDip);
                }
                index++;
            }
        }

        var (lastRenderableRow, lastRenderableColumn) = GetRenderableDrawingAnchorBounds(visibleRight, visibleBottom);
        var metricLookups = GetRenderMetricLookups(Viewport);
        if (DrawingShapes is not null)
        {
            var index = 1;
            foreach (var shape in DrawingShapes)
            {
                if (ShouldDisplayAnchoredDrawingObject(shape.IsVisible, shape.Anchor, lastRenderableRow, lastRenderableColumn) &&
                    TryCreateAnchoredObjectRect(
                        metricLookups,
                        shape.Anchor,
                        shape.Width,
                        shape.Height,
                        MinimumShapeObjectWidth,
                        MinimumShapeObjectHeight,
                        out var rect,
                        shape.AnchorOffsetX,
                        shape.AnchorOffsetY) &&
                    ShouldDisplayDrawingObjectRect(rect, shape.RotationDegrees, visibleRight, visibleBottom))
                    DrawObjectPlaceholder(dc, rect, CreateObjectPlaceholderMetadata("Shape", shape.Name, index).Label, pixelsPerDip);
                index++;
            }
        }

        if (Pictures is not null)
        {
            var index = 1;
            foreach (var picture in Pictures)
            {
                if (ShouldDisplayAnchoredDrawingObject(picture.IsVisible, picture.Anchor, lastRenderableRow, lastRenderableColumn) &&
                    TryCreateAnchoredObjectRect(
                        metricLookups,
                        picture.Anchor,
                        picture.Width,
                        picture.Height,
                        MinimumPictureObjectWidth,
                        MinimumPictureObjectHeight,
                        out var rect,
                        picture.AnchorOffsetX,
                        picture.AnchorOffsetY) &&
                    ShouldDisplayDrawingObjectRect(rect, picture.RotationDegrees, visibleRight, visibleBottom))
                    DrawObjectPlaceholder(dc, rect, CreateObjectPlaceholderMetadata("Picture", picture.Name, index).Label, pixelsPerDip);
                index++;
            }
        }

        if (TextBoxes is not null)
        {
            var index = 1;
            foreach (var textBox in TextBoxes)
            {
                if (ShouldDisplayAnchoredDrawingObject(textBox.IsVisible, textBox.Anchor, lastRenderableRow, lastRenderableColumn) &&
                    TryCreateAnchoredObjectRect(
                        metricLookups,
                        textBox.Anchor,
                        textBox.Width,
                        textBox.Height,
                        MinimumTextBoxObjectWidth,
                        MinimumTextBoxObjectHeight,
                        out var rect,
                        textBox.AnchorOffsetX,
                        textBox.AnchorOffsetY) &&
                    ShouldDisplayDrawingObjectRect(rect, textBox.RotationDegrees, visibleRight, visibleBottom))
                    DrawObjectPlaceholder(dc, rect, CreateObjectPlaceholderMetadata("Text Box", textBox.Name, index).Label, pixelsPerDip);
                index++;
            }
        }

        if (NativeSlicers is not null)
        {
            var index = 1;
            foreach (var slicer in NativeSlicers)
            {
                if (slicer.DrawingAnchor is { } anchor &&
                    ShouldDisplayDrawingAnchorRange(anchor, lastRenderableRow, lastRenderableColumn) &&
                    TryCreateDrawingAnchorRect(metricLookups, anchor, out var rect))
                {
                    var controlRect = EnsureMinimumControlRect(rect);
                    if (ShouldDisplayDrawingObjectRect(controlRect, 0, visibleRight, visibleBottom))
                        DrawObjectPlaceholder(dc, controlRect, CreateObjectPlaceholderMetadata("Slicer", slicer.DrawingShapeName ?? slicer.Caption ?? slicer.Name, index).Label, pixelsPerDip);
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
                    ShouldDisplayDrawingAnchorRange(anchor, lastRenderableRow, lastRenderableColumn) &&
                    TryCreateDrawingAnchorRect(metricLookups, anchor, out var rect))
                {
                    var controlRect = EnsureMinimumControlRect(rect);
                    if (ShouldDisplayDrawingObjectRect(controlRect, 0, visibleRight, visibleBottom))
                        DrawObjectPlaceholder(dc, controlRect, CreateObjectPlaceholderMetadata("Timeline", timeline.DrawingShapeName ?? timeline.Caption ?? timeline.Name, index).Label, pixelsPerDip);
                }
                index++;
            }
        }
    }

    public static DrawingObjectPlaceholderMetadata CreateObjectPlaceholderMetadata(string objectType, string? objectName, int index)
        => GridDrawingObjectPlanner.CreateObjectPlaceholderMetadata(objectType, objectName, index);

    public static string CreateObjectPlaceholderLabel(string objectType, string? objectName, int index)
        => GridDrawingObjectPlanner.CreateObjectPlaceholderLabel(objectType, objectName, index);

    public bool TryCreateAnchoredObjectRect(
        CellAddress anchor,
        double width,
        double height,
        double minimumWidth,
        double minimumHeight,
        out Rect rect,
        double anchorOffsetX = 0,
        double anchorOffsetY = 0) =>
        GridDrawingObjectPlanner.TryCreateAnchoredObjectRect(
            Viewport,
            anchor,
            ActualRowHeaderWidth,
            EffectiveColHeaderHeight,
            width,
            height,
            minimumWidth,
            minimumHeight,
            out rect,
            anchorOffsetX,
            anchorOffsetY);

    private bool TryCreateAnchoredObjectRect(
        RenderMetricLookupCache metricLookups,
        CellAddress anchor,
        double width,
        double height,
        double minimumWidth,
        double minimumHeight,
        out Rect rect,
        double anchorOffsetX = 0,
        double anchorOffsetY = 0) =>
        GridDrawingObjectPlanner.TryCreateAnchoredObjectRect(
            metricLookups.Rows,
            metricLookups.Columns,
            anchor,
            ActualRowHeaderWidth,
            EffectiveColHeaderHeight,
            width,
            height,
            minimumWidth,
            minimumHeight,
            out rect,
            anchorOffsetX,
            anchorOffsetY);

    private bool TryCreateDrawingAnchorRect(
        RenderMetricLookupCache metricLookups,
        DrawingAnchorRange anchor,
        out Rect rect) =>
        GridDrawingObjectPlanner.TryCreateDrawingAnchorRect(
            metricLookups.Rows,
            metricLookups.Columns,
            anchor,
            ActualRowHeaderWidth,
            EffectiveColHeaderHeight,
            out rect);

    private void DrawObjectPlaceholder(DrawingContext dc, Rect rect, string label, double pixelsPerDip)
    {
        dc.DrawRectangle(ObjectPlaceholderFill, ObjectPlaceholderPen, rect);
        DrawPlaceholderDiagonals(dc, rect);

        var textWidth = Math.Max(1, rect.Width - 8);
        var textHeight = Math.Max(1, rect.Height - 8);
        var textClipRect = new Rect(rect.Left + 4, rect.Top + 4, textWidth, textHeight);
        var text = GetDrawingObjectText(
            label,
            ObjectPlaceholderTextBrush,
            11,
            textWidth,
            textHeight,
            pixelsPerDip,
            TextTrimming.CharacterEllipsis);

        var textPoint = new Point(
            rect.Left + Math.Max(4, (rect.Width - text.Width) / 2),
            rect.Top + Math.Max(4, (rect.Height - text.Height) / 2));
        dc.PushClip(GetDrawingObjectClipGeometry(textClipRect));
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

    /// <summary>
    /// Builds the outline <see cref="Pen"/> for a drawing shape using portable outline metadata.
    /// Returns <see langword="null"/> when the shape explicitly has no border.
    /// </summary>
    private Pen? GetDrawingShapeOutlinePen(
        CellColor outlineColor,
        DrawingShapeOutlineRenderMetadata outline)
    {
        if (!outline.HasOutline)
            return null;

        // Convert points → WPF DIPs (96 DPI screen): 1 pt = 96/72 DIP
        var thicknessDip = outline.ThicknessDip;

        // For solid outlines use the cached pen path (fast path).
        if (outline.Dash == DrawingShapeOutlineDash.Solid)
            return GetDrawingObjectPen(255, outlineColor, thicknessDip);

        // Dashed pens are rare; build without caching to keep cache key simple.
        var brush = GetDrawingObjectBrush(255, outlineColor);
        var dashStyle = outline.Dash switch
        {
            DrawingShapeOutlineDash.Dash => DashStyles.Dash,
            DrawingShapeOutlineDash.Dot => DashStyles.Dot,
            DrawingShapeOutlineDash.DashDot => DashStyles.DashDot,
            DrawingShapeOutlineDash.LongDash => DashStyles.DashDot, // closest WPF built-in
            DrawingShapeOutlineDash.LongDashDot => DashStyles.DashDotDot,
            DrawingShapeOutlineDash.LongDashDotDot => DashStyles.DashDotDot,
            DrawingShapeOutlineDash.SystemDash => DashStyles.Dash,
            DrawingShapeOutlineDash.SystemDot => DashStyles.Dot,
            DrawingShapeOutlineDash.SystemDashDot => DashStyles.DashDot,
            _ => DashStyles.Solid
        };
        var pen = new Pen(brush, thicknessDip) { DashStyle = dashStyle };
        pen.Freeze();
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
            Color.FromRgb(startColor.R, startColor.G, startColor.B),
            Color.FromRgb(endColor.R, endColor.G, endColor.B),
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
        TextTrimming trimming = TextTrimming.None,
        bool isBold = false,
        bool isItalic = false,
        bool isUnderline = false)
    {
        var key = new DrawingObjectTextLayoutKey(
            textValue,
            CultureInfo.CurrentCulture.Name,
            brush,
            fontSize,
            maxTextWidth,
            maxTextHeight,
            pixelsPerDip,
            trimming,
            isBold,
            isItalic,
            isUnderline);
        if (_drawingObjectTextLayoutCache.TryGetValue(key, out var cached))
            return cached;

        if (_drawingObjectTextLayoutCache.Count >= DrawingObjectTextLayoutCacheLimit)
            _drawingObjectTextLayoutCache.Clear();

        var typeface = (isBold || isItalic)
            ? new Typeface(
                DefaultTypeface.FontFamily,
                isItalic ? FontStyles.Italic : FontStyles.Normal,
                isBold ? FontWeights.Bold : FontWeights.Normal,
                FontStretches.Normal)
            : DefaultTypeface;

        var formatted = new FormattedText(
            textValue,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            brush,
            pixelsPerDip)
        {
            MaxTextWidth = maxTextWidth,
            MaxTextHeight = maxTextHeight,
            Trimming = trimming
        };
        if (isUnderline)
            formatted.SetTextDecorations(TextDecorations.Underline);
        _drawingObjectTextLayoutCache.Add(key, formatted);
        return formatted;
    }

    /// <summary>
    /// Draws the shape's <see cref="DrawingShapeModel.ShapeText"/> inside <paramref name="rect"/>
    /// using the font/alignment/anchor stored in the model.  Text is clipped to the rect and
    /// positioned according to the vertical anchor (top / middle / bottom).
    /// For WordArt shapes, draws styled text with a gradient fill and/or text outline;
    /// warp presets are preserved but rendered flat (deferred).
    /// </summary>
    private void DrawShapeText(DrawingContext dc, DrawingShapeModel shape, Rect rect, double pixelsPerDip)
    {
        const double ShapeTextHPad = 4;
        const double ShapeTextVPad = 2;
        const double DefaultShapeTextFontSize = 11.0; // pt → matches Excel fallback

        var text = shape.ShapeText;
        if (string.IsNullOrEmpty(text))
            return;

        // R60-render-drawing-shapes-6-4: when "Wrap text in shape" is off, Excel keeps the text on
        // a single unconstrained line (which may overflow the shape's left/right edges) instead of
        // word-wrapping it. Passing 0 as the FormattedText width cap -- the same sentinel the
        // cell-text renderer uses for its own wrap-off path (see GetDefaultWrappedFormattedText
        // callers in GridView.Rendering.cs) -- disables wrapping entirely.
        var textWidth = shape.ShapeTextWrap
            ? Math.Max(1, rect.Width - ShapeTextHPad * 2)
            : 0;
        var textHeight = Math.Max(1, rect.Height - ShapeTextVPad * 2);

        // Resolve text color: theme → explicit → white-on-dark heuristic.
        var resolvedColor = shape.ResolveShapeTextColor(WorkbookTheme);
        Brush textBrush;
        if (resolvedColor is { } c)
        {
            textBrush = GetDrawingObjectBrush(255, c);
        }
        else
        {
            // Default: white on dark fill, black on light fill.
            var fillColor = shape.ResolveFillColor(WorkbookTheme, DrawingShapeModel.DefaultFillColor)
                            ?? DrawingShapeModel.DefaultFillColor;
            var luminance = 0.299 * fillColor.R + 0.587 * fillColor.G + 0.114 * fillColor.B;
            textBrush = luminance < 128 ? Brushes.White : Brushes.Black;
        }

        // WordArt gradient text fill: override the text brush with a LinearGradientBrush
        // when a gradient end color is available. Start = resolvedColor, End = gradientEndColor.
        // Approximation: gradient is applied to the whole text block (not per-glyph).
        var gradEndColor = shape.ResolveShapeTextGradientEndColor(WorkbookTheme);
        if (shape.IsWordArt && gradEndColor is { } gradEnd && resolvedColor is { } startColor)
        {
            textBrush = GetDrawingObjectGradientBrush(
                startColor, gradEnd,
                DrawingShapeGradientDirection.Vertical);
        }

        var fontSize = shape.ShapeTextFontSizePoints > 0
            ? shape.ShapeTextFontSizePoints
            : DefaultShapeTextFontSize;
        // pt → WPF DIPs at 96 dpi: 1 pt = 96/72 DIP
        const double PtToDip = 96.0 / 72.0;
        var fontSizeDip = fontSize * PtToDip;

        var trimming = shape.ShapeTextWrap ? TextTrimming.None : TextTrimming.CharacterEllipsis;
        var hAlign = shape.ShapeTextHAlign switch
        {
            DrawingShapeTextHAlign.Center => TextAlignment.Center,
            DrawingShapeTextHAlign.Right => TextAlignment.Right,
            _ => TextAlignment.Left,
        };
        var formatted = GetDrawingObjectText(
            text,
            textBrush,
            fontSizeDip,
            textWidth,
            textHeight,
            pixelsPerDip,
            trimming,
            shape.ShapeTextBold,
            shape.ShapeTextItalic,
            shape.ShapeTextUnderline);

        // TextAlignment must be set after creation — it is not part of the typeface.
        formatted.TextAlignment = hAlign;

        // Vertical anchor: position the text block within rect.
        var textBlockHeight = formatted.Height;
        var textTop = shape.ShapeTextVAnchor switch
        {
            DrawingShapeTextVAnchor.Top => rect.Top + ShapeTextVPad,
            DrawingShapeTextVAnchor.Bottom => Math.Max(rect.Top + ShapeTextVPad,
                rect.Bottom - ShapeTextVPad - textBlockHeight),
            _ => // Middle
                rect.Top + ShapeTextVPad + Math.Max(0, (textHeight - textBlockHeight) / 2),
        };

        // Horizontal origin: for Left alignment start at left+pad; Center/Right anchors are
        // expressed from the left edge of the max-width box.
        var textLeft = rect.Left + ShapeTextHPad;

        // Clip to the shape's bounding rectangle so text doesn't bleed outside.
        var clipRect = new Rect(rect.Left, rect.Top, rect.Width, rect.Height);
        dc.PushClip(GetDrawingObjectClipGeometry(clipRect));

        // WordArt text outline: build glyph geometry + DrawGeometry with a fill+stroke pen.
        // This gives the true "stroked text" look (outline around each letter).
        var textOutlineColor = shape.ResolveShapeTextOutlineColor(WorkbookTheme);
        if (shape.IsWordArt && textOutlineColor is { } outlineColor)
        {
            const double PtToDipLocal = 96.0 / 72.0;
            var outlineWidthDip = shape.ShapeTextOutlineWidthPoints > 0
                ? shape.ShapeTextOutlineWidthPoints * PtToDipLocal
                : 0.5; // thin default outline
            var outlinePen = GetDrawingObjectPen(255, outlineColor, outlineWidthDip);

            // Build text geometry and draw it with both fill and stroke.
            var textGeometry = formatted.BuildGeometry(new Point(textLeft, textTop));
            var suppressWordArtTextFill = shape.IsWordArt && shape.ShapeTextHasNoFill;
            dc.DrawGeometry(suppressWordArtTextFill ? null : textBrush, outlinePen, textGeometry);
        }
        else if (!(shape.IsWordArt && shape.ShapeTextHasNoFill))
        {
            dc.DrawText(formatted, new Point(textLeft, textTop));
        }

        dc.Pop();
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
        TextTrimming Trimming,
        bool IsBold,
        bool IsItalic,
        bool IsUnderline);

    /// <summary>
    /// Hit-tests <paramref name="pos"/> against all visible native slicer and timeline controls,
    /// firing the matching event (clear-filter, tile-toggle, timeline-range, or granularity-cycle)
    /// when a hit is found. Returns <c>true</c> when an event was fired and the caller should mark
    /// the input event as handled; <c>false</c> when the point falls outside every control.
    /// </summary>
    internal bool TryHandleNativeSlicerTimelineClick(Point pos)
    {
        if (Viewport is not { } viewport)
            return false;
        if (NativeSlicers is not { Count: > 0 } && NativeTimelines is not { Count: > 0 })
            return false;

        var metricLookups = GetRenderMetricLookups(viewport);

        if (NativeSlicers is not null)
        {
            foreach (var slicer in NativeSlicers)
            {
                if (slicer.DrawingAnchor is not { } anchor ||
                    !TryCreateDrawingAnchorRect(metricLookups, anchor, out var rect))
                    continue;

                var controlRect = EnsureMinimumControlRect(rect);
                if (!controlRect.Contains(pos))
                    continue;

                // Build the portable layout to access the icon rects, using the control bounds.
                var modelBounds = new LayoutRect(
                    controlRect.Left, controlRect.Top, controlRect.Width, controlRect.Height);
                var availableItems = slicer.AvailableItems.Count > 0
                    ? (IEnumerable<string>)slicer.AvailableItems
                    : slicer.SelectedItems;
                var layout = FreeX.App.Presentation.SlicerTimeline.SlicerLayoutBuilder.BuildFull(
                    slicer, availableItems, modelBounds);
                var hitPoint = new LayoutPoint(pos.X, pos.Y);

                // Clear-filter icon hit?
                if (layout.HasActiveFilter &&
                    ContainsLayoutPoint(layout.ClearFilterIconRect, hitPoint))
                {
                    NativeSlicerClearFilterRequested?.Invoke(slicer.Name);
                    return true;
                }

                // Tile hit?
                if (FreeX.App.Presentation.SlicerTimeline.SlicerLayoutBuilder.HitTest(layout, hitPoint) is
                    { IsAllPreview: false } tile)
                {
                    NativeSlicerTileToggleRequested?.Invoke(slicer.Name, tile.Caption);
                    return true;
                }

                // Click inside the control but not on an actionable region — consume to prevent
                // inadvertent cell selection / object drag.
                return true;
            }
        }

        if (NativeTimelines is not null)
        {
            foreach (var timeline in NativeTimelines)
            {
                if (timeline.DrawingAnchor is not { } anchor ||
                    !TryCreateDrawingAnchorRect(metricLookups, anchor, out var rect))
                    continue;

                var controlRect = EnsureMinimumControlRect(rect);
                if (!controlRect.Contains(pos))
                    continue;

                var modelBounds = new LayoutRect(
                    controlRect.Left, controlRect.Top, controlRect.Width, controlRect.Height);
                var layout = FreeX.App.Presentation.SlicerTimeline.TimelineLayoutBuilder.Build(
                    timeline, modelBounds, SlicerTimelineGranularity.Resolve(timeline));
                var hitPoint = new LayoutPoint(pos.X, pos.Y);

                // Clear-filter icon hit?
                if (layout.HasActiveFilter &&
                    ContainsLayoutPoint(layout.ClearFilterIconRect, hitPoint))
                {
                    NativeTimelineClearFilterRequested?.Invoke(timeline.Name);
                    return true;
                }

                // Granularity dropdown hit?
                if (layout.GranularityDropdownRect.Width > 0 &&
                    ContainsLayoutPoint(layout.GranularityDropdownRect, hitPoint))
                {
                    NativeTimelineGranularityToggleRequested?.Invoke(timeline.Name);
                    return true;
                }

                // Track / handle hit → range command
                var hit = FreeX.App.Presentation.SlicerTimeline.TimelineLayoutBuilder.HitTest(layout, hitPoint);
                if (hit.Kind != FreeX.App.Presentation.SlicerTimeline.TimelineHitKind.None &&
                    hit.Date is { } hitDate)
                {
                    var (newStart, newEnd) = FreeX.App.Presentation.SlicerTimeline.SlicerTimelineHitDateResolver.ResolveRange(
                        layout, hit.Kind, hitDate);
                    NativeTimelineRangeRequested?.Invoke(
                        timeline.Name,
                        newStart?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                        newEnd?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
                    return true;
                }

                // Click inside the control — consume.
                return true;
            }
        }

        return false;
    }

    private static bool ContainsLayoutPoint(
        LayoutRect rect,
        LayoutPoint point) =>
        rect.Width > 0 && rect.Height > 0 &&
        point.X >= rect.Left && point.X <= rect.Right &&
        point.Y >= rect.Top && point.Y <= rect.Bottom;

}

public readonly record struct DrawingObjectColors(CellColor Fill, CellColor Outline);
