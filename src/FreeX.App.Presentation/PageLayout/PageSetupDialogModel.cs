using System.Globalization;

using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.PageLayout;

/// <summary>
/// Non-UI glue backing the cross-platform Page Setup dialog. It mirrors the active sheet's page-setup
/// state into discrete dialog fields, resolves the scaling choice (adjust-to percent vs. fit-to W×H),
/// validates and parses the free-text inputs (margins, scale, print area, print titles, first-page
/// number, print quality), and emits the commands the shell runs through the workbook session
/// (<see cref="SetPageSetupCommand"/> for the sheet/page options, <see cref="SetHeaderFooterCommand"/>
/// for headers/footers). None of this depends on a running UI, so it is unit-tested directly and the
/// macOS/Windows shells reuse it; UI code-behind only wires controls to these fields.
/// </summary>

/// <summary>Which of the two mutually exclusive scaling modes the dialog is editing.</summary>
public enum PageSetupScalingMode
{
    AdjustToPercent,
    FitToPages
}

/// <summary>A renderer-neutral Page Setup combo-box row. Shells resolve <see cref="LabelResourceKey"/> locally.</summary>
public sealed record PageSetupChoice<T>(T Value, string LabelResourceKey);

/// <summary>The editable dialog field that caused shared Page Setup validation to fail.</summary>
public enum PageSetupValidationTarget
{
    Orientation,
    PaperSize,
    Margins,
    HeaderMargin,
    FooterMargin,
    Scaling,
    FirstPageNumber,
    PrintQuality,
    PrintArea,
    RepeatRows,
    RepeatColumns,
    PageOrder,
    PrintErrorValue,
    PrintComments
}

/// <summary>Renderer-neutral Page Setup dialog tabs used when routing shared validation failures.</summary>
public enum PageSetupDialogTab
{
    Page,
    Margins,
    Sheet
}

/// <summary>Renderer-neutral Page Setup dialog fields used when routing shared validation failures.</summary>
public enum PageSetupDialogField
{
    Orientation,
    PaperSize,
    Margins,
    HeaderMargin,
    FooterMargin,
    Scaling,
    FirstPageNumber,
    PrintQuality,
    PrintArea,
    RepeatRows,
    RepeatColumns,
    PageOrder,
    PrintErrorValue,
    PrintComments
}

public sealed record PageSetupValidationRoute(PageSetupDialogTab Tab, PageSetupDialogField Field);

/// <summary>
/// A snapshot of every editable page-setup field, decoupled from the Core.Model so the dialog can edit
/// strings/enums without mutating the sheet until the user commits. Built from a <see cref="Sheet"/>
/// via <see cref="PageSetupDialogModel.FromSheet"/> and turned back into commands via
/// <see cref="PageSetupDialogModel.TryBuildCommand"/> / <see cref="PageSetupDialogModel.TryBuildCommandPlan"/>.
/// </summary>
public sealed record PageSetupDialogFields
{
    public WorksheetPageOrientation Orientation { get; init; } = WorksheetPageOrientation.Portrait;
    public WorksheetPaperSize PaperSize { get; init; } = WorksheetPaperSize.A4;

    /// <summary>Margins as a comma-separated "left, right, top, bottom" inch string (parser format).</summary>
    public string MarginsText { get; init; } = "0.5, 0.5, 0.5, 0.5";

    /// <summary>Header margin (inches) text; blank keeps the current value.</summary>
    public string HeaderMarginText { get; init; } = "0.3";

    /// <summary>Footer margin (inches) text; blank keeps the current value.</summary>
    public string FooterMarginText { get; init; } = "0.3";

    public bool CenterHorizontally { get; init; }
    public bool CenterVertically { get; init; }

    public PageSetupScalingMode ScalingMode { get; init; } = PageSetupScalingMode.AdjustToPercent;

    /// <summary>Adjust-to percent text (e.g. "100"); only consulted when <see cref="ScalingMode"/> is AdjustToPercent.</summary>
    public string ScalePercentText { get; init; } = "100";

    /// <summary>Fit-to pages-wide text; "" / "auto" means automatic (no horizontal cap).</summary>
    public string FitToWideText { get; init; } = "1";

