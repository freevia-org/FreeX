using FreeX.Core.Model;

namespace FreeX.Core.IO;

public sealed partial class NativeJsonAdapter
{
    // N13 (partial — see notes on ToWorksheetCustomViewState/ToCustomViewSheetDto below): the
    // hidden-rows/cols/filter and print-setting fields WorksheetCustomViewState gained in wave 1
    // (MODEL-A, Workbook.cs) are NOT yet round-tripped through native .fxl JSON. CustomViewSheetDto
    // (private class, declared in NativeJsonAdapter.Dto.cs — out of scope for this change) has no
    // matching properties, so there is nowhere to read/write them from in this file without editing
    // that DTO. The XLSX side (XlsxCustomViewMapper.cs) and the in-memory planner
    // (CustomViewStatePlanner.cs) are both fully wired; only this native-JSON leg is outstanding.
    private static WorksheetCustomViewState ToWorksheetCustomViewState(CustomViewSheetDto sheetDto)
    {
        var frozenRows = NativeJsonValueSanitizer.ValidFrozenRowsOrZero(sheetDto.FrozenRows);
        var frozenCols = NativeJsonValueSanitizer.ValidFrozenColumnsOrZero(sheetDto.FrozenCols);
        var hasFrozenPanes = frozenRows > 0 || frozenCols > 0;
        return new WorksheetCustomViewState(
            sheetDto.SheetName,
            Enum.IsDefined(sheetDto.ViewMode) ? sheetDto.ViewMode : WorksheetViewMode.Normal,
            frozenRows,
            frozenCols,
            hasFrozenPanes ? null : NativeJsonValueSanitizer.ValidRowPaneOrNull(sheetDto.SplitRow),
            hasFrozenPanes ? null : NativeJsonValueSanitizer.ValidColumnPaneOrNull(sheetDto.SplitColumn),
            sheetDto.ShowGridlines ?? true,
            sheetDto.ShowHeadings ?? true,
            sheetDto.ShowRulers ?? true,
            NativeJsonValueSanitizer.ValidZoomPercentOrDefault(sheetDto.ZoomPercent),
            sheetDto.ShowFormulas ?? false,
            NativeJsonValueSanitizer.ValidRowPaneOrNull(sheetDto.ActiveRow),
            NativeJsonValueSanitizer.ValidColumnPaneOrNull(sheetDto.ActiveCol),
            NativeJsonValueSanitizer.ValidRowPaneOrNull(sheetDto.ViewTopRow),
            NativeJsonValueSanitizer.ValidColumnPaneOrNull(sheetDto.ViewLeftCol));
    }

    private static WorksheetCustomViewState? ToWorksheetCustomViewState(
        CustomViewSheetDto? sheetDto,
        Workbook workbook,
        IReadOnlyDictionary<string, Sheet> loadedSheetsBySourceName)
    {
        if (string.IsNullOrWhiteSpace(sheetDto?.SheetName))
            return null;

        var state = ToWorksheetCustomViewState(sheetDto);
        var sheet = ResolveLoadedSheet(workbook, loadedSheetsBySourceName, state.SheetName);
        return sheet is null ? null : state with { SheetName = sheet.Name };
    }

    private static CustomViewSheetDto ToCustomViewSheetDto(WorksheetCustomViewState state)
    {
        var frozenRows = NativeJsonValueSanitizer.ValidFrozenRowsOrZero(state.FrozenRows);
        var frozenCols = NativeJsonValueSanitizer.ValidFrozenColumnsOrZero(state.FrozenCols);
        var hasFrozenPanes = frozenRows > 0 || frozenCols > 0;
        return new CustomViewSheetDto
        {
            SheetName = state.SheetName,
            ViewMode = NativeJsonValueSanitizer.ValidEnumOrDefault(state.ViewMode, WorksheetViewMode.Normal),
            FrozenRows = frozenRows,
            FrozenCols = frozenCols,
            SplitRow = hasFrozenPanes ? null : NativeJsonValueSanitizer.ValidRowPaneOrNull(state.SplitRow),
            SplitColumn = hasFrozenPanes ? null : NativeJsonValueSanitizer.ValidColumnPaneOrNull(state.SplitColumn),
            ShowGridlines = state.ShowGridlines,
            ShowHeadings = state.ShowHeadings,
            ShowRulers = state.ShowRulers,
            ZoomPercent = NativeJsonValueSanitizer.ValidZoomPercentOrDefault(state.ZoomPercent),
            ShowFormulas = state.ShowFormulas,
            ActiveRow = NativeJsonValueSanitizer.ValidRowPaneOrNull(state.ActiveRow),
            ActiveCol = NativeJsonValueSanitizer.ValidColumnPaneOrNull(state.ActiveCol),
            ViewTopRow = NativeJsonValueSanitizer.ValidRowPaneOrNull(state.ViewTopRow),
            ViewLeftCol = NativeJsonValueSanitizer.ValidColumnPaneOrNull(state.ViewLeftCol)
        };
    }
}
