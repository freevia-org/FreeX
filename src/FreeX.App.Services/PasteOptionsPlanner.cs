using FreeX.Core.Model;

namespace FreeX.App.Services;

/// <summary>One row of the Paste Options menu: a group heading or a paste variant (a Home ▸ Paste command id).</summary>
public sealed record PasteOptionsMenuItem(string Header, string? RibbonCommandId, bool IsGroupHeading = false);

/// <summary>
/// Excel's Paste Options button: the "(Ctrl)" button left at the bottom-right of a pasted range,
/// whose menu re-does the paste another way. Each option is the matching Home ▸ Paste command,
/// run after undoing the paste it replaces.
/// </summary>
public static class PasteOptionsPlanner
{
    public const string ButtonAutomationId = "PasteOptionsButton";
    public const string ButtonText = "(Ctrl)";
    public const string ButtonToolTip = "Paste Options";
    public const double ButtonHeight = 20;

    public static IReadOnlyList<PasteOptionsMenuItem> Items { get; } =
    [
        new("Paste", null, IsGroupHeading: true),
        new("Paste", "Paste"),
        new("Formulas", "Paste Formulas"),
        new("Keep Source Column Widths", "Keep Source Column Widths"),
        new("Transpose", "Transpose Paste"),
        new("Paste Values", null, IsGroupHeading: true),
        new("Values", "Paste Values"),
        new("Values & Source Formatting", "Values & Source Formatting"),
        new("Other Paste Options", null, IsGroupHeading: true),
        new("Formatting", "Paste Formatting"),
        new("Paste Link", "Paste Link"),
        new("Picture", "Picture"),
        new("Linked Picture", "Linked Picture"),
    ];

    /// <summary>Whether an undo entry is a paste of cells (pictures, charts and shapes get no Paste Options).</summary>
    public static bool IsCellPasteLabel(string? label) =>
        label is not null &&
        label.StartsWith("Paste", StringComparison.Ordinal) &&
        !label.StartsWith("Paste Picture", StringComparison.Ordinal) &&
        !label.StartsWith("Paste Chart", StringComparison.Ordinal) &&
        !label.StartsWith("Paste Shape", StringComparison.Ordinal) &&
        !label.StartsWith("Paste Text Box", StringComparison.Ordinal);
}

/// <summary>
/// Follows the undo history to decide when the Paste Options button shows: right after a paste of
/// cells, until anything else is done (an edit, a command, an undo) or the user dismisses it.
/// Selecting other cells leaves it in place, as in Excel.
/// </summary>
public sealed class PasteOptionsTracker
{
    private int _seenUndoDepth = -1;
    private long _seenUndoVersion = -1;

    /// <summary>The pasted range the button belongs to, or null when no button shows.</summary>
    public GridRange? Range { get; private set; }

    /// <summary>Call after every refresh with the session and the current selection.</summary>
    public GridRange? Update(WorkbookSession session, GridRange selection)
    {
        ArgumentNullException.ThrowIfNull(session);
        var depth = session.UndoStackDepth;
        var version = session.UndoStackVersion;
        if (depth == _seenUndoDepth && version == _seenUndoVersion)
            return Range;

        // A new top entry without the stack shrinking: a command ran (a capped stack keeps its
        // depth). Undo shrinks it, so undoing back onto an older paste shows no button.
        var pushed = _seenUndoDepth >= 0 && version != _seenUndoVersion && depth >= _seenUndoDepth && depth > 0;
        _seenUndoDepth = depth;
        _seenUndoVersion = version;
        Range = pushed &&
                (version == session.LastPasteUndoVersion ||
                 PasteOptionsPlanner.IsCellPasteLabel(session.GetUndoHistory(1).FirstOrDefault()?.Label))
            ? selection
            : null;
        return Range;
    }

    /// <summary>Esc, or choosing an option (the replacement paste shows a fresh button).</summary>
    public void Dismiss() => Range = null;
}
