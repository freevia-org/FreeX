using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Host;

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

public sealed record PrintPreviewSettings(
    bool IgnorePrintArea = false,
    int Copies = 1,
    string? PrinterName = null,
    PrintWhat PrintWhat = PrintWhat.ActiveSheets,
    int? PageFrom = null,
    int? PageTo = null,
    PrintPreviewSidesMode Sides = PrintPreviewSidesMode.OneSided,
    bool Collated = true);

public static class PrintSettingsPlanner
{
    public static PrintSettingsPlan Build(Sheet sheet, bool ignorePrintArea = false)
    {
        var lines = new List<string>
        {
            DescribeScope(sheet, ignorePrintArea),
            UiText.Format("PrintSettings_OrientationFormat", DescribeOrientation(sheet.PageOrientation)),
            UiText.Format("PrintSettings_PaperSizeFormat", DescribePaperSize(sheet.PaperSize)),
            UiText.Format("PrintSettings_ScalingFormat", DescribeScaling(sheet.ScaleToFit)),
            UiText.Format("PrintSettings_GridlinesFormat", DescribeOnOff(sheet.PrintGridlines)),
            UiText.Format("PrintSettings_HeadingsFormat", DescribeOnOff(sheet.PrintHeadings))
        };

        return new PrintSettingsPlan(lines);
    }

    // ─── Scaling index ↔ WorksheetScaleToFit ────────────────────────────────

    /// <summary>
    /// Maps a scaling combo-box index (0=No Scaling, 1=Fit Sheet, 2=Fit Columns, 3=Fit Rows,
    /// 4=Custom Scaling Options…) to a <see cref="WorksheetScaleToFit"/>.
    /// Index 4 is the "Custom Scaling Options…" item — callers must handle it before calling
    /// this method; it is treated as No Scaling here.
    /// </summary>
    public static WorksheetScaleToFit ScaleIndexToScaleToFit(int index) =>
        index switch
        {
            1 => new WorksheetScaleToFit(null, 1, 1),
            2 => new WorksheetScaleToFit(null, 1, null),
            3 => new WorksheetScaleToFit(null, null, 1),
            _ => WorksheetScaleToFit.Default
        };

    /// <summary>
    /// Maps a <see cref="WorksheetScaleToFit"/> back to a scaling combo-box index.
    /// </summary>
    public static int ScaleToFitToIndex(WorksheetScaleToFit stf) =>
        stf switch
        {
            { FitToPagesWide: 1, FitToPagesTall: 1 } => 1,
            { FitToPagesWide: 1, FitToPagesTall: null } => 2,
            { FitToPagesWide: null, FitToPagesTall: 1 } => 3,
            _ => 0
        };

    // ─── Sides index ↔ PrintPreviewSidesMode ────────────────────────────────

    /// <summary>Maps a sides combo-box index to a <see cref="PrintPreviewSidesMode"/>.</summary>
    public static PrintPreviewSidesMode SidesIndexToMode(int index) =>
        PrintPreviewToolbarStatePlanner.SidesIndexToMode(index);

    /// <summary>Maps a <see cref="PrintPreviewSidesMode"/> back to a sides combo-box index.</summary>
    public static int SidesModeToIndex(PrintPreviewSidesMode mode) =>
        PrintPreviewToolbarStatePlanner.SidesModeToIndex(mode);

    // ─── Copies clamping ────────────────────────────────────────────────────

    /// <summary>Clamps a copies value to the valid 1..999 range.</summary>
    public static int ClampCopies(int copies) => Math.Clamp(copies, 1, 999);

    // ─── Page-range validation ───────────────────────────────────────────────

    /// <summary>
    /// Validates and normalises a page range.
    /// Returns <c>true</c> when the range is valid; <paramref name="from"/> and
    /// <paramref name="to"/> are set to the clamped values.
    /// </summary>
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

    // ─── Describe helpers ────────────────────────────────────────────────────

    private static string DescribeScope(Sheet sheet, bool ignorePrintArea)
    {
        if (sheet.PrintArea is null)
            return UiText.Get("PrintSettings_PrintActiveSheet");

        return ignorePrintArea
            ? UiText.Get("PrintSettings_PrintActiveSheetIgnorePrintArea")
            : UiText.Get("PrintSettings_PrintSelectedPrintArea");
    }

    private static string DescribeScaling(WorksheetScaleToFit scale)
    {
        var parts = new List<string>();
        if (scale.ScalePercent is not null)
            parts.Add($"{scale.ScalePercent}%");
        if (scale.FitToPagesWide is not null || scale.FitToPagesTall is not null)
            parts.Add(UiText.Format(
                "PrintSettings_FitPagesWideByTall",
                scale.FitToPagesWide?.ToString() ?? UiText.Get("PrintSettings_Auto"),
                scale.FitToPagesTall?.ToString() ?? UiText.Get("PrintSettings_Auto")));

        return parts.Count == 0 ? UiText.Get("PrintSettings_Automatic") : string.Join("; ", parts);
    }

    private static string DescribeOrientation(WorksheetPageOrientation orientation) =>
        orientation == WorksheetPageOrientation.Landscape
            ? UiText.Get("PageSetup_Landscape")
            : UiText.Get("PageSetup_Portrait");

    private static string DescribePaperSize(WorksheetPaperSize paperSize) => paperSize switch
    {
        WorksheetPaperSize.Letter => UiText.Get("MainWindow_Header_Letter"),
        WorksheetPaperSize.Legal => UiText.Get("MainWindow_Header_Legal"),
        _ => UiText.Get("MainWindow_Header_A4")
    };

    private static string DescribeOnOff(bool value) =>
        value ? UiText.Get("PrintSettings_On") : UiText.Get("PrintSettings_Off");
}
