using System.Windows.Input;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public static class ExcelEditKeyPlanner
{
    public static bool ShouldCycleFormulaReference(Key key, ModifierKeys modifiers, Key systemKey = Key.None)
    {
        var effectiveKey = key == Key.None || key == Key.System ? systemKey : key;
        return effectiveKey == Key.F4 && modifiers == ModifierKeys.None;
    }

    public static ExcelEditKeyIntent GetIntent(
        Key key,
        ModifierKeys modifiers,
        CellAddress current,
        int pageSize,
        bool allowFormulaBarNavigationKeys,
        bool formulaRangeEntryActive = false,
        bool inlineEditorCommitsOnArrow = false,
        bool moveSelectionAfterEnter = true,
        FreeXEnterDirection enterDirection = FreeXEnterDirection.Down,
        Key systemKey = Key.None)
    {
        var effectiveKey = key == Key.None || key == Key.System ? systemKey : key;

        if (effectiveKey == Key.Enter && modifiers == ModifierKeys.Alt)
            return new ExcelEditKeyIntent(ExcelEditKeyAction.InsertLineBreak, null);

        if (effectiveKey == Key.Enter && modifiers == ModifierKeys.Control)
            return new ExcelEditKeyIntent(ExcelEditKeyAction.CommitSelection, null);

        if (modifiers is not ModifierKeys.None and not ModifierKeys.Shift)
            return ExcelEditKeyIntent.None;

        var shiftHeld = (modifiers & ModifierKeys.Shift) != 0;

        if (formulaRangeEntryActive && effectiveKey is Key.Up or Key.Down or Key.Left or Key.Right or Key.PageUp or Key.PageDown)
        {
            var referenceTarget = effectiveKey switch
            {
                Key.Up => new CellAddress(current.Sheet, current.Row > 1 ? current.Row - 1 : 1u, current.Col),
                Key.Down => new CellAddress(current.Sheet, Math.Min(current.Row + 1, CellAddress.MaxRow), current.Col),
                Key.Left => new CellAddress(current.Sheet, current.Row, current.Col > 1 ? current.Col - 1 : 1u),
                Key.Right => new CellAddress(current.Sheet, current.Row, Math.Min(current.Col + 1, CellAddress.MaxCol)),
                Key.PageUp => new CellAddress(current.Sheet, (uint)Math.Max(1, (int)current.Row - pageSize), current.Col),
                Key.PageDown => new CellAddress(current.Sheet, Math.Min(CellAddress.MaxRow, current.Row + (uint)pageSize), current.Col),
                _ => (CellAddress?)null
            };

            return referenceTarget is { } formulaReferenceTarget
                ? new ExcelEditKeyIntent(ExcelEditKeyAction.SelectFormulaReference, formulaReferenceTarget)
                : ExcelEditKeyIntent.None;
        }

        if (inlineEditorCommitsOnArrow && modifiers == ModifierKeys.None && effectiveKey is Key.Up or Key.Down or Key.Left or Key.Right)
        {
            var emptyEditorTarget = effectiveKey switch
            {
                Key.Up => new CellAddress(current.Sheet, current.Row > 1 ? current.Row - 1 : 1u, current.Col),
                Key.Down => new CellAddress(current.Sheet, Math.Min(current.Row + 1, CellAddress.MaxRow), current.Col),
                Key.Left => new CellAddress(current.Sheet, current.Row, current.Col > 1 ? current.Col - 1 : 1u),
                Key.Right => new CellAddress(current.Sheet, current.Row, Math.Min(current.Col + 1, CellAddress.MaxCol)),
                _ => (CellAddress?)null
            };

            return emptyEditorTarget is { } targetCell
                ? new ExcelEditKeyIntent(ExcelEditKeyAction.CommitAndMove, targetCell)
                : ExcelEditKeyIntent.None;
        }

        var target = effectiveKey switch
        {
            Key.Enter => moveSelectionAfterEnter
                ? GetEnterTarget(current, shiftHeld, enterDirection)
                : current,
            Key.Tab => shiftHeld
                ? new CellAddress(current.Sheet, current.Row, current.Col > 1 ? current.Col - 1 : 1u)
                : new CellAddress(current.Sheet, current.Row, Math.Min(current.Col + 1, CellAddress.MaxCol)),
            Key.Up when allowFormulaBarNavigationKeys && !shiftHeld =>
                new CellAddress(current.Sheet, current.Row > 1 ? current.Row - 1 : 1u, current.Col),
            Key.Down when allowFormulaBarNavigationKeys && !shiftHeld =>
                new CellAddress(current.Sheet, Math.Min(current.Row + 1, CellAddress.MaxRow), current.Col),
            Key.PageUp when allowFormulaBarNavigationKeys && !shiftHeld =>
                new CellAddress(current.Sheet, (uint)Math.Max(1, (int)current.Row - pageSize), current.Col),
            Key.PageDown when allowFormulaBarNavigationKeys && !shiftHeld =>
                new CellAddress(current.Sheet, Math.Min(CellAddress.MaxRow, current.Row + (uint)pageSize), current.Col),
            _ => (CellAddress?)null
        };

        return target is { } moveTarget
            ? new ExcelEditKeyIntent(ExcelEditKeyAction.CommitAndMove, moveTarget)
            : ExcelEditKeyIntent.None;
    }

    private static CellAddress GetEnterTarget(CellAddress current, bool reverse, FreeXEnterDirection direction)
    {
        var effectiveDirection = reverse
            ? direction switch
            {
                FreeXEnterDirection.Down => FreeXEnterDirection.Up,
                FreeXEnterDirection.Up => FreeXEnterDirection.Down,
                FreeXEnterDirection.Right => FreeXEnterDirection.Left,
                FreeXEnterDirection.Left => FreeXEnterDirection.Right,
                _ => direction
            }
            : direction;

        return effectiveDirection switch
        {
            FreeXEnterDirection.Right => new CellAddress(current.Sheet, current.Row, Math.Min(current.Col + 1, CellAddress.MaxCol)),
            FreeXEnterDirection.Up => new CellAddress(current.Sheet, current.Row > 1 ? current.Row - 1 : 1u, current.Col),
            FreeXEnterDirection.Left => new CellAddress(current.Sheet, current.Row, current.Col > 1 ? current.Col - 1 : 1u),
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
