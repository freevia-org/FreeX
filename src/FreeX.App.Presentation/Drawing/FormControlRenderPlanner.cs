using FreeX.App.Presentation.Charts;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Drawing;

public readonly record struct FormControlInteractionPlan(
    FormControlGesture Gesture,
    int ListItemIndex);

public enum FormControlTriangleDirection
{
    Up,
    Down,
    Left,
    Right,
}

public readonly record struct FormControlButtonPairLayout(
    LayoutRect FirstButton,
    LayoutRect SecondButton,
    FormControlTriangleDirection FirstDirection,
    FormControlTriangleDirection SecondDirection);

public readonly record struct FormControlGroupBoxLayout(
    LayoutRect Frame,
    LayoutRect Caption);

public readonly record struct FormControlTriangleLayout(
    LayoutPoint First,
    LayoutPoint Second,
    LayoutPoint Third);

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

    public static LayoutRect GetGlyphRect(LayoutRect rect, double maximumSize)
    {
        var size = Math.Min(maximumSize, Math.Min(rect.Width, rect.Height));
        var top = rect.Top + Math.Max(0, (rect.Height - size) / 2);
        return new LayoutRect(rect.Left + 1, top, size, size);
    }

    public static FormControlButtonPairLayout GetSpinnerButtonLayout(
        LayoutRect rect,
        double maximumButtonWidth)
    {
        var width = Math.Max(8, Math.Min(rect.Width, maximumButtonWidth));
        var half = rect.Height / 2;
        return new FormControlButtonPairLayout(
            new LayoutRect(rect.Left, rect.Top, width, half),
            new LayoutRect(rect.Left, rect.Top + half, width, rect.Height - half),
            FormControlTriangleDirection.Up,
            FormControlTriangleDirection.Down);
    }

    public static FormControlButtonPairLayout GetScrollBarButtonLayout(LayoutRect rect)
    {
        if (rect.Width >= rect.Height)
        {
            var size = Math.Min(rect.Height, rect.Width / 2);
            return new FormControlButtonPairLayout(
                new LayoutRect(rect.Left, rect.Top, size, rect.Height),
                new LayoutRect(rect.Right - size, rect.Top, size, rect.Height),
                FormControlTriangleDirection.Left,
                FormControlTriangleDirection.Right);
        }

        var verticalSize = Math.Min(rect.Width, rect.Height / 2);
        return new FormControlButtonPairLayout(
            new LayoutRect(rect.Left, rect.Top, rect.Width, verticalSize),
            new LayoutRect(rect.Left, rect.Bottom - verticalSize, rect.Width, verticalSize),
            FormControlTriangleDirection.Up,
            FormControlTriangleDirection.Down);
    }

    public static FormControlGroupBoxLayout GetGroupBoxLayout(LayoutRect rect, double captionHeight) =>
        new(
            new LayoutRect(rect.Left + 1, rect.Top + 7, Math.Max(1, rect.Width - 2), Math.Max(1, rect.Height - 8)),
            new LayoutRect(rect.Left, rect.Top, rect.Width, captionHeight));

    public static IReadOnlyList<double> GetListRowSeparatorYCoordinates(LayoutRect rect)
    {
        var separators = new List<double>();
        for (var y = rect.Top + ListItemRowHeight; y < rect.Bottom - 1; y += ListItemRowHeight)
            separators.Add(y);
        return separators;
    }

    public static FormControlTriangleLayout GetTriangleLayout(
        LayoutRect rect,
        FormControlTriangleDirection direction)
    {
        var centerX = rect.Left + rect.Width / 2;
        var centerY = rect.Top + rect.Height / 2;
        var size = Math.Max(2, Math.Min(rect.Width, rect.Height) * 0.3);

        return direction switch
        {
            FormControlTriangleDirection.Left => new(
                new LayoutPoint(centerX - size, centerY),
                new LayoutPoint(centerX + size, centerY - size),
                new LayoutPoint(centerX + size, centerY + size)),
            FormControlTriangleDirection.Right => new(
                new LayoutPoint(centerX + size, centerY),
                new LayoutPoint(centerX - size, centerY - size),
                new LayoutPoint(centerX - size, centerY + size)),
            FormControlTriangleDirection.Up => new(
                new LayoutPoint(centerX, centerY - size),
                new LayoutPoint(centerX - size, centerY + size),
                new LayoutPoint(centerX + size, centerY + size)),
            _ => new(
                new LayoutPoint(centerX, centerY + size),
                new LayoutPoint(centerX - size, centerY - size),
                new LayoutPoint(centerX + size, centerY - size)),
        };
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
        var layout = GetScrollBarButtonLayout(rect);
        return Contains(layout.FirstButton, position)
            ? FormControlGesture.StepUp
            : FormControlGesture.StepDown;
    }

    private static bool Contains(LayoutRect rect, LayoutPoint point) =>
        point.X >= rect.Left &&
        point.X <= rect.Right &&
        point.Y >= rect.Top &&
        point.Y <= rect.Bottom;
}
