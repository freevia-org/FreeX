using FreeX.App.Presentation.Charts;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Drawing;

public readonly record struct FormControlInteractionPlan(
    FormControlGesture Gesture,
    int ListItemIndex);

/// <summary>
/// Pure, framework-free layout/state helpers for rendering legacy Excel form controls
/// (<see cref="FormControlModel"/>) as static chrome on the desktop hosts' drawing surfaces. Maps a
/// control's 1-based cell-range <see cref="FormControlModel.Anchor"/> to the 0-based
/// <see cref="DrawingAnchorRange"/> understood by the drawing-object anchor planner, decides which
/// kinds get drawn, computes the drop-down sub-rects, and resolves the caption/selected text. Shared
/// by the desktop hosts; carries no platform-specific rectangle types — rectangles are expressed
/// with <see cref="LayoutRect"/> and converted at each host's rendering boundary.
/// </summary>
public static class FormControlRenderPlanner
{
    public const double ListItemRowHeight = 15;

    /// <summary>
    /// Converts the control's 1-based <see cref="GridRange"/> anchor into the 0-based
    /// <see cref="DrawingAnchorRange"/> the drawing-object anchor planner consumes. Returns
    /// false when the control has no anchor.
    /// </summary>
    public static bool TryCreateAnchorRange(FormControlModel control, out DrawingAnchorRange? anchor)
    {
        anchor = null;

        // Prefer the preserved sub-cell EMU offsets (already a 0-based DrawingAnchorRange) so the
        // control rect reflects its true sub-cell position+size rather than snapping to whole cells.
        if (control.AnchorOffsets is { } offsets)
        {
            anchor = offsets;
            return true;
        }

        if (control.Anchor is not { } range)
            return false;

        // Whole-cell fallback (offsets absent): CellAddress is 1-based (Excel convention);
        // DrawingAnchorRange is 0-based (the planner re-adds +1 internally), so subtract one.
        anchor = new DrawingAnchorRange(
            new DrawingAnchorPoint(range.Start.Col - 1, 0, range.Start.Row - 1, 0),
            new DrawingAnchorPoint(range.End.Col - 1, 0, range.End.Row - 1, 0));
        return true;
    }

    /// <summary>
    /// Whether the control carries preserved sub-cell EMU offsets. When true the render uses the
    /// offset-aware drawing-anchor rect; when false it falls back to a whole-cell span.
    /// </summary>
    public static bool HasSubCellOffsets(FormControlModel control) => control.AnchorOffsets is not null;

    /// <summary>
    /// Whether this control kind has a static-chrome renderer. Only <see cref="FormControlKind.Unknown"/>
    /// (no modeled appearance) returns false.
    /// </summary>
    public static bool IsRenderable(FormControlKind kind) =>
        kind is FormControlKind.CheckBox
            or FormControlKind.OptionButton
            or FormControlKind.Spinner
            or FormControlKind.ScrollBar
            or FormControlKind.Label
            or FormControlKind.GroupBox
            or FormControlKind.DropDown
            or FormControlKind.ListBox
            or FormControlKind.Button;

    /// <summary>Whether the rendered control accepts a user gesture.</summary>
    public static bool IsInteractive(FormControlKind kind) =>
        IsRenderable(kind) && kind is not (FormControlKind.GroupBox or FormControlKind.Label);

    /// <summary>
    /// Classifies a native pointer position after the host converts its rectangle and point into
    /// layout coordinates. The caller supplies the width of the spinner button chrome because the
    /// two renderers intentionally retain their existing native hit extents.
    /// </summary>
    public static FormControlInteractionPlan PlanInteraction(
        FormControlModel control,
        LayoutRect rect,
        LayoutPoint position,
        double spinnerButtonWidth)
    {
        ArgumentNullException.ThrowIfNull(control);

        var gesture = control.Kind switch
        {
            FormControlKind.Spinner => PlanSpinnerGesture(rect, position, spinnerButtonWidth),
            FormControlKind.ScrollBar => PlanScrollBarGesture(rect, position),
            _ => FormControlGesture.Body,
        };
        var listItemIndex = control.Kind == FormControlKind.ListBox
            ? Math.Max(1, (int)Math.Floor((position.Y - rect.Top) / ListItemRowHeight) + 1)
            : 0;
        return new FormControlInteractionPlan(gesture, listItemIndex);
    }

