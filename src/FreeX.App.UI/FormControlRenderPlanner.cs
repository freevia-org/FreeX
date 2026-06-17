using FreeX.Core.Model;

namespace FreeX.App.UI;

/// <summary>
/// Pure layout/state helpers for rendering legacy Excel form controls
/// (<see cref="FormControlModel"/>) as static chrome on the GridView. Maps a control's
/// 1-based cell-range <see cref="FormControlModel.Anchor"/> to the 0-based
/// <see cref="DrawingAnchorRange"/> understood by <see cref="GridDrawingObjectPlanner"/>,
/// decides which kinds get drawn, and resolves the caption text.
/// </summary>
internal static class FormControlRenderPlanner
{
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
    /// Whether this control kind has a static-chrome renderer. Interactive-only or
    /// not-yet-drawn kinds (Button/DropDown/ListBox/Unknown) return false.
    /// </summary>
    public static bool IsRenderable(FormControlKind kind) =>
        kind is FormControlKind.CheckBox
            or FormControlKind.OptionButton
            or FormControlKind.Spinner
            or FormControlKind.ScrollBar
            or FormControlKind.Label
            or FormControlKind.GroupBox;

    /// <summary>
    /// Resolves the caption text drawn next to / inside the control. Prefers the control's
    /// authored name, falling back to a friendly kind label (e.g. "Check Box").
    /// </summary>
    public static string GetCaption(FormControlModel control)
    {
        if (!string.IsNullOrWhiteSpace(control.Name))
            return control.Name.Trim();

        return control.Kind switch
        {
            FormControlKind.CheckBox => "Check Box",
            FormControlKind.OptionButton => "Option Button",
            FormControlKind.Spinner => "Spinner",
            FormControlKind.ScrollBar => "Scroll Bar",
            FormControlKind.Label => "Label",
            FormControlKind.GroupBox => "Group Box",
            FormControlKind.Button => "Button",
            FormControlKind.DropDown => "Drop Down",
            FormControlKind.ListBox => "List Box",
            _ => "Control"
        };
    }
}
