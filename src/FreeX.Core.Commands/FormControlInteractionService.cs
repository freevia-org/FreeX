using System.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>Host-neutral form-control gesture after native event translation.</summary>
public enum FormControlGesture
{
    Body,
    StepUp,
    StepDown,
}

/// <summary>Normalized request sent by either host to the shared form-control dispatcher.</summary>
public readonly record struct FormControlInteractionRequest(
    FormControlModel Control,
    FormControlGesture Gesture,
    int ListItemIndex = 0);

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
    /// <summary>
    /// Dispatches a normalized host gesture to the shared form-control command factory. Native
    /// hosts retain responsibility for translating their pointer/mouse event into the gesture
    /// and list-item index before calling this method.
    /// </summary>
    public static IWorkbookCommand? CreateCommand(
        FormControlInteractionRequest request,
        IReadOnlyList<FormControlModel> controls,
        SheetId sheetId,
        Workbook workbook)
    {
        ArgumentNullException.ThrowIfNull(request.Control);
        ArgumentNullException.ThrowIfNull(controls);
        ArgumentNullException.ThrowIfNull(workbook);

        return request.Control.Kind switch
        {
            FormControlKind.CheckBox =>
                CreateToggleCheckBoxCommand(request.Control, sheetId, workbook),
            FormControlKind.OptionButton =>
                CreateSelectOptionButtonCommand(request.Control, controls, sheetId, workbook),
            FormControlKind.Spinner =>
                CreateStepCommand(
                    request.Control,
                    request.Gesture == FormControlGesture.StepUp ? +1 : -1,
                    sheetId,
                    workbook),
            FormControlKind.ScrollBar =>
                CreateStepCommand(
                    request.Control,
                    request.Gesture == FormControlGesture.StepUp ? -1 : +1,
                    sheetId,
                    workbook),
            FormControlKind.ListBox =>
                request.ListItemIndex > 0
                    ? CreateSelectListItemCommand(
                        request.Control,
                        request.ListItemIndex,
                        sheetId,
                        workbook)
                    : null,
            FormControlKind.DropDown =>
                CreateAdvanceListSelectionCommand(request.Control, sheetId, workbook),
            FormControlKind.Button or FormControlKind.GroupBox or FormControlKind.Label or _ => null,
        };
    }

    // ── Cell → Control sync ──────────────────────────────────────────────────

    /// <summary>
    /// Re-derives every form control's in-model state (IsChecked / Value / SelectedIndex) from its
    /// <see cref="FormControlModel.LinkedCell"/>'s CURRENT cell value, so a linked cell edited
    /// directly (typed over, or recalculated by a formula) without clicking the control is still
    /// reflected the next time the control renders — matching Excel, where a form control always
    /// mirrors its linked cell's live value regardless of how that cell changed.
    ///
    /// <para>Call this from each shell's render/refresh hook (the same place
    /// <see cref="FormControlListResolver.PopulateSelectedText"/> is called) so the sync runs on
    /// every viewport refresh, not just after a control click.</para>
    /// </summary>
    public static void SyncControlsFromLinkedCells(Sheet sheet, Workbook workbook)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(workbook);

        foreach (var control in sheet.FormControls)
        {
            switch (control.Kind)
            {
                case FormControlKind.CheckBox:
                    if (TryResolveLinkedCell(control.LinkedCell, sheet.Id, workbook, out var checkBoxAddress))
                        control.IsChecked = IsTruthy(workbook.GetSheet(checkBoxAddress.Sheet)?.GetCell(checkBoxAddress)?.Value);
                    break;

                case FormControlKind.OptionButton:
                    SyncOptionButtonGroup(control, sheet, workbook);
                    break;

                case FormControlKind.Spinner:
                case FormControlKind.ScrollBar:
                    if (TryResolveLinkedCell(control.LinkedCell, sheet.Id, workbook, out var stepAddress) &&
                        workbook.GetSheet(stepAddress.Sheet)?.GetCell(stepAddress)?.Value is NumberValue stepValue)
                    {
                        var min = control.Min ?? 0;
                        var max = control.Max ?? 30000;
                        control.Value = ClampToRange((int)Math.Round(stepValue.Value), min, max);
                    }
                    break;

                case FormControlKind.ListBox:
                case FormControlKind.DropDown:
                    if (TryResolveLinkedCell(control.LinkedCell, sheet.Id, workbook, out var listAddress) &&
                        workbook.GetSheet(listAddress.Sheet)?.GetCell(listAddress)?.Value is NumberValue selValue)
                    {
                        control.SelectedIndex = (int)Math.Round(selValue.Value);
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Re-derives an option button's IsChecked from its group's shared linked cell: the button
    /// whose 1-based position within the group equals the cell's current numeric value is checked;
    /// every other group member is cleared. No-ops when the control has no resolvable linked cell.
    /// </summary>
    private static void SyncOptionButtonGroup(FormControlModel control, Sheet sheet, Workbook workbook)
    {
        if (!TryResolveLinkedCell(control.LinkedCell, sheet.Id, workbook, out var linkedAddress))
            return;

        var cellValue = workbook.GetSheet(linkedAddress.Sheet)?.GetCell(linkedAddress)?.Value;
        if (cellValue is not NumberValue numberValue)
            return;

        var selectedIndex = (int)Math.Round(numberValue.Value);
        var group = CollectOptionButtonGroup(linkedAddress, sheet.FormControls, sheet.Id, workbook);
        for (var i = 0; i < group.Count; i++)
            group[i].IsChecked = i + 1 == selectedIndex;
    }

    /// <summary>Mirrors Excel's linked-cell truthiness for checkboxes: TRUE, or any non-zero number.</summary>
    private static bool IsTruthy(ScalarValue? value) => value switch
    {
        BoolValue b => b.Value,
        NumberValue n => n.Value != 0,
        _ => false,
    };

    /// <summary>
    /// Clamps <paramref name="value"/> into [<paramref name="min"/>, <paramref name="max"/>], same as
    /// <see cref="Math.Clamp(int, int, int)"/> — except <see cref="Math.Clamp(int, int, int)"/> THROWS
    /// an <see cref="ArgumentException"/> when <paramref name="min"/> exceeds <paramref name="max"/>,
    /// which a malformed spinner/scroll-bar (e.g. an XLSX-loaded control whose Min defaults above an
    /// explicit Max) can trigger on every step/sync. A control with an inverted range has no valid
    /// window to clamp into, so it collapses to the single value <paramref name="min"/>, matching
    /// Excel's own tolerance of a degenerate/reversed range (it never crashes on this input).
    /// </summary>
    private static int ClampToRange(int value, int min, int max) =>
        min > max ? min : Math.Clamp(value, min, max);

    // ── CheckBox ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Toggles the <see cref="FormControlModel.IsChecked"/> state of a checkbox, then writes
    /// <c>TRUE</c> / <c>FALSE</c> into <see cref="FormControlModel.LinkedCell"/> via an
    /// undoable command.  Returns <see langword="null"/> when there is nothing to do (no linked
    /// cell, the control has no sheet context, or the linked cell's sheet is protected against
    /// the write — in the protected case the control's visible state is left untouched, matching
    /// Excel: a rejected write never flips the checkbox).
    /// </summary>
    public static IWorkbookCommand? CreateToggleCheckBoxCommand(
        FormControlModel control,
        SheetId sheetId,
        Workbook workbook)
    {
        // Resolve the linked cell and verify the write is actually allowed BEFORE mutating any
        // in-model control state — otherwise a protection rejection (a normal, non-throwing
        // CommandOutcome failure that CommandBus never reverts) would leave the checkbox flipped
        // with no repair path even though the linked cell never changed.
        //
        // A control with NO linked cell at all has nothing to protect: Excel still flips the
        // checkbox's visible state on click even when it isn't wired to a cell, so that case must
        // fall through to the model mutation below (no command is returned since there's nothing
        // to write). Only an actually-BLOCKED write to a resolvable linked cell must leave the
        // model untouched.
        var hasLinkedCell = TryResolveLinkedCell(control.LinkedCell, sheetId, workbook, out var address);
        if (hasLinkedCell && !CanWriteLinkedCell(workbook, address))
            return null;

        // Snapshot prior state before any mutation so undo can restore it.
        var priorIsChecked = control.IsChecked;
        var priorValue = control.Value;

        // Flip the in-model state immediately so re-renders during the current frame look correct.
        control.IsChecked = !control.IsChecked;

        // R40-meta-1: an explicit user toggle always resolves a tri-state checkbox (Value ==
        // 0/1/2 for Unchecked/Checked/Mixed — see XlsxFormControlMapper.ReadControlProperties) to
        // the concrete 0/1 state matching the NEW IsChecked, clearing any inherited "Mixed" (2).
        // Excel's Mixed state only ever exists BEFORE the user interacts with the control (e.g. a
        // checkbox loaded from a workbook whose ctrlProp carried checked="Mixed"); once the user
        // clicks it, it commits to Checked/Unchecked like any two-state control. Without this reset,
        // a Mixed control that the user checks/unchecks keeps writing checked="Mixed" to the XLSX on
        // every subsequent save (XlsxWorksheetFormControlPreserver.ApplyControlStateToFormControlPr
        // prefers Value==2 over IsChecked — see R39-meta-2), silently undoing the user's click on
        // every save forever. This must run BEFORE Wrap() below, which snapshots control.Value as
        // the "applied" state captured for undo/redo.
        control.Value = control.IsChecked ? 1 : 0;

        // No linked cell → nothing to write; the model flip above is the whole of Excel's
        // behaviour here, so no undoable command is produced.
        if (!hasLinkedCell)
            return null;

        var value = control.IsChecked ? new BoolValue(true) : new BoolValue(false);
        var cellEdit = EditCellsCommand.ForValue(address.Sheet, address, value);
        return FormControlInteractionCommand.Wrap(
            control, cellEdit, "Toggle CheckBox",
            priorIsChecked, priorValue, control.SelectedIndex);
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
    public static IWorkbookCommand? CreateSelectOptionButtonCommand(
        FormControlModel clicked,
        IReadOnlyList<FormControlModel> allSheetControls,
        SheetId sheetId,
        Workbook workbook)
    {
        if (!TryResolveLinkedCell(clicked.LinkedCell, sheetId, workbook, out var linkedAddress))
        {
            // Still update model state even without linked cell (no undoable command is returned,
            // matching the existing no-linked-cell contract). Scope the clear to the clicked button's
            // own GroupBox (Excel's grouping signal when there is no linked cell), falling back to a
            // sheet-wide default group only when the button sits in no GroupBox at all.
            var unlinkedGroup = CollectUnlinkedOptionButtonGroup(clicked, allSheetControls);

            foreach (var btn in unlinkedGroup)
                btn.IsChecked = ReferenceEquals(btn, clicked);
            clicked.IsChecked = true;

            return null;
        }

        // Verify the write is actually allowed BEFORE mutating any group member's IsChecked —
        // otherwise a protection rejection would leave the whole group's visible selection changed
        // with no repair path even though the linked cell never changed.
        if (!CanWriteLinkedCell(workbook, linkedAddress))
            return null;

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

        // Snapshot prior state of every group member (not just the clicked one) so undo can restore
        // the WHOLE group — a sibling that was checked before this click and gets cleared below must
        // come back on Revert, not just the clicked button's own prior state.
        var priorGroupChecked = group.ToDictionary(c => c, c => c.IsChecked);

        // Update model state for all group members
        foreach (var btn in group)
            btn.IsChecked = ReferenceEquals(btn, clicked);

        var cellEdit = EditCellsCommand.ForValue(linkedAddress.Sheet, linkedAddress, new NumberValue(index));
        return FormControlInteractionCommand.WrapGroupSelection(
            clicked, cellEdit, "Select Option Button", priorGroupChecked, clicked.Value, clicked.SelectedIndex);
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
    public static IWorkbookCommand? CreateStepCommand(
        FormControlModel control,
        int delta,
        SheetId sheetId,
        Workbook workbook)
    {
        var increment = Math.Max(1, control.Increment ?? 1);
        var min = control.Min ?? 0;
        var max = control.Max ?? 30000;

        // NN4: prefer the linked cell's current numeric value as the step base so that
        // externally-set cell values (via formula or direct edit) are honoured, matching Excel.
        // Fall back to control.Value only when the linked cell is absent or non-numeric.
        var current = control.Value ?? 0;
        if (TryResolveLinkedCell(control.LinkedCell, sheetId, workbook, out var address))
        {
            var sheet = workbook.GetSheet(address.Sheet);
            var cell = sheet?.GetCell(address);
            if (cell?.Value is NumberValue nv)
                current = (int)Math.Round(nv.Value);
        }

        var hasLinkedCell = TryResolveLinkedCell(control.LinkedCell, sheetId, workbook, out address);

        // Verify the write is actually allowed BEFORE mutating control.Value — otherwise a
        // protection rejection would leave the spinner/scroll-bar showing the stepped value with
        // no repair path even though the linked cell never changed. A control with NO linked cell
        // has nothing to protect (nothing is written), so it must still step below — only an
        // actually-BLOCKED write to a resolvable linked cell skips the mutation entirely.
        if (hasLinkedCell && !CanWriteLinkedCell(workbook, address))
            return null;

        var priorValue = control.Value;
        var newValue = ClampToRange(current + delta * increment, min, max);
        control.Value = newValue;

        // No linked cell → nothing to write; the model step above is the whole of Excel's
        // behaviour here, so no undoable command is produced.
        if (!hasLinkedCell)
            return null;

        var cellEdit = EditCellsCommand.ForValue(address.Sheet, address, new NumberValue(newValue));
        return FormControlInteractionCommand.Wrap(
            control, cellEdit, "Step Spinner",
            control.IsChecked, priorValue, control.SelectedIndex);
    }

    // ── DropDown / ListBox ────────────────────────────────────────────────────

    /// <summary>
    /// Selects item at 1-based <paramref name="oneBasedIndex"/> in a drop-down or list-box,
    /// updates <see cref="FormControlModel.SelectedIndex"/>, and writes the index into the linked
    /// cell (matching Excel's behavior: it stores the 1-based selection index, not the item text).
    /// </summary>
    public static IWorkbookCommand? CreateSelectListItemCommand(
        FormControlModel control,
        int oneBasedIndex,
        SheetId sheetId,
        Workbook workbook)
    {
        // NN3: clamp the index to [1, itemCount] so clicking below the last visible item
        // never writes an out-of-range value to the linked cell (mirrors Excel behaviour).
        var itemCount = EstimateListItemCount(control, sheetId, workbook);
        if (itemCount > 0 && oneBasedIndex > itemCount)
            return null; // click is in the empty area below the last item — no-op

        if (oneBasedIndex < 1)
            return null;

        if (!TryResolveLinkedCell(control.LinkedCell, sheetId, workbook, out var address))
            return null;

        // Verify the write is actually allowed BEFORE mutating control.SelectedIndex — otherwise a
        // protection rejection would leave the list/drop-down showing the new selection with no
        // repair path even though the linked cell never changed.
        if (!CanWriteLinkedCell(workbook, address))
            return null;

        var priorSelectedIndex = control.SelectedIndex;
        control.SelectedIndex = oneBasedIndex;

        var cellEdit = EditCellsCommand.ForValue(address.Sheet, address, new NumberValue(oneBasedIndex));
        return FormControlInteractionCommand.Wrap(
            control, cellEdit, "Select List Item",
            control.IsChecked, control.Value, priorSelectedIndex);
    }

    /// <summary>
    /// Advances a drop-down/list-box selection by one item, wrapping to the first item. This is the
    /// shell-neutral fallback used while a native picker is unavailable.
    /// </summary>
    public static IWorkbookCommand? CreateAdvanceListSelectionCommand(
        FormControlModel control,
        SheetId sheetId,
        Workbook workbook)
    {
        var sheet = workbook.GetSheet(sheetId);
        if (sheet is null)
            return null;

        var itemCount = FormControlListResolver.EstimateItemCount(control, sheet, workbook);
        if (itemCount <= 0)
            itemCount = 1;

        var current = control.SelectedIndex ?? 0;
        var next = current >= itemCount ? 1 : current + 1;
        return CreateSelectListItemCommand(control, next, sheetId, workbook);
    }

    /// <summary>
    /// Estimates how many items are in the control's <see cref="FormControlModel.ListFillRange"/>.
    /// Returns 0 when the range cannot be resolved.
    /// </summary>
    public static int EstimateListItemCount(FormControlModel control, SheetId sheetId, Workbook workbook)
    {
        var sheet = workbook.GetSheet(sheetId);
        return sheet is null ? 0 : FormControlListResolver.EstimateItemCount(control, sheet, workbook);
    }

    // ── Linked-cell resolution ────────────────────────────────────────────────

    /// <summary>
    /// Checks — WITHOUT mutating anything — whether a write to <paramref name="address"/> would be
    /// accepted by <see cref="EditCellsCommand.Apply"/>, i.e. mirrors BOTH of its guards: the
    /// protection guard (<see cref="CommandGuards.CanEditCell"/>) AND the legacy-array guard
    /// (<see cref="CommandGuards.RejectIfSplitsArray"/>, which <see cref="EditCellsCommand.Apply"/>
    /// runs unconditionally, independent of sheet protection). Callers must run this BEFORE flipping
    /// any form-control in-model state, so a protected/locked linked cell OR a linked cell that lands
    /// on a legacy Ctrl+Shift+Enter array member (Format Control's "Cell link" field can validly point
    /// anywhere, including inside an existing array's footprint) never lets the control's visible
    /// state (checked/value/selected index) drift from the cell it supposedly reflects — matching
    /// Excel, where a rejected write never changes the control's appearance.
    /// </summary>
    private static bool CanWriteLinkedCell(Workbook workbook, CellAddress address)
    {
        var sheet = workbook.GetSheet(address.Sheet);
        if (sheet is null || !CommandGuards.CanEditCell(workbook, sheet, address))
            return false;

        // Mirrors EditCellsCommand.Apply's allowDynamicSpillMemberWrite: true — a modern dynamic
        // array's spill members (and its anchor) may always be written directly; only a legacy CSE
        // array's member/anchor is rejected when the full declared range isn't part of the write.
        return CommandGuards.RejectIfSplitsArray(sheet, [address], allowDynamicSpillMemberWrite: true) is null;
    }

    /// <summary>
    /// Resolves a <c>LinkedCell</c> reference string into a <see cref="CellAddress"/>.
    /// Handles:
    /// <list type="bullet">
    ///   <item><c>$A$1</c> — absolute same-sheet with dollar signs</item>
    ///   <item><c>A1</c> — relative same-sheet</item>
    ///   <item><c>Sheet2!$A$1</c> — cross-sheet with or without dollars</item>
    ///   <item><c>'My Sheet'!A1</c> — quoted sheet name</item>
    ///   <item><c>MyFlag</c> — a defined name (workbook-scoped or scoped to the control's sheet),
    ///     resolved to the top-left cell of the name's target range, matching Excel where the
    ///     Cell link field of a form control may hold a defined name.</item>
    /// </list>
    /// Returns <see langword="false"/> and sets <paramref name="address"/> to
    /// <c>default</c> when the string is empty, malformed, or the sheet/name cannot be found.
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

        if (CellAddress.TryParse(cellPart, sheetId, out address))
            return true;

        // Not a plain A1 reference — the Cell link field may hold a defined name (Excel allows
        // this). Only attempted for the unqualified (no '!') form, which is how Excel writes it.
        if (bangIdx < 0 && workbook.TryGetNamedRange(cellPart, fallbackSheetId, out var namedRange))
        {
            address = namedRange.Start;
            return true;
        }

        return false;
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

    /// <summary>
    /// Collects the sibling group for an unlinked (no <see cref="FormControlModel.LinkedCell"/>)
    /// option button: every other unlinked <see cref="FormControlKind.OptionButton"/> anchored
    /// inside the same enclosing <see cref="FormControlKind.GroupBox"/> as <paramref name="clicked"/>
    /// — Excel's fallback grouping signal when there is no linked-cell to key off of. When
    /// <paramref name="clicked"/> is not anchored inside any GroupBox, falls back to the sheet-level
    /// default group (every unlinked OptionButton not contained by ANY GroupBox), so independent
    /// GroupBox'd groups on the same sheet never cross-clear each other.
    /// </summary>
    private static List<FormControlModel> CollectUnlinkedOptionButtonGroup(
        FormControlModel clicked,
        IReadOnlyList<FormControlModel> allControls)
    {
        var groupBoxes = allControls
            .Where(c => c.Kind == FormControlKind.GroupBox && c.Anchor is not null)
            .Select(c => c.Anchor!.Value)
            .ToList();

        var enclosingGroupBox = clicked.Anchor is { } clickedAnchor
            ? FindEnclosingGroupBox(clickedAnchor, groupBoxes)
            : null;

        var group = new List<FormControlModel>();
        foreach (var c in allControls)
        {
            if (c.Kind != FormControlKind.OptionButton || !string.IsNullOrWhiteSpace(c.LinkedCell))
                continue;

            if (enclosingGroupBox is { } box)
            {
                // Scoped to the same GroupBox as the clicked button.
                if (c.Anchor is { } candidateAnchor && FindEnclosingGroupBox(candidateAnchor, groupBoxes) is { } candidateBox &&
                    candidateBox.Equals(box))
                {
                    group.Add(c);
                }
            }
            else
            {
                // Sheet-level default group: every unlinked OptionButton anchored inside no GroupBox.
                if (c.Anchor is not { } anchor || FindEnclosingGroupBox(anchor, groupBoxes) is null)
                    group.Add(c);
            }
        }

        if (!group.Contains(clicked))
            group.Add(clicked);

        return group;
    }

    /// <summary>Finds the first GroupBox range (if any) that fully contains <paramref name="anchor"/>.</summary>
    private static GridRange? FindEnclosingGroupBox(GridRange anchor, IReadOnlyList<GridRange> groupBoxes)
    {
        foreach (var box in groupBoxes)
        {
            if (box.Contains(anchor))
                return box;
        }

        return null;
    }
}
