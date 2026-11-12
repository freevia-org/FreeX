using Avalonia.Media;
using FreeX.App.Services;

namespace FreeX.App.Avalonia;

/// <summary>
/// Font tab helpers for the Format Cells dialog (WPF <c>FormatCellsDialog.Font.cs</c>): the font
/// name list and the labels the shared <see cref="FormatCellsDialogPlanner"/> uses to classify the
/// font style and underline drop-down choices.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// WPF FontNamesWithFallback: the installed font families, de-duplicated and sorted, with the
    /// cell's current font put first when it is not among them. The curated names are merged in:
    /// workbooks authored elsewhere use them even when this machine does not have them installed,
    /// and they are the only entries when the platform font list cannot be read.
    /// </summary>
    private static string[] BuildFormatCellsFontNameChoices(
        IReadOnlyList<string> curatedFontNames,
        string? currentFontName)
    {
        var names = new List<string>(curatedFontNames);
        try
        {
            foreach (var family in FontManager.Current.SystemFonts)
            {
                var name = family.Name;
                // Hidden platform faces start with a dot on macOS and are not user-selectable.
                if (!string.IsNullOrWhiteSpace(name) && !name.StartsWith('.'))
                    names.Add(name);
            }
        }
        catch (Exception)
        {
            // No platform font manager (for example a headless host): keep the curated names.
        }

        var fonts = names
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(font => font, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var current = currentFontName?.Trim();
        if (!string.IsNullOrWhiteSpace(current)
            && !fonts.Contains(current, StringComparer.CurrentCultureIgnoreCase))
        {
            fonts.Insert(0, current);
        }

        return fonts.ToArray();
    }

    /// <summary>The same label set the WPF dialog hands the shared planner.</summary>
    private static FormatCellsDialogFontLabels CreateFormatCellsFontLabels() =>
        new(
            UiText.Get("FormatCells_FontStyleRegular"),
            UiText.Get("FormatCells_FontStyleItalic"),
            UiText.Get("FormatCells_FontStyleBold"),
            UiText.Get("FormatCells_FontStyleBoldItalic"),
            UiText.Get("FormatCells_UnderlineNone"),
            UiText.Get("FormatCells_UnderlineSingle"),
            UiText.Get("FormatCells_UnderlineDouble"),
            UiText.Get("FormatCells_UnderlineSingleAccounting"),
            UiText.Get("FormatCells_UnderlineDoubleAccounting"));
}
