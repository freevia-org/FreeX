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

/// <summary>
/// A snapshot of every editable page-setup field, decoupled from the Core.Model so the dialog can edit
/// strings/enums without mutating the sheet until the user commits. Built from a <see cref="Sheet"/>
/// via <see cref="PageSetupDialogModel.FromSheet"/> and turned back into commands via
/// <see cref="PageSetupDialogModel.TryBuildCommand"/> / <see cref="PageSetupDialogModel.BuildHeaderFooterCommand"/>.
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

    /// <summary>Header/footer center text (the dialog edits center-only presets; left/right preserved).</summary>
    public WorksheetHeaderFooter Header { get; init; } = new("", "", "");
    public WorksheetHeaderFooter Footer { get; init; } = new("", "", "");
    public bool DifferentFirstPage { get; init; }
    public bool DifferentOddEvenPages { get; init; }
    public bool ScaleHeaderFooterWithDocument { get; init; } = true;
    public bool AlignHeaderFooterWithMargins { get; init; } = true;
}

/// <summary>
/// The outcome of validating + building a command from the dialog fields: either a ready-to-run
/// command, or a human-readable error describing the first invalid field.
/// </summary>
public sealed record PageSetupCommandBuildResult(SetPageSetupCommand? Command, string? Error)
{
    public bool Success => Command is not null;

    public static PageSetupCommandBuildResult Ok(SetPageSetupCommand command) => new(command, null);
    public static PageSetupCommandBuildResult Fail(string error) => new(null, error);
}

public static class PageSetupDialogModel
{
    /// <summary>The paper sizes the dialog offers, in display order.</summary>
    public static IReadOnlyList<WorksheetPaperSize> PaperSizes { get; } =
        [WorksheetPaperSize.Letter, WorksheetPaperSize.A4, WorksheetPaperSize.Legal];

    /// <summary>The header/footer center presets the dialog offers, as (token) values; "" means none.</summary>
    public static IReadOnlyList<string> HeaderFooterPresets { get; } =
    [
        "",
        "&[Page]",
        "Page &[Page] of &[Pages]",
        "&[Tab]",
        "&[File]",
        "&[File], &[Tab]",
        "&[Date]",
        "&[Time]",
        "&[Date], Page &[Page]",
        "Confidential, Page &[Page]",
        "&[Path]&[File]",
    ];

