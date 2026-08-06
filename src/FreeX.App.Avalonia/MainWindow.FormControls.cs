using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.Drawing;
using FreeX.Core.Commands;
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

        // Mirror WPF's per-viewport-refresh sync (MainWindow.Viewport.cs): re-derive each control's
        // IsChecked/Value/SelectedIndex from its linked cell's current value (so a direct cell edit
        // or formula recalc is reflected even without clicking the control), and populate DropDown/
        // ListBox SelectedText from ListFillRange so the caption actually renders on Avalonia too.
        FormControlInteractionService.SyncControlsFromLinkedCells(sheet, _session.Workbook);
        FormControlListResolver.PopulateSelectedText(sheet, _session.Workbook);

        var showHeadings = sheet.ShowHeadings;
        var zoomFactor = GetActiveZoomFactor();

        foreach (var control in sheet.FormControls)
        {
            if (!FormControlRenderPlanner.IsRenderable(control.Kind))
                continue;
            if (!FormControlRenderPlanner.TryCreateAnchorRange(control, out var anchor) || anchor is null)
                continue;
            if (!TryResolveAnchorBounds(viewport, anchor, showHeadings, zoomFactor, out var bounds))
                continue;

            var visual = new FormControlVisual(control, zoomFactor, OnFormControlClicked)
            {
                Width = Math.Max(1, bounds.Width),
                Height = Math.Max(1, bounds.Height),
            };
            Canvas.SetLeft(visual, bounds.Left);
            Canvas.SetTop(visual, bounds.Top);
            overlay.Children.Add(visual);
        }
    }

    /// <summary>
    /// Called by <see cref="FormControlVisual"/> when the user clicks a form control.
    /// Routes to <see cref="FormControlInteractionService"/> and executes the resulting command
    /// through the session (undoable, triggers recalc), then refreshes the shell.
    /// </summary>
    private void OnFormControlClicked(FormControlModel control, FormControlInteractionPlan interaction)
    {
        var sheet = _session.ActiveSheet;
        var sheetId = sheet.Id;
        var workbook = _session.Workbook;

        var command = FormControlInteractionService.CreateCommand(
            new FormControlInteractionRequest(control, interaction.Gesture, interaction.ListItemIndex),
            sheet.FormControls,
            sheetId,
            workbook);

        if (command is null)
        {
            // In-model state already mutated — just refresh rendering.
            RefreshShell(string.Empty);
            return;
        }

        var result = _session.ExecuteReviewCommand(command);
        if (!result.Success)
        {
            RefreshShell(result.ErrorMessage ?? "Form control interaction failed.");
            return;
        }

        if (result.RecalcReport is not null)
            RefreshShell(string.Empty);
        else
            RefreshShell(string.Empty);
    }

}

/// <summary>Draws one legacy form control's static chrome, mirroring the WPF renderer's appearance.</summary>
internal sealed class FormControlVisual : Control
{
    private static readonly IBrush GlyphBrush = Brushes.Black;
    private static readonly IBrush BoxFill = Brushes.White;
    private static readonly IBrush ChromeFill = new ImmutableSolidColorBrush(Color.FromRgb(240, 240, 240));
    private static readonly IBrush ShadowBrush = new ImmutableSolidColorBrush(Color.FromRgb(128, 128, 128));
    private static readonly IBrush HighlightBrush = Brushes.White;
    private static readonly IBrush DarkShadowBrush = new ImmutableSolidColorBrush(Color.FromRgb(105, 105, 105));

    private readonly FormControlModel _control;
    private readonly double _zoom;
    private readonly Action<FormControlModel, FormControlInteractionPlan>? _clickCallback;

