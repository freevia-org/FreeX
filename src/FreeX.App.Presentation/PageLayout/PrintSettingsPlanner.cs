using System.Globalization;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.PageLayout;

public sealed record PrintSettingsPlan(IReadOnlyList<string> Lines)
{
    public string Summary => string.Join("; ", Lines);
}

public enum PrintWhat
{
    ActiveSheets,
    EntireWorkbook,
    Selection
}

public enum PrintDialogFocusTarget
{
    ConfirmAction
}

public sealed record PrintPreviewSettings(
    bool IgnorePrintArea = false,
    int Copies = 1,
    string? PrinterName = null,
    PrintWhat PrintWhat = PrintWhat.ActiveSheets,
    int? PageFrom = null,
    int? PageTo = null,
    PrintPreviewSidesMode Sides = PrintPreviewSidesMode.OneSided,
    bool Collated = true);

public sealed record PrintSettingsTextResolver(
    Func<string, string> GetText,
    Func<string, object?[], string> FormatText)
{
    public string Get(string key, string fallback) =>
        GetText(key) ?? fallback;

    public string Format(string key, string fallbackFormat, params object?[] args) =>
        FormatText(key, args) ?? string.Format(CultureInfo.InvariantCulture, fallbackFormat, args);
}

public static class PrintSettingsPlanner
{
    public static PrintDialogFocusTarget InitialDialogFocusTarget =>
        PrintDialogFocusTarget.ConfirmAction;

    public static PrintSettingsPlan Build(
        Sheet sheet,
        bool ignorePrintArea = false,
        PrintSettingsTextResolver? textResolver = null)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        var lines = new List<string>
        {
            DescribeScope(sheet, ignorePrintArea, textResolver),
            Format(
                textResolver,
                "PrintSettings_OrientationFormat",
                "Orientation: {0}",
                DescribeOrientation(sheet.PageOrientation, textResolver)),
            Format(
                textResolver,
                "PrintSettings_PaperSizeFormat",
                "Paper size: {0}",
                DescribePaperSize(sheet.PaperSize, textResolver)),
            Format(
                textResolver,
                "PrintSettings_ScalingFormat",
                "Scaling: {0}",
                DescribeScaling(sheet.ScaleToFit, textResolver)),
            Format(
                textResolver,
                "PrintSettings_GridlinesFormat",
                "Gridlines: {0}",
                DescribeOnOff(sheet.PrintGridlines, textResolver)),
            Format(
                textResolver,
                "PrintSettings_HeadingsFormat",
                "Headings: {0}",
                DescribeOnOff(sheet.PrintHeadings, textResolver))
        };

        return new PrintSettingsPlan(lines);
    }

    public static WorksheetScaleToFit ScaleIndexToScaleToFit(int index) =>
        index switch
        {
            1 => new WorksheetScaleToFit(null, 1, 1),
            2 => new WorksheetScaleToFit(null, 1, null),
            3 => new WorksheetScaleToFit(null, null, 1),
            _ => WorksheetScaleToFit.Default
        };

    public static int ScaleToFitToIndex(WorksheetScaleToFit stf) =>
        stf switch
        {
            { FitToPagesWide: 1, FitToPagesTall: 1 } => 1,
            { FitToPagesWide: 1, FitToPagesTall: null } => 2,
            { FitToPagesWide: null, FitToPagesTall: 1 } => 3,
            _ => 0
        };

    public static PrintPreviewSidesMode SidesIndexToMode(int index) =>
        PrintPreviewToolbarStatePlanner.SidesIndexToMode(index);

    public static int SidesModeToIndex(PrintPreviewSidesMode mode) =>
        PrintPreviewToolbarStatePlanner.SidesModeToIndex(mode);

    public static int ClampCopies(int copies) => Math.Clamp(copies, 1, 999);

    public static bool TryValidatePageRange(int? fromRaw, int? toRaw, int totalPages, out int from, out int to)
    {
        from = 1;
        to = Math.Max(1, totalPages);

        if (fromRaw is null && toRaw is null)
            return true;

        var f = fromRaw ?? 1;
        var t = toRaw ?? Math.Max(1, totalPages);

        if (f < 1 || t < 1 || f > totalPages || t > totalPages || f > t)
            return false;

        from = f;
        to = t;
        return true;
    }

    private static string DescribeScope(Sheet sheet, bool ignorePrintArea, PrintSettingsTextResolver? textResolver)
    {
        if (sheet.PrintArea is null)
            return Get(textResolver, "PrintSettings_PrintActiveSheet", "Print active sheet");

        return ignorePrintArea
            ? Get(textResolver, "PrintSettings_PrintActiveSheetIgnorePrintArea", "Print active sheet (ignore print area)")
            : Get(textResolver, "PrintSettings_PrintSelectedPrintArea", "Print selected print area");
    }

    private static string DescribeScaling(WorksheetScaleToFit scale, PrintSettingsTextResolver? textResolver)
    {
        var parts = new List<string>();
        if (scale.ScalePercent is not null)
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{scale.ScalePercent}%"));
        if (scale.FitToPagesWide is not null || scale.FitToPagesTall is not null)
        {
            parts.Add(Format(
                textResolver,
                "PrintSettings_FitPagesWideByTall",
                "fit {0} page wide by {1} tall",
                scale.FitToPagesWide?.ToString(CultureInfo.InvariantCulture) ?? Get(textResolver, "PrintSettings_Auto", "Auto"),
                scale.FitToPagesTall?.ToString(CultureInfo.InvariantCulture) ?? Get(textResolver, "PrintSettings_Auto", "Auto")));
        }

        return parts.Count == 0
            ? Get(textResolver, "PrintSettings_Automatic", "Automatic")
            : string.Join("; ", parts);
    }

    private static string DescribeOrientation(
        WorksheetPageOrientation orientation,
        PrintSettingsTextResolver? textResolver) =>
        orientation == WorksheetPageOrientation.Landscape
            ? Get(textResolver, "PageSetup_Landscape", "Landscape")
            : Get(textResolver, "PageSetup_Portrait", "Portrait");

    private static string DescribePaperSize(WorksheetPaperSize paperSize, PrintSettingsTextResolver? textResolver) => paperSize switch
    {
        WorksheetPaperSize.Letter => Get(textResolver, "MainWindow_Header_Letter", "Letter"),
        WorksheetPaperSize.Legal => Get(textResolver, "MainWindow_Header_Legal", "Legal"),
        _ => Get(textResolver, "MainWindow_Header_A4", "A4")
    };

    private static string DescribeOnOff(bool value, PrintSettingsTextResolver? textResolver) =>
        value ? Get(textResolver, "PrintSettings_On", "on") : Get(textResolver, "PrintSettings_Off", "off");

    private static string Get(PrintSettingsTextResolver? textResolver, string key, string fallback) =>
        textResolver?.Get(key, fallback) ?? fallback;

    private static string Format(
        PrintSettingsTextResolver? textResolver,
        string key,
        string fallbackFormat,
        params object?[] args) =>
        textResolver?.Format(key, fallbackFormat, args)
        ?? string.Format(CultureInfo.InvariantCulture, fallbackFormat, args);
}
