using System.Windows.Media;
using Free.Shared.Ribbon.Wpf;

namespace FreeX.App.Host;

public static partial class RibbonIconFactory
{
    // Key the cache by the EXACT rendered size, not a coarse small/large bucket. The vector is re-wrapped
    // per size (the shared loader scales stroke widths to the target), so sharing one drawing across e.g.
    // 18/20/22px left strokes mis-scaled and the glyph looked soft/blurry.
    private static readonly SvgCommandIconLoader CommandIconLoader = new(
        resourceFolder: "CommandIconsSvg",
        slugFromCommandName: name => ToCommandIconSlug(NormalizeCommandIconName(name)),
        slugCandidates: GetCommandIconSlugCandidates,
        sizeKeySelector: size => ((int)Math.Round(size)).ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static ImageSource? TryLoadCommandIcon(string commandName, Brush glyphBrush, double size) =>
        CommandIconLoader.TryLoad(commandName, glyphBrush, size);

    private static IEnumerable<string> GetCommandIconSlugCandidates(string slug)
    {
        yield return slug;

        var alias = slug switch
        {
            "increase-font-size" => "grow-font",
            "decrease-font-size" => "shrink-font",
            "accounting-number-format" => "accounting-currency",
            "increase-decimal-places" => "increase-decimal",
            "decrease-decimal-places" => "decrease-decimal",
            "merge-and-center" => "merge-center",
            "sort-and-filter" => "sort",
            "find-and-select" => "find",
            "insert-link" => "hyperlink",
            "header-and-footer" => "header-footer",
            "pictures" => "picture",
            "percent-style" => "percent-style",
            "advanced" => "advanced-filter",
            "clear-filter" => "clear-filter",
            "page-setup-dialog" => "page-setup",
            "view-gridlines" => "gridlines",
            "print-gridlines" => "print-gridlines",
            "view-headings" => "headings",
            "print-headings" => "print-headings",
            "object-fill" => "fill",
            "object-outline" => "outline-color",
            "object-size" => "size",
            "object-rotate" => "rotate",
            "shape-gradient" => "gradient",
            "shape-fill" => "fill",
            "shape-outline" => "outline-color",
            "shape-effects" => "effects",
            "object-effects" => "effects",
            "selection-pane" => "selection-pane",
            "ink-to-shape" => "shapes",
            "ink-to-math" => "math-trig",
            "math" => "math-trig",
            "recently-used" => "recent",
            "date" => "date-time",
            "lookup" => "lookup-reference",
            "formula-auditing" => "evaluate-formula",
            "calculation" => "calculate-now",
            "workbook-stats" => "statistics",
            "workbook-statistics" => "statistics",
            "accessibility" => "accessibility-checker",
            "refresh-pivot" => "refresh-all",
            "show-details" => "show-detail",
            "links-and-objects" => "hyperlink",
            "help-online" => "help",
            "contact-support" => "contact-support",
            "what-s-new" => "what-s-new",
            "whats-new" => "what-s-new",
            "about-freex" => "about",
            "side-by-side" => "view-side-by-side",
            "sync-scrolling" => "synchronous-scrolling",
            "reset-position" => "reset-window-position",
            "100" => "zoom-to-100",
            "save-as" => "save-as",
            "export-pdf-xps" => "export",
            "page-orientation" => "page-orientation",
            "hide" => "hide-sheet",
            "unhide" => "unhide-sheet",
            "show-detail" => "show-detail",
            "hide-detail" => "hide-detail",
            "collapse-group" => "hide-detail",
            "expand-group" => "show-detail",
            "add-watch" => "watch-add",
            "delete-watch" => "watch-delete",
            "reapply" => "reapply-filter",
            "reapply-filter" => "reapply-filter",
            "sort-a-to-z" => "sort-ascending",
            "sort-z-to-a" => "sort-descending",
            "pick-from-drop-down-list" => "pick-from-dropdown",
            "macros" => "macros",
            "macro" => "macros",
            "queries-connections" => "queries-connections",
            "check-for-updates" => "check-for-updates",
            "pin-to-list" => "pin-to-list",
            "unpin-from-list" => "unpin-from-list",
            "remove-from-list" => "remove-from-list",
            "rename" => "rename-sheet",
            "duplicate" => "duplicate-sheet",
            "plus-minus-buttons" => "show-detail",
            "buttons" => "show-detail",
            _ => ""
        };

        if (alias.Length > 0 && !string.Equals(alias, slug, StringComparison.Ordinal))
            yield return alias;
    }

    private static string NormalizeCommandIconName(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var handlerIndex = text.IndexOf('#', StringComparison.Ordinal);
        if (handlerIndex >= 0 && text.Equals("Clear#ClearFilterButton_Click", StringComparison.OrdinalIgnoreCase))
            return "Clear Filter";

        return handlerIndex >= 0
            ? text[..handlerIndex]
            : text;
    }

    private static string ToCommandIconSlug(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var lower = text
            .Trim()
            .ToLowerInvariant()
            .Replace("&amp;", "and", StringComparison.Ordinal)
            .Replace("&", "and", StringComparison.Ordinal);
        var builder = new System.Text.StringBuilder(lower.Length);
        var pendingDash = false;

        foreach (var ch in lower)
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                if (pendingDash && builder.Length > 0)
                    builder.Append('-');
                builder.Append(ch);
                pendingDash = false;
            }
            else
            {
                pendingDash = builder.Length > 0;
            }
        }

        return builder.ToString().Trim('-');
    }

}
