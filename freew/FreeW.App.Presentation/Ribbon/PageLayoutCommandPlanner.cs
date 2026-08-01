using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

public enum PageMarginPreset
{
    Normal,
    Narrow,
    Wide
}

public enum PagePaperSizePreset
{
    Letter,
    A4
}

public enum PageColumnPreset
{
    One,
    Two,
    Three,
    Left,
    Right
}

/// <summary>
/// Shared policy for Layout-ribbon quick actions and dialog result application. Hosts remain responsible
/// for committing the planned mutation through their undoable page-settings command.
/// </summary>
public static class PageLayoutCommandPlanner
{
    private const double NormalMarginPt = 72;
    private const double NarrowMarginPt = 36;
    private const double WideHorizontalMarginPt = 108;

    public static void ToggleOrientation(PageSettings page)
    {
        ArgumentNullException.ThrowIfNull(page);
        (page.WidthPt, page.HeightPt) = (page.HeightPt, page.WidthPt);
        page.Landscape = !page.Landscape;
    }

    public static void ToggleNormalNarrowMargins(PageSettings page) =>
        ApplyMarginPreset(page, HasNormalMargins(page) ? PageMarginPreset.Narrow : PageMarginPreset.Normal);

    public static void ApplyMarginPreset(PageSettings page, PageMarginPreset preset)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (preset == PageMarginPreset.Wide)
        {
            page.MarginTopPt = NormalMarginPt;
            page.MarginBottomPt = NormalMarginPt;
            page.MarginLeftPt = WideHorizontalMarginPt;
            page.MarginRightPt = WideHorizontalMarginPt;
            return;
        }

        var value = preset == PageMarginPreset.Narrow ? NarrowMarginPt : NormalMarginPt;
        page.MarginTopPt = value;
        page.MarginBottomPt = value;
        page.MarginLeftPt = value;
        page.MarginRightPt = value;
    }

    public static bool TryParseMarginPreset(string? value, out PageMarginPreset preset) =>
        Enum.TryParse(value, ignoreCase: true, out preset);

    public static void ToggleLetterA4Paper(PageSettings page) =>
        ApplyPaperSize(page, HasLetterPaperSize(page) ? PagePaperSizePreset.A4 : PagePaperSizePreset.Letter);

    public static void ApplyPaperSize(PageSettings page, PagePaperSizePreset preset)
    {
        ArgumentNullException.ThrowIfNull(page);

        var landscape = page.Landscape || page.WidthPt > page.HeightPt;
        var (portraitWidth, portraitHeight) = preset switch
        {
            PagePaperSizePreset.A4 => (595.3, 841.9),
            _ => (612.0, 792.0)
        };
        page.WidthPt = landscape ? portraitHeight : portraitWidth;
        page.HeightPt = landscape ? portraitWidth : portraitHeight;
    }

    public static bool TryParsePaperSize(string? value, out PagePaperSizePreset preset) =>
        Enum.TryParse(value, ignoreCase: true, out preset);

    public static void ApplyColumnPreset(PageSettings page, PageColumnPreset preset)
    {
        ArgumentNullException.ThrowIfNull(page);

        var presetIndex = preset switch
        {
            PageColumnPreset.One => 0,
            PageColumnPreset.Two => 1,
            PageColumnPreset.Three => 2,
            PageColumnPreset.Left => 3,
            _ => 4
        };
        page.ColumnCount = ColumnsDialogPlanner.ColumnCountForPreset(presetIndex);
        page.ColumnsLineBetween = false;
        page.ColumnWidthsPt = ColumnsDialogPlanner.PlanUnequalWidths(
            presetIndex,
            ColumnsDialogPlanner.ContentWidthFor(page),
            page.ColumnSpacingPt);
    }

    public static void ApplyColumnsResult(PageSettings page, ColumnsDialogResult result)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(result);

        page.ColumnCount = result.Count;
        page.ColumnSpacingPt = result.SpacingPt;
        page.ColumnsLineBetween = result.LineBetween;
        page.ColumnWidthsPt = result.WidthsPt;
    }

    public static bool IsColumnPresetChecked(PageSettings page, PageColumnPreset preset)
    {
        ArgumentNullException.ThrowIfNull(page);
        var expectedIndex = preset switch
        {
            PageColumnPreset.One => 0,
            PageColumnPreset.Two => 1,
            PageColumnPreset.Three => 2,
            PageColumnPreset.Left => 3,
            _ => 4
        };
        return ColumnsDialogPlanner.PresetIndexFor(page) == expectedIndex;
    }

    public static void ApplyPageSetupResult(PageSettings page, PageSetupDialogResult result) =>
        PageSetupDialogPlanner.ApplyToPageSettings(page, result);

    public static void CycleLineNumberMode(PageSettings page)
    {
        ArgumentNullException.ThrowIfNull(page);
        page.LineNumberMode = page.LineNumberMode switch
        {
            LineNumberMode.None => LineNumberMode.Continuous,
            LineNumberMode.Continuous => LineNumberMode.RestartEachPage,
            LineNumberMode.RestartEachPage => LineNumberMode.RestartEachSection,
            _ => LineNumberMode.None
        };
    }

    public static void ApplyLineNumberOptions(PageSettings page, LineNumberOptionsDialogResult result)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(result);
        page.LineNumberStartAt = result.StartAt;
        page.LineNumberCountBy = result.CountBy;
        page.LineNumberMode = result.Mode;
    }

    public static bool IsLineNumberModeChecked(PageSettings page, LineNumberMode mode)
    {
        ArgumentNullException.ThrowIfNull(page);
        return page.LineNumberMode == mode;
    }

    public static void ToggleHyphenation(PageSettings page)
    {
        ArgumentNullException.ThrowIfNull(page);
        page.AutoHyphenation = !page.AutoHyphenation;
    }

    public static void ApplyHyphenationOptions(PageSettings page, HyphenationOptionsDialogResult result)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(result);
        page.AutoHyphenation = result.AutoHyphenation;
        page.HyphenationZonePt = result.ZonePt;
        page.ConsecutiveHyphenLimit = result.ConsecutiveLimit;
        page.DoNotHyphenateCaps = !result.HyphenateCaps;
    }

    public static int CountHyphenationCandidates(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var count = 0;
        foreach (var block in document.Blocks)
        {
            if (block is not Paragraph { Formatting.SuppressAutoHyphens: false } paragraph)
                continue;

            foreach (var run in paragraph.Runs)
            foreach (var token in run.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                var word = token.Trim('(', ')', ',', '.', ';', ':', '"', '\'');
                if (Hyphenator.BreakPoints(word).Count > 0)
                    count++;
            }
        }

        return count;
    }

    public static bool HasNormalMargins(PageSettings page) =>
        Nearly(page.MarginTopPt, NormalMarginPt) &&
        Nearly(page.MarginBottomPt, NormalMarginPt) &&
        Nearly(page.MarginLeftPt, NormalMarginPt) &&
        Nearly(page.MarginRightPt, NormalMarginPt);

    public static bool HasLetterPaperSize(PageSettings page)
    {
        var width = Math.Min(page.WidthPt, page.HeightPt);
        var height = Math.Max(page.WidthPt, page.HeightPt);
        return Nearly(width, 612.0) && Nearly(height, 792.0);
    }

    private static bool Nearly(double left, double right) => Math.Abs(left - right) <= 0.5;
}