    public FormControlVisual(
        FormControlModel control,
        double zoom,
        Action<FormControlModel, FormControlInteractionPlan>? clickCallback = null)
    {
        _control = control;
        _zoom = zoom <= 0 ? 1 : zoom;
        _clickCallback = clickCallback;

        // Enable hit-testing for interactive controls; GroupBox and Label have no interaction.
        var isInteractive = FormControlRenderPlanner.IsInteractive(control.Kind) && clickCallback is not null;
        IsHitTestVisible = isInteractive;

        if (isInteractive)
        {
            AddHandler(PointerPressedEvent, OnPointerPressed, handledEventsToo: false);
            Cursor = new Cursor(StandardCursorType.Hand);
        }
    }

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
            case FormControlKind.DropDown: DrawDropDown(context, rect); break;
            case FormControlKind.ListBox: DrawListBox(context, rect); break;
            case FormControlKind.Button: DrawButton(context, rect); break;
        }
    }

    // ── Pointer interaction ──────────────────────────────────────────────────

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_clickCallback is null)
            return;

        var point = e.GetPosition(this);
        var rect = new Rect(0, 0, Bounds.Width, Bounds.Height);

        var interaction = FormControlRenderPlanner.PlanInteraction(
            _control,
            ToLayoutRect(rect),
            new LayoutPoint(point.X, point.Y),
            rect.Width);
        _clickCallback(_control, interaction);
        InvalidateVisual();
        e.Handled = true;
    }

    // ── Drawing helpers ──────────────────────────────────────────────────────

    private void DrawCheckBox(DrawingContext context, Rect rect)
    {
        var box = ToAvaloniaRect(FormControlRenderPlanner.GetGlyphRect(ToLayoutRect(rect), 13 * _zoom));
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
        var box = ToAvaloniaRect(FormControlRenderPlanner.GetGlyphRect(ToLayoutRect(rect), 13 * _zoom));
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
        var layout = FormControlRenderPlanner.GetSpinnerButtonLayout(ToLayoutRect(rect), 17 * _zoom);
        var up = ToAvaloniaRect(layout.FirstButton);
        var down = ToAvaloniaRect(layout.SecondButton);
        DrawRaisedButton(context, up);
        DrawRaisedButton(context, down);
        DrawTriangle(context, up, layout.FirstDirection);
        DrawTriangle(context, down, layout.SecondDirection);
    }

    private void DrawScrollBar(DrawingContext context, Rect rect)
    {
        context.DrawRectangle(ChromeFill, BoxBorderPen, rect);
        var layout = FormControlRenderPlanner.GetScrollBarButtonLayout(ToLayoutRect(rect));
        var firstButton = ToAvaloniaRect(layout.FirstButton);
        var secondButton = ToAvaloniaRect(layout.SecondButton);
        DrawRaisedButton(context, firstButton);
        DrawRaisedButton(context, secondButton);
        DrawTriangle(context, firstButton, layout.FirstDirection);
        DrawTriangle(context, secondButton, layout.SecondDirection);
    }

    private void DrawDropDown(DrawingContext context, Rect rect)
    {
        context.DrawRectangle(BoxFill, BoxBorderPen, rect);
        var buttonLayout = FormControlRenderPlanner.GetDropDownButtonRect(ToLayoutRect(rect));
        var button = ToAvaloniaRect(buttonLayout);
        DrawRaisedButton(context, button);
        DrawTriangle(context, button, FormControlTriangleDirection.Down);

        var text = FormControlRenderPlanner.GetSelectedText(_control);
        if (!string.IsNullOrEmpty(text))
        {
            var textLayout = FormControlRenderPlanner.GetDropDownTextRect(
                ToLayoutRect(rect), buttonLayout);
            var textRect = ToAvaloniaRect(textLayout);
            DrawCaption(context, textRect, textRect.Left + 3);
        }
    }

    private static void DrawListBox(DrawingContext context, Rect rect)
    {
        var borderPen = new Pen(ShadowBrush, 1);
        context.DrawRectangle(BoxFill, borderPen, rect);

        var rowPen = new Pen(new SolidColorBrush(Color.FromRgb(224, 224, 224)), 1);
        foreach (var y in FormControlRenderPlanner.GetListRowSeparatorYCoordinates(ToLayoutRect(rect)))
            context.DrawLine(rowPen, new Point(rect.Left + 1, y), new Point(rect.Right - 1, y));
    }

    private void DrawButton(DrawingContext context, Rect rect)
    {
        DrawRaisedButton(context, rect);
        var caption = FormControlRenderPlanner.GetCaption(_control);
        if (!string.IsNullOrEmpty(caption))
            DrawCaption(context, rect, rect.Left + Math.Max(0, (rect.Width - MeasureTextWidth(caption)) / 2));
    }

    private double MeasureTextWidth(string text)
    {
        var formatted = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            11 * _zoom,
            GlyphBrush);
        return formatted.Width;
    }

    private void DrawGroupBox(DrawingContext context, Rect rect)
    {
        var layout = FormControlRenderPlanner.GetGroupBoxLayout(ToLayoutRect(rect), 14 * _zoom);
        context.DrawRectangle(null, BoxBorderPen, ToAvaloniaRect(layout.Frame));
        DrawCaption(context, ToAvaloniaRect(layout.Caption), rect.Left + 8);
    }

    private void DrawRaisedButton(DrawingContext context, Rect rect)
    {
        context.DrawRectangle(ChromeFill, null, rect);
        context.DrawLine(new Pen(HighlightBrush, 1), rect.TopLeft, rect.TopRight);
        context.DrawLine(new Pen(HighlightBrush, 1), rect.TopLeft, rect.BottomLeft);
        context.DrawLine(new Pen(DarkShadowBrush, 1), rect.BottomLeft, rect.BottomRight);
        context.DrawLine(new Pen(DarkShadowBrush, 1), rect.TopRight, rect.BottomRight);
    }

    private void DrawTriangle(DrawingContext context, Rect rect, FormControlTriangleDirection direction)
    {
        var layout = FormControlRenderPlanner.GetTriangleLayout(ToLayoutRect(rect), direction);
        var first = new Point(layout.First.X, layout.First.Y);
        var second = new Point(layout.Second.X, layout.Second.Y);
        var third = new Point(layout.Third.X, layout.Third.Y);

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(first, isFilled: true);
            ctx.LineTo(second);
            ctx.LineTo(third);
            ctx.EndFigure(isClosed: true);
        }

        context.DrawGeometry(GlyphBrush, null, geometry);
    }

    private void DrawCaption(DrawingContext context, Rect rect, double textLeft)
    {
        var caption = FormControlRenderPlanner.GetCaption(_control);
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

    private static LayoutRect ToLayoutRect(Rect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    private static Rect ToAvaloniaRect(LayoutRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);
}
