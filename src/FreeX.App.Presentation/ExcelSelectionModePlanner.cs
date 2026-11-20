using Free.Shared.Shell;
using Free.Shared.AppServices;

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
        FreeKeyModifiers modifiers,
        ExcelSelectionMode current,
        out ExcelSelectionMode next)
    {
        next = current;
        if (key != ExcelSelectionKey.F8)
            return false;

        if (modifiers == FreeKeyModifiers.None)
        {
            next = current == ExcelSelectionMode.Extend ? ExcelSelectionMode.Normal : ExcelSelectionMode.Extend;
            return true;
        }

        if (modifiers == FreeKeyModifiers.Shift)
        {
            next = current == ExcelSelectionMode.Add ? ExcelSelectionMode.Normal : ExcelSelectionMode.Add;
            return true;
        }

        return false;
    }

    public static bool ShouldExtendSelection(ExcelSelectionMode mode, FreeKeyModifiers modifiers) =>
        mode == ExcelSelectionMode.Extend ||
        modifiers is FreeKeyModifiers.Shift or
            (FreeKeyModifiers.Control | FreeKeyModifiers.Shift);

    public static string StatusBarModeResourceKey(ExcelSelectionMode mode) =>
        mode switch
        {
            ExcelSelectionMode.Extend => StatusBarTextResourceKeys.ExtendSelectionMode,
            ExcelSelectionMode.Add => StatusBarTextResourceKeys.AddToSelectionMode,
            _ => StatusBarTextResourceKeys.ReadyText
        };

    public static string EndModeStatusBarResourceKey(bool enabled) =>
        enabled ? StatusBarTextResourceKeys.EndMode : StatusBarTextResourceKeys.ReadyText;
}
