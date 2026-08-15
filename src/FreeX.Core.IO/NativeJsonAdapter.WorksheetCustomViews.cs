using FreeX.Core.Model;

namespace FreeX.Core.IO;

public sealed partial class NativeJsonAdapter
{
    private static WorksheetCustomViewState ToWorksheetCustomViewState(CustomViewSheetDto sheetDto, SheetId sheetId)
    {
        var frozenRows = NativeJsonValueSanitizer.ValidFrozenRowsOrZero(sheetDto.FrozenRows);
        var frozenCols = NativeJsonValueSanitizer.ValidFrozenColumnsOrZero(sheetDto.FrozenCols);
        var hasFrozenPanes = frozenRows > 0 || frozenCols > 0;
        WorksheetScaleToFit? scaleToFit = sheetDto.ScaleToFit is { } scale
            ? NativeJsonValueSanitizer.ValidScaleToFitOrDefault(
                new WorksheetScaleToFit(scale.ScalePercent, scale.FitToPagesWide, scale.FitToPagesTall),
                WorksheetScaleToFit.Default)
            : null;
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
            NativeJsonValueSanitizer.ValidColumnPaneOrNull(sheetDto.ViewLeftCol),
            SanitizeRows(sheetDto.HiddenRows),
            SanitizeColumns(sheetDto.HiddenCols),
            SanitizeRows(sheetDto.FilterHiddenRows),
            ToWorksheetAutoFilter(sheetDto.AutoFilter, sheetId),
            ParsePrintAreas(sheetDto.PrintAreas, sheetId),
            NativeJsonValueSanitizer.ValidNullableEnumOrNull(sheetDto.PageOrientation),
            NativeJsonValueSanitizer.ValidNullableEnumOrNull(sheetDto.PaperSize),
            sheetDto.PaperSizeCode is > 0 ? sheetDto.PaperSizeCode : null,
            sheetDto.PageMargins is { } margins
                ? NativeJsonValueSanitizer.ValidPageMarginsOrDefault(
                    new WorksheetPageMargins(margins.Left, margins.Right, margins.Top, margins.Bottom),
                    WorksheetPageMargins.Narrow)
                : null,
            ValidNonNegativeFiniteOrNull(sheetDto.HeaderMargin),
            ValidNonNegativeFiniteOrNull(sheetDto.FooterMargin),
            sheetDto.PrintGridlines,
            sheetDto.PrintHeadings,
            scaleToFit,
            sheetDto.FitToPage);
    }

    private static WorksheetCustomViewState? ToWorksheetCustomViewState(
        CustomViewSheetDto? sheetDto,
        Workbook workbook,
        IReadOnlyDictionary<string, Sheet> loadedSheetsBySourceName)
    {
        if (string.IsNullOrWhiteSpace(sheetDto?.SheetName))
            return null;

        var sheet = ResolveLoadedSheet(workbook, loadedSheetsBySourceName, sheetDto.SheetName);
        return sheet is null
            ? null
            : ToWorksheetCustomViewState(sheetDto, sheet.Id) with { SheetName = sheet.Name };
    }

    private static CustomViewSheetDto ToCustomViewSheetDto(WorksheetCustomViewState state)
    {
        var frozenRows = NativeJsonValueSanitizer.ValidFrozenRowsOrZero(state.FrozenRows);
        var frozenCols = NativeJsonValueSanitizer.ValidFrozenColumnsOrZero(state.FrozenCols);
        var hasFrozenPanes = frozenRows > 0 || frozenCols > 0;
        var serializedSheetId = state.PrintAreas is { Count: > 0 }
            ? state.PrintAreas[0].Start.Sheet
            : SheetId.New();
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
            ViewLeftCol = NativeJsonValueSanitizer.ValidColumnPaneOrNull(state.ViewLeftCol),
            HiddenRows = SanitizeRows(state.HiddenRows)?.ToList(),
            HiddenCols = SanitizeColumns(state.HiddenCols)?.ToList(),
            FilterHiddenRows = SanitizeRows(state.FilterHiddenRows)?.ToList(),
            AutoFilter = ToWorksheetAutoFilterDto(state.AutoFilter, serializedSheetId),
            PrintAreas = state.PrintAreas?.Select(range => range.ToString()).ToArray(),
            PageOrientation = NativeJsonValueSanitizer.ValidNullableEnumOrNull(state.PageOrientation),
            PaperSize = NativeJsonValueSanitizer.ValidNullableEnumOrNull(state.PaperSize),
            PaperSizeCode = state.PaperSizeCode is > 0 ? state.PaperSizeCode : null,
            PageMargins = state.PageMargins is { } margins
                ? FromPageMargins(NativeJsonValueSanitizer.ValidPageMarginsOrDefault(margins, WorksheetPageMargins.Narrow))
                : null,
            HeaderMargin = ValidNonNegativeFiniteOrNull(state.HeaderMargin),
            FooterMargin = ValidNonNegativeFiniteOrNull(state.FooterMargin),
            PrintGridlines = state.PrintGridlines,
            PrintHeadings = state.PrintHeadings,
            ScaleToFit = state.ScaleToFit is { } scale
                ? ToScaleToFitDto(NativeJsonValueSanitizer.ValidScaleToFitOrDefault(scale, WorksheetScaleToFit.Default))
                : null,
            FitToPage = state.FitToPage
        };
    }

    private static IReadOnlyList<uint>? SanitizeRows(IEnumerable<uint>? rows) =>
        rows?.Where(NativeJsonValueSanitizer.IsValidRowIndex).Distinct().OrderBy(row => row).ToArray();

    private static IReadOnlyList<uint>? SanitizeColumns(IEnumerable<uint>? columns) =>
        columns?.Where(NativeJsonValueSanitizer.IsValidColumnIndex).Distinct().OrderBy(column => column).ToArray();

    private static IReadOnlyList<GridRange>? ParsePrintAreas(string[]? references, SheetId sheetId)
    {
        if (references is null)
            return null;

        var areas = new List<GridRange>(references.Length);
        foreach (var reference in references)
        {
            if (string.IsNullOrWhiteSpace(reference))
                continue;
            try { areas.Add(GridRange.Parse(reference, sheetId)); }
            catch (FormatException) { }
        }
        return areas;
    }

    private static double? ValidNonNegativeFiniteOrNull(double? value) =>
        value is { } concrete && NativeJsonValueSanitizer.IsNonNegativeFinite(concrete) ? concrete : null;

    private static ScaleToFitDto ToScaleToFitDto(WorksheetScaleToFit scale) =>
        new()
        {
            ScalePercent = scale.ScalePercent,
            FitToPagesWide = scale.FitToPagesWide,
            FitToPagesTall = scale.FitToPagesTall
        };
}
