using System.Windows.Input;
using FreeX.App.Presentation;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public static class ExcelWorksheetNavigationPlanner
{
    public static bool TryToggleEndMode(Key key, ModifierKeys modifiers, bool current, out bool next)
    {
        return FreeX.App.Presentation.ExcelWorksheetNavigationPlanner.TryToggleEndMode(
            MapKey(key),
            MapModifiers(modifiers),
            current,
            out next);
    }

    public static bool ShouldUseDataBoundary(Key key, ModifierKeys modifiers, bool endMode) =>
        FreeX.App.Presentation.ExcelWorksheetNavigationPlanner.ShouldUseDataBoundary(
            MapKey(key),
            MapModifiers(modifiers),
            endMode);

    public static bool ShouldHandleWorksheetNavigationKey(
        Key key,
        Key systemKey,
        ModifierKeys modifiers,
        bool endMode) =>
        FreeX.App.Presentation.ExcelWorksheetNavigationPlanner.ShouldHandleWorksheetNavigationKey(
            MapKey(key),
            MapKey(systemKey),
            MapModifiers(modifiers),
            endMode);

    public static CellAddress? GetHorizontalPageTarget(
        Key key,
        Key systemKey,
        ModifierKeys modifiers,
        CellAddress current,
        int pageSize) =>
        FreeX.App.Presentation.ExcelWorksheetNavigationPlanner.GetHorizontalPageTarget(
            MapKey(key),
            MapKey(systemKey),
            MapModifiers(modifiers),
            current,
            pageSize);

    public static CellAddress FindVerticalDataBoundary(Sheet? sheet, CellAddress current, int rowDirection)
        => FreeX.App.Presentation.ExcelWorksheetNavigationPlanner.FindVerticalDataBoundary(
            sheet,
            current,
            rowDirection);

    public static CellAddress FindHorizontalDataBoundary(Sheet? sheet, CellAddress current, int columnDirection)
        => FreeX.App.Presentation.ExcelWorksheetNavigationPlanner.FindHorizontalDataBoundary(
            sheet,
            current,
            columnDirection);

    public static CellAddress GetCtrlEndCell(Sheet? sheet, SheetId sheetId)
        => FreeX.App.Presentation.ExcelWorksheetNavigationPlanner.GetCtrlEndCell(sheet, sheetId);

    public static CellAddress GetHomeTarget(Sheet? sheet, SheetId sheetId, CellAddress current, bool ctrlHeld, bool endMode)
        => FreeX.App.Presentation.ExcelWorksheetNavigationPlanner.GetHomeTarget(sheet, sheetId, current, ctrlHeld, endMode);

    public static CellAddress AdjustTargetPastMerge(Sheet? sheet, CellAddress from, CellAddress next) =>
        FreeX.App.Presentation.ExcelWorksheetNavigationPlanner.AdjustTargetPastMerge(sheet, from, next);

    public static CellAddress? ResolveProtectedSheetTarget(
        Workbook workbook,
        Sheet sheet,
        CellAddress target,
        Key key,
        bool shiftHeld) =>
        FreeX.App.Presentation.ExcelWorksheetNavigationPlanner.ResolveProtectedSheetTarget(
            workbook,
            sheet,
            target,
            MapKey(key),
            shiftHeld);

    internal static ExcelWorksheetNavigationModifiers MapModifiers(ModifierKeys modifiers)
    {
        var mapped = ExcelWorksheetNavigationModifiers.None;

        if ((modifiers & ModifierKeys.Shift) != 0)
            mapped |= ExcelWorksheetNavigationModifiers.Shift;
        if ((modifiers & ModifierKeys.Control) != 0)
            mapped |= ExcelWorksheetNavigationModifiers.Control;
        if ((modifiers & ModifierKeys.Alt) != 0)
            mapped |= ExcelWorksheetNavigationModifiers.Alt;
        if ((modifiers & ModifierKeys.Windows) != 0)
            mapped |= ExcelWorksheetNavigationModifiers.Windows;

        return mapped;
    }

    private static ExcelWorksheetNavigationKey MapKey(Key key) =>
        key switch
        {
            Key.None => ExcelWorksheetNavigationKey.None,
            Key.System => ExcelWorksheetNavigationKey.System,
            Key.Up => ExcelWorksheetNavigationKey.Up,
            Key.Down => ExcelWorksheetNavigationKey.Down,
            Key.Left => ExcelWorksheetNavigationKey.Left,
            Key.Right => ExcelWorksheetNavigationKey.Right,
            Key.Home => ExcelWorksheetNavigationKey.Home,
            Key.End => ExcelWorksheetNavigationKey.End,
            Key.PageUp => ExcelWorksheetNavigationKey.PageUp,
            Key.PageDown => ExcelWorksheetNavigationKey.PageDown,
            Key.Enter => ExcelWorksheetNavigationKey.Enter,
            Key.Tab => ExcelWorksheetNavigationKey.Tab,
            _ => ExcelWorksheetNavigationKey.Other
        };
}
