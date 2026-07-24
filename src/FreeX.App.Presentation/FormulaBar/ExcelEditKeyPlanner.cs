using FreeX.Core.Model;

namespace FreeX.App.Presentation.FormulaBar;

public static class ExcelEditKeyPlanner
{
    public static bool ShouldCycleFormulaReference(
        FormulaEditorKey key,
        FormulaEditorModifiers modifiers,
        FormulaEditorKey systemKey = FormulaEditorKey.None)
    {
        var effectiveKey = key == FormulaEditorKey.None || key == FormulaEditorKey.System ? systemKey : key;
        return effectiveKey == FormulaEditorKey.F4 && modifiers == FormulaEditorModifiers.None;
    }

    public static ExcelEditKeyIntent GetIntent(
        FormulaEditorKey key,
        FormulaEditorModifiers modifiers,
        CellAddress current,
        int pageSize,
        bool allowFormulaBarNavigationKeys,
        bool formulaRangeEntryActive = false,
        bool inlineEditorCommitsOnArrow = false,
        bool moveSelectionAfterEnter = true,
        FormulaEditorEnterDirection enterDirection = FormulaEditorEnterDirection.Down,
        FormulaEditorKey systemKey = FormulaEditorKey.None)
    {
        var effectiveKey = key == FormulaEditorKey.None || key == FormulaEditorKey.System ? systemKey : key;
        var pageStep = (uint)Math.Max(1, pageSize);

        if (effectiveKey == FormulaEditorKey.Enter && modifiers == FormulaEditorModifiers.Alt)
            return new ExcelEditKeyIntent(ExcelEditKeyAction.InsertLineBreak, null);

        if (effectiveKey == FormulaEditorKey.Enter && modifiers == FormulaEditorModifiers.Control)
            return new ExcelEditKeyIntent(ExcelEditKeyAction.CommitSelection, null);

        if (modifiers is not FormulaEditorModifiers.None and not FormulaEditorModifiers.Shift)
            return ExcelEditKeyIntent.None;

        var shiftHeld = (modifiers & FormulaEditorModifiers.Shift) != 0;

        if (formulaRangeEntryActive && effectiveKey is FormulaEditorKey.Up or FormulaEditorKey.Down or FormulaEditorKey.Left or FormulaEditorKey.Right or FormulaEditorKey.PageUp or FormulaEditorKey.PageDown)
        {
            var referenceTarget = effectiveKey switch
            {
                FormulaEditorKey.Up => new CellAddress(current.Sheet, current.Row > 1 ? current.Row - 1 : 1u, current.Col),
                FormulaEditorKey.Down => new CellAddress(current.Sheet, Math.Min(current.Row + 1, CellAddress.MaxRow), current.Col),
                FormulaEditorKey.Left => new CellAddress(current.Sheet, current.Row, current.Col > 1 ? current.Col - 1 : 1u),
                FormulaEditorKey.Right => new CellAddress(current.Sheet, current.Row, Math.Min(current.Col + 1, CellAddress.MaxCol)),
                FormulaEditorKey.PageUp => new CellAddress(current.Sheet, current.Row > pageStep ? current.Row - pageStep : 1u, current.Col),
                FormulaEditorKey.PageDown => new CellAddress(current.Sheet, Math.Min(CellAddress.MaxRow, current.Row + pageStep), current.Col),
                _ => (CellAddress?)null
            };

            return referenceTarget is { } formulaReferenceTarget
                ? new ExcelEditKeyIntent(ExcelEditKeyAction.SelectFormulaReference, formulaReferenceTarget)
                : ExcelEditKeyIntent.None;
        }

        if (inlineEditorCommitsOnArrow && modifiers == FormulaEditorModifiers.None && effectiveKey is FormulaEditorKey.Up or FormulaEditorKey.Down or FormulaEditorKey.Left or FormulaEditorKey.Right)
        {
            var emptyEditorTarget = effectiveKey switch
            {
                FormulaEditorKey.Up => new CellAddress(current.Sheet, current.Row > 1 ? current.Row - 1 : 1u, current.Col),
                FormulaEditorKey.Down => new CellAddress(current.Sheet, Math.Min(current.Row + 1, CellAddress.MaxRow), current.Col),
                FormulaEditorKey.Left => new CellAddress(current.Sheet, current.Row, current.Col > 1 ? current.Col - 1 : 1u),
                FormulaEditorKey.Right => new CellAddress(current.Sheet, current.Row, Math.Min(current.Col + 1, CellAddress.MaxCol)),
                _ => (CellAddress?)null
            };

            return emptyEditorTarget is { } targetCell
                ? new ExcelEditKeyIntent(ExcelEditKeyAction.CommitAndMove, targetCell)
                : ExcelEditKeyIntent.None;
        }

        var target = effectiveKey switch
        {
            FormulaEditorKey.Enter => moveSelectionAfterEnter
                ? GetEnterTarget(current, shiftHeld, enterDirection)
                : current,
            FormulaEditorKey.Tab => shiftHeld
                ? new CellAddress(current.Sheet, current.Row, current.Col > 1 ? current.Col - 1 : 1u)
                : new CellAddress(current.Sheet, current.Row, Math.Min(current.Col + 1, CellAddress.MaxCol)),
            FormulaEditorKey.Up when allowFormulaBarNavigationKeys && !shiftHeld =>
                new CellAddress(current.Sheet, current.Row > 1 ? current.Row - 1 : 1u, current.Col),
            FormulaEditorKey.Down when allowFormulaBarNavigationKeys && !shiftHeld =>
                new CellAddress(current.Sheet, Math.Min(current.Row + 1, CellAddress.MaxRow), current.Col),
            FormulaEditorKey.PageUp when allowFormulaBarNavigationKeys && !shiftHeld =>
                new CellAddress(current.Sheet, current.Row > pageStep ? current.Row - pageStep : 1u, current.Col),
            FormulaEditorKey.PageDown when allowFormulaBarNavigationKeys && !shiftHeld =>
                new CellAddress(current.Sheet, Math.Min(CellAddress.MaxRow, current.Row + pageStep), current.Col),
            _ => (CellAddress?)null
        };

        return target is { } moveTarget
            ? new ExcelEditKeyIntent(ExcelEditKeyAction.CommitAndMove, moveTarget)
            : ExcelEditKeyIntent.None;
    }