    /// <summary>Fit-to pages-tall text; "" / "auto" means automatic (no vertical cap).</summary>
    public string FitToTallText { get; init; } = "1";

    /// <summary>First page number text; "" / "auto" means automatic (sheet's first-page number cleared).</summary>
    public string FirstPageNumberText { get; init; } = "";

    /// <summary>Print quality in DPI; "" means use the printer/default (no explicit DPI).</summary>
    public string PrintQualityDpiText { get; init; } = "";

    /// <summary>Print area, e.g. "A1:D20"; empty clears the explicit print area.</summary>
    public string PrintAreaText { get; init; } = "";

    /// <summary>Print-title repeat rows, e.g. "1:2"; empty / "none" clears them.</summary>
    public string RepeatRowsText { get; init; } = "";

    /// <summary>Print-title repeat columns, e.g. "A:B"; empty / "none" clears them.</summary>
    public string RepeatColumnsText { get; init; } = "";

    public bool PrintGridlines { get; init; }
    public bool PrintHeadings { get; init; }
    public bool PrintBlackAndWhite { get; init; }
    public bool PrintDraftQuality { get; init; }
    public WorksheetPrintErrorValue PrintErrorValue { get; init; } = WorksheetPrintErrorValue.Displayed;
    public WorksheetPrintComments PrintComments { get; init; } = WorksheetPrintComments.None;
    public WorksheetPageOrder PageOrder { get; init; } = WorksheetPageOrder.DownThenOver;

    public HeaderFooterEditorState HeaderFooter { get; init; } = HeaderFooterEditorState.Empty;
}

/// <summary>
/// The complete shared Page Setup command plan, or the first validation error that prevented building it.
/// </summary>
public sealed record PageSetupCommandPlanBuildResult(
    PageSetupCommandPlan? Plan,
    string? Error,
    PageSetupValidationTarget? Target = null)
{
    public bool Success => Plan is not null;

    public static PageSetupCommandPlanBuildResult Ok(PageSetupCommandPlan plan) => new(plan, null);
    public static PageSetupCommandPlanBuildResult Fail(string error, PageSetupValidationTarget? target = null) =>
        new(null, error, target);
}

public static class PageSetupDialogModel
{
    public static IReadOnlyList<PageSetupChoice<WorksheetPageOrientation>> OrientationChoices { get; } =
    [
        new(WorksheetPageOrientation.Portrait, "PageSetup_Portrait"),
        new(WorksheetPageOrientation.Landscape, "PageSetup_Landscape"),
    ];

    public static IReadOnlyList<PageSetupChoice<WorksheetPaperSize>> PaperSizeChoices { get; } =
    [
        new(WorksheetPaperSize.Letter,    "PageSetup_Letter85X11"),
        new(WorksheetPaperSize.A4,        "PageSetup_A4210X297Mm"),
        new(WorksheetPaperSize.Legal,     "PageSetup_Legal85X14"),
        new(WorksheetPaperSize.Tabloid,   "PageSetup_Tabloid11X17"),
        new(WorksheetPaperSize.Executive, "PageSetup_Executive725X105"),
        new(WorksheetPaperSize.A3,        "PageSetup_A3297X420Mm"),
        new(WorksheetPaperSize.A5,        "PageSetup_A5148X210Mm"),
        new(WorksheetPaperSize.B4,        "PageSetup_B4257X364Mm"),
        new(WorksheetPaperSize.B5,        "PageSetup_B5176X250Mm"),
    ];

    /// <summary>The paper sizes the dialog offers, in display order.</summary>
    public static IReadOnlyList<WorksheetPaperSize> PaperSizes { get; } =
        PaperSizeChoices.Select(choice => choice.Value).ToArray();

    public static IReadOnlyList<PageSetupChoice<WorksheetPageOrder>> PageOrderChoices { get; } =
    [
        new(WorksheetPageOrder.DownThenOver, "PageSetup_DownThenOver"),
        new(WorksheetPageOrder.OverThenDown, "PageSetup_OverThenDown"),
    ];

