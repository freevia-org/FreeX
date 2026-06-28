namespace FreeX.App.Presentation;

public enum ExcelSelectionKey
{
    Other,
    F8
}

public enum ExcelSelectionMode
{
    Normal,
    Extend,
    Add
}

public static class ExcelSelectionModePlanner
{
    public static bool TryToggle(
        ExcelSelectionKey key,
        ExcelWorksheetNavigationModifiers modifiers,
        ExcelSelectionMode current,
        out ExcelSelectionMode next)
    {
        next = current;
        if (key != ExcelSelectionKey.F8)
            return false;

        if (modifiers == ExcelWorksheetNavigationModifiers.None)
        {
            next = current == ExcelSelectionMode.Extend ? ExcelSelectionMode.Normal : ExcelSelectionMode.Extend;
            return true;
        }

        if (modifiers == ExcelWorksheetNavigationModifiers.Shift)
        {
            next = current == ExcelSelectionMode.Add ? ExcelSelectionMode.Normal : ExcelSelectionMode.Add;
            return true;
        }

        return false;
    }

    public static bool ShouldExtendSelection(ExcelSelectionMode mode, ExcelWorksheetNavigationModifiers modifiers) =>
        mode == ExcelSelectionMode.Extend ||
        modifiers is ExcelWorksheetNavigationModifiers.Shift or
            (ExcelWorksheetNavigationModifiers.Control | ExcelWorksheetNavigationModifiers.Shift);
}
