using FreeX.App.Presentation.Text;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.PageLayout;

public sealed record PrintExportDrawingEvidencePage(
    int PageNumber,
    int ChartCount,
    int ChartTextOverlayCount,
    int TextBoxCount,
    int TextBoxTextRunCount,
    IReadOnlyDictionary<PrintChartTextOverlayRole, int> ChartTextOverlayRoleCounts)
{
    public bool HasDrawingContent => ChartCount > 0 || TextBoxCount > 0;

    public bool HasSelectableChartText => ChartTextOverlayCount > 0;

    public bool HasSelectableTextBoxText => TextBoxTextRunCount > 0;

    public int ChartTitleOverlayCount => GetRoleCount(PrintChartTextOverlayRole.ChartTitle);

    public int ChartAxisTitleOverlayCount =>
        GetRoleCount(PrintChartTextOverlayRole.CategoryAxisTitle) +
        GetRoleCount(PrintChartTextOverlayRole.ValueAxisTitle);

    public int ChartLegendEntryOverlayCount => GetRoleCount(PrintChartTextOverlayRole.LegendEntry);

    public int ChartCategoryTickOverlayCount => GetRoleCount(PrintChartTextOverlayRole.CategoryTickLabel);

    public int ChartValueTickOverlayCount => GetRoleCount(PrintChartTextOverlayRole.ValueTickLabel);

    public int ChartDataLabelOverlayCount => GetRoleCount(PrintChartTextOverlayRole.DataLabel);

    public bool HasBroadChartTextEvidence =>
        ChartTitleOverlayCount > 0 &&
        ChartAxisTitleOverlayCount > 0 &&
        ChartLegendEntryOverlayCount > 0 &&
        ChartCategoryTickOverlayCount > 0 &&
        ChartValueTickOverlayCount > 0 &&
        ChartDataLabelOverlayCount > 0;

    private int GetRoleCount(PrintChartTextOverlayRole role) =>
        ChartTextOverlayRoleCounts.TryGetValue(role, out var count) ? count : 0;
}

public sealed record PrintExportDrawingEvidencePlan(
    IReadOnlyList<PrintExportDrawingEvidencePage> Pages)
{
    public int PageCount => Pages.Count;

    public int ChartCount => Pages.Sum(page => page.ChartCount);

    public int ChartTextOverlayCount => Pages.Sum(page => page.ChartTextOverlayCount);

    public int TextBoxCount => Pages.Sum(page => page.TextBoxCount);

    public int TextBoxTextRunCount => Pages.Sum(page => page.TextBoxTextRunCount);

    public int ChartTitleOverlayCount => Pages.Sum(page => page.ChartTitleOverlayCount);

    public int ChartAxisTitleOverlayCount => Pages.Sum(page => page.ChartAxisTitleOverlayCount);

    public int ChartLegendEntryOverlayCount => Pages.Sum(page => page.ChartLegendEntryOverlayCount);

    public int ChartCategoryTickOverlayCount => Pages.Sum(page => page.ChartCategoryTickOverlayCount);

    public int ChartValueTickOverlayCount => Pages.Sum(page => page.ChartValueTickOverlayCount);

    public int ChartDataLabelOverlayCount => Pages.Sum(page => page.ChartDataLabelOverlayCount);

    public bool HasDrawingContent => Pages.Any(page => page.HasDrawingContent);

    public bool HasBroadChartTextEvidence => Pages.Any(page => page.HasBroadChartTextEvidence);

    public string StatusText =>
        $"Print/export drawing evidence: {FormatCount(PageCount, "page")}, " +
        $"{FormatCount(ChartCount, "chart")}, " +
        $"{ChartTextOverlayCount} selectable chart text {Pluralize(ChartTextOverlayCount, "overlay")}, " +
        $"{FormatCount(TextBoxCount, "text box", "text boxes")}, and " +
        $"{TextBoxTextRunCount} text-box text {Pluralize(TextBoxTextRunCount, "run")}; " +
        $"chart text roles: {ChartTitleOverlayCount} title, " +
        $"{ChartAxisTitleOverlayCount} axis {Pluralize(ChartAxisTitleOverlayCount, "title")}, " +
        $"{ChartLegendEntryOverlayCount} legend {Pluralize(ChartLegendEntryOverlayCount, "entry", "entries")}, " +
        $"{ChartCategoryTickOverlayCount} category tick {Pluralize(ChartCategoryTickOverlayCount, "label")}, " +
        $"{ChartValueTickOverlayCount} value tick {Pluralize(ChartValueTickOverlayCount, "label")}, and " +
        $"{ChartDataLabelOverlayCount} data {Pluralize(ChartDataLabelOverlayCount, "label")}.";

    private static string FormatCount(int count, string singular, string? plural = null) =>
        $"{count} {Pluralize(count, singular, plural)}";

    private static string Pluralize(int count, string singular, string? plural = null) =>
        count == 1 ? singular : plural ?? $"{singular}s";
}

/// <summary>
/// Summarizes the renderer-neutral drawing/chart content available to print preview and export
/// paths. Desktop hosts can consume the same <see cref="PageContentLayout"/> evidence,
/// while renderer-specific code remains responsible for native print dialogs, file pickers, and
/// final PDF/XPS painting.
/// </summary>
public static class PrintExportDrawingEvidencePlanner
{
    public static PrintExportDrawingEvidencePlan Build(
        Workbook workbook,
        Sheet sheet,
        PagePaginationResult pagePlan,
        ITextMeasurer textMeasurer,
        DateTime? now = null,
        string workbookDirectory = "")
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(pagePlan);
        ArgumentNullException.ThrowIfNull(textMeasurer);

        var pages = new List<PrintExportDrawingEvidencePage>(pagePlan.PageCount);
        for (var pageIndex = 0; pageIndex < pagePlan.PageCount; pageIndex++)
        {
            var layout = PageContentRenderModelBuilder.Build(
                workbook,
                sheet,
                pagePlan,
                pageIndex,
                textMeasurer,
                now,
                workbookDirectory);
            if (layout is null)
                continue;

            pages.Add(new PrintExportDrawingEvidencePage(
                layout.PageNumber,
                layout.Charts.Count,
                layout.Charts.Sum(chart => chart.TextOverlays.Count),
                layout.TextBoxes.Count,
                layout.TextBoxes.Count(textBox => !string.IsNullOrWhiteSpace(textBox.Text)),
                CountChartTextOverlayRoles(layout)));
        }

        return new PrintExportDrawingEvidencePlan(pages);
    }

    private static IReadOnlyDictionary<PrintChartTextOverlayRole, int> CountChartTextOverlayRoles(
        PageContentLayout layout)
    {
        var counts = new Dictionary<PrintChartTextOverlayRole, int>();
        foreach (var overlay in layout.Charts.SelectMany(chart => chart.TextOverlays))
        {
            counts.TryGetValue(overlay.Role, out var count);
            counts[overlay.Role] = count + 1;
        }

        return counts;
    }
}