    public static IReadOnlyList<PageSetupChoice<WorksheetPrintErrorValue>> PrintErrorValueChoices { get; } =
    [
        new(WorksheetPrintErrorValue.Displayed, "PageSetup_ErrorsDisplayed"),
        new(WorksheetPrintErrorValue.Blank, "PageSetup_ErrorsBlank"),
        new(WorksheetPrintErrorValue.Dash, "PageSetup_ErrorsDash"),
        new(WorksheetPrintErrorValue.NotAvailable, "PageSetup_ErrorsNotAvailable"),
    ];

    public static IReadOnlyList<PageSetupChoice<WorksheetPrintComments>> PrintCommentChoices { get; } =
    [
        new(WorksheetPrintComments.None, "PageSetup_CommentsNone"),
        new(WorksheetPrintComments.AtEnd, "PageSetup_CommentsAtEnd"),
        new(WorksheetPrintComments.AsDisplayed, "PageSetup_CommentsAsDisplayed"),
    ];

    /// <summary>The Page Setup header center presets the dialog offers. The value is the persisted token text; "" means none.</summary>
    public static IReadOnlyList<PageSetupChoice<string>> HeaderPresetChoices { get; } =
    [
        new("", "PageSetup_None"),
        new("&[Page]", "PageSetup_Page1"),
        new("Page &[Page] of &[Pages]", "PageSetup_Page1Of"),
        new("&[Tab]", "PageSetup_Sheet1"),
        new("&[File]", "PageSetup_Book1"),
        new("&[File]", "PageSetup_Book1Xlsx"),
        new("&[File], &[Tab]", "PageSetup_Book1XlsxSheet1"),
        new("Confidential, Page &[Page]", "PageSetup_ConfidentialPage1"),
        new("&[Date], Page &[Page]", "PageSetup_DatePage1"),
        new("&[Tab]", "PageSetup_SheetName"),
        new("&[File]", "PageSetup_FileName"),
        new("&[Path]&[File]", "PageSetup_FilePath"),
    ];

    /// <summary>The Page Setup footer center presets the dialog offers. The value is the persisted token text; "" means none.</summary>
    public static IReadOnlyList<PageSetupChoice<string>> FooterPresetChoices { get; } =
    [
        new("", "PageSetup_None"),
        new("&[Page]", "PageSetup_Page1"),
        new("Page &[Page] of &[Pages]", "PageSetup_Page1Of"),
        new("&[Tab]", "PageSetup_Sheet1"),
        new("&[File]", "PageSetup_Book1"),
        new("&[File]", "PageSetup_Book1Xlsx"),
        new("&[File], &[Tab]", "PageSetup_Book1XlsxSheet1"),
        new("&[Date]", "PageSetup_Date"),
        new("&[Time]", "PageSetup_Time"),
        new("&[Date], Page &[Page]", "PageSetup_DatePage1"),
        new("&[Path]&[File]", "PageSetup_FilePath"),
        new("&[File]", "PageSetup_FileName"),
    ];

    /// <summary>The compact cross-platform Page Setup preset catalog used by shells that render one shared header/footer list.</summary>
    public static IReadOnlyList<PageSetupChoice<string>> HeaderFooterPresetChoices { get; } =
    [
        new("", "PageSetup_None"),
        new("&[Page]", "PageSetup_PresetPage"),
        new("Page &[Page] of &[Pages]", "PageSetup_PresetPageOf"),
        new("&[Tab]", "PageSetup_PresetSheetName"),
        new("&[File]", "PageSetup_PresetFileName"),
        new("&[File], &[Tab]", "PageSetup_PresetFileSheet"),
        new("&[Date]", "PageSetup_PresetDate"),
        new("&[Time]", "PageSetup_PresetTime"),
        new("&[Date], Page &[Page]", "PageSetup_PresetDatePage"),
        new("Confidential, Page &[Page]", "PageSetup_PresetConfidential"),
        new("&[Path]&[File]", "PageSetup_PresetFilePath"),
    ];

    /// <summary>The compact preset values in display order. Prefer the choice catalogs when labels are needed.</summary>
    public static IReadOnlyList<string> HeaderFooterPresets { get; } =
        HeaderFooterPresetChoices.Select(choice => choice.Value).ToArray();

