namespace Free.Shared.Ribbon.Icons;

/// <summary>
/// Canonical aliases for command-icon filenames shared by the sister ribbon hosts.
/// The canonical slug is tried first so a product can remove an alternate filename while retaining
/// the command's historical slug at the command-definition boundary.
/// </summary>
public static class RibbonCommandIconSlugAliases
{
    private static readonly IReadOnlyDictionary<string, string> Aliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["orientation"] = "page-orientation", ["size"] = "paper-size",
            ["allow-edit-ranges"] = "allow-users-to-edit-ranges",
            ["date-and-time"] = "date-time", ["lookup-and-reference"] = "lookup-reference",
            ["math-and-trig"] = "math-trig",
            ["custom-paragraph-spacing"] = "paragraph-spacing",
            ["font-family"] = "fonts", ["font-size"] = "fonts", ["font-dialog"] = "fonts",
            ["highlight"] = "highlighter", ["clear-formatting"] = "clear",
            ["customize-colors"] = "theme-colors", ["customize-fonts"] = "theme-fonts",
            ["draftview"] = "draft-view",
            ["align-center"] = "center", ["align-center-small"] = "center-small",
            ["align-justify"] = "distributed-justify", ["indent-increase"] = "increase-indent",
            ["indent-decrease"] = "decrease-indent", ["indent-increase-small"] = "increase-indent-small",
            ["indent-decrease-small"] = "decrease-indent-small", ["multilevel-list"] = "multilevel-define",
            ["merge-cells"] = "merge-center", ["para-border"] = "borders", ["para-shading"] = "fill-color",
            ["page-valign"] = "middle-align", ["style"] = "styles", ["manage-styles"] = "styles",
            ["style-normal"] = "normal", ["style-heading1"] = "heading-1", ["style-heading2"] = "heading-2",
            ["style-heading3"] = "headings", ["style-title"] = "title", ["style-clear"] = "clear",
            ["horizontal-rule"] = "line", ["cell-shading"] = "fill-color", ["shape-ellipse"] = "ellipse",
            ["shape-rectangle"] = "rectangle", ["shape-rounded"] = "rectangle", ["shape-textbox"] = "text-box",
            ["chart"] = "column-chart", ["datetime"] = "date-time", ["index-mark"] = "index",
            ["chart-colors-colorful1"] = "chart-color-colorful1",
            ["chart-colors-colorful2"] = "chart-color-colorful2",
            ["chart-colors-colorful3"] = "chart-color-colorful3",
            ["chart-colors-colorful4"] = "chart-color-colorful4",
            ["chart-colors-mono-blue"] = "chart-color-mono-blue",
            ["chart-colors-mono-grey"] = "chart-color-mono-grey",
            ["chart-colors-mono-orange"] = "chart-color-mono-orange",
            ["toc-refresh"] = "refresh-all", ["tof-refresh"] = "refresh-all", ["citation-style"] = "citation",
            ["insert-file"] = "insert", ["insert-quickpart"] = "paste-special", ["insert-table"] = "table",
            ["insert-caption-equation"] = "equation", ["insert-caption-figure"] = "caption",
            ["insert-caption-table"] = "table", ["save-quickpart"] = "save", ["field"] = "insert-function",
            ["object"] = "insert", ["image-align-left"] = "align-left", ["image-align-center"] = "center",
            ["image-align-right"] = "align-right", ["image-alt-text"] = "alt-text", ["image-size"] = "size",
            ["cc-text"] = "text-box", ["hyperlink-tooltip"] = "comment-note", ["edit-hyperlink"] = "hyperlink",
            ["remove-hyperlink"] = "hyperlink", ["link-bookmark"] = "hyperlink", ["print-layout"] = "page-layout",
            ["print-preview"] = "print", ["page-border"] = "borders", ["restrict-editing"] = "protect-sheet",
            ["merge-data"] = "mail-merge", ["merge-email"] = "mail-merge", ["merge-field"] = "mail-merge",
            ["merge-finish"] = "mail-merge", ["merge-preview"] = "mail-merge",
            ["merge-find-recipient"] = "find-replace-dialog", ["merge-check-errors"] = "inspect-document",
            ["chart-size-dialog"] = "chart-size",
            ["merge-sequence-number"] = "merge-sequence-number",
            ["merge-rules"] = "merge-rule-if",
            ["merge-rule-skip-record-if"] = "merge-rule-if", ["merge-rule-next-record-if"] = "merge-next-record",
            ["merge-rule-fill-in"] = "merge-rule-ask", ["merge-rule-set"] = "merge-rule-ask",
            ["merge-rule-ref"] = "merge-rule-ask", ["theme"] = "themes", ["spellcheck-toggle"] = "spelling",
            ["accept-all"] = "accept-change", ["reject-all"] = "reject-change", ["paste-plain"] = "paste-special",
            ["track-formatting"] = "track-changes",
            ["paste-merge"] = "paste-special", ["bullet-list"] = "bullets", ["bulleted-list"] = "bullets",
            ["numbered-list"] = "numbering", ["pictures"] = "picture", ["table-insert"] = "table",
            ["table-of-contents-gallery"] = "table-of-contents", ["toc"] = "table-of-contents", ["tof"] = "table-of-contents",
            ["tof-equation"] = "equation",
            ["tof-figure"] = "caption", ["tof-table"] = "table", ["zoom-dialog"] = "zoom",
            ["index-insert"] = "index",
            ["smartart-colors"] = "smartart-change-colors", ["smartart-layout"] = "smartart-change-layout",
            ["table-borders"] = "cell-borders", ["table-insert-below"] = "table-insert-row",
            ["table-insert-col-right"] = "table-insert-col", ["table-merge-cells"] = "merge-center",
            ["table-shading"] = "fill-color", ["table-split-cell"] = "split-cell",
            ["image-brightness-minus40"] = "image-brightness-minus20", ["image-brightness-plus40"] = "image-brightness-plus20",
            ["image-saturation-0"] = "image-saturation-50", ["image-saturation-200"] = "image-saturation-50",
            ["image-transparency-25"] = "image-transparency-50", ["image-transparency-75"] = "image-transparency-50",
            ["shape-flip-horizontal"] = "image-flip-horizontal", ["shape-flip-vertical"] = "image-flip-vertical",
            ["shape-position"] = "image-position", ["shape-rotate-left90"] = "image-rotate-left90",
            ["shape-rotate-right90"] = "image-rotate-right90", ["shape-rotate"] = "image-rotate",
            ["shape-wrap"] = "image-wrap", ["shape-wrap-behind"] = "image-wrap-behind",
            ["shape-wrap-front"] = "image-wrap-front", ["shape-wrap-inline"] = "image-wrap-inline",
            ["shape-wrap-square"] = "image-wrap-square", ["shape-wrap-tight"] = "image-wrap-tight",
            ["shape-wrap-top-bottom"] = "image-wrap-top-bottom",
            ["multilevel-preset-0"] = "multilevel-define",
            ["multilevel-preset-1"] = "multilevel-define", ["multilevel-preset-2"] = "multilevel-define",
            ["printlayout"] = "print-layout", ["reset-style-set"] = "style-set", ["reviewingpane"] = "reviewing-pane",
            ["weblayout"] = "web-layout",
            ["layout-bring-forward"] = "bring-forward", ["layout-rotate"] = "rotate",
            ["layout-selection-pane"] = "selection-pane", ["layout-send-backward"] = "send-backward",
            ["layout-wrap"] = "wrap-text", ["test-crash-reporting"] = "feedback",
            ["layout-1"] = "chart-quick-layout-1", ["layout-2"] = "chart-quick-layout-2",
            ["layout-3"] = "chart-quick-layout-3", ["layout-4"] = "chart-quick-layout-4",
            ["layout-5"] = "chart-quick-layout-5", ["layout-6"] = "chart-quick-layout-6",
            ["layout-7"] = "chart-quick-layout-7", ["layout-8"] = "chart-quick-layout-8",
            ["layout-9"] = "chart-quick-layout-9",
        };

    public static bool TryGetCanonicalSlug(string slug, out string canonicalSlug) =>
        Aliases.TryGetValue(slug, out canonicalSlug!);

    public static IEnumerable<string> GetCandidates(string slug)
    {
        ArgumentNullException.ThrowIfNull(slug);
        if (TryGetCanonicalSlug(slug, out var canonicalSlug) &&
            !string.Equals(canonicalSlug, slug, StringComparison.OrdinalIgnoreCase))
            yield return canonicalSlug;
        yield return slug;
    }
}
