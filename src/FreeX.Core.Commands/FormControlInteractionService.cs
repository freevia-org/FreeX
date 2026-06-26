using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// State-transition logic for legacy Excel form controls: maps a user gesture (click, step,
/// select) to the correct model mutation and linked-cell write, and wraps the whole thing in
/// an undoable <see cref="EditCellsCommand"/> so dependent formulas recalculate and the undo
/// stack captures the change.
///
/// Both the WPF and the Avalonia frontends call the static helpers here; neither renderer
/// contains the business logic itself.
/// </summary>
public static class FormControlInteractionService
{
    // ── CheckBox ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Toggles the <see cref="FormControlModel.IsChecked"/> state of a checkbox, then writes
    /// <c>TRUE</c> / <c>FALSE</c> into <see cref="FormControlModel.LinkedCell"/> via an
    /// undoable command.  Returns <see langword="null"/> when there is nothing to do (no linked
    /// cell, or the control has no sheet context).
    /// </summary>
    public static EditCellsCommand? CreateToggleCheckBoxCommand(
        FormControlModel control,
        SheetId sheetId,
        Workbook workbook)
    {
        // Flip the in-model state immediately so re-renders during the current frame look correct.
        control.IsChecked = !control.IsChecked;

        if (!TryResolveLinkedCell(control.LinkedCell, sheetId, workbook, out var address))
            return null;

        var value = control.IsChecked ? new BoolValue(true) : new BoolValue(false);
        return EditCellsCommand.ForValue(address.Sheet, address, value);
    }

    // ── OptionButton ──────────────────────────────────────────────────────────

    /// <summary>
    /// Selects an option button inside a group, clears all sibling option buttons that share the
    /// same <see cref="FormControlModel.LinkedCell"/> (Excel's group-boundary signal) or the same
    /// <see cref="FormControlModel.Anchor"/> groupbox, and writes the clicked button's 1-based
    /// index (its position among group siblings) into the linked cell.
    ///
    /// <para>Siblings are all <see cref="FormControlKind.OptionButton"/> controls on
    /// <paramref name="allSheetControls"/> that share the same linked-cell address, which is how
    /// Excel groups radio buttons — they all point to the same cell and the selected one writes
    /// its 1-based position (1, 2, 3 …) into that cell.</para>
    /// </summary>
    public static EditCellsCommand? CreateSelectOptionButtonCommand(
        FormControlModel clicked,
        IReadOnlyList<FormControlModel> allSheetControls,
        SheetId sheetId,
        Workbook workbook)
    {
        if (!TryResolveLinkedCell(clicked.LinkedCell, sheetId, workbook, out var linkedAddress))
        {
            // Still update model state even without linked cell
            ClearGroup(clicked, allSheetControls);
            clicked.IsChecked = true;
            return null;
        }

        // Collect the sibling group: all OptionButtons that share this linked cell address.
        var group = CollectOptionButtonGroup(linkedAddress, allSheetControls, sheetId, workbook);

        // Figure out 1-based index of the clicked button within the group.
        var index = 0;
        for (var i = 0; i < group.Count; i++)
        {
            if (ReferenceEquals(group[i], clicked))
            {
                index = i + 1;
                break;
            }
        }

        if (index == 0)
        {
            // Fallback: not found in group; treat as index 1
            index = 1;
        }

        // Update model state for all group members
        foreach (var btn in group)
            btn.IsChecked = ReferenceEquals(btn, clicked);

        return EditCellsCommand.ForValue(linkedAddress.Sheet, linkedAddress, new NumberValue(index));
    }

    // ── Spinner / ScrollBar ───────────────────────────────────────────────────

    /// <summary>
    /// Steps a spinner or scroll-bar by <paramref name="delta"/> increments, clamping the result
    /// to [<see cref="FormControlModel.Min"/>, <see cref="FormControlModel.Max"/>], and writes the
    /// new integer value to the linked cell.
    ///
    /// <para>For example, a spinner with Min=1, Max=10, Increment=1, Value=5 stepped by +1 writes 6
    /// to the linked cell; stepped past 10 it stays at 10 (clamped).</para>
    /// </summary>
    public static EditCellsCommand? CreateStepCommand(
        FormControlModel control,
        int delta,
        SheetId sheetId,
        Workbook workbook)
    {
        var current = control.Value ?? 0;
        var increment = Math.Max(1, control.Increment ?? 1);
        var min = control.Min ?? 0;
        var max = control.Max ?? 30000;

        var newValue = Math.Clamp(current + delta * increment, min, max);
        control.Value = newValue;

        if (!TryResolveLinkedCell(control.LinkedCell, sheetId, workbook, out var address))
            return null;

        return EditCellsCommand.ForValue(address.Sheet, address, new NumberValue(newValue));
    }