    public static int HeaderFooterPresetIndex(IReadOnlyList<PageSetupChoice<string>> choices, string centerText) =>
        ChoiceIndex(choices, centerText, "");

    public static int HeaderFooterPresetExactIndex(IReadOnlyList<PageSetupChoice<string>> choices, string centerText)
    {
        ArgumentNullException.ThrowIfNull(choices);

        for (var index = 0; index < choices.Count; index++)
        {
            if (choices[index].Value == centerText)
                return index;
        }

        return -1;
    }

    public static string HeaderFooterPresetValue(IReadOnlyList<PageSetupChoice<string>> choices, int selectedIndex) =>
        ChoiceValue(choices, selectedIndex, "");

    public static string BuildHeaderFooterPreview(WorksheetHeaderFooter value, string noneText)
    {
        var parts = new[] { value.Left, value.Center, value.Right }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();
        return parts.Length == 0 ? noneText : string.Join(" | ", parts);
    }

    /// <summary>Friendly label for a paper size shown in the combo box.</summary>
    public static string DescribePaperSize(WorksheetPaperSize paperSize) =>
        paperSize switch
        {
            WorksheetPaperSize.Letter    => "Letter (8.5\" x 11\")",
            WorksheetPaperSize.Legal     => "Legal (8.5\" x 14\")",
            WorksheetPaperSize.Tabloid   => "Tabloid (11\" x 17\")",
            WorksheetPaperSize.Executive => "Executive (7.25\" x 10.5\")",
            WorksheetPaperSize.A3        => "A3 (297mm x 420mm)",
            WorksheetPaperSize.A5        => "A5 (148mm x 210mm)",
            WorksheetPaperSize.B5        => "B5 (176mm x 250mm)",
            WorksheetPaperSize.Ledger    => "Ledger (17\" x 11\")",
            WorksheetPaperSize.Statement => "Statement (5.5\" x 8.5\")",
            WorksheetPaperSize.B4        => "B4 (257mm x 364mm)",
            WorksheetPaperSize.Folio     => "Folio (8.5\" x 13\")",
            _                            => "A4 (210mm x 297mm)"
        };

    public static int ChoiceIndex<T>(IReadOnlyList<PageSetupChoice<T>> choices, T value, T fallback)
    {
        ArgumentNullException.ThrowIfNull(choices);
        var comparer = EqualityComparer<T>.Default;
        var fallbackIndex = -1;

        for (var index = 0; index < choices.Count; index++)
        {
            var choice = choices[index];
            if (comparer.Equals(choice.Value, value))
                return index;

            if (fallbackIndex < 0 && comparer.Equals(choice.Value, fallback))
                fallbackIndex = index;
        }

        return fallbackIndex >= 0
            ? fallbackIndex
            : choices.Count > 0 ? 0 : -1;
    }

    public static T ChoiceValue<T>(IReadOnlyList<PageSetupChoice<T>> choices, int selectedIndex, T fallback)
    {
        ArgumentNullException.ThrowIfNull(choices);
        if (selectedIndex >= 0 && selectedIndex < choices.Count)
            return choices[selectedIndex].Value;

        var fallbackIndex = ChoiceIndex(choices, fallback, fallback);
        return fallbackIndex >= 0 ? choices[fallbackIndex].Value : fallback;
    }

