namespace Free.Shared.Ribbon.Icons;

/// <summary>
/// Platform-neutral command-id normalization and SVG slug candidate policy.
/// Renderers own only resource loading and drawing; this policy owns the ordered names they try.
/// </summary>
public static class RibbonCommandIconPolicy
{
    /// <summary>Removes the host handler suffix used by composite command ids.</summary>
    public static string NormalizeCommandIconName(string? text)
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

    /// <summary>Converts a command label to the normalized command-icon filename slug.</summary>
    public static string ToCommandIconSlug(string? text)
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

    /// <summary>
    /// Converts an application-qualified command id to a filename slug after removing its prefix.
    /// </summary>
    public static string ToCommandIconSlug(string? text, string applicationPrefix)
    {
        ArgumentNullException.ThrowIfNull(applicationPrefix);

        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var normalized = text.Trim();
        if (applicationPrefix.Length > 0 &&
            normalized.StartsWith(applicationPrefix, StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[applicationPrefix.Length..];
        }

        return ToCommandIconSlug(normalized);
    }

    /// <summary>
    /// Returns candidates in resolution order. Shared canonical aliases are tried first, followed by
    /// the historical slug and then the legacy FreeX command-label alias. Each mapping is one-level
    /// only: aliases are not recursively expanded, so cycles cannot alter fallback behavior.
    /// </summary>
    public static IEnumerable<string> GetCommandIconSlugCandidates(string slug)
    {
        ArgumentNullException.ThrowIfNull(slug);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in RibbonCommandIconSlugAliases.GetCandidates(slug))
        {
            if (seen.Add(candidate))
                yield return candidate;
        }

        var alias = GetLegacyAlias(slug);
        if (alias.Length > 0 && seen.Add(alias))
            yield return alias;
    }

    private static string GetLegacyAlias(string slug) => slug switch
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
        _ => string.Empty
    };
}
