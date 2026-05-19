using System.Windows.Input;

namespace Freexcel.App.Host;

public static class ExcelSelectionModePlanner
{
    public static bool TryToggle(
        Key key,
        ModifierKeys modifiers,
        ExcelSelectionMode current,
        out ExcelSelectionMode next)
    {
        next = current;
        if (key != Key.F8)
            return false;

        if (modifiers == ModifierKeys.None)
        {
            next = current == ExcelSelectionMode.Extend ? ExcelSelectionMode.Normal : ExcelSelectionMode.Extend;
            return true;
        }

        if (modifiers == ModifierKeys.Shift)
        {
            next = current == ExcelSelectionMode.Add ? ExcelSelectionMode.Normal : ExcelSelectionMode.Add;
            return true;
        }

        return false;
    }

    public static bool ShouldExtendSelection(ExcelSelectionMode mode, ModifierKeys modifiers) =>
        mode == ExcelSelectionMode.Extend || (modifiers & ModifierKeys.Shift) != 0;
}

public enum ExcelSelectionMode
{
    Normal,
    Extend,
    Add
}