    public static PageSetupValidationRoute GetValidationRoute(PageSetupValidationTarget? target) =>
        target switch
        {
            PageSetupValidationTarget.PaperSize => new(PageSetupDialogTab.Page, PageSetupDialogField.PaperSize),
            PageSetupValidationTarget.Margins => new(PageSetupDialogTab.Margins, PageSetupDialogField.Margins),
            PageSetupValidationTarget.HeaderMargin => new(PageSetupDialogTab.Margins, PageSetupDialogField.HeaderMargin),
            PageSetupValidationTarget.FooterMargin => new(PageSetupDialogTab.Margins, PageSetupDialogField.FooterMargin),
            PageSetupValidationTarget.Scaling => new(PageSetupDialogTab.Page, PageSetupDialogField.Scaling),
            PageSetupValidationTarget.FirstPageNumber => new(PageSetupDialogTab.Page, PageSetupDialogField.FirstPageNumber),
            PageSetupValidationTarget.PrintQuality => new(PageSetupDialogTab.Page, PageSetupDialogField.PrintQuality),
            PageSetupValidationTarget.PrintArea => new(PageSetupDialogTab.Sheet, PageSetupDialogField.PrintArea),
            PageSetupValidationTarget.RepeatRows => new(PageSetupDialogTab.Sheet, PageSetupDialogField.RepeatRows),
            PageSetupValidationTarget.RepeatColumns => new(PageSetupDialogTab.Sheet, PageSetupDialogField.RepeatColumns),
            PageSetupValidationTarget.PageOrder => new(PageSetupDialogTab.Sheet, PageSetupDialogField.PageOrder),
            PageSetupValidationTarget.PrintErrorValue => new(PageSetupDialogTab.Sheet, PageSetupDialogField.PrintErrorValue),
            PageSetupValidationTarget.PrintComments => new(PageSetupDialogTab.Sheet, PageSetupDialogField.PrintComments),
            _ => new(PageSetupDialogTab.Page, PageSetupDialogField.Orientation),
        };

    /// <summary>Reads the editable page-setup state of <paramref name="sheet"/> into dialog fields.</summary>
    public static PageSetupDialogFields FromSheet(Sheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        var scaleToFit = sheet.ScaleToFit;
        var usesPercent = scaleToFit.ScalePercent is not null ||
            (scaleToFit.FitToPagesWide is null && scaleToFit.FitToPagesTall is null);

        return new PageSetupDialogFields
        {
            Orientation = sheet.PageOrientation,
            PaperSize = sheet.PaperSize,
            MarginsText = FormatMargins(sheet.PageMargins),
            HeaderMarginText = FormatMargin(sheet.HeaderMargin),
            FooterMarginText = FormatMargin(sheet.FooterMargin),
            CenterHorizontally = sheet.CenterHorizontallyOnPage,
            CenterVertically = sheet.CenterVerticallyOnPage,
            ScalingMode = usesPercent ? PageSetupScalingMode.AdjustToPercent : PageSetupScalingMode.FitToPages,
            ScalePercentText = (scaleToFit.ScalePercent ?? 100).ToString(CultureInfo.InvariantCulture),
            FitToWideText = FormatFitTo(scaleToFit.FitToPagesWide),
            FitToTallText = FormatFitTo(scaleToFit.FitToPagesTall),
            FirstPageNumberText = sheet.FirstPageNumber?.ToString(CultureInfo.InvariantCulture) ?? "",
            PrintQualityDpiText = sheet.PrintQualityDpi?.ToString(CultureInfo.InvariantCulture) ?? "",
            PrintAreaText = FormatPrintAreas(sheet.PrintAreas, sheet.Id),
            RepeatRowsText = FormatRepeatRows(sheet.PrintTitleRows),
            RepeatColumnsText = FormatRepeatColumns(sheet.PrintTitleColumns),
            PrintGridlines = sheet.PrintGridlines,
            PrintHeadings = sheet.PrintHeadings,
            PrintBlackAndWhite = sheet.PrintBlackAndWhite,
            PrintDraftQuality = sheet.PrintDraftQuality,
            PrintErrorValue = sheet.PrintErrorValue,
            PrintComments = sheet.PrintComments,
            PageOrder = sheet.PageOrder,
            HeaderFooter = HeaderFooterEditorState.FromSheet(sheet),
        };
    }

    /// <summary>
    /// Validates dialog fields and builds the shared command plan. The renderer decides whether to run
    /// the commands separately or compose them into a single undoable command.
    /// </summary>
    public static PageSetupCommandPlanBuildResult TryBuildCommandPlan(
        Sheet sheet,
        PageSetupDialogFields fields) =>
        TryBuildCommandPlan(sheet, fields, sheet.Id);

