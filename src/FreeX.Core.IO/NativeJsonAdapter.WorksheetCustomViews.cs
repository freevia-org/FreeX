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
            sheetDto.FitToPage,
            PrintSettings: ToCustomViewPrintSettings(sheetDto.PrintSettings),
            PrintAreaReferenceForms: ParsePrintAreaForms(sheetDto.PrintAreaReferenceForms, sheetId),
            OpaquePrintAreaExpression: sheetDto.OpaquePrintAreaExpression,
            OpaquePrintTitlesExpression: sheetDto.OpaquePrintTitlesExpression);
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
            OpaquePrintAreaExpression = state.OpaquePrintAreaExpression,
            OpaquePrintTitlesExpression = state.OpaquePrintTitlesExpression,
            PrintAreaReferenceForms = state.PrintAreaReferenceForms?.Select(form => new CustomViewPrintAreaFormDto
            {
                Range = form.Range.ToString(), Kind = form.Kind
            }).ToList(),
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
            FitToPage = state.FitToPage,
            PrintSettings = FromCustomViewPrintSettings(state.PrintSettings)
        };
    }

    // The wrapper's presence distinguishes an uncaptured print facet from a captured
    // snapshot whose optional settings (such as FitToPage) are null.
    private static WorksheetCustomViewPrintSettings? ToCustomViewPrintSettings(CustomViewPrintSettingsDto? dto) =>
        dto is null ? null : new WorksheetCustomViewPrintSettings
        {
            PageHeader = ToHeaderFooter(dto.PageHeader),
            PageFooter = ToHeaderFooter(dto.PageFooter),
            FirstPageHeader = ToHeaderFooter(dto.FirstPageHeader),
            FirstPageFooter = ToHeaderFooter(dto.FirstPageFooter),
            EvenPageHeader = ToHeaderFooter(dto.EvenPageHeader),
            EvenPageFooter = ToHeaderFooter(dto.EvenPageFooter),
            DifferentFirstPageHeaderFooter = dto.DifferentFirstPageHeaderFooter,
            DifferentOddEvenHeaderFooter = dto.DifferentOddEvenHeaderFooter,
            HeaderFooterScaleWithDocument = dto.HeaderFooterScaleWithDocument,
            HeaderFooterAlignWithMargins = dto.HeaderFooterAlignWithMargins,
            CenterHorizontallyOnPage = dto.CenterHorizontallyOnPage,
            CenterVerticallyOnPage = dto.CenterVerticallyOnPage,
            UsePrinterDefaults = dto.UsePrinterDefaults,
            PrintBlackAndWhite = dto.PrintBlackAndWhite,
            PrintDraftQuality = dto.PrintDraftQuality,
            AutoPageBreaksCaptured = dto.AutoPageBreaksCaptured ?? dto.AutoPageBreaks is not null,
            AutoPageBreaks = dto.AutoPageBreaks,
            FitToPage = dto.FitToPage,
            LegacyPrintSizeCaptured = dto.LegacyPrintSizeCaptured ?? dto.LegacyPrintSize is not null,
            LegacyPrintSize = dto.LegacyPrintSize is > 0 and <= ushort.MaxValue ? dto.LegacyPrintSize : null,
            PageSetupMetadataCaptured = dto.PageSetupMetadataCaptured ?? dto.PageSetupMetadata is not null,
            PageSetupMetadata = ToWorksheetPageSetupMetadata(dto.PageSetupMetadata),
            HeaderFooterMetadataCaptured = dto.HeaderFooterMetadataCaptured ?? dto.HeaderFooterMetadata is not null,
            HeaderFooterMetadata = ToWorksheetHeaderFooterMetadata(dto.HeaderFooterMetadata),
            PageMarginsMetadataCaptured = dto.PageMarginsMetadataCaptured ?? dto.PageMarginsMetadata is not null,
            PageMarginsMetadata = ToWorksheetPageMarginsMetadata(dto.PageMarginsMetadata),
            PrintOptionsMetadataCaptured = dto.PrintOptionsMetadataCaptured ?? dto.PrintOptionsMetadata is not null,
            PrintOptionsMetadata = ToWorksheetPrintOptionsMetadata(dto.PrintOptionsMetadata),
            FirstPageNumber = dto.FirstPageNumber is > 0 ? dto.FirstPageNumber : null,
            PrintCopies = dto.PrintCopies is > 0 ? dto.PrintCopies : null,
            PrintQualityDpi = dto.PrintQualityDpi is > 0 ? dto.PrintQualityDpi : null,
            PrintQualityVerticalDpi = dto.PrintQualityVerticalDpi is > 0 ? dto.PrintQualityVerticalDpi : null,
            PageOrder = NativeJsonValueSanitizer.ValidEnumOrDefault(dto.PageOrder, WorksheetPageOrder.DownThenOver),
            PrintErrorValue = NativeJsonValueSanitizer.ValidEnumOrDefault(dto.PrintErrorValue, WorksheetPrintErrorValue.Displayed),
            PrintComments = NativeJsonValueSanitizer.ValidEnumOrDefault(dto.PrintComments, WorksheetPrintComments.None),
            PrintTitleRows = ToRepeatRange(dto.PrintTitleRows, CellAddress.MaxRow),
            PrintTitleColumns = ToRepeatRange(dto.PrintTitleColumns, CellAddress.MaxCol),
            RowPageBreaks = (dto.RowPageBreaks ?? []).Where(x => x is >= 2 and <= CellAddress.MaxRow).Distinct().OrderBy(x => x).ToArray(),
            RowPageBreaksMetadata = ToWorksheetPageBreaksMetadata(dto.RowPageBreaksMetadata),
            ColumnPageBreaks = (dto.ColumnPageBreaks ?? []).Where(x => x is >= 2 and <= CellAddress.MaxCol).Distinct().OrderBy(x => x).ToArray(),
            ColumnPageBreaksMetadata = ToWorksheetPageBreaksMetadata(dto.ColumnPageBreaksMetadata),
        };

    private static CustomViewPrintSettingsDto? FromCustomViewPrintSettings(WorksheetCustomViewPrintSettings? value) =>
        value is null ? null : new CustomViewPrintSettingsDto
        {
            PageHeader = FromHeaderFooter(value.PageHeader),
            PageFooter = FromHeaderFooter(value.PageFooter),
            FirstPageHeader = FromHeaderFooter(value.FirstPageHeader),
            FirstPageFooter = FromHeaderFooter(value.FirstPageFooter),
            EvenPageHeader = FromHeaderFooter(value.EvenPageHeader),
            EvenPageFooter = FromHeaderFooter(value.EvenPageFooter),
            DifferentFirstPageHeaderFooter = value.DifferentFirstPageHeaderFooter,
            DifferentOddEvenHeaderFooter = value.DifferentOddEvenHeaderFooter,
            HeaderFooterScaleWithDocument = value.HeaderFooterScaleWithDocument,
            HeaderFooterAlignWithMargins = value.HeaderFooterAlignWithMargins,
            CenterHorizontallyOnPage = value.CenterHorizontallyOnPage,
            CenterVerticallyOnPage = value.CenterVerticallyOnPage,
            UsePrinterDefaults = value.UsePrinterDefaults,
            PrintBlackAndWhite = value.PrintBlackAndWhite,
            PrintDraftQuality = value.PrintDraftQuality,
            AutoPageBreaksCaptured = value.AutoPageBreaksCaptured,
            AutoPageBreaks = value.AutoPageBreaks,
            FitToPage = value.FitToPage,
            LegacyPrintSizeCaptured = value.LegacyPrintSizeCaptured,
            LegacyPrintSize = value.LegacyPrintSize is > 0 and <= ushort.MaxValue ? value.LegacyPrintSize : null,
            PageSetupMetadataCaptured = value.PageSetupMetadataCaptured,
            PageSetupMetadata = FromWorksheetPageSetupMetadata(value.PageSetupMetadata),
            HeaderFooterMetadataCaptured = value.HeaderFooterMetadataCaptured,
            HeaderFooterMetadata = FromWorksheetHeaderFooterMetadata(value.HeaderFooterMetadata),
            PageMarginsMetadataCaptured = value.PageMarginsMetadataCaptured,
            PageMarginsMetadata = FromWorksheetPageMarginsMetadata(value.PageMarginsMetadata),
            PrintOptionsMetadataCaptured = value.PrintOptionsMetadataCaptured,
            PrintOptionsMetadata = FromWorksheetPrintOptionsMetadata(value.PrintOptionsMetadata),
            FirstPageNumber = value.FirstPageNumber is > 0 ? value.FirstPageNumber : null,
            PrintCopies = value.PrintCopies is > 0 ? value.PrintCopies : null,
            PrintQualityDpi = value.PrintQualityDpi is > 0 ? value.PrintQualityDpi : null,
            PrintQualityVerticalDpi = value.PrintQualityVerticalDpi is > 0 ? value.PrintQualityVerticalDpi : null,
            PageOrder = NativeJsonValueSanitizer.ValidEnumOrDefault(value.PageOrder, WorksheetPageOrder.DownThenOver),
            PrintErrorValue = NativeJsonValueSanitizer.ValidEnumOrDefault(value.PrintErrorValue, WorksheetPrintErrorValue.Displayed),
            PrintComments = NativeJsonValueSanitizer.ValidEnumOrDefault(value.PrintComments, WorksheetPrintComments.None),
            PrintTitleRows = FromValidRepeatRange(value.PrintTitleRows, CellAddress.MaxRow),
            PrintTitleColumns = FromValidRepeatRange(value.PrintTitleColumns, CellAddress.MaxCol),
            RowPageBreaks = value.RowPageBreaks.Where(x => x is >= 2 and <= CellAddress.MaxRow).Distinct().OrderBy(x => x).ToList(),
            RowPageBreaksMetadata = FromWorksheetPageBreaksMetadata(value.RowPageBreaksMetadata),
            ColumnPageBreaks = value.ColumnPageBreaks.Where(x => x is >= 2 and <= CellAddress.MaxCol).Distinct().OrderBy(x => x).ToList(),
            ColumnPageBreaksMetadata = FromWorksheetPageBreaksMetadata(value.ColumnPageBreaksMetadata),
        };

    private static IReadOnlyList<WorksheetCustomViewPrintAreaForm>? ParsePrintAreaForms(
        IEnumerable<CustomViewPrintAreaFormDto>? forms, SheetId sheetId)
    {
        if (forms is null) return null;
        var result = new List<WorksheetCustomViewPrintAreaForm>();
        foreach (var form in forms)
        {
            if (form is null || !Enum.IsDefined(form.Kind)) return null;
            try { result.Add(new WorksheetCustomViewPrintAreaForm(GridRange.Parse(form.Range, sheetId), form.Kind)); }
            catch (FormatException) { return null; }
        }
        return result;
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