    /// <summary>
    /// The square grey drop-down button rect for a <see cref="FormControlKind.DropDown"/> control:
    /// sized to the control height and flush against the right edge, but never wider than half the
    /// control so a short/tall box still shows a text area. Mirrors Excel's drop-down chrome.
    /// </summary>
    public static LayoutRect GetDropDownButtonRect(LayoutRect rect)
    {
        var size = Math.Max(1, Math.Min(rect.Height, rect.Width / 2));
        return new LayoutRect(rect.Right - size, rect.Top, size, rect.Height);
    }

    /// <summary>
    /// The text area of a drop-down (the white field to the left of the <paramref name="button"/>),
    /// where the selected item text is drawn when resolvable.
    /// </summary>
    public static LayoutRect GetDropDownTextRect(LayoutRect rect, LayoutRect button)
    {
        var width = Math.Max(0, button.Left - rect.Left);
        return new LayoutRect(rect.Left, rect.Top, width, rect.Height);
    }

    /// <summary>
    /// The selected-item text drawn inside a list-style control's field: the host-resolved
    /// <see cref="FormControlModel.SelectedText"/> (the <see cref="FormControlModel.SelectedIndex"/>-th
    /// item of <see cref="FormControlModel.ListFillRange"/>). Returns an empty string when nothing is
    /// selected or the source range could not be resolved — the caller then draws a blank field,
    /// matching the prior behavior.
    /// </summary>
    public static string GetSelectedText(FormControlModel control)
        => string.IsNullOrWhiteSpace(control.SelectedText) ? string.Empty : control.SelectedText.Trim();

    /// <summary>
    /// Resolves the caption text drawn next to / inside the control: the control's authored display
    /// text (<see cref="FormControlModel.Caption"/>, read from its VML textbox). Returns an empty
    /// string when the control has no authored caption — Excel draws no label in that case, so the
    /// caller renders nothing. The internal shape <see cref="FormControlModel.Name"/> is NOT used.
    /// </summary>
    public static string GetCaption(FormControlModel control)
        => string.IsNullOrWhiteSpace(control.Caption) ? string.Empty : control.Caption.Trim();

    private static FormControlGesture PlanSpinnerGesture(
        LayoutRect rect,
        LayoutPoint position,
        double spinnerButtonWidth)
    {
        var upperButton = new LayoutRect(
            rect.Left,
            rect.Top,
            Math.Max(0, spinnerButtonWidth),
            rect.Height / 2);
        return Contains(upperButton, position)
            ? FormControlGesture.StepUp
            : FormControlGesture.StepDown;
    }

    private static FormControlGesture PlanScrollBarGesture(LayoutRect rect, LayoutPoint position)
    {
        if (rect.Width >= rect.Height)
        {
            var size = Math.Min(rect.Height, rect.Width / 2);
            return Contains(new LayoutRect(rect.Left, rect.Top, size, rect.Height), position)
                ? FormControlGesture.StepUp
                : FormControlGesture.StepDown;
        }

        var verticalSize = Math.Min(rect.Width, rect.Height / 2);
        return Contains(new LayoutRect(rect.Left, rect.Top, rect.Width, verticalSize), position)
            ? FormControlGesture.StepUp
            : FormControlGesture.StepDown;
    }

    private static bool Contains(LayoutRect rect, LayoutPoint point) =>
        point.X >= rect.Left &&
        point.X <= rect.Right &&
        point.Y >= rect.Top &&
        point.Y <= rect.Bottom;
}
