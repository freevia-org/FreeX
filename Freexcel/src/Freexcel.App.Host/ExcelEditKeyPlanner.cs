using System.Windows.Input;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public static class ExcelEditKeyPlanner
{
    public static ExcelEditKeyIntent GetIntent(
        Key key,
        ModifierKeys modifiers,
        CellAddress current,
        int pageSize,
        bool allowFormulaBarNavigationKeys,
        bool formulaRangeEntryActive = false,
        bool inlineEditorCommitsOnArrow = false,
        bool moveSelectionAfterEnter = true,
        FreexcelEnterDirection enterDirection = FreexcelEnterDirection.Down)
    {
        if (key == Key.Enter && modifiers == ModifierKeys.Alt)
            return new ExcelEditKeyIntent(ExcelEditKeyAction.InsertLineBreak, null);

        if (key == Key.Enter && modifiers == ModifierKeys.Control)
            return new ExcelEditKeyIntent(ExcelEditKeyAction.CommitSelection, null);

        if (modifiers is not ModifierKeys.None and not ModifierKeys.Shift)
            return ExcelEditKeyIntent.None;

        var shiftHeld = (modifiers & ModifierKeys.Shift) != 0;

        if (formulaRangeEntryActive && key is Key.Up or Key.Down or Key.Left or Key.Right or Key.PageUp or Key.PageDown)
        {
            var referenceTarget = key switch
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

        if (inlineEditorCommitsOnArrow && modifiers == ModifierKeys.None && key is Key.Up or Key.Down or Key.Left or Key.Right)
        {
            var emptyEditorTarget = key switch
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

        var target = key switch
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

    private static CellAddress GetEnterTarget(CellAddress current, bool reverse, FreexcelEnterDirection direction)
    {
        var effectiveDirection = reverse
            ? direction switch
            {
                FreexcelEnterDirection.Down => FreexcelEnterDirection.Up,
                FreexcelEnterDirection.Up => FreexcelEnterDirection.Down,
                FreexcelEnterDirection.Right => FreexcelEnterDirection.Left,
                FreexcelEnterDirection.Left => FreexcelEnterDirection.Right,
                _ => direction
            }
            : direction;

        return effectiveDirection switch
        {
            FreexcelEnterDirection.Right => new CellAddress(current.Sheet, current.Row, Math.Min(current.Col + 1, CellAddress.MaxCol)),
            FreexcelEnterDirection.Up => new CellAddress(current.Sheet, current.Row > 1 ? current.Row - 1 : 1u, current.Col),
            FreexcelEnterDirection.Left => new CellAddress(current.Sheet, current.Row, current.Col > 1 ? current.Col - 1 : 1u),
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
