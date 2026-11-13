using FreeX.App.Presentation.FormulaBar;
using FreeX.App.Presentation.GridInteraction;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation;

[Flags]
public enum ExcelWorksheetNavigationModifiers
{
    None = 0,
    Shift = 1,
    Control = 2,
    Alt = 4,
    Windows = 8
}

public enum ExcelWorksheetNavigationKey
{
    None,
    System,
    Other,
    Up,
    Down,
    Left,
    Right,
    Home,
    End,
    PageUp,
    PageDown,
    Enter,
    Tab
}

/// <summary>
/// Host-supplied inputs for <see cref="ExcelWorksheetNavigationPlanner.ResolveTarget"/>. Everything
/// that depends on the native toolkit (key/modifier conversion, viewport-derived page sizes, the
/// user's "After pressing Enter, move selection" option) is flattened into this value so the
/// key-to-target decision itself is portable and identical for the WPF and Avalonia shells.
/// </summary>
/// <param name="Modifiers">Held modifiers (Meta is folded into Control by the Avalonia mapper).</param>
/// <param name="EndMode">Whether Excel's sticky End mode is armed (read BEFORE the host clears it).</param>
/// <param name="PageRows">Rows per Page Up/Down step (visible scrollable rows less one, at least 1).</param>
/// <param name="PageColumns">Columns per Alt+Page Up/Down step (visible scrollable columns less one, at least 1).</param>
/// <param name="MoveSelectionAfterEnter">The "After pressing Enter, move selection" option; when false ready-mode Enter does not move.</param>
/// <param name="EnterDirection">The configured Enter direction; Shift+Enter moves the opposite way.</param>
/// <param name="SystemKey">WPF's <c>e.SystemKey</c> (the real key when Alt is held and the key is reported as System).</param>
public readonly record struct ExcelWorksheetNavigationRequest(
    ExcelWorksheetNavigationModifiers Modifiers,
    bool EndMode,
    int PageRows,
    int PageColumns,
    bool MoveSelectionAfterEnter = true,
    FormulaEditorEnterDirection EnterDirection = FormulaEditorEnterDirection.Down,
    ExcelWorksheetNavigationKey SystemKey = ExcelWorksheetNavigationKey.None);

/// <summary>
/// Result of <see cref="ExcelWorksheetNavigationPlanner.ResolveTarget"/>.
/// <paramref name="CycleKey"/> is non-null exactly for Enter/Tab (which move the active cell, never
/// extend the selection) and is the key to hand to
/// <see cref="GridSelectionNavigationPlanner.PlanCycle"/> for those two.
/// </summary>
public readonly record struct ExcelWorksheetNavigationResolution(
    CellAddress Target,
    GridSelectionCycleKey? CycleKey);

