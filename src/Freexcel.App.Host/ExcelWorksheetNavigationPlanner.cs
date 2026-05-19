using System.Windows.Input;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public static class ExcelWorksheetNavigationPlanner
{
    public static bool TryToggleEndMode(Key key, ModifierKeys modifiers, bool current, out bool next)
    {
        next = current;
        if (key != Key.End || modifiers != ModifierKeys.None)
            return false;

        next = !current;
        return true;
    }

    public static bool ShouldUseDataBoundary(Key key, ModifierKeys modifiers, bool endMode) =>
        key is Key.Up or Key.Down or Key.Left or Key.Right &&
        (endMode || (modifiers & ModifierKeys.Control) != 0);

    public static CellAddress? GetHorizontalPageTarget(
        Key key,
        Key systemKey,
        ModifierKeys modifiers,
        CellAddress current,
        int pageSize)
    {
        if (modifiers is not ModifierKeys.Alt and not (ModifierKeys.Alt | ModifierKeys.Shift))
            return null;

        var effectiveKey = key == Key.None ? systemKey : key;
        return effectiveKey switch
        {
            Key.PageDown => new CellAddress(
                current.Sheet,
                current.Row,
                Math.Min(current.Col + (uint)Math.Max(1, pageSize), CellAddress.MaxCol)),
            Key.PageUp => new CellAddress(
                current.Sheet,
                current.Row,
                (uint)Math.Max(1, (int)current.Col - Math.Max(1, pageSize))),
            _ => null
        };
    }
}
