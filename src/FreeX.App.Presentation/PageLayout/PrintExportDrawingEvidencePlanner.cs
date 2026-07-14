using FreeX.App.Presentation.Text;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.PageLayout;

public sealed record PrintExportDrawingEvidencePage(
    int PageNumber,
    int ChartCount,
    int ChartTextOverlayCount,
    int TextBoxCount,
    int TextBoxTextRunCount)
{
    public bool HasDrawingContent => ChartCount > 0 || TextBoxCount > 0;

    public bool HasSelectableChartText => ChartTextOverlayCount > 0;

    public bool HasSelectableTextBoxText => TextBoxTextRunCount > 0;
}

public sealed record PrintExportDrawingEvidencePlan(
    IReadOnlyList<PrintExportDrawingEvidencePage> Pages)
{
    public int PageCount => Pages.Count;

    public int ChartCount => Pages.Sum(page => page.ChartCount);

    public int ChartTextOverlayCount => Pages.Sum(page => page.ChartTextOverlayCount);

    public int TextBoxCount => Pages.Sum(page => page.TextBoxCount);

    public int TextBoxTextRunCount => Pages.Sum(page => page.TextBoxTextRunCount);

    public bool HasDrawingContent => Pages.Any(page => page.HasDrawingContent);

    public string StatusText =>
        $"Print/export drawing evidence: {FormatCount(PageCount, "page")}, " +
        $"{FormatCount(ChartCount, "chart")}, " +
        $"{ChartTextOverlayCount} selectable chart text {Pluralize(ChartTextOverlayCount, "overlay")}, " +
        $"{FormatCount(TextBoxCount, "text box", "text boxes")}, and " +
        $"{TextBoxTextRunCount} text-box text {Pluralize(TextBoxTextRunCount, "run")}.";

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
                layout.TextBoxes.Count(textBox => !string.IsNullOrWhiteSpace(textBox.Text))));
        }

        return new PrintExportDrawingEvidencePlan(pages);
    }
}