public static class ExcelWorksheetNavigationPlanner
{
    /// <summary>
    /// The single "key -> target cell" decision shared by the WPF and Avalonia shells. Returns null
    /// when the key/modifier combination is not a worksheet navigation key (see
    /// <see cref="ShouldHandleWorksheetNavigationKey"/>). Plain arrow steps, Enter and Tab are all
    /// advanced past a merged block (<see cref="AdjustTargetPastMerge"/>) so the key press is never
    /// absorbed by a step that lands back inside the same merge (R51-render-merged-cell-edit-nav-3-1);
    /// Ctrl/End-mode data-boundary jumps, Home, Ctrl+End and paging are not. Ready-mode Enter honours
    /// the "After pressing Enter, move selection" option and direction (Shift reverses it), and does
    /// not move when the option is off, exactly like the in-edit commit path
    /// (<see cref="ExcelEditKeyPlanner.GetEnterTarget"/>; R82-app-keyboard-nav-5-1).
    /// Hosts keep selection cycling, protected-sheet skipping and the actual selection/scroll update
    /// because those depend on live selection state.
    /// </summary>
    public static ExcelWorksheetNavigationResolution? ResolveTarget(
        Sheet? sheet,
        SheetId sheetId,
        CellAddress current,
        ExcelWorksheetNavigationKey key,
        ExcelWorksheetNavigationRequest request)
    {
        var modifiers = request.Modifiers;
        if (!ShouldHandleWorksheetNavigationKey(key, request.SystemKey, modifiers, request.EndMode))
            return null;

        var effectiveKey = key is ExcelWorksheetNavigationKey.None or ExcelWorksheetNavigationKey.System
            ? request.SystemKey
            : key;
        var shiftHeld = (modifiers & ExcelWorksheetNavigationModifiers.Shift) != 0;
        var ctrlHeld = (modifiers & ExcelWorksheetNavigationModifiers.Control) != 0;
        var useDataBoundary = ShouldUseDataBoundary(effectiveKey, modifiers, request.EndMode);
        var pageRows = Math.Max(1, request.PageRows);

        // Alt+PageUp/PageDown is a horizontal screen-page move; it wins over the vertical paging below.
        var target = GetHorizontalPageTarget(key, request.SystemKey, modifiers, current, request.PageColumns);

        target ??= effectiveKey switch
        {
            ExcelWorksheetNavigationKey.Up => useDataBoundary
                ? FindVerticalDataBoundary(sheet, current, -1)
                : AdjustTargetPastMerge(sheet, current, StepAddress(sheetId, current, -1, 0)),
            ExcelWorksheetNavigationKey.Down => useDataBoundary
                ? FindVerticalDataBoundary(sheet, current, +1)
                : AdjustTargetPastMerge(sheet, current, StepAddress(sheetId, current, 1, 0)),
            ExcelWorksheetNavigationKey.Left => useDataBoundary
                ? FindHorizontalDataBoundary(sheet, current, -1)
                : AdjustTargetPastMerge(sheet, current, StepAddress(sheetId, current, 0, -1)),
            ExcelWorksheetNavigationKey.Right => useDataBoundary
                ? FindHorizontalDataBoundary(sheet, current, +1)
                : AdjustTargetPastMerge(sheet, current, StepAddress(sheetId, current, 0, 1)),
            ExcelWorksheetNavigationKey.PageUp => StepAddress(sheetId, current, -pageRows, 0),
            ExcelWorksheetNavigationKey.PageDown => StepAddress(sheetId, current, pageRows, 0),
            // Includes the "End, Home" -> Ctrl+End jump and the frozen-pane origin for Ctrl+Home.
            ExcelWorksheetNavigationKey.Home => GetHomeTarget(sheet, sheetId, current, ctrlHeld, request.EndMode),
            ExcelWorksheetNavigationKey.End => ctrlHeld ? GetCtrlEndCell(sheet, sheetId) : null,
            ExcelWorksheetNavigationKey.Enter => request.MoveSelectionAfterEnter
                ? AdjustTargetPastMerge(
                    sheet,
                    current,
                    ExcelEditKeyPlanner.GetEnterTarget(current, shiftHeld, request.EnterDirection))
                : current,
            ExcelWorksheetNavigationKey.Tab => AdjustTargetPastMerge(
                sheet,
                current,
                shiftHeld ? StepAddress(sheetId, current, 0, -1) : StepAddress(sheetId, current, 0, 1)),
            _ => null
        };

        if (target is not { } resolved)
            return null;

        GridSelectionCycleKey? cycleKey = effectiveKey switch
        {
            ExcelWorksheetNavigationKey.Enter => GridSelectionCycleKey.Enter,
            ExcelWorksheetNavigationKey.Tab => GridSelectionCycleKey.Tab,
            _ => null
        };
        return new ExcelWorksheetNavigationResolution(resolved, cycleKey);
    }

    private static CellAddress StepAddress(SheetId sheetId, CellAddress current, int rowDelta, int colDelta) =>
        new(
            sheetId,
            (uint)Math.Clamp((long)current.Row + rowDelta, 1, CellAddress.MaxRow),
            (uint)Math.Clamp((long)current.Col + colDelta, 1, CellAddress.MaxCol));

    public static bool TryToggleEndMode(
        ExcelWorksheetNavigationKey key,
        ExcelWorksheetNavigationModifiers modifiers,
        bool current,
        out bool next)
    {
        next = current;
        if (key != ExcelWorksheetNavigationKey.End || modifiers != ExcelWorksheetNavigationModifiers.None)
            return false;

        next = !current;
        return true;
    }

    public static bool ShouldUseDataBoundary(
        ExcelWorksheetNavigationKey key,
        ExcelWorksheetNavigationModifiers modifiers,
        bool endMode) =>
        (key is ExcelWorksheetNavigationKey.Up or ExcelWorksheetNavigationKey.Down or
            ExcelWorksheetNavigationKey.Left or ExcelWorksheetNavigationKey.Right) &&
        (endMode
            ? modifiers is ExcelWorksheetNavigationModifiers.None or ExcelWorksheetNavigationModifiers.Shift
            : modifiers is ExcelWorksheetNavigationModifiers.Control or
                (ExcelWorksheetNavigationModifiers.Control | ExcelWorksheetNavigationModifiers.Shift));