    // ── DropDown / ListBox ────────────────────────────────────────────────────

    /// <summary>
    /// Selects item at 1-based <paramref name="oneBasedIndex"/> in a drop-down or list-box,
    /// updates <see cref="FormControlModel.SelectedIndex"/>, and writes the index into the linked
    /// cell (matching Excel's behavior: it stores the 1-based selection index, not the item text).
    /// </summary>
    public static EditCellsCommand? CreateSelectListItemCommand(
        FormControlModel control,
        int oneBasedIndex,
        SheetId sheetId,
        Workbook workbook)
    {
        control.SelectedIndex = oneBasedIndex;

        if (!TryResolveLinkedCell(control.LinkedCell, sheetId, workbook, out var address))
            return null;

        return EditCellsCommand.ForValue(address.Sheet, address, new NumberValue(oneBasedIndex));
    }

    // ── Linked-cell resolution ────────────────────────────────────────────────

    /// <summary>
    /// Resolves a <c>LinkedCell</c> reference string into a <see cref="CellAddress"/>.
    /// Handles:
    /// <list type="bullet">
    ///   <item><c>$A$1</c> — absolute same-sheet with dollar signs</item>
    ///   <item><c>A1</c> — relative same-sheet</item>
    ///   <item><c>Sheet2!$A$1</c> — cross-sheet with or without dollars</item>
    ///   <item><c>'My Sheet'!A1</c> — quoted sheet name</item>
    /// </list>
    /// Returns <see langword="false"/> and sets <paramref name="address"/> to
    /// <c>default</c> when the string is empty, malformed, or the sheet cannot be found.
    /// </summary>
    public static bool TryResolveLinkedCell(
        string? linkedCell,
        SheetId fallbackSheetId,
        Workbook workbook,
        out CellAddress address)
    {
        address = default;

        if (string.IsNullOrWhiteSpace(linkedCell))
            return false;

        var raw = linkedCell.Trim();

        // Strip leading '=' if present (some XLSX files emit "=$A$1")
        if (raw.StartsWith('='))
            raw = raw[1..].Trim();

        // Strip dollar signs from column/row identifiers (but not sheet separators)
        // e.g. "Sheet1!$A$1" → "Sheet1!A1", "$B$3" → "B3"
        // We handle the sheet separator first, then strip $ from the cell part.
        var bangIdx = raw.IndexOf('!');
        string cellPart;
        SheetId sheetId;

        if (bangIdx >= 0)
        {
            var sheetPart = raw[..bangIdx].Trim().Trim('\'');
            cellPart = raw[(bangIdx + 1)..].Trim().Replace("$", string.Empty, StringComparison.Ordinal);

            var found = workbook.GetSheet(sheetPart);
            if (found is null)
                return false;

            sheetId = found.Id;
        }
        else
        {
            cellPart = raw.Replace("$", string.Empty, StringComparison.Ordinal);
            sheetId = fallbackSheetId;
        }

        return CellAddress.TryParse(cellPart, sheetId, out address);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static List<FormControlModel> CollectOptionButtonGroup(
        CellAddress linkedAddress,
        IReadOnlyList<FormControlModel> allControls,
        SheetId fallbackSheetId,
        Workbook workbook)
    {
        var group = new List<FormControlModel>();

        foreach (var c in allControls)
        {
            if (c.Kind != FormControlKind.OptionButton)
                continue;

            if (!TryResolveLinkedCell(c.LinkedCell, fallbackSheetId, workbook, out var addr))
                continue;

            if (addr == linkedAddress)
                group.Add(c);
        }

        return group;
    }

    private static void ClearGroup(FormControlModel clicked, IReadOnlyList<FormControlModel> allControls)
    {
        // Fallback when no linked cell: clear all OptionButtons with no linked cell
        foreach (var c in allControls)
        {
            if (c.Kind == FormControlKind.OptionButton && string.IsNullOrWhiteSpace(c.LinkedCell))
                c.IsChecked = false;
        }

        clicked.IsChecked = true;
    }
}
