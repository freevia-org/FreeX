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
        bool allowFormulaBarNavigationKeys)
    {
        if (key == Key.Enter && modifiers == ModifierKeys.Alt)
            return new ExcelEditKeyIntent(ExcelEditKeyAction.InsertLineBreak, null);

        if (key == Key.Enter && modifiers == ModifierKeys.Control)
            return new ExcelEditKeyIntent(ExcelEditKeyAction.CommitSelection, null);

        if (modifiers is not ModifierKeys.None and not ModifierKeys.Shift)
            return ExcelEditKeyIntent.None;

        var shiftHeld = (modifiers & ModifierKeys.Shift) != 0;
        var target = key switch
        {
            Key.Enter => shiftHeld
                ? new CellAddress(current.Sheet, current.Row > 1 ? current.Row - 1 : 1u, current.Col)
                : new CellAddress(current.Sheet, Math.Min(current.Row + 1, CellAddress.MaxRow), current.Col),
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
    CommitSelection
}