    public static bool ShouldHandleWorksheetNavigationKey(
        ExcelWorksheetNavigationKey key,
        ExcelWorksheetNavigationKey systemKey,
        ExcelWorksheetNavigationModifiers modifiers,
        bool endMode)
    {
        var effectiveKey = key is ExcelWorksheetNavigationKey.None or ExcelWorksheetNavigationKey.System ? systemKey : key;
        return effectiveKey switch
        {
            ExcelWorksheetNavigationKey.Up or ExcelWorksheetNavigationKey.Down or
                ExcelWorksheetNavigationKey.Left or ExcelWorksheetNavigationKey.Right =>
                endMode
                    ? modifiers is ExcelWorksheetNavigationModifiers.None or ExcelWorksheetNavigationModifiers.Shift
                    : modifiers is ExcelWorksheetNavigationModifiers.None or ExcelWorksheetNavigationModifiers.Shift or
                        ExcelWorksheetNavigationModifiers.Control or
                        (ExcelWorksheetNavigationModifiers.Control | ExcelWorksheetNavigationModifiers.Shift),
            ExcelWorksheetNavigationKey.Home =>
                modifiers is ExcelWorksheetNavigationModifiers.None or ExcelWorksheetNavigationModifiers.Shift or
                    ExcelWorksheetNavigationModifiers.Control or
                    (ExcelWorksheetNavigationModifiers.Control | ExcelWorksheetNavigationModifiers.Shift),
            ExcelWorksheetNavigationKey.End =>
                modifiers is ExcelWorksheetNavigationModifiers.Control or
                    (ExcelWorksheetNavigationModifiers.Control | ExcelWorksheetNavigationModifiers.Shift),
            ExcelWorksheetNavigationKey.PageUp or ExcelWorksheetNavigationKey.PageDown =>
                modifiers is ExcelWorksheetNavigationModifiers.None or ExcelWorksheetNavigationModifiers.Shift or
                    ExcelWorksheetNavigationModifiers.Alt or
                    (ExcelWorksheetNavigationModifiers.Alt | ExcelWorksheetNavigationModifiers.Shift),
            ExcelWorksheetNavigationKey.Enter or ExcelWorksheetNavigationKey.Tab =>
                modifiers is ExcelWorksheetNavigationModifiers.None or ExcelWorksheetNavigationModifiers.Shift,
            _ => false
        };
    }

    public static CellAddress? GetHorizontalPageTarget(
        ExcelWorksheetNavigationKey key,
        ExcelWorksheetNavigationKey systemKey,
        ExcelWorksheetNavigationModifiers modifiers,
        CellAddress current,
        int pageSize)
    {
        if (modifiers is not ExcelWorksheetNavigationModifiers.Alt and not
            (ExcelWorksheetNavigationModifiers.Alt | ExcelWorksheetNavigationModifiers.Shift))
        {
            return null;
        }

        var effectiveKey = key is ExcelWorksheetNavigationKey.None or ExcelWorksheetNavigationKey.System ? systemKey : key;
        return effectiveKey switch
        {
            ExcelWorksheetNavigationKey.PageDown => new CellAddress(
                current.Sheet,
                current.Row,
                Math.Min(current.Col + (uint)Math.Max(1, pageSize), CellAddress.MaxCol)),
            ExcelWorksheetNavigationKey.PageUp => new CellAddress(
                current.Sheet,
                current.Row,
                (uint)Math.Max(1, (int)current.Col - Math.Max(1, pageSize))),
            _ => null
        };
    }

    public static CellAddress FindVerticalDataBoundary(Sheet? sheet, CellAddress current, int rowDirection)
    {
        var startFull = CellHasData(sheet, current.Row, current.Col);
        if (!startFull)
            return FindVerticalDataBoundaryFromBlank(sheet, current, rowDirection);

        var row = current.Row;
        while (true)
        {
            var next = (long)row + rowDirection;
            if (next is < 1 or > CellAddress.MaxRow)
                break;

            var nextRow = (uint)next;
            var nextFull = CellHasData(sheet, nextRow, current.Col);
            if (startFull && !nextFull && row == current.Row)
            {
                return FindVerticalDataBoundaryFromBlank(
                    sheet,
                    new CellAddress(current.Sheet, nextRow, current.Col),
                    rowDirection);
            }

            if (startFull && !nextFull)
                break;

            row = nextRow;
            if (!startFull && nextFull)
                break;
        }

        return new CellAddress(current.Sheet, row, current.Col);
    }

