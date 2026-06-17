using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    // Parity with the WPF GridView.FormControls renderer: draw static chrome for legacy Excel form
    // controls (checkbox/option/spinner/scrollbar/groupbox/label) on the Avalonia drawing overlay.
    // Positioning reuses the same anchor->displayed-bounds path slicers/timelines use.
    private void AddFormControlOverlays(Canvas overlay, ViewportModel viewport)
    {
        var sheet = _session.ActiveSheet;
        if (sheet.FormControls is not { Count: > 0 })
            return;

        var showHeadings = sheet.ShowHeadings;
        var zoomFactor = GetActiveZoomFactor();

        foreach (var control in sheet.FormControls)
        {
            if (!FormControlVisual.IsRenderable(control.Kind))
                continue;
            if (!FormControlVisual.TryCreateAnchorRange(control, out var anchor) || anchor is null)
                continue;
            if (!TryResolveAnchorBounds(viewport, anchor, showHeadings, zoomFactor, out var bounds))
                continue;

            var visual = new FormControlVisual(control, zoomFactor)
            {
                Width = Math.Max(1, bounds.Width),
                Height = Math.Max(1, bounds.Height),
            };
            Canvas.SetLeft(visual, bounds.Left);
            Canvas.SetTop(visual, bounds.Top);
            overlay.Children.Add(visual);
        }
    }
}

/// <summary>Draws one legacy form control's static chrome, mirroring the WPF renderer's appearance.</summary>
internal sealed class FormControlVisual : Control
{
    private static readonly IBrush GlyphBrush = Brushes.Black;
    private static readonly IBrush BoxFill = Brushes.White;
    private static readonly IBrush ChromeFill = new SolidColorBrush(Color.FromRgb(240, 240, 240));
    private static readonly IBrush ShadowBrush = new SolidColorBrush(Color.FromRgb(128, 128, 128));
    private static readonly IBrush HighlightBrush = Brushes.White;
    private static readonly IBrush DarkShadowBrush = new SolidColorBrush(Color.FromRgb(105, 105, 105));

    private readonly FormControlModel _control;
    private readonly double _zoom;

    public FormControlVisual(FormControlModel control, double zoom)
    {
        _control = control;
        _zoom = zoom <= 0 ? 1 : zoom;
        IsHitTestVisible = false;
    }

    // Inlined from the shared FormControlRenderPlanner (which lives in the WPF-only FreeX.App.UI
    // assembly the Avalonia project can't reference); kept byte-equivalent so both platforms match.
    public static bool IsRenderable(FormControlKind kind) =>
        kind is FormControlKind.CheckBox or FormControlKind.OptionButton or FormControlKind.Spinner
            or FormControlKind.ScrollBar or FormControlKind.Label or FormControlKind.GroupBox;

    public static bool TryCreateAnchorRange(FormControlModel control, out DrawingAnchorRange? anchor)
    {
        anchor = null;
        if (control.AnchorOffsets is { } offsets)
        {
            anchor = offsets;
            return true;
        }

        if (control.Anchor is not { } range)
            return false;

        anchor = new DrawingAnchorRange(
            new DrawingAnchorPoint(range.Start.Col - 1, 0, range.Start.Row - 1, 0),
            new DrawingAnchorPoint(range.End.Col - 1, 0, range.End.Row - 1, 0));
        return true;
    }

    private static string GetCaption(FormControlModel control) =>
        string.IsNullOrWhiteSpace(control.Caption) ? string.Empty : control.Caption!.Trim();

    private double GlyphSize => Math.Min(13 * _zoom, Math.Min(Bounds.Width, Bounds.Height));
    private IPen BoxBorderPen => new Pen(ShadowBrush, 1);
    private IPen GlyphPen => new Pen(GlyphBrush, 1.6 * _zoom);

    public override void Render(DrawingContext context)
    {
        var rect = new Rect(0, 0, Bounds.Width, Bounds.Height);
        switch (_control.Kind)
        {
            case FormControlKind.CheckBox: DrawCheckBox(context, rect); break;
            case FormControlKind.OptionButton: DrawOptionButton(context, rect); break;
            case FormControlKind.Spinner: DrawSpinner(context, rect); break;
            case FormControlKind.ScrollBar: DrawScrollBar(context, rect); break;
            case FormControlKind.GroupBox: DrawGroupBox(context, rect); break;
            case FormControlKind.Label: DrawCaption(context, rect, rect.Left + 2); break;
        }
    }

    private Rect GlyphRect(Rect rect)
    {
        var size = Math.Min(GlyphSize, Math.Min(rect.Width, rect.Height));
        var top = rect.Top + Math.Max(0, (rect.Height - size) / 2);
        return new Rect(rect.Left + 1, top, size, size);
    }

    private void DrawCheckBox(DrawingContext context, Rect rect)
    {
        var box = GlyphRect(rect);
        context.DrawRectangle(BoxFill, BoxBorderPen, box);
        context.DrawLine(new Pen(ShadowBrush, 1), box.TopLeft, box.TopRight);
        context.DrawLine(new Pen(ShadowBrush, 1), box.TopLeft, box.BottomLeft);
        if (_control.IsChecked)
        {
            var p1 = new Point(box.Left + box.Width * 0.20, box.Top + box.Height * 0.52);
            var p2 = new Point(box.Left + box.Width * 0.42, box.Top + box.Height * 0.74);
            var p3 = new Point(box.Left + box.Width * 0.80, box.Top + box.Height * 0.24);
            context.DrawLine(GlyphPen, p1, p2);
            context.DrawLine(GlyphPen, p2, p3);
        }

        DrawCaption(context, rect, box.Right + 4);
    }

