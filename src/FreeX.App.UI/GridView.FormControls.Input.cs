using System.Windows;
using System.Windows.Media;
using FreeX.App.Presentation.Drawing;
using FreeX.Core.Model;

namespace FreeX.App.UI;

/// <summary>
/// Describes what part of a form control was clicked, enough for the host to route the interaction.
/// </summary>
public enum FormControlClickRegion
{
    /// <summary>Main body / toggle area (checkbox, option button, listbox row, dropdown, button).</summary>
    Body,
    /// <summary>Upper arrow on a vertical spinner, or left arrow on a horizontal scroll-bar.</summary>
    StepUp,
    /// <summary>Lower arrow on a vertical spinner, or right arrow on a horizontal scroll-bar.</summary>
    StepDown,
}

/// <summary>Describes a form-control click as fired by <see cref="GridView.FormControlClicked"/>.</summary>
public sealed class FormControlClickEventArgs(
    FormControlModel control,
    FormControlClickRegion region,
    Point clickPosition) : EventArgs
{
    /// <summary>The clicked control.</summary>
    public FormControlModel Control { get; } = control;

    /// <summary>Which sub-region of the control was clicked.</summary>
    public FormControlClickRegion Region { get; } = region;

    /// <summary>Screen position of the click inside the GridView.</summary>
    public Point ClickPosition { get; } = clickPosition;

    /// <summary>
    /// For list-style controls (ListBox/DropDown), the 1-based item index corresponding to the
    /// row the user clicked.  Zero when not applicable or not determinable.
    /// </summary>
    public int ListItemIndex { get; init; }
}

public partial class GridView
{
    /// <summary>
    /// Fired when the user left-clicks a form control.  The host subscribes to this event and
    /// calls <c>FormControlInteractionService</c> to apply the state transition + linked-cell write.
    /// </summary>
    public event EventHandler<FormControlClickEventArgs>? FormControlClicked;

    // ── Hit-testing ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the form control (and the sub-region within it) that the given position falls into,
    /// or <see langword="null"/> when the position does not hit any renderable form control.
    /// </summary>
    private (FormControlModel Control, FormControlClickRegion Region, int ListItemIndex)? HitTestFormControl(Point pos)
    {
        if (FormControls is not { Count: > 0 } || Viewport == null)
            return null;

        var visibleRight = GetDrawingViewportRight();
        var visibleBottom = GetDrawingViewportBottom();
        var (lastRenderableRow, lastRenderableColumn) = GetRenderableDrawingAnchorBounds(visibleRight, visibleBottom);
        var metricLookups = GetRenderMetricLookups(Viewport);

        // Iterate in reverse so the topmost (last-drawn) control wins.
        for (var i = FormControls.Count - 1; i >= 0; i--)
        {
            var control = FormControls[i];

            if (!FormControlRenderPlanner.IsRenderable(control.Kind))
                continue;

            // GroupBox and Label have no interactive behaviour.
            if (control.Kind is FormControlKind.GroupBox or FormControlKind.Label)
                continue;

            if (!FormControlRenderPlanner.TryCreateAnchorRange(control, out var anchorRange) ||
                anchorRange is not { } anchor)
                continue;

            if (!CanAnchoredObjectReachDrawingViewport(anchor, lastRenderableRow, lastRenderableColumn))
                continue;

            var hasOffsets = FormControlRenderPlanner.HasSubCellOffsets(control);
            bool built;
            Rect rect;
            if (hasOffsets)
                built = GridDrawingObjectPlanner.TryCreateDrawingAnchorRect(
                    metricLookups.Rows, metricLookups.Columns, anchor,
                    ActualRowHeaderWidth, EffectiveColHeaderHeight, out rect);
            else
                built = GridDrawingObjectPlanner.TryCreateSpanningAnchorRect(
                    metricLookups.Rows, metricLookups.Columns, anchor,
                    ActualRowHeaderWidth, EffectiveColHeaderHeight, out rect);

            if (!built)
                continue;

            if (!rect.Contains(pos))
                continue;

            // Hit! Now figure out which sub-region.
            var region = ClassifyClickRegion(control, rect, pos);
            var listItemIndex = ClassifyListItemIndex(control, rect, pos);

            return (control, region, listItemIndex);
        }

        return null;
    }

    private static FormControlClickRegion ClassifyClickRegion(FormControlModel control, Rect rect, Point pos)
    {
        switch (control.Kind)
        {
            case FormControlKind.Spinner:
            {
                var width = Math.Max(8, Math.Min(rect.Width, 17));
                var buttonRect = new Rect(rect.Left, rect.Top, width, rect.Height);
                var half = buttonRect.Height / 2;
                var upRect = new Rect(buttonRect.Left, buttonRect.Top, buttonRect.Width, half);
                if (upRect.Contains(pos))
                    return FormControlClickRegion.StepUp;
                return FormControlClickRegion.StepDown;
            }

            case FormControlKind.ScrollBar:
            {
                var horizontal = rect.Width >= rect.Height;
                if (horizontal)
                {
                    var size = Math.Min(rect.Height, rect.Width / 2);
                    var leftRect = new Rect(rect.Left, rect.Top, size, rect.Height);
                    if (leftRect.Contains(pos))
                        return FormControlClickRegion.StepUp;   // left arrow = decrement
                    return FormControlClickRegion.StepDown;     // right arrow = increment
                }
                else
                {
                    var size = Math.Min(rect.Width, rect.Height / 2);
                    var topRect = new Rect(rect.Left, rect.Top, rect.Width, size);
                    if (topRect.Contains(pos))
                        return FormControlClickRegion.StepUp;   // up arrow = decrement
                    return FormControlClickRegion.StepDown;     // down arrow = increment
                }
            }

            default:
                return FormControlClickRegion.Body;
        }
    }

    private static int ClassifyListItemIndex(FormControlModel control, Rect rect, Point pos)
    {
        if (control.Kind != FormControlKind.ListBox)
            return 0;

        const double rowHeight = 15;
        var relativeY = pos.Y - rect.Top;
        var row = (int)Math.Floor(relativeY / rowHeight);
        return row + 1; // 1-based
    }

    // ── Input handling ──────────────────────────────────────────────────────

    /// <summary>
    /// Called from <see cref="OnMouseLeftButtonDown"/> before the normal drawing-object hit test
    /// so that form-control clicks are consumed and do not accidentally select/drag the shape layer.
    /// Returns <see langword="true"/> when a form control was hit and the event has been handled.
    /// </summary>
    private bool TryHandleFormControlClick(Point pos)
    {
        var hit = HitTestFormControl(pos);
        if (hit is null)
            return false;

        var args = new FormControlClickEventArgs(hit.Value.Control, hit.Value.Region, pos)
        {
            ListItemIndex = hit.Value.ListItemIndex,
        };
        FormControlClicked?.Invoke(this, args);

        // Re-render so any in-model state change (IsChecked flip, etc.) is immediately visible.
        InvalidateVisual();
        return true;
    }
}
