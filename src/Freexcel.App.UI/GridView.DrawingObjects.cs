using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Freexcel.Core.Model;

namespace Freexcel.App.UI;

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

    private void RenderCharts(DrawingContext dc)
    {
        if (Charts == null || Viewport == null) return;
        foreach (var chart in Charts)
        {
            if (!chart.IsVisible) continue;
            var img = ChartRenderer.Render(chart, Viewport, WorkbookTheme);
            if (img == null) continue;
            var rect = new Rect(
                chart.Left + ActualRowHeaderWidth, chart.Top + EffectiveColHeaderHeight,
                chart.Width, chart.Height);
            dc.DrawImage(img, rect);
        }
    }

    private void RenderTextBoxes(DrawingContext dc)
    {
        if (TextBoxes == null || Viewport == null) return;

        foreach (var textBox in TextBoxes)
        {
            if (!textBox.IsVisible) continue;
            var row = Viewport.RowMetrics.FirstOrDefault(r => r.Row == textBox.Anchor.Row);
            var col = Viewport.ColMetrics.FirstOrDefault(c => c.Col == textBox.Anchor.Col);
            if (row is null || col is null) continue;

            var rect = new Rect(
                col.LeftOffset + ActualRowHeaderWidth,
                row.TopOffset + EffectiveColHeaderHeight,
                Math.Max(24, textBox.Width),
                Math.Max(18, textBox.Height));
            var rotationPushed = PushRotation(dc, textBox.RotationDegrees, rect);
            var colors = ResolveTextBoxColors(textBox, WorkbookTheme);
            DrawTextBoxThemeEffect(dc, rect, WorkbookTheme);
            var fillBrush = MakeBrushAlpha(242, colors.Fill.R, colors.Fill.G, colors.Fill.B);
            var borderPen = new Pen(MakeBrush(colors.Outline.R, colors.Outline.G, colors.Outline.B), 1);
            borderPen.Freeze();
            dc.DrawRectangle(fillBrush, borderPen, rect);

            var text = new FormattedText(
                textBox.Text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                DefaultTypeface,
                12,
                TextBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip)
            {
                MaxTextWidth = Math.Max(1, rect.Width - 8),
                MaxTextHeight = Math.Max(1, rect.Height - 8)
            };

            dc.PushClip(new RectangleGeometry(new Rect(rect.Left + 4, rect.Top + 4, rect.Width - 8, rect.Height - 8)));
            dc.DrawText(text, new Point(rect.Left + 4, rect.Top + 4));
            dc.Pop();
            if (rotationPushed) dc.Pop();
        }
    }

    private void RenderDrawingShapes(DrawingContext dc)
    {
        if (DrawingShapes == null || Viewport == null) return;

        foreach (var shape in DrawingShapes)
        {
            if (!shape.IsVisible) continue;
            var row = Viewport.RowMetrics.FirstOrDefault(r => r.Row == shape.Anchor.Row);
            var col = Viewport.ColMetrics.FirstOrDefault(c => c.Col == shape.Anchor.Col);
            if (row is null || col is null) continue;

            var rect = new Rect(
                col.LeftOffset + ActualRowHeaderWidth,
                row.TopOffset + EffectiveColHeaderHeight,
                Math.Max(8, shape.Width),
                Math.Max(8, shape.Height));

            var rotationPushed = PushRotation(dc, shape.RotationDegrees, rect);
            var colors = ResolveDrawingShapeColors(shape, WorkbookTheme);
            DrawShapeThemeEffect(dc, shape.Kind, rect, WorkbookTheme);
            DrawShapeAuthoredEffect(dc, shape.Kind, rect, shape);
            var pen = new Pen(MakeBrush(colors.Outline.R, colors.Outline.G, colors.Outline.B), 1.5);
            pen.Freeze();
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
        if (Viewport == null)
            return;

        if (NativeSlicers is not null)
        {
            foreach (var slicer in NativeSlicers)
            {
                if (slicer.DrawingAnchor is not { } anchor ||
                    !TryCreateDrawingAnchorRect(Viewport, anchor, ActualRowHeaderWidth, EffectiveColHeaderHeight, out var rect))
                    continue;

                DrawNativeSlicerControl(dc, EnsureMinimumControlRect(rect), slicer);
            }
        }

        if (NativeTimelines is not null)
        {
            foreach (var timeline in NativeTimelines)
            {
                if (timeline.DrawingAnchor is not { } anchor ||
                    !TryCreateDrawingAnchorRect(Viewport, anchor, ActualRowHeaderWidth, EffectiveColHeaderHeight, out var rect))
                    continue;

                DrawNativeTimelineControl(dc, EnsureMinimumControlRect(rect), timeline);
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

    private void DrawNativeSlicerControl(DrawingContext dc, Rect rect, SlicerModel slicer)
    {
        DrawNativeControlFrame(dc, rect, GetNativeControlCaption(slicer.Caption, slicer.Name, slicer.DrawingShapeName));

        var items = slicer.SelectedItems.Count == 0
            ? new[] { slicer.SourceFieldName ?? slicer.CacheName ?? "All" }
            : slicer.SelectedItems.Take(4).ToArray();
        var tileTop = rect.Top + 26;
        var tileHeight = Math.Max(14, Math.Min(22, (rect.Bottom - tileTop - 6) / Math.Max(1, items.Length)));
        for (var index = 0; index < items.Length; index++)
        {
            var tileRect = new Rect(rect.Left + 6, tileTop + index * (tileHeight + 3), Math.Max(1, rect.Width - 12), tileHeight);
            dc.DrawRoundedRectangle(
                slicer.SelectedItems.Count == 0 ? NativeControlSelectedTileBrush : NativeControlTileBrush,
                null,
                tileRect,
                2,
                2);
            DrawClippedText(dc, items[index], tileRect, NativeControlMutedTextBrush, 10, verticalPadding: 1);
        }
    }

    private void DrawNativeTimelineControl(DrawingContext dc, Rect rect, TimelineModel timeline)
    {
        DrawNativeControlFrame(dc, rect, GetNativeControlCaption(timeline.Caption, timeline.Name, timeline.DrawingShapeName));

        var label = FormatTimelineRange(timeline);
        var barRect = new Rect(rect.Left + 8, rect.Top + 34, Math.Max(1, rect.Width - 16), Math.Max(6, Math.Min(14, rect.Height - 42)));
        dc.DrawRoundedRectangle(NativeControlTileBrush, null, barRect, 3, 3);
        var selectedRect = new Rect(
            barRect.Left + barRect.Width * 0.18,
            barRect.Top,
            Math.Max(6, barRect.Width * 0.56),
            barRect.Height);
        dc.DrawRoundedRectangle(NativeControlSelectedTileBrush, null, selectedRect, 3, 3);
        DrawClippedText(dc, label, new Rect(rect.Left + 6, rect.Top + 22, Math.Max(1, rect.Width - 12), 12), NativeControlMutedTextBrush, 9, verticalPadding: 0);
    }

    private void DrawNativeControlFrame(DrawingContext dc, Rect rect, string caption)
    {
        dc.DrawRectangle(NativeControlBodyBrush, NativeControlBorderPen, rect);
        var headerRect = new Rect(rect.Left, rect.Top, rect.Width, Math.Min(22, rect.Height));
        dc.DrawRectangle(NativeControlHeaderBrush, null, headerRect);
        DrawClippedText(dc, caption, new Rect(headerRect.Left + 5, headerRect.Top + 2, Math.Max(1, headerRect.Width - 10), Math.Max(1, headerRect.Height - 4)), Brushes.White, 11, verticalPadding: 0);
    }

    private void DrawClippedText(DrawingContext dc, string textValue, Rect rect, Brush brush, double fontSize, double verticalPadding)
    {
        var text = new FormattedText(
            string.IsNullOrWhiteSpace(textValue) ? " " : textValue,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            DefaultTypeface,
            fontSize,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = Math.Max(1, rect.Width),
            MaxTextHeight = Math.Max(1, rect.Height),
            Trimming = TextTrimming.CharacterEllipsis
        };

        dc.PushClip(new RectangleGeometry(rect));
        dc.DrawText(text, new Point(rect.Left, rect.Top + verticalPadding));
        dc.Pop();
    }

    private static string GetNativeControlCaption(string? caption, string name, string? shapeName)
        => GridDrawingObjectPlanner.GetNativeControlCaption(caption, name, shapeName);

    private static string FormatTimelineRange(TimelineModel timeline)
        => GridDrawingObjectPlanner.FormatTimelineRange(timeline);

    private static Brush CreateDrawingShapeFill(DrawingShapeModel shape, CellColor startColor)
    {
        if (shape.GradientFillEndColor is { } endColor && shape.Kind != DrawingShapeKind.Line)
        {
            var brush = new LinearGradientBrush(
                Color.FromArgb(72, startColor.R, startColor.G, startColor.B),
                Color.FromArgb(72, endColor.R, endColor.G, endColor.B),
                new Point(0, 0),
                new Point(1, 1));
            brush.Freeze();
            return brush;
        }

        return MakeBrushAlpha(32, startColor.R, startColor.G, startColor.B);
    }

    private static void DrawShapeAuthoredEffect(DrawingContext dc, DrawingShapeKind kind, Rect rect, DrawingShapeModel shape)
    {
        if (!shape.HasShadowEffect)
            return;

        var shadowRect = rect;
        shadowRect.Offset(3, 3);
        var shadowBrush = MakeBrushAlpha(58, 0, 0, 0);
        var shadowPen = new Pen(shadowBrush, 2);
        shadowPen.Freeze();

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

    private static void DrawTextBoxThemeEffect(DrawingContext dc, Rect rect, WorkbookTheme theme)
    {
        var effect = WorkbookThemeEffectStyle.FromTheme(theme);
        if (!effect.HasShadow)
            return;

        var shadowRect = rect;
        shadowRect.Offset(effect.ShadowOffsetX, effect.ShadowOffsetY);
        var alpha = (byte)Math.Clamp(Math.Round(255 * effect.ShadowOpacity), 0, 255);
        dc.DrawRectangle(MakeBrushAlpha(alpha, 0, 0, 0), null, shadowRect);
    }

    private static void DrawShapeThemeEffect(DrawingContext dc, DrawingShapeKind kind, Rect rect, WorkbookTheme theme)
    {
        var effect = WorkbookThemeEffectStyle.FromTheme(theme);
        if (!effect.HasShadow)
            return;

        var shadowRect = rect;
        shadowRect.Offset(effect.ShadowOffsetX, effect.ShadowOffsetY);
        var alpha = (byte)Math.Clamp(Math.Round(255 * effect.ShadowOpacity), 0, 255);
        var shadowBrush = MakeBrushAlpha(alpha, 0, 0, 0);
        var shadowPen = new Pen(shadowBrush, 2);
        shadowPen.Freeze();

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

        if (Charts is not null)
        {
            var index = 1;
            foreach (var chart in Charts)
            {
                if (chart.IsVisible)
                    DrawObjectPlaceholder(dc, new Rect(
                        chart.Left + ActualRowHeaderWidth,
                        chart.Top + EffectiveColHeaderHeight,
                        Math.Max(24, chart.Width),
                        Math.Max(18, chart.Height)), CreateObjectPlaceholderLabel("Chart", chart.Name, index));
                index++;
            }
        }

        if (DrawingShapes is not null)
        {
            var index = 1;
            foreach (var shape in DrawingShapes)
            {
                if (shape.IsVisible && TryCreateAnchoredObjectRect(shape.Anchor, shape.Width, shape.Height, 8, 8, out var rect))
                    DrawObjectPlaceholder(dc, rect, CreateObjectPlaceholderLabel("Shape", shape.Name, index));
                index++;
            }
        }

        if (Pictures is not null)
        {
            var index = 1;
            foreach (var picture in Pictures)
            {
                if (picture.IsVisible && TryCreateAnchoredObjectRect(picture.Anchor, picture.Width, picture.Height, 24, 18, out var rect))
                    DrawObjectPlaceholder(dc, rect, CreateObjectPlaceholderLabel("Picture", picture.Name, index));
                index++;
            }
        }

        if (TextBoxes is not null)
        {
            var index = 1;
            foreach (var textBox in TextBoxes)
            {
                if (textBox.IsVisible && TryCreateAnchoredObjectRect(textBox.Anchor, textBox.Width, textBox.Height, 24, 18, out var rect))
                    DrawObjectPlaceholder(dc, rect, CreateObjectPlaceholderLabel("Text Box", textBox.Name, index));
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
                    DrawObjectPlaceholder(dc, EnsureMinimumControlRect(rect), CreateObjectPlaceholderLabel("Slicer", slicer.DrawingShapeName ?? slicer.Caption ?? slicer.Name, index));
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
                    DrawObjectPlaceholder(dc, EnsureMinimumControlRect(rect), CreateObjectPlaceholderLabel("Timeline", timeline.DrawingShapeName ?? timeline.Caption ?? timeline.Name, index));
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
        out Rect rect)
    {
        rect = default;
        if (Viewport == null)
            return false;

        var row = Viewport.RowMetrics.FirstOrDefault(r => r.Row == anchor.Row);
        var col = Viewport.ColMetrics.FirstOrDefault(c => c.Col == anchor.Col);
        if (row is null || col is null)
            return false;

        rect = new Rect(
            col.LeftOffset + ActualRowHeaderWidth,
            row.TopOffset + EffectiveColHeaderHeight,
            Math.Max(minimumWidth, width),
            Math.Max(minimumHeight, height));
        return true;
    }

    private void DrawObjectPlaceholder(DrawingContext dc, Rect rect, string label)
    {
        dc.DrawRectangle(ObjectPlaceholderFill, ObjectPlaceholderPen, rect);
        DrawPlaceholderDiagonals(dc, rect);

        var text = new FormattedText(
            label,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            DefaultTypeface,
            11,
            ObjectPlaceholderTextBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = Math.Max(1, rect.Width - 8),
            MaxTextHeight = Math.Max(1, rect.Height - 8),
            Trimming = TextTrimming.CharacterEllipsis
        };

        var textPoint = new Point(
            rect.Left + Math.Max(4, (rect.Width - text.Width) / 2),
            rect.Top + Math.Max(4, (rect.Height - text.Height) / 2));
        dc.PushClip(new RectangleGeometry(new Rect(rect.Left + 4, rect.Top + 4, Math.Max(1, rect.Width - 8), Math.Max(1, rect.Height - 8))));
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

}

public readonly record struct DrawingObjectColors(CellColor Fill, CellColor Outline);
