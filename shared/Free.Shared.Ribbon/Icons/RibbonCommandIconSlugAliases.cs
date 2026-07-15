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
            ["font-family"] = "fonts", ["font-size"] = "fonts", ["font-dialog"] = "fonts",
            ["highlight"] = "highlighter", ["clear-formatting"] = "clear",
            ["align-center"] = "center", ["align-center-small"] = "center-small",
            ["align-justify"] = "distributed-justify", ["indent-increase"] = "increase-indent",
            ["indent-decrease"] = "decrease-indent", ["indent-increase-small"] = "increase-indent-small",
            ["indent-decrease-small"] = "decrease-indent-small", ["multilevel-list"] = "numbering",
            ["merge-cells"] = "merge-center", ["para-border"] = "borders", ["para-shading"] = "fill-color",
            ["page-valign"] = "middle-align", ["style"] = "styles", ["manage-styles"] = "styles",
            ["style-normal"] = "normal", ["style-heading1"] = "heading-1", ["style-heading2"] = "heading-2",
            ["style-heading3"] = "headings", ["style-title"] = "title", ["style-clear"] = "clear",
            ["horizontal-rule"] = "line", ["cell-shading"] = "fill-color", ["shape-ellipse"] = "ellipse",
            ["shape-rectangle"] = "rectangle", ["shape-rounded"] = "rectangle", ["shape-textbox"] = "text-box",
            ["chart"] = "column-chart", ["datetime"] = "date-time", ["index-mark"] = "index",
            ["toc-refresh"] = "refresh-all", ["tof-refresh"] = "refresh-all", ["citation-style"] = "citation",
            ["insert-file"] = "insert", ["insert-quickpart"] = "insert", ["insert-table"] = "table",
            ["insert-caption-equation"] = "equation", ["insert-caption-figure"] = "caption",
            ["insert-caption-table"] = "table", ["save-quickpart"] = "save", ["field"] = "insert-function",
            ["object"] = "insert", ["image-align-left"] = "align-left", ["image-align-center"] = "center",
            ["image-align-right"] = "align-right", ["image-alt-text"] = "alt-text", ["image-size"] = "size",
            ["cc-text"] = "text-box", ["hyperlink-tooltip"] = "comment-note", ["edit-hyperlink"] = "hyperlink",
            ["remove-hyperlink"] = "hyperlink", ["link-bookmark"] = "hyperlink", ["print-layout"] = "page-layout",
            ["print-preview"] = "print", ["page-border"] = "borders", ["restrict-editing"] = "protect-sheet",
            ["merge-data"] = "mail-merge", ["merge-email"] = "mail-merge", ["merge-field"] = "mail-merge",
            ["merge-finish"] = "mail-merge", ["merge-preview"] = "mail-merge", ["merge-rules"] = "merge-rules",
            ["merge-sequence-number"] = "merge-sequence-number", ["merge-rule-if"] = "merge-rules",
            ["merge-rule-skip-record-if"] = "merge-rules", ["merge-rule-next-record-if"] = "merge-next-record",
            ["merge-rule-fill-in"] = "field", ["merge-rule-ask"] = "field", ["merge-rule-set"] = "field",
            ["merge-rule-ref"] = "field", ["theme"] = "themes", ["spellcheck-toggle"] = "spelling",
            ["accept-all"] = "accept-change", ["reject-all"] = "reject-change", ["paste-plain"] = "paste-special",
            ["paste-merge"] = "paste-special", ["bullet-list"] = "bullets", ["bulleted-list"] = "bullets",
            ["numbered-list"] = "numbering", ["pictures"] = "picture", ["table-insert"] = "table",
            ["table-of-contents-gallery"] = "table-of-contents", ["tof-equation"] = "equation",
            ["tof-figure"] = "caption", ["tof-table"] = "table", ["zoom-dialog"] = "zoom",
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
