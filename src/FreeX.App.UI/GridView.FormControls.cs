using System.Windows;
using System.Windows.Media;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.Drawing;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public partial class GridView
{
    // Static chrome for legacy Excel form controls (checkboxes, option buttons, spinners,
    // scroll bars, group boxes, labels) anchored over the GridView. Interactivity
    // (click -> linked cell) is out of scope; this layer only draws the appearance,
    // reading checked/selected state from the FormControlModel.

    private static readonly Brush FormControlGlyphBrush = MakeBrush(0, 0, 0);
    private static readonly Brush FormControlCaptionBrush = MakeBrush(0, 0, 0);
    private static readonly Brush FormControlBoxFillBrush = MakeBrush(255, 255, 255);
    private static readonly Brush FormControlChromeFillBrush = MakeBrush(240, 240, 240);
    private static readonly Pen FormControlGlyphPen = CreateFrozenPen(FormControlGlyphBrush, 1.6);
    private static readonly Pen FormControlBoxBorderPen = CreateFrozenPen(MakeBrush(128, 128, 128), 1);
    // 3-D shading: light highlight (top/left) + dark shadow (bottom/right) for raised chrome.
    private static readonly Pen FormControlHighlightPen = CreateFrozenPen(MakeBrush(255, 255, 255), 1);
    private static readonly Pen FormControlShadowPen = CreateFrozenPen(MakeBrush(128, 128, 128), 1);
    private static readonly Pen FormControlDarkShadowPen = CreateFrozenPen(MakeBrush(105, 105, 105), 1);
    // Faint horizontal row separators inside a list-box well.
    private static readonly Pen FormControlListRowPen = CreateFrozenPen(MakeBrush(224, 224, 224), 1);

    private const double FormControlGlyphSize = 13;

    private void RenderFormControls(DrawingContext dc)
    {
        if (FormControls is not { Count: > 0 } || Viewport == null)
            return;

        var visibleRight = GetDrawingViewportRight();
        var visibleBottom = GetDrawingViewportBottom();
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var (lastRenderableRow, lastRenderableColumn) = GetRenderableDrawingAnchorBounds(visibleRight, visibleBottom);
        var metricLookups = GetRenderMetricLookups(Viewport);

        foreach (var control in FormControls)
        {
            if (!FormControlRenderPlanner.IsRenderable(control.Kind))
                continue;
            if (!FormControlRenderPlanner.TryCreateAnchorRange(control, out var anchorRange) ||
                anchorRange is not { } anchor)
                continue;
            if (!CanAnchoredObjectReachDrawingViewport(anchor, lastRenderableRow, lastRenderableColumn))
                continue;

            // When sub-cell EMU offsets were preserved at load, use the offset-aware drawing-anchor
            // rect (same path pictures/slicers use) so the control sits at its true sub-cell position
            // and size. Otherwise fall back to the whole-cell span over the from/to anchor cells.
            var hasOffsets = FormControlRenderPlanner.HasSubCellOffsets(control);
            var built = hasOffsets
                ? GridDrawingObjectPlanner.TryCreateDrawingAnchorRect(
                    metricLookups.Rows,
                    metricLookups.Columns,
                    anchor,
                    ActualRowHeaderWidth,
                    EffectiveColHeaderHeight,
                    out var rect)
                : GridDrawingObjectPlanner.TryCreateSpanningAnchorRect(
                    metricLookups.Rows,
                    metricLookups.Columns,
                    anchor,
                    ActualRowHeaderWidth,
                    EffectiveColHeaderHeight,
                    out rect);
            if (!built)
                continue;
            if (!IntersectsDrawingViewport(rect, 0, visibleRight, visibleBottom))
                continue;

            DrawFormControl(dc, control, rect, pixelsPerDip);
        }
    }

    private void DrawFormControl(DrawingContext dc, FormControlModel control, Rect rect, double pixelsPerDip)
    {
        switch (control.Kind)
        {
            case FormControlKind.CheckBox:
                DrawFormCheckBox(dc, control, rect, pixelsPerDip);
                break;
            case FormControlKind.OptionButton:
                DrawFormOptionButton(dc, control, rect, pixelsPerDip);
                break;
            case FormControlKind.Spinner:
                DrawFormSpinner(dc, rect);
                break;
            case FormControlKind.ScrollBar:
                DrawFormScrollBar(dc, rect);
                break;
            case FormControlKind.GroupBox:
                DrawFormGroupBox(dc, control, rect, pixelsPerDip);
                break;
            case FormControlKind.Label:
                DrawFormLabel(dc, control, rect, pixelsPerDip);
                break;
            case FormControlKind.DropDown:
                DrawFormDropDown(dc, control, rect, pixelsPerDip);
                break;
            case FormControlKind.ListBox:
                DrawFormListBox(dc, rect);
                break;
            case FormControlKind.Button:
                DrawFormButton(dc, control, rect, pixelsPerDip);
                break;
        }
    }

    private void DrawFormDropDown(DrawingContext dc, FormControlModel control, Rect rect, double pixelsPerDip)
    {
        // White field with a thin border, a grey raised drop-down button (down-triangle) flush right,
        // and the selected item text in the field when resolvable (best-effort: blank otherwise).
        dc.DrawRectangle(FormControlBoxFillBrush, FormControlBoxBorderPen, rect);

        var buttonLayout = FormControlRenderPlanner.GetDropDownButtonRect(ToLayoutRect(rect));
        var button = ToWpfRect(buttonLayout);
        DrawFormControlRaisedButton(dc, button);
        DrawFormTriangle(dc, button, FormControlTriangleDirection.Down);

        // A drop-down has no authored caption in Excel — its field shows the SELECTED ITEM text
        // (the host resolves ListFillRange[SelectedIndex] into SelectedText). Blank when unresolved.
        var text = FormControlRenderPlanner.GetSelectedText(control);
        if (!string.IsNullOrEmpty(text))
        {
            var textRect = ToWpfRect(FormControlRenderPlanner.GetDropDownTextRect(ToLayoutRect(rect), buttonLayout));
            DrawFormControlCaption(dc, text, textRect, textRect.Left + 3, pixelsPerDip);
        }
    }

    private static void DrawFormListBox(DrawingContext dc, Rect rect)
    {
        // A bordered white box with faint row lines, matching Excel's list-box well.
        dc.DrawRectangle(FormControlBoxFillBrush, FormControlBoxBorderPen, rect);

        foreach (var y in FormControlRenderPlanner.GetListRowSeparatorYCoordinates(ToLayoutRect(rect)))
            dc.DrawLine(FormControlListRowPen, new Point(rect.Left + 1, y), new Point(rect.Right - 1, y));
    }

    private void DrawFormButton(DrawingContext dc, FormControlModel control, Rect rect, double pixelsPerDip)
    {
        // A 3-D raised push-button face with the caption centered.
        DrawFormControlRaisedButton(dc, rect);

        var caption = FormControlRenderPlanner.GetCaption(control);
        if (string.IsNullOrEmpty(caption))
            return;

        var text = GetDrawingObjectText(
            caption,
            FormControlCaptionBrush,
            11,
            Math.Max(1, rect.Width - 6),
            Math.Max(1, rect.Height),
            pixelsPerDip,
            TextTrimming.CharacterEllipsis);
        var textLeft = rect.Left + Math.Max(0, (rect.Width - text.Width) / 2);
        var textTop = rect.Top + Math.Max(0, (rect.Height - text.Height) / 2);

        dc.PushClip(GetDrawingObjectClipGeometry(rect));
        dc.DrawText(text, new Point(textLeft, textTop));
        dc.Pop();
    }

    private void DrawFormCheckBox(DrawingContext dc, FormControlModel control, Rect rect, double pixelsPerDip)
    {
        var box = ToWpfRect(FormControlRenderPlanner.GetGlyphRect(ToLayoutRect(rect), FormControlGlyphSize));
        dc.DrawRectangle(FormControlBoxFillBrush, FormControlBoxBorderPen, box);
        DrawFormControlSunkenEdge(dc, box);

        if (control.IsChecked)
            DrawFormCheckGlyph(dc, box);

        DrawFormControlCaption(dc, FormControlRenderPlanner.GetCaption(control), rect, box.Right + 6, pixelsPerDip);
    }

    private void DrawFormOptionButton(DrawingContext dc, FormControlModel control, Rect rect, double pixelsPerDip)
    {
        var box = ToWpfRect(FormControlRenderPlanner.GetGlyphRect(ToLayoutRect(rect), FormControlGlyphSize));
        var center = new Point(box.Left + box.Width / 2, box.Top + box.Height / 2);
        var radius = box.Width / 2;
        dc.DrawEllipse(FormControlBoxFillBrush, FormControlBoxBorderPen, center, radius, radius);

        if (control.IsChecked)
        {
            var dotRadius = Math.Max(1.5, radius - 3.5);
            dc.DrawEllipse(FormControlGlyphBrush, null, center, dotRadius, dotRadius);
        }

        DrawFormControlCaption(dc, FormControlRenderPlanner.GetCaption(control), rect, box.Right + 6, pixelsPerDip);
    }

    private void DrawFormSpinner(DrawingContext dc, Rect rect)
    {
        // Two stacked raised buttons (up over down) with black triangle glyphs.
        var layout = FormControlRenderPlanner.GetSpinnerButtonLayout(ToLayoutRect(rect), maximumButtonWidth: 17);
        var upRect = ToWpfRect(layout.FirstButton);
        var downRect = ToWpfRect(layout.SecondButton);

        DrawFormControlRaisedButton(dc, upRect);
        DrawFormControlRaisedButton(dc, downRect);
        DrawFormTriangle(dc, upRect, layout.FirstDirection);
        DrawFormTriangle(dc, downRect, layout.SecondDirection);
    }

    private void DrawFormScrollBar(DrawingContext dc, Rect rect)
    {
        dc.DrawRectangle(FormControlChromeFillBrush, FormControlBoxBorderPen, rect);
        var layout = FormControlRenderPlanner.GetScrollBarButtonLayout(ToLayoutRect(rect));
        var firstButton = ToWpfRect(layout.FirstButton);
        var secondButton = ToWpfRect(layout.SecondButton);
        DrawFormControlRaisedButton(dc, firstButton);
        DrawFormControlRaisedButton(dc, secondButton);
        DrawFormTriangle(dc, firstButton, layout.FirstDirection);
        DrawFormTriangle(dc, secondButton, layout.SecondDirection);
    }

    private void DrawFormGroupBox(DrawingContext dc, FormControlModel control, Rect rect, double pixelsPerDip)
    {
        // Etched rectangle with the caption breaking the top border at the left.
        var layout = FormControlRenderPlanner.GetGroupBoxLayout(ToLayoutRect(rect), captionHeight: 14);
        dc.DrawRectangle(null, FormControlBoxBorderPen, ToWpfRect(layout.Frame));
        DrawFormControlCaption(
            dc,
            FormControlRenderPlanner.GetCaption(control),
            ToWpfRect(layout.Caption),
            rect.Left + 8,
            pixelsPerDip);
    }

    private void DrawFormLabel(DrawingContext dc, FormControlModel control, Rect rect, double pixelsPerDip)
    {
        DrawFormControlCaption(dc, FormControlRenderPlanner.GetCaption(control), rect, rect.Left + 2, pixelsPerDip);
    }

    // Boundary conversions between the portable planner's LayoutRect and WPF's System.Windows.Rect.
    private static LayoutRect ToLayoutRect(Rect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    private static Rect ToWpfRect(LayoutRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    private static LayoutPoint ToLayoutPoint(Point point) => new(point.X, point.Y);

    private static void DrawFormControlSunkenEdge(DrawingContext dc, Rect box)
    {
        // Dark shadow lines on the top/left edges give the box a slightly recessed look (the cell
        // background supplies the lighter bottom/right), approximating Excel's sunken checkbox well.
        dc.DrawLine(FormControlShadowPen, box.TopLeft, box.TopRight);
        dc.DrawLine(FormControlShadowPen, box.TopLeft, box.BottomLeft);
    }

    private static void DrawFormCheckGlyph(DrawingContext dc, Rect box)
    {
        var p1 = new Point(box.Left + box.Width * 0.20, box.Top + box.Height * 0.52);
        var p2 = new Point(box.Left + box.Width * 0.42, box.Top + box.Height * 0.74);
        var p3 = new Point(box.Left + box.Width * 0.80, box.Top + box.Height * 0.24);
        dc.DrawLine(FormControlGlyphPen, p1, p2);
        dc.DrawLine(FormControlGlyphPen, p2, p3);
    }

    private static void DrawFormControlRaisedButton(DrawingContext dc, Rect rect)
    {
        dc.DrawRectangle(FormControlChromeFillBrush, null, rect);
        dc.DrawLine(FormControlHighlightPen, rect.TopLeft, rect.TopRight);
        dc.DrawLine(FormControlHighlightPen, rect.TopLeft, rect.BottomLeft);
        dc.DrawLine(FormControlDarkShadowPen, rect.BottomLeft, rect.BottomRight);
        dc.DrawLine(FormControlDarkShadowPen, rect.TopRight, rect.BottomRight);
    }

    private static void DrawFormTriangle(
        DrawingContext dc,
        Rect rect,
        FormControlTriangleDirection direction)
    {
        var layout = FormControlRenderPlanner.GetTriangleLayout(ToLayoutRect(rect), direction);
        var first = new Point(layout.First.X, layout.First.Y);
        var second = new Point(layout.Second.X, layout.Second.Y);
        var third = new Point(layout.Third.X, layout.Third.Y);

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(first, isFilled: true, isClosed: true);
            ctx.LineTo(second, isStroked: false, isSmoothJoin: false);
            ctx.LineTo(third, isStroked: false, isSmoothJoin: false);
        }

        geometry.Freeze();
        dc.DrawGeometry(FormControlGlyphBrush, null, geometry);
    }

    private void DrawFormControlCaption(DrawingContext dc, string caption, Rect rect, double textLeft, double pixelsPerDip)
    {
        if (string.IsNullOrWhiteSpace(caption))
            return;

        var textWidth = Math.Max(1, rect.Right - textLeft - 2);
        var textHeight = Math.Max(1, rect.Height);
        var text = GetDrawingObjectText(
            caption,
            FormControlCaptionBrush,
            11,
            textWidth,
            textHeight,
            pixelsPerDip,
            TextTrimming.CharacterEllipsis);
        var textTop = rect.Top + Math.Max(0, (rect.Height - text.Height) / 2);
        var clipRect = new Rect(textLeft, rect.Top, textWidth, textHeight);

        dc.PushClip(GetDrawingObjectClipGeometry(clipRect));
        dc.DrawText(text, new Point(textLeft, textTop));
        dc.Pop();
    }
}