    /// <summary>
    /// Validates dialog fields and builds the shared command plan for a grouped edit target.
    /// </summary>
    public static PageSetupCommandPlanBuildResult TryBuildCommandPlan(
        Sheet sheet,
        PageSetupDialogFields fields,
        SheetId targetSheetId)
    {
        var request = TryBuildRequest(sheet, fields);
        return request.Success
            ? PageSetupCommandPlanBuildResult.Ok(PageSetupCommandFactory.Build(targetSheetId, request.Request!))
            : PageSetupCommandPlanBuildResult.Fail(request.Error ?? "Page setup is invalid.", request.Target);
    }

    /// <summary>
    /// Builds the companion <see cref="SetHeaderFooterCommand"/> that applies the dialog's header/footer
    /// text and picture state.
    /// </summary>
    public static SetHeaderFooterCommand BuildHeaderFooterCommand(Sheet sheet, PageSetupDialogFields fields) =>
        BuildHeaderFooterCommand(sheet, fields, sheet.Id);

    /// <summary>
    /// Builds the companion <see cref="SetHeaderFooterCommand"/> for a grouped edit target.
    /// </summary>
    public static SetHeaderFooterCommand BuildHeaderFooterCommand(
        Sheet sheet,
        PageSetupDialogFields fields,
        SheetId targetSheetId)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(fields);

