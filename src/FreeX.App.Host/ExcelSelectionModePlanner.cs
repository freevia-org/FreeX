using System.Windows.Input;
using FreeX.App.Presentation;

namespace FreeX.App.Host;

public static class ExcelSelectionModePlanner
{
    public static bool TryToggle(
        Key key,
        ModifierKeys modifiers,
        ExcelSelectionMode current,
        out ExcelSelectionMode next) =>
        FreeX.App.Presentation.ExcelSelectionModePlanner.TryToggle(
            MapKey(key),
            ExcelWorksheetNavigationPlanner.MapModifiers(modifiers),
            current,
            out next);

    public static bool ShouldExtendSelection(ExcelSelectionMode mode, ModifierKeys modifiers) =>
        FreeX.App.Presentation.ExcelSelectionModePlanner.ShouldExtendSelection(
            mode,
            ExcelWorksheetNavigationPlanner.MapModifiers(modifiers));

    private static ExcelSelectionKey MapKey(Key key) =>
        key == Key.F8 ? ExcelSelectionKey.F8 : ExcelSelectionKey.Other;
}