    public static CellAddress FindHorizontalDataBoundary(Sheet? sheet, CellAddress current, int columnDirection)
    {
        var startFull = CellHasData(sheet, current.Row, current.Col);
        if (!startFull)
            return FindHorizontalDataBoundaryFromBlank(sheet, current, columnDirection);

        var column = current.Col;
        while (true)
        {
            var next = (long)column + columnDirection;
            if (next is < 1 or > CellAddress.MaxCol)
                break;

            var nextColumn = (uint)next;
            var nextFull = CellHasData(sheet, current.Row, nextColumn);
            if (startFull && !nextFull && column == current.Col)
            {
                return FindHorizontalDataBoundaryFromBlank(
                    sheet,
                    new CellAddress(current.Sheet, current.Row, nextColumn),
                    columnDirection);
            }

            if (startFull && !nextFull)
                break;

            column = nextColumn;
            if (!startFull && nextFull)
                break;
        }

        return new CellAddress(current.Sheet, current.Row, column);
    }

    public static CellAddress GetCtrlEndCell(Sheet? sheet, SheetId sheetId)
    {
        var usedRangeEnd = sheet?.GetUsedRange()?.End;
        return usedRangeEnd ?? new CellAddress(sheetId, 1, 1);
    }

    /// <summary>
    /// Home's target cell. Excel's Ctrl+Home jumps to the top-left cell of the *scrollable*
    /// region -- the first unfrozen row/column -- rather than always to A1 once panes are frozen;
    /// plain Home (no Ctrl) moves to column A of the current row regardless of freeze
    /// (R52-render-scroll-viewport-nav-3-1). When End's sticky mode is active, "End, Home"
    /// reproduces Ctrl+End instead -- jumping to the last used cell on the worksheet -- matching
    /// how "End, &lt;arrow&gt;" reproduces Ctrl+&lt;arrow&gt; (R82-app-keyboard-nav-5-2).
    /// </summary>
    public static CellAddress GetHomeTarget(Sheet? sheet, SheetId sheetId, CellAddress current, bool ctrlHeld, bool endMode)
    {
        if (endMode)
            return GetCtrlEndCell(sheet, sheetId);

        if (!ctrlHeld)
            return new CellAddress(sheetId, current.Row, 1u);

        var firstUnfrozenRow = (sheet?.FrozenRows ?? 0) + 1;
        var firstUnfrozenCol = (sheet?.FrozenCols ?? 0) + 1;
        return new CellAddress(sheetId, firstUnfrozenRow, firstUnfrozenCol);
    }

    /// <summary>
    /// When <paramref name="from"/> (the cell that was just being edited) belongs to a merged
    /// region and the plain +1/-1 step in <paramref name="next"/> still lands inside that same
    /// merge, advances past the merge's far edge in the direction of travel instead. Without this,
    /// Enter/Tab from inside a merge spanning more than one row/column recomputes "next" from the
    /// merge's own top-left anchor (selecting a cell always collapses the selection to the merge's
    /// bounds), so a plain current+1 still falls inside the same merge and the cursor never
    /// advances -- unlike Excel, which always steps past the whole merged block.
    /// </summary>
    public static CellAddress AdjustTargetPastMerge(Sheet? sheet, CellAddress from, CellAddress next)
    {
        if (sheet is not { MergedRegions.Count: > 0 } || sheet.GetMergeRegion(from) is not { } merge)
            return next;

        if (!merge.Contains(next))
            return next;

        var row = next.Row;
        var col = next.Col;
        if (next.Row != from.Row)
        {
            row = next.Row > from.Row
                ? Math.Min(merge.End.Row + 1, CellAddress.MaxRow)
                : (merge.Start.Row > 1 ? merge.Start.Row - 1 : 1u);
        }
        else if (next.Col != from.Col)
        {
            col = next.Col > from.Col
                ? Math.Min(merge.End.Col + 1, CellAddress.MaxCol)
                : (merge.Start.Col > 1 ? merge.Start.Col - 1 : 1u);
        }

        return new CellAddress(next.Sheet, row, col);
    }

    /// <summary>
    /// Resolves an Arrow, Enter, Tab, Home, End, Page Up, or Page Down target on a protected
    /// sheet, skipping cells that the workbook protection policy does not allow the user to
    /// select. Other navigation keys retain their original target. Returns null when no
    /// selectable cell remains before the sheet edge.
    /// </summary>
    public static CellAddress? ResolveProtectedSheetTarget(
        Workbook workbook,
        Sheet sheet,
        CellAddress target,
        ExcelWorksheetNavigationKey key,
        bool shiftHeld)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(sheet);