        return PageSetupCommandFactory.BuildHeaderFooterCommand(targetSheetId, fields.HeaderFooter);
    }

    /// <summary>
    /// Resolves the dialog's scaling choice into a <see cref="WorksheetScaleToFit"/>. Adjust-to mode
    /// yields an explicit percent (10–400). Fit-to mode yields the pages-wide/tall caps, each optional
    /// (blank / "auto" maps to null = no cap on that axis); fitting to zero pages is rejected.
    /// </summary>
    public static bool TryResolveScaleToFit(
        PageSetupDialogFields fields,
        out WorksheetScaleToFit scaleToFit,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(fields);
        scaleToFit = WorksheetScaleToFit.Default;
        error = null;

        if (fields.ScalingMode == PageSetupScalingMode.AdjustToPercent)
        {
            if (!int.TryParse(fields.ScalePercentText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var percent) ||
                percent is < 10 or > 400)
            {
                error = "Scale must be a whole percent between 10 and 400.";
                return false;
            }

            scaleToFit = new WorksheetScaleToFit(percent, null, null);
            return true;
        }

        if (!TryParseFitToPages(fields.FitToWideText, out var wide))
        {
            error = "Pages wide must be a positive whole number or blank for automatic.";
            return false;
        }

        if (!TryParseFitToPages(fields.FitToTallText, out var tall))
        {
            error = "Pages tall must be a positive whole number or blank for automatic.";
            return false;
        }

        if (wide is null && tall is null)
        {
            error = "Enter at least one fit-to page count, or use Adjust to.";
            return false;
        }

        scaleToFit = new WorksheetScaleToFit(null, wide, tall);
        return true;
    }

    /// <summary>Parses the print-area free-text field; blank input yields a null range (clear).</summary>
    public static bool TryParsePrintArea(string input, SheetId sheetId, out GridRange? printArea) =>
        PageLayoutInputParser.TryParseOptionalPrintArea(input, sheetId, out printArea);

    /// <summary>
    /// Parses the print-area free-text field as a comma-separated list of ranges (Excel's multi-region
    /// print area, e.g. "A1:C10,E1:G10"). Blank input yields an empty list (clear all regions); any
    /// invalid segment fails the whole parse.
    /// </summary>
    public static bool TryParsePrintAreas(string input, SheetId sheetId, out IReadOnlyList<GridRange> printAreas)
    {
        printAreas = [];
        var trimmed = (input ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            return true;

        var areas = new List<GridRange>();
        foreach (var segment in trimmed.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!TryParsePrintArea(segment, sheetId, out var area) || area is not { } range)
                return false;

            areas.Add(range);
        }

        if (areas.Count == 0)
            return false;

        printAreas = areas;
        return true;
    }

    private static PageSetupRequestBuildResult TryBuildRequest(Sheet sheet, PageSetupDialogFields fields)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(fields);

        if (!Enum.IsDefined(fields.Orientation))
            return PageSetupRequestBuildResult.Fail("Choose a page orientation.", PageSetupValidationTarget.Orientation);
        if (!Enum.IsDefined(fields.PaperSize))
            return PageSetupRequestBuildResult.Fail("Choose a paper size.", PageSetupValidationTarget.PaperSize);
        if (!Enum.IsDefined(fields.PageOrder))
            return PageSetupRequestBuildResult.Fail("Choose a page order.", PageSetupValidationTarget.PageOrder);
        if (!Enum.IsDefined(fields.PrintErrorValue))
            return PageSetupRequestBuildResult.Fail("Choose how cell errors print.", PageSetupValidationTarget.PrintErrorValue);
        if (!Enum.IsDefined(fields.PrintComments))
            return PageSetupRequestBuildResult.Fail("Choose how comments print.", PageSetupValidationTarget.PrintComments);

        if (!PageMarginInputParser.TryParse(fields.MarginsText, out var margins, out var marginError))
            return PageSetupRequestBuildResult.Fail(marginError ?? "Margins are invalid.", PageSetupValidationTarget.Margins);

        if (!TryParseMargin(fields.HeaderMarginText, sheet.HeaderMargin, out var headerMargin))
            return PageSetupRequestBuildResult.Fail("Header margin must be a non-negative number of inches.", PageSetupValidationTarget.HeaderMargin);

        if (!TryParseMargin(fields.FooterMarginText, sheet.FooterMargin, out var footerMargin))
            return PageSetupRequestBuildResult.Fail("Footer margin must be a non-negative number of inches.", PageSetupValidationTarget.FooterMargin);

        if (!TryResolveScaleToFit(fields, out var scaleToFit, out var scaleError))
            return PageSetupRequestBuildResult.Fail(scaleError!, PageSetupValidationTarget.Scaling);

        if (!TryParseFirstPageNumber(fields.FirstPageNumberText, out var firstPageNumber))
            return PageSetupRequestBuildResult.Fail("First page number must be a positive whole number or blank for automatic.", PageSetupValidationTarget.FirstPageNumber);

        if (!TryParsePrintQualityDpi(fields.PrintQualityDpiText, out var printQualityDpi))
            return PageSetupRequestBuildResult.Fail("Print quality must be a positive DPI value or blank.", PageSetupValidationTarget.PrintQuality);

        if (!TryParsePrintAreas(fields.PrintAreaText, sheet.Id, out var printAreas))
            return PageSetupRequestBuildResult.Fail("Print area must be a cell range like A1:D20.", PageSetupValidationTarget.PrintArea);

        if (!PageLayoutInputParser.TryParseRepeatRows(fields.RepeatRowsText, out var repeatRows))
            return PageSetupRequestBuildResult.Fail("Rows to repeat at top must be a row range like 1:2.", PageSetupValidationTarget.RepeatRows);

        if (!PageLayoutInputParser.TryParseRepeatColumns(fields.RepeatColumnsText, out var repeatColumns))
            return PageSetupRequestBuildResult.Fail("Columns to repeat at left must be a column range like A:B.", PageSetupValidationTarget.RepeatColumns);

        return PageSetupRequestBuildResult.Ok(new PageSetupCommandRequest
        {
            PrintAreas = printAreas,
            Orientation = fields.Orientation,
            PaperSize = fields.PaperSize,
            Margins = margins,
            PrintGridlines = fields.PrintGridlines,
            PrintHeadings = fields.PrintHeadings,
            ScaleToFit = scaleToFit,
            PrintTitleRows = repeatRows,
            PrintTitleColumns = repeatColumns,
            CenterHorizontally = fields.CenterHorizontally,
            CenterVertically = fields.CenterVertically,
            PageOrder = fields.PageOrder,
            FirstPageNumber = firstPageNumber,
            HeaderMargin = headerMargin,
            FooterMargin = footerMargin,
            PrintBlackAndWhite = fields.PrintBlackAndWhite,
            PrintDraftQuality = fields.PrintDraftQuality,
            PrintQualityDpi = printQualityDpi,
            PrintErrorValue = fields.PrintErrorValue,
            PrintComments = fields.PrintComments,
            HeaderFooter = fields.HeaderFooter.DeepClone()
        });
    }

    private sealed record PageSetupRequestBuildResult(
        PageSetupCommandRequest? Request,
        string? Error,
        PageSetupValidationTarget? Target = null)
    {
        public bool Success => Request is not null;

        public static PageSetupRequestBuildResult Ok(PageSetupCommandRequest request) => new(request, null);
        public static PageSetupRequestBuildResult Fail(string error, PageSetupValidationTarget? target = null) =>
            new(null, error, target);
    }

    private static bool TryParseFitToPages(string input, out int? pages)
    {
        pages = null;
        var trimmed = input.Trim();
        if (trimmed.Length == 0 || trimmed.Equals("auto", StringComparison.OrdinalIgnoreCase))
            return true;

        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value >= 1)
        {
            pages = value;
            return true;
        }

        return false;
    }

    private static bool TryParseFirstPageNumber(string input, out int? firstPageNumber)
    {
        firstPageNumber = null;
        var trimmed = (input ?? string.Empty).Trim();
        if (trimmed.Length == 0 || trimmed.Equals("auto", StringComparison.OrdinalIgnoreCase))
            return true;

        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value >= 1)
        {
            firstPageNumber = value;
            return true;
        }

        return false;
    }

    private static bool TryParsePrintQualityDpi(string input, out int? dpi)
    {
        dpi = null;
        var trimmed = (input ?? string.Empty).Trim();
        if (trimmed.Length == 0 || trimmed.Equals("auto", StringComparison.OrdinalIgnoreCase))
            return true;

        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value >= 1)
        {
            dpi = value;
            return true;
        }

        return false;
    }

    private static bool TryParseMargin(string input, double fallback, out double margin)
    {
        margin = fallback;
        var trimmed = (input ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            return true;

        if (NumericInputParser.TryParseFiniteDouble(trimmed, CultureInfo.CurrentCulture, CultureInfo.InvariantCulture, out var value) &&
            value >= 0)
        {
            margin = value;
            return true;
        }

        return false;
    }

    private static string FormatMargins(WorksheetPageMargins margins) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "{0:0.##}, {1:0.##}, {2:0.##}, {3:0.##}",
            margins.Left,
            margins.Right,
            margins.Top,
            margins.Bottom);

    private static string FormatMargin(double margin) =>
        margin.ToString("0.##", CultureInfo.InvariantCulture);

    private static string FormatFitTo(int? pages) =>
        pages?.ToString(CultureInfo.InvariantCulture) ?? "";

    private static string FormatPrintArea(GridRange? printArea, SheetId sheetId)
    {
        if (printArea is not { } range || range.Start.Sheet != sheetId)
            return "";

        var start = CellAddress.NumberToColumnName(range.Start.Col) + range.Start.Row.ToString(CultureInfo.InvariantCulture);
        var end = CellAddress.NumberToColumnName(range.End.Col) + range.End.Row.ToString(CultureInfo.InvariantCulture);
        return start == end ? start : $"{start}:{end}";
    }

    /// <summary>Formats every configured print area, comma-separated, so multi-region areas round-trip.</summary>
    private static string FormatPrintAreas(IReadOnlyList<GridRange> printAreas, SheetId sheetId) =>
        string.Join(",", printAreas
            .Where(range => range.Start.Sheet == sheetId)
            .Select(range => FormatPrintArea(range, sheetId)));

    private static string FormatRepeatRows(WorksheetRepeatRange? range)
    {
        if (range is not { } value)
            return "";

        return value.Start == value.End
            ? value.Start.ToString(CultureInfo.InvariantCulture)
            : $"{value.Start}:{value.End}";
    }

    private static string FormatRepeatColumns(WorksheetRepeatRange? range)
    {
        if (range is not { } value)
            return "";

        var start = CellAddress.NumberToColumnName(value.Start);
        var end = CellAddress.NumberToColumnName(value.End);
        return value.Start == value.End ? start : $"{start}:{end}";
    }
}