    /// <summary>Friendly label for a paper size shown in the combo box.</summary>
    public static string DescribePaperSize(WorksheetPaperSize paperSize) =>
        paperSize switch
        {
            WorksheetPaperSize.Letter => "Letter (8.5\" x 11\")",
            WorksheetPaperSize.Legal => "Legal (8.5\" x 14\")",
            _ => "A4 (210mm x 297mm)"
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
            PrintAreaText = FormatPrintArea(sheet.PrintArea, sheet.Id),
            RepeatRowsText = FormatRepeatRows(sheet.PrintTitleRows),
            RepeatColumnsText = FormatRepeatColumns(sheet.PrintTitleColumns),
            PrintGridlines = sheet.PrintGridlines,
            PrintHeadings = sheet.PrintHeadings,
            PrintBlackAndWhite = sheet.PrintBlackAndWhite,
            PrintDraftQuality = sheet.PrintDraftQuality,
            PrintErrorValue = sheet.PrintErrorValue,
            PrintComments = sheet.PrintComments,
            PageOrder = sheet.PageOrder,
            Header = sheet.PageHeader,
            Footer = sheet.PageFooter,
            DifferentFirstPage = sheet.DifferentFirstPageHeaderFooter,
            DifferentOddEvenPages = sheet.DifferentOddEvenHeaderFooter,
            ScaleHeaderFooterWithDocument = sheet.HeaderFooterScaleWithDocument,
            AlignHeaderFooterWithMargins = sheet.HeaderFooterAlignWithMargins,
        };
    }

    /// <summary>
    /// Validates <paramref name="fields"/> and, on success, builds a <see cref="SetPageSetupCommand"/>
    /// that carries every page-setup property the dialog now surfaces (orientation, paper, margins,
    /// header/footer margins, centering, scaling, print titles, print quality, first-page number, page
    /// order, gridlines/headings, black-and-white, draft quality, cell-error and comment display). The
    /// first invalid field stops the build and is reported in the result so the caller can surface it.
    /// Header/footer text is applied by the companion <see cref="BuildHeaderFooterCommand"/>.
    /// </summary>
    public static PageSetupCommandBuildResult TryBuildCommand(Sheet sheet, PageSetupDialogFields fields)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(fields);

        if (!Enum.IsDefined(fields.Orientation))
            return PageSetupCommandBuildResult.Fail("Choose a page orientation.");
        if (!Enum.IsDefined(fields.PaperSize))
            return PageSetupCommandBuildResult.Fail("Choose a paper size.");
        if (!Enum.IsDefined(fields.PageOrder))
            return PageSetupCommandBuildResult.Fail("Choose a page order.");
        if (!Enum.IsDefined(fields.PrintErrorValue))
            return PageSetupCommandBuildResult.Fail("Choose how cell errors print.");
        if (!Enum.IsDefined(fields.PrintComments))
            return PageSetupCommandBuildResult.Fail("Choose how comments print.");

        if (!PageMarginInputParser.TryParse(fields.MarginsText, out var margins, out var marginError))
            return PageSetupCommandBuildResult.Fail(marginError ?? "Margins are invalid.");

        if (!TryParseMargin(fields.HeaderMarginText, sheet.HeaderMargin, out var headerMargin))
            return PageSetupCommandBuildResult.Fail("Header margin must be a non-negative number of inches.");

        if (!TryParseMargin(fields.FooterMarginText, sheet.FooterMargin, out var footerMargin))
            return PageSetupCommandBuildResult.Fail("Footer margin must be a non-negative number of inches.");

        if (!TryResolveScaleToFit(fields, out var scaleToFit, out var scaleError))
            return PageSetupCommandBuildResult.Fail(scaleError!);

        if (!TryParseFirstPageNumber(fields.FirstPageNumberText, out var firstPageNumber))
            return PageSetupCommandBuildResult.Fail("First page number must be a positive whole number or blank for automatic.");

        if (!TryParsePrintQualityDpi(fields.PrintQualityDpiText, out var printQualityDpi))
            return PageSetupCommandBuildResult.Fail("Print quality must be a positive DPI value or blank.");

        if (!TryParsePrintArea(fields.PrintAreaText, sheet.Id, out _))
            return PageSetupCommandBuildResult.Fail("Print area must be a cell range like A1:D20.");

        if (!PageLayoutInputParser.TryParseRepeatRows(fields.RepeatRowsText, out var repeatRows))
            return PageSetupCommandBuildResult.Fail("Rows to repeat at top must be a row range like 1:2.");

        if (!PageLayoutInputParser.TryParseRepeatColumns(fields.RepeatColumnsText, out var repeatColumns))
            return PageSetupCommandBuildResult.Fail("Columns to repeat at left must be a column range like A:B.");

        var command = new SetPageSetupCommand(
            sheet.Id,
            fields.Orientation,
            fields.PaperSize,
            margins,
            fields.PrintGridlines,
            fields.PrintHeadings,
            scaleToFit,
            repeatRows,
            repeatColumns,
            fields.CenterHorizontally,
            fields.CenterVertically,
            fields.PageOrder,
            firstPageNumber,
            headerMargin,
            footerMargin,
            fields.PrintBlackAndWhite,
            fields.PrintDraftQuality,
            printQualityDpi,
            fields.PrintErrorValue,
            fields.PrintComments);

        // The print area lives outside SetPageSetupCommand; the shell applies it separately. We still
        // validate it here so the dialog rejects a bad range before committing anything.
        return PageSetupCommandBuildResult.Ok(command);
    }

    /// <summary>
    /// Builds the companion <see cref="SetHeaderFooterCommand"/> that applies the dialog's header/footer
    /// text, preserving the sheet's existing first-page / even-page text and pictures (the simple Page
    /// Setup surface only edits the center preset; the dedicated Header/Footer dialog edits the rest).
    /// </summary>
    public static SetHeaderFooterCommand BuildHeaderFooterCommand(Sheet sheet, PageSetupDialogFields fields)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(fields);

        return new SetHeaderFooterCommand(
            sheet.Id,
            fields.Header,
            fields.Footer,
            sheet.FirstPageHeader,
            sheet.FirstPageFooter,
            sheet.EvenPageHeader,
            sheet.EvenPageFooter,
            fields.DifferentFirstPage,
            fields.DifferentOddEvenPages,
            fields.ScaleHeaderFooterWithDocument,
            fields.AlignHeaderFooterWithMargins,
            sheet.PageHeaderPictures,
            sheet.PageFooterPictures,
            sheet.FirstPageHeaderPictures,
            sheet.FirstPageFooterPictures,
            sheet.EvenPageHeaderPictures,
            sheet.EvenPageFooterPictures);
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

        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value >= 0)
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
