using System.Windows.Input;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public static class ExcelWorksheetNavigationPlanner
{
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
