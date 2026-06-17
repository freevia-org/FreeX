using System.Globalization;

using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Dialogs;

/// <summary>
/// Non-UI glue backing the Avalonia Page Setup dialog. It mirrors the active sheet's page-setup state
/// into discrete dialog fields, resolves the scaling choice (adjust-to percent vs. fit-to W×H),
/// validates and parses the free-text inputs (margins, scale, print area, print titles), and emits a
/// <see cref="SetPageSetupCommand"/> the shell runs through the workbook session. None of this depends
/// on a running UI, so it is unit-tested directly. UI code-behind only wires controls to these fields.
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
/// via <see cref="PageSetupDialogModel.FromSheet"/> and turned back into a command via
/// <see cref="PageSetupDialogModel.TryBuildCommand"/>.
/// </summary>
public sealed record PageSetupDialogFields
{
    public WorksheetPageOrientation Orientation { get; init; } = WorksheetPageOrientation.Portrait;
    public WorksheetPaperSize PaperSize { get; init; } = WorksheetPaperSize.A4;

    /// <summary>Margins as a comma-separated "left, right, top, bottom" inch string (parser format).</summary>
    public string MarginsText { get; init; } = "0.5, 0.5, 0.5, 0.5";

    public PageSetupScalingMode ScalingMode { get; init; } = PageSetupScalingMode.AdjustToPercent;

    /// <summary>Adjust-to percent text (e.g. "100"); only consulted when <see cref="ScalingMode"/> is AdjustToPercent.</summary>
    public string ScalePercentText { get; init; } = "100";

    /// <summary>Fit-to pages-wide text; "" / "auto" means automatic (no horizontal cap).</summary>
    public string FitToWideText { get; init; } = "1";

    /// <summary>Fit-to pages-tall text; "" / "auto" means automatic (no vertical cap).</summary>
    public string FitToTallText { get; init; } = "1";

    /// <summary>Print area, e.g. "A1:D20"; empty clears the explicit print area.</summary>
    public string PrintAreaText { get; init; } = "";

    /// <summary>Print-title repeat rows, e.g. "1:2"; empty / "none" clears them.</summary>
    public string RepeatRowsText { get; init; } = "";

    /// <summary>Print-title repeat columns, e.g. "A:B"; empty / "none" clears them.</summary>
    public string RepeatColumnsText { get; init; } = "";

    public bool PrintGridlines { get; init; }
    public bool PrintHeadings { get; init; }
    public WorksheetPageOrder PageOrder { get; init; } = WorksheetPageOrder.DownThenOver;
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
            ScalingMode = usesPercent ? PageSetupScalingMode.AdjustToPercent : PageSetupScalingMode.FitToPages,
            ScalePercentText = (scaleToFit.ScalePercent ?? 100).ToString(CultureInfo.InvariantCulture),
            FitToWideText = FormatFitTo(scaleToFit.FitToPagesWide),
            FitToTallText = FormatFitTo(scaleToFit.FitToPagesTall),
            PrintAreaText = FormatPrintArea(sheet.PrintArea, sheet.Id),
            RepeatRowsText = FormatRepeatRows(sheet.PrintTitleRows),
            RepeatColumnsText = FormatRepeatColumns(sheet.PrintTitleColumns),
            PrintGridlines = sheet.PrintGridlines,
            PrintHeadings = sheet.PrintHeadings,
            PageOrder = sheet.PageOrder,
        };
    }

    /// <summary>
    /// Validates <paramref name="fields"/> and, on success, builds a <see cref="SetPageSetupCommand"/>
    /// that carries every page-setup property forward (preserving the header/footer/quality fields the
    /// dialog does not surface, taken from <paramref name="sheet"/>). The first invalid field stops the
    /// build and is reported in the result so the caller can surface it.
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

        if (!PageMarginInputParser.TryParse(fields.MarginsText, out var margins, out var marginError))
            return PageSetupCommandBuildResult.Fail(marginError ?? "Margins are invalid.");

        if (!TryResolveScaleToFit(fields, out var scaleToFit, out var scaleError))
            return PageSetupCommandBuildResult.Fail(scaleError!);

        if (!TryParsePrintArea(fields.PrintAreaText, sheet.Id, out _))
            return PageSetupCommandBuildResult.Fail("Print area must be a cell range like A1:D20.");

        if (!PageSetupRangeParser.TryParseRepeatRows(fields.RepeatRowsText, out var repeatRows))
            return PageSetupCommandBuildResult.Fail("Rows to repeat at top must be a row range like 1:2.");

        if (!PageSetupRangeParser.TryParseRepeatColumns(fields.RepeatColumnsText, out var repeatColumns))
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
            sheet.CenterHorizontallyOnPage,
            sheet.CenterVerticallyOnPage,
            fields.PageOrder,
            sheet.FirstPageNumber,
            sheet.HeaderMargin,
            sheet.FooterMargin,
            sheet.PrintBlackAndWhite,
            sheet.PrintDraftQuality,
            sheet.PrintQualityDpi,
            sheet.PrintErrorValue,
            sheet.PrintComments);

        // The print area lives outside SetPageSetupCommand; the shell applies it separately. We still
        // validate it here so the dialog rejects a bad range before committing anything.
        return PageSetupCommandBuildResult.Ok(command);
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
        PageSetupRangeParser.TryParsePrintArea(input, sheetId, out printArea);

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

    private static string FormatMargins(WorksheetPageMargins margins) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "{0:0.##}, {1:0.##}, {2:0.##}, {3:0.##}",
            margins.Left,
            margins.Right,
            margins.Top,
            margins.Bottom);

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
