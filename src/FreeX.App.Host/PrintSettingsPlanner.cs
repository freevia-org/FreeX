using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed record PrintSettingsPlan(IReadOnlyList<string> Lines)
{
    public string Summary => string.Join("; ", Lines);
}

public sealed record PrintPreviewSettings(bool IgnorePrintArea = false);

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
