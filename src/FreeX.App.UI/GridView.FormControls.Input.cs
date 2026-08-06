using System.Windows;
using System.Windows.Media;
using FreeX.App.Presentation.Drawing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.UI;

/// <summary>Describes a form-control click as fired by <see cref="GridView.FormControlClicked"/>.</summary>
public sealed class FormControlClickEventArgs(
    FormControlModel control,
    FormControlGesture gesture,
    Point clickPosition) : EventArgs
{
    /// <summary>The clicked control.</summary>
    public FormControlModel Control { get; } = control;

    /// <summary>The portable gesture translated from the native click.</summary>
    public FormControlGesture Gesture { get; } = gesture;

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
    private (FormControlModel Control, FormControlInteractionPlan Interaction)? HitTestFormControl(Point pos)
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

            if (!FormControlRenderPlanner.IsInteractive(control.Kind))
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

            var interaction = FormControlRenderPlanner.PlanInteraction(
                control,
                ToLayoutRect(rect),
                ToLayoutPoint(pos),
                Math.Max(8, Math.Min(rect.Width, 17)));
            return (control, interaction);
        }

        return null;
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

        var args = new FormControlClickEventArgs(hit.Value.Control, hit.Value.Interaction.Gesture, pos)
        {
            ListItemIndex = hit.Value.Interaction.ListItemIndex,
        };
        FormControlClicked?.Invoke(this, args);

        // Re-render so any in-model state change (IsChecked flip, etc.) is immediately visible.
        InvalidateVisual();
        return true;
    }
}
