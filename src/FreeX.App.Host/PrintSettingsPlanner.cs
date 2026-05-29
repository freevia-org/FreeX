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
            $"Orientation: {sheet.PageOrientation}",
            $"Paper size: {sheet.PaperSize}",
            $"Scaling: {DescribeScaling(sheet.ScaleToFit)}",
            $"Gridlines: {(sheet.PrintGridlines ? "on" : "off")}",
            $"Headings: {(sheet.PrintHeadings ? "on" : "off")}"
        };

        return new PrintSettingsPlan(lines);
    }

    private static string DescribeScope(Sheet sheet, bool ignorePrintArea)
    {
        if (sheet.PrintArea is null)
            return "Print active sheet";

        return ignorePrintArea
            ? "Print active sheet (ignore print area)"
            : "Print selected print area";
    }

    private static string DescribeScaling(WorksheetScaleToFit scale)
    {
        var parts = new List<string>();
        if (scale.ScalePercent is not null)
            parts.Add($"{scale.ScalePercent}%");
        if (scale.FitToPagesWide is not null || scale.FitToPagesTall is not null)
            parts.Add($"fit {scale.FitToPagesWide?.ToString() ?? "auto"} page wide by {scale.FitToPagesTall?.ToString() ?? "auto"} tall");

        return parts.Count == 0 ? "Automatic" : string.Join("; ", parts);
    }
}
