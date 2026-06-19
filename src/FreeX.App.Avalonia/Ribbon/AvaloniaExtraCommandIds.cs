using System.Collections.Generic;

namespace FreeX.App.Avalonia.Ribbon;

/// <summary>
/// The canonical (shared-definition) command ids the Avalonia shell wires by their RAW canonical id — i.e.
/// directly as <c>ExtraCommands</c> dictionary keys in <c>MainWindow</c>, rather than through the dotted
/// <see cref="AvaloniaCommandIdAdapter"/> handler ids. These are mostly split-button / dropdown menu items
/// (Sum/Find/Replace/Clear-*, fill/clear directions, sheet ops, zoom presets, conditional-format presets,
/// theme submenu items, …) whose canonical id IS the descriptive label the shared definition emits, so no
/// dotted alias is needed.
///
/// This is the single source of truth for the raw-canonical wirings: it lets the functional parity matrix
/// account for the full Avalonia binding surface (adapter ids ∪ these ∪ the cell-style gallery presets)
/// without instantiating the UI. A source-hygiene guard test asserts this set stays in lock-step with the
/// literal keys present in the MainWindow source, so it can never silently drift.
/// </summary>
internal static class AvaloniaExtraCommandIds
{
    public static readonly IReadOnlySet<string> RawCanonical = new HashSet<string>(System.StringComparer.Ordinal)
    {
        // ── Number / Fill / Borders quick-format menu items ───────────────────────────────────────────
        "Bottom Border", "Bottom Double Border", "Inside Borders", "Left Border", "Right Border",
        "Top Border", "Top and Bottom Border", "Top and Double Bottom Border", "Top and Thick Bottom Border",
        "Thick Bottom Border", "Thick Outside Borders", "More Borders", "Draw Border Grid", "Erase Border",
        "More Accounting Formats",
        // ── Alignment ▸ Orientation submenu ───────────────────────────────────────────────────────────
        "Angle Clockwise", "Angle Counterclockwise", "Horizontal", "Vertical Text",
        "Rotate Text Up", "Rotate Text Down",
        // ── Clipboard ▸ Paste split-button menu items ─────────────────────────────────────────────────
        "Paste Formulas", "Transpose Paste", "Picture", "Linked Picture",
        // ── Editing ▸ AutoSum / Fill / Clear / Find & Select menu items ───────────────────────────────
        "Sum", "Average", "Count Numbers", "Count All", "Max", "Min", "More Functions",
        "Down", "Right", "Up", "Left", "Series",
        "Clear All", "Clear Formats", "Clear Contents", "Clear Comments and Notes", "Clear Hyperlinks",
        "Find", "Replace", "Go To", "Go To Special", "Formulas", "Notes", "Constants",
        "Data Validation", "Select Objects", "Selection Pane",
        // ── Cells ▸ Insert / Delete / Format menu items ───────────────────────────────────────────────
        "Insert Cells", "Insert Sheet", "Insert Sheet Rows", "Insert Sheet Columns",
        "Delete Cells", "Delete Sheet", "Delete Sheet Rows", "Delete Sheet Columns",
        "Row Height", "Column Width", "AutoFit Row Height", "AutoFit Column Width",
        "Hide Rows", "Hide Columns", "Hide Sheet", "Unhide Rows", "Unhide Columns", "Unhide Sheet",
        "Rename Sheet", "Tab Color", "Lock Cell", "Format Cells", "Protect Sheet",
        // ── Styles ▸ Conditional Formatting preset / rule menu items ──────────────────────────────────
        "Greater Than", "Less Than", "Between", "Equal To", "Text that Contains", "A Date Occurring",
        "Duplicate Values", "Top 10 Items", "Top 10%", "Bottom 10 Items", "Bottom 10%", "Above Average",
        "Below Average", "Data Bars", "Color Scales", "New Rule", "New Formula Rule", "Clear Rules",
        "Manage Rules", "More Rules",
        // Icon-set gallery items.
        "3 Arrows", "3 Arrows (Gray)", "3 Flags", "3 Signs", "3 Symbols", "3 Symbols (Uncircled)",
        "3 Traffic Lights", "3 Traffic Lights (Rimmed)", "4 Arrows", "4 Arrows (Gray)", "4 Ratings",
        "4 Red To Black", "4 Traffic Lights", "5 Arrows", "5 Arrows (Gray)", "5 Boxes", "5 Quarters",
        "5 Ratings",
        // ── Formulas tab menu items ───────────────────────────────────────────────────────────────────
        "Use in Formula", "Automatic", "Automatic Except Data Tables", "Manual", "Calculate Sheet",
        "Watch Window", "Error Checking Options", "Remove Precedent Arrows", "Remove Dependent Arrows",
        "Group#GroupRowsMenuItem_Click", "Ungroup#UngroupRowsMenuItem_Click",
        // ── Data tab menu items ───────────────────────────────────────────────────────────────────────
        "Sort", "Sort A to Z", "Sort Z to A", "Filter", "Custom Sort", "Goal Seek", "Scenario Manager",
        "Data Table", "Show Detail", "Hide Detail", "Clear Outline",
        // ── Review tab menu items ─────────────────────────────────────────────────────────────────────
        "Allow Users to Edit Ranges", "Show Comments", "Next Comment", "Previous Comment",
        "Delete Note", "Edit Note", "Workbook Statistics", "Share",
        // ── View tab menu items ───────────────────────────────────────────────────────────────────────
        "Ruler", "Switch Windows", "Reset Window Position", "Custom Views",
        "Freeze Panes#FreezeAtSelectionMenuItem_Click", "Freeze Top Row", "Freeze First Column",
        "Unfreeze Panes", "Tiled", "Cascade", "Vertical", "Horizontal#ArrangeAllMenuItem_Click",
        "200%", "100%#ZoomPresetMenuItem_Click", "75%", "50%", "25%",
        // ── Page Layout menu items ────────────────────────────────────────────────────────────────────
        "Custom Margins", "Narrow", "Wide", "Normal", "Portrait", "Landscape",
        "Set Print Area", "Clear Print Area", "Choose Background", "Delete Background",
        // Paper sizes the Avalonia shell wires individually.
        "Letter", "Legal", "Executive", "Statement", "Tabloid", "A3", "A4", "A5", "B4", "B5",
    };
}