    /// <summary>
    /// Computes the active-cell target for a plain (non-Ctrl) Enter keypress, honoring the
    /// configured "After pressing Enter, move selection" direction and its Shift-reversal --
    /// shared by both the in-edit commit path (<see cref="GetIntent"/>) and ready-mode Enter on
    /// an already-selected, non-edited cell.
    /// </summary>
    public static CellAddress GetEnterTarget(CellAddress current, bool reverse, FormulaEditorEnterDirection direction)
    {
        var effectiveDirection = reverse
            ? direction switch
            {
                FormulaEditorEnterDirection.Down => FormulaEditorEnterDirection.Up,
                FormulaEditorEnterDirection.Up => FormulaEditorEnterDirection.Down,
                FormulaEditorEnterDirection.Right => FormulaEditorEnterDirection.Left,
                FormulaEditorEnterDirection.Left => FormulaEditorEnterDirection.Right,
                _ => direction
            }
            : direction;

        return effectiveDirection switch
        {
            FormulaEditorEnterDirection.Right => new CellAddress(current.Sheet, current.Row, Math.Min(current.Col + 1, CellAddress.MaxCol)),
            FormulaEditorEnterDirection.Up => new CellAddress(current.Sheet, current.Row > 1 ? current.Row - 1 : 1u, current.Col),
            FormulaEditorEnterDirection.Left => new CellAddress(current.Sheet, current.Row, current.Col > 1 ? current.Col - 1 : 1u),
            _ => new CellAddress(current.Sheet, Math.Min(current.Row + 1, CellAddress.MaxRow), current.Col)
        };
    }
}

public readonly record struct ExcelEditKeyIntent(ExcelEditKeyAction Action, CellAddress? Target)
{
    public static ExcelEditKeyIntent None => new(ExcelEditKeyAction.None, null);
}

public enum ExcelEditKeyAction
{
    None,
    CommitAndMove,
    InsertLineBreak,
    CommitSelection,
    SelectFormulaReference
}
