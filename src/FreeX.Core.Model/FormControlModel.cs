namespace FreeX.Core.Model;

/// <summary>
/// The kind of legacy Excel form control (the controls under the Developer tab's
/// "Form Controls" group, stored as VML shapes + ctrlProps in the XLSX package).
/// </summary>
public enum FormControlKind
{
    /// <summary>An unrecognized or not-yet-modeled control type. The native XML is still preserved.</summary>
    Unknown,
    Button,
    CheckBox,
    OptionButton,
    DropDown,
    ListBox,
    GroupBox,
    Label,
    ScrollBar,
    Spinner,
}

/// <summary>
/// A legacy Excel form control loaded from an XLSX worksheet. Captures the modeled state
/// (type, anchor, checked/value/min/max, linked cell) so the control is no longer silently
/// dropped on load. Round-trip preservation of the underlying VML/ctrlProps package parts is
/// handled separately so unmodeled attributes are not lost.
/// </summary>
public sealed class FormControlModel
{
    /// <summary>Modeled control kind.</summary>
    public FormControlKind Kind { get; set; } = FormControlKind.Unknown;

    /// <summary>The control shape name (e.g. "Check Box 1"), when present.</summary>
    public string? Name { get; set; }

    /// <summary>The drawing shape id from the worksheet control element.</summary>
    public uint? ShapeId { get; set; }

    /// <summary>The cell anchor range the control is positioned over (from/to anchor cells).</summary>
    public GridRange? Anchor { get; set; }

    /// <summary>
    /// The control's sub-cell anchor offsets (per-cell <c>colOff</c>/<c>rowOff</c> in EMU), preserved
    /// from the worksheet <c>controlPr/anchor</c> or the VML <c>x:ClientData/x:Anchor</c>. Mirrors how
    /// pictures/slicers carry a <see cref="DrawingAnchorRange"/> so the control rect reflects the true
    /// sub-cell position+size rather than snapping to whole-cell spans. The anchor's cell columns/rows
    /// are 0-based (matching <see cref="DrawingAnchorRange"/>); <see langword="null"/> when no offsets
    /// were recoverable, in which case the render falls back to a whole-cell span over <see cref="Anchor"/>.
    /// </summary>
    public DrawingAnchorRange? AnchorOffsets { get; set; }

    /// <summary>
    /// The worksheet cell the control is linked to (its result/state is mirrored there).
    /// May be an A1 reference, a defined-name, or a cross-sheet reference. Preserved verbatim.
    /// </summary>
    public string? LinkedCell { get; set; }

    /// <summary>For list-style controls, the source range whose items populate the list. Preserved verbatim.</summary>
    public string? ListFillRange { get; set; }

    /// <summary>For checkboxes and option buttons: whether the control is checked.</summary>
    public bool IsChecked { get; set; }

    /// <summary>For spinner/scrollbar controls: the current value.</summary>
    public int? Value { get; set; }

    /// <summary>For spinner/scrollbar controls: the minimum value.</summary>
    public int? Min { get; set; }

    /// <summary>For spinner/scrollbar controls: the maximum value.</summary>
    public int? Max { get; set; }

    /// <summary>For spinner/scrollbar controls: the incremental step.</summary>
    public int? Increment { get; set; }

    /// <summary>For scrollbar controls: the page-change amount.</summary>
    public int? PageChange { get; set; }

    /// <summary>For list/dropdown controls: the selected item index (1-based, Excel <c>sel</c>).</summary>
    public int? SelectedIndex { get; set; }
}