        if (!sheet.IsProtected ||
            GetProtectedNavigationStep(key, shiftHeld) is not { } step ||
            CommandGuards.CanSelectCell(workbook, sheet, target))
        {
            return target;
        }

        var candidate = target;
        while (!CommandGuards.CanSelectCell(workbook, sheet, candidate))
        {
            var nextRow = (long)candidate.Row + step.RowStep;
            var nextCol = (long)candidate.Col + step.ColStep;
            if (nextRow is < 1 or > CellAddress.MaxRow || nextCol is < 1 or > CellAddress.MaxCol)
                return null;

            candidate = new CellAddress(candidate.Sheet, (uint)nextRow, (uint)nextCol);
        }

        return candidate;
    }

    private static bool CellHasData(Sheet? sheet, uint row, uint col)
    {
        if (sheet is null)
            return false;

        var value = sheet.GetValue(new CellAddress(sheet.Id, row, col));
        return value is not null and not BlankValue;
    }

    private static (int RowStep, int ColStep)? GetProtectedNavigationStep(
        ExcelWorksheetNavigationKey key,
        bool shiftHeld) =>
        key switch
        {
            ExcelWorksheetNavigationKey.Up => (-1, 0),
            ExcelWorksheetNavigationKey.Down => (1, 0),
            ExcelWorksheetNavigationKey.Left => (0, -1),
            ExcelWorksheetNavigationKey.Right => (0, 1),
            ExcelWorksheetNavigationKey.Enter => shiftHeld ? (-1, 0) : (1, 0),
            ExcelWorksheetNavigationKey.Tab => shiftHeld ? (0, -1) : (0, 1),
            // Home/Ctrl+Home land at the leftmost (and, with Ctrl, topmost) reachable cell, so a
            // locked landing cell is skipped by continuing rightward -- away from the left edge
            // the jump aimed for and back toward the rest of the row -- mirroring how Right
            // already skips a locked cell in that same direction.
            ExcelWorksheetNavigationKey.Home => (0, 1),
            // Ctrl+End (the only End variant reaching protected navigation -- plain End only
            // arms sticky End-mode) lands at the bottom-right of the used range, so a locked
            // landing cell is skipped by continuing leftward, the mirror image of Home.
            ExcelWorksheetNavigationKey.End => (0, -1),
            ExcelWorksheetNavigationKey.PageUp => (-1, 0),
            ExcelWorksheetNavigationKey.PageDown => (1, 0),
            _ => null
        };

    private static CellAddress FindVerticalDataBoundaryFromBlank(Sheet? sheet, CellAddress current, int rowDirection)
    {
        if (sheet is null)
        {
            return new CellAddress(
                current.Sheet,
                rowDirection > 0 ? CellAddress.MaxRow : 1,
                current.Col);
        }

        uint? targetRow = null;
        foreach (var address in sheet.EnumerateValueBearingCells())
        {
            if (address.Col != current.Col)
                continue;

            if (rowDirection > 0)
            {
                if (address.Row <= current.Row)
                    continue;

                if (targetRow is null || address.Row < targetRow.Value)
                    targetRow = address.Row;
            }
            else
            {
                if (address.Row >= current.Row)
                    continue;

                if (targetRow is null || address.Row > targetRow.Value)
                    targetRow = address.Row;
            }
        }

        return new CellAddress(
            current.Sheet,
            targetRow ?? (rowDirection > 0 ? CellAddress.MaxRow : 1),
            current.Col);
    }

    private static CellAddress FindHorizontalDataBoundaryFromBlank(Sheet? sheet, CellAddress current, int columnDirection)
    {
        if (sheet is null)
        {
            return new CellAddress(
                current.Sheet,
                current.Row,
                columnDirection > 0 ? CellAddress.MaxCol : 1);
        }

        uint? targetColumn = null;
        foreach (var address in sheet.EnumerateValueBearingCells())
        {
            if (address.Row != current.Row)
                continue;

            if (columnDirection > 0)
            {
                if (address.Col <= current.Col)
                    continue;

                if (targetColumn is null || address.Col < targetColumn.Value)
                    targetColumn = address.Col;
            }
            else
            {
                if (address.Col >= current.Col)
                    continue;

                if (targetColumn is null || address.Col > targetColumn.Value)
                    targetColumn = address.Col;
            }
        }

        return new CellAddress(
            current.Sheet,
            current.Row,
            targetColumn ?? (columnDirection > 0 ? CellAddress.MaxCol : 1));
    }
}