    private void DrawOptionButton(DrawingContext context, Rect rect)
    {
        var box = GlyphRect(rect);
        var center = new Point(box.Left + box.Width / 2, box.Top + box.Height / 2);
        var radius = box.Width / 2;
        context.DrawEllipse(BoxFill, BoxBorderPen, center, radius, radius);
        if (_control.IsChecked)
        {
            var dot = Math.Max(1.5, radius - 3.5);
            context.DrawEllipse(GlyphBrush, null, center, dot, dot);
        }

        DrawCaption(context, rect, box.Right + 4);
    }

    private void DrawSpinner(DrawingContext context, Rect rect)
    {
        var width = Math.Max(8, Math.Min(rect.Width, 17 * _zoom));
        var half = rect.Height / 2;
        var up = new Rect(rect.Left, rect.Top, width, half);
        var down = new Rect(rect.Left, rect.Top + half, width, rect.Height - half);
        DrawRaisedButton(context, up);
        DrawRaisedButton(context, down);
        DrawTriangle(context, up, TriangleDirection.Up);
        DrawTriangle(context, down, TriangleDirection.Down);
    }

    private void DrawScrollBar(DrawingContext context, Rect rect)
    {
        context.DrawRectangle(ChromeFill, BoxBorderPen, rect);
        if (rect.Width >= rect.Height)
        {
            var size = Math.Min(rect.Height, rect.Width / 2);
            var left = new Rect(rect.Left, rect.Top, size, rect.Height);
            var right = new Rect(rect.Right - size, rect.Top, size, rect.Height);
            DrawRaisedButton(context, left);
            DrawRaisedButton(context, right);
            DrawTriangle(context, left, TriangleDirection.Left);
            DrawTriangle(context, right, TriangleDirection.Right);
        }
        else
        {
            var size = Math.Min(rect.Width, rect.Height / 2);
            var top = new Rect(rect.Left, rect.Top, rect.Width, size);
            var bottom = new Rect(rect.Left, rect.Bottom - size, rect.Width, size);
            DrawRaisedButton(context, top);
            DrawRaisedButton(context, bottom);
            DrawTriangle(context, top, TriangleDirection.Up);
            DrawTriangle(context, bottom, TriangleDirection.Down);
        }
    }

    private void DrawGroupBox(DrawingContext context, Rect rect)
    {
        var frame = new Rect(rect.Left + 1, rect.Top + 7, Math.Max(1, rect.Width - 2), Math.Max(1, rect.Height - 8));
        context.DrawRectangle(null, BoxBorderPen, frame);
        DrawCaption(context, new Rect(rect.Left, rect.Top, rect.Width, 14 * _zoom), rect.Left + 8);
    }

    private void DrawRaisedButton(DrawingContext context, Rect rect)
    {
        context.DrawRectangle(ChromeFill, null, rect);
        context.DrawLine(new Pen(HighlightBrush, 1), rect.TopLeft, rect.TopRight);
        context.DrawLine(new Pen(HighlightBrush, 1), rect.TopLeft, rect.BottomLeft);
        context.DrawLine(new Pen(DarkShadowBrush, 1), rect.BottomLeft, rect.BottomRight);
        context.DrawLine(new Pen(DarkShadowBrush, 1), rect.TopRight, rect.BottomRight);
    }

    private enum TriangleDirection { Up, Down, Left, Right }

    private void DrawTriangle(DrawingContext context, Rect rect, TriangleDirection direction)
    {
        var cx = rect.Left + rect.Width / 2;
        var cy = rect.Top + rect.Height / 2;
        var size = Math.Max(2, Math.Min(rect.Width, rect.Height) * 0.3);

        Point a, b, c;
        switch (direction)
        {
            case TriangleDirection.Left: a = new(cx - size, cy); b = new(cx + size, cy - size); c = new(cx + size, cy + size); break;
            case TriangleDirection.Right: a = new(cx + size, cy); b = new(cx - size, cy - size); c = new(cx - size, cy + size); break;
            case TriangleDirection.Up: a = new(cx, cy - size); b = new(cx - size, cy + size); c = new(cx + size, cy + size); break;
            default: a = new(cx, cy + size); b = new(cx - size, cy - size); c = new(cx + size, cy - size); break;
        }

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(a, isFilled: true);
            ctx.LineTo(b);
            ctx.LineTo(c);
            ctx.EndFigure(isClosed: true);
        }

        context.DrawGeometry(GlyphBrush, null, geometry);
    }

    private void DrawCaption(DrawingContext context, Rect rect, double textLeft)
    {
        var caption = GetCaption(_control);
        if (string.IsNullOrWhiteSpace(caption))
            return;

        var textWidth = Math.Max(1, rect.Right - textLeft - 2);
        var formatted = new FormattedText(
            caption,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            11 * _zoom,
            GlyphBrush)
        {
            MaxTextWidth = textWidth,
            Trimming = TextTrimming.CharacterEllipsis,
        };

        var textTop = rect.Top + Math.Max(0, (rect.Height - formatted.Height) / 2);
        using (context.PushClip(new Rect(textLeft, rect.Top, textWidth, rect.Height)))
            context.DrawText(formatted, new Point(textLeft, textTop));
    }
}
