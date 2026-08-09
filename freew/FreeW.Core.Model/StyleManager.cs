using System.Text;

namespace FreeW.Core.Model;

/// <summary>
/// Pure, deterministic create/modify/delete operations over a <see cref="TextDocument"/>'s
/// <see cref="TextDocument.Styles"/> catalog. These back the editor's New Style / Modify Style /
/// Manage Styles UI but carry no UI dependency, so they are fully unit-testable. A style added here
/// round-trips through docx via the existing <c>DocxWriter.BuildStyles</c> — no I/O changes required.
/// </summary>
public static class StyleManager
{
    /// <summary>
    /// Built-in style ids that are guarded even though they are not (yet) part of
    /// <see cref="BuiltInStyles.Gallery"/> — the Caption / Index / Table-of-Figures styles the outline
    /// and TOC/TOF tooling seed directly. Guarding them keeps the built-in catalog intact so existing
    /// documents and that tooling keep resolving.
    /// </summary>
    public static readonly IReadOnlySet<string> BuiltInStyleIds = new HashSet<string>(StringComparer.Ordinal)
    {
        "Normal",
        "Heading1", "Heading2", "Heading3", "Heading4", "Heading5", "Heading6",
        "Title", "Subtitle", "Quote",
        "Caption",
        "IndexHeading", "IndexEntry",
        "TableOfFiguresHeading", "TableOfFiguresEntry",
    };

    /// <summary>
    /// True when <paramref name="styleId"/> names a guarded built-in style: either listed explicitly in
    /// <see cref="BuiltInStyleIds"/>, or present in the authoritative <see cref="BuiltInStyles.Gallery"/>
    /// (every style the app can seed via Home &gt; Styles, e.g. Strong, Emphasis, NoSpacing,
    /// ListParagraph, IntenseQuote). Deriving from the gallery means this can never drift out of sync
    /// with what the app actually offers as a built-in — Word never allows deleting any of these, and
    /// deleting one here would silently strip formatting from any content still referencing its id.
    /// </summary>
    public static bool IsBuiltIn(string styleId) =>
        styleId is not null && (BuiltInStyleIds.Contains(styleId) || BuiltInStyles.Find(styleId) is not null);

    /// <summary>
    /// Create a new custom paragraph style from <paramref name="name"/>, give it a collision-free id
    /// derived from the name, add it to <paramref name="doc"/>'s catalog, and return it. The id is the
    /// name with non-alphanumeric characters removed; if that is empty or already taken (by a built-in
    /// or a previously created style) a numeric suffix (<c>2</c>, <c>3</c>, …) is appended until unique.
    /// The display <see cref="DocumentStyle.Name"/> is likewise disambiguated (case-insensitively, Word's
    /// own comparison for style names) against every existing style's name — Word does not allow two
    /// styles to share a display name (styles.xml would carry two identical <c>w:name</c> values, which
    /// Word treats as invalid and collapses on load) — so a name colliding with an existing style (built-in
    /// or custom) gets a " 2", " 3", … suffix appended until unique, mirroring the id's own disambiguation.
    /// </summary>
    /// <param name="doc">The document whose <see cref="TextDocument.Styles"/> catalog is extended.</param>
    /// <param name="name">
    /// The human-readable style name (e.g. "My Heading"); trimmed, must be non-empty. The style's actual
    /// stored name may differ from this if it collides with an existing style's name (see remarks above).
    /// </param>
    /// <param name="basedOnId">
    /// The id of the style this one inherits from, or null for none. Ignored when it does not name an
    /// existing style, so a stale based-on never produces a dangling reference in the catalog.
    /// </param>
    /// <param name="run">The run (character) formatting carried by the style.</param>
    /// <param name="para">The paragraph formatting carried by the style.</param>
    /// <param name="nextStyleId">
    /// The id of the style applied to the following paragraph (Word's "Style for following paragraph",
    /// <c>w:next</c>), or null for none. Ignored when it does not name an existing style, so a stale
    /// next-style never produces a dangling reference. A style may point its follow-on at itself.
    /// </param>
    public static DocumentStyle CreateStyle(
        TextDocument doc,
        string name,
        string? basedOnId,
        RunFormatting run,
        ParagraphFormatting para,
        string? nextStyleId = null)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(para);
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            throw new ArgumentException("Style name must be non-empty.", nameof(name));

        var id = GenerateUniqueId(doc, trimmed);
        var uniqueName = GenerateUniqueName(doc, trimmed);
        var basedOn = basedOnId is { Length: > 0 } && doc.Styles.ContainsKey(basedOnId) ? basedOnId : null;
        // A next-style may point at an existing style or at the new style itself (Word allows a style to
        // chain to itself, e.g. a body style whose follow-on is the same style). Anything else is dropped.
        var next = nextStyleId is { Length: > 0 }
            && (doc.Styles.ContainsKey(nextStyleId) || string.Equals(nextStyleId, id, StringComparison.Ordinal))
            ? nextStyleId
            : null;
        var style = new DocumentStyle
        {
            Id = id,
            Name = uniqueName,
            BasedOnStyleId = basedOn,
            NextStyleId = next,
            Run = run,
            Paragraph = para,
        };
        doc.Styles[id] = style;
        return style;
    }

    /// <summary>
    /// Update an existing style's mutable formatting (and optionally its name / based-on). Returns the
    /// style, or null when <paramref name="styleId"/> is not in the catalog. The id is never changed (so
    /// paragraphs referencing it keep resolving). A <paramref name="basedOnId"/> that does not name an
    /// existing style (other than clearing it with null) is ignored, preventing dangling references; a
    /// style is never allowed to base on itself, directly or indirectly. A <paramref name="basedOnId"/>
    /// whose own based-on chain loops back to <paramref name="styleId"/> at any depth (A based on B based
    /// on A, or longer) is likewise ignored — accepting it would make the style-resolution walk that
    /// every effective-formatting consumer performs run in circles.
    /// </summary>
    public static DocumentStyle? ModifyStyle(
        TextDocument doc,
        string styleId,
        RunFormatting? run = null,
        ParagraphFormatting? para = null,
        string? name = null,
        string? basedOnId = null,
        bool clearBasedOn = false,
        string? nextStyleId = null,
        bool clearNext = false)
    {
        ArgumentNullException.ThrowIfNull(doc);
        if (styleId is null || !doc.Styles.TryGetValue(styleId, out var existing))
            return null;

        var newName = existing.Name;
        if (name is { } candidate)
        {
            var trimmed = candidate.Trim();
            if (trimmed.Length > 0)
                newName = trimmed;
        }

        var newBasedOn = existing.BasedOnStyleId;
        if (clearBasedOn)
            newBasedOn = null;
        else if (basedOnId is { Length: > 0 }
            && !string.Equals(basedOnId, styleId, StringComparison.Ordinal)
            && doc.Styles.ContainsKey(basedOnId)
            && !CreatesBasedOnCycle(doc, styleId, basedOnId))
            newBasedOn = basedOnId;

        // The follow-on style (w:next). Clearing wins; otherwise a value naming an existing style (or this
        // style itself, which Word permits) replaces it, and anything else is ignored — symmetric with
        // based-on but allowing the self-reference a next-style legitimately uses.
        var newNext = existing.NextStyleId;
        if (clearNext)
            newNext = null;
        else if (nextStyleId is { Length: > 0 }
            && (doc.Styles.ContainsKey(nextStyleId) || string.Equals(nextStyleId, styleId, StringComparison.Ordinal)))
            newNext = nextStyleId;

        // DocumentStyle's Id/Name/BasedOn are init-only, so replace the entry rather than mutate it.
        var updated = new DocumentStyle
        {
            Id = existing.Id,
            Name = newName,
            Type = existing.Type,
            BasedOnStyleId = newBasedOn,
            NextStyleId = newNext,
            OutlineLevel = existing.OutlineLevel,
            Run = run ?? existing.Run,
            Paragraph = para ?? existing.Paragraph,
            // Preserve read-only structural data the modify dialog does not edit, so modifying a style read
            // from a docx does not silently drop its table borders or preserved numbering on the next save.
            TableBorders = existing.TableBorders,
            PreservedNumbering = existing.PreservedNumbering,
        };
        doc.Styles[styleId] = updated;
        return updated;
    }

    /// <summary>
    /// Remove the custom style <paramref name="styleId"/> from the catalog. Returns false (and removes nothing)
    /// when the id is a guarded built-in (see <see cref="IsBuiltIn"/>) or is not present. Returns
    /// true when a custom style was removed. Paragraphs still referencing the removed id fall back to the
    /// document default formatting (an unknown StyleId resolves to nothing), mirroring Word.
    /// </summary>
    public static bool DeleteStyle(TextDocument doc, string styleId)
    {
        ArgumentNullException.ThrowIfNull(doc);
        if (styleId is null || IsBuiltIn(styleId))
            return false;
        return doc.Styles.Remove(styleId);
    }

    // Derive a unique style id from the display name: keep ASCII letters/digits, then disambiguate with a
    // numeric suffix against the existing catalog (which includes the built-ins). Deterministic.
    private static string GenerateUniqueId(TextDocument doc, string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (char.IsAsciiLetterOrDigit(ch))
                sb.Append(ch);
        }
        var baseId = sb.Length > 0 ? sb.ToString() : "Style";
        // A leading digit is legal as a dictionary key but unusual for a style id; prefix to be safe.
        if (char.IsAsciiDigit(baseId[0]))
            baseId = "Style" + baseId;

        if (!doc.Styles.ContainsKey(baseId))
            return baseId;

        for (var n = 2; ; n++)
        {
            var candidate = baseId + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!doc.Styles.ContainsKey(candidate))
                return candidate;
        }
    }

    // Disambiguate the display name against every existing style's Name (case-insensitively — Word treats
    // style names as case-insensitively unique), appending " 2", " 3", … until unique. Word itself uses this
    // same "<name> N" convention (e.g. pasting a style from another document that already has "Heading 1
    // Char" produces "Heading 1 Char 2"), so this mirrors real Word behaviour rather than inventing a
    // FreeW-only convention.
    private static string GenerateUniqueName(TextDocument doc, string name)
    {
        if (!NameInUse(doc, name))
            return name;

        for (var n = 2; ; n++)
        {
            var candidate = name + " " + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!NameInUse(doc, candidate))
                return candidate;
        }
    }

    // True when re-pointing styleId's based-on to candidateBasedOnId would introduce a cycle at any depth:
    // walk candidateBasedOnId's own based-on chain and see whether it ever reaches styleId. The visited set
    // both stops on the target (a genuine cycle through styleId) and terminates safely if the existing
    // catalog already contains an unrelated cycle elsewhere (e.g. authored by a corrupt/hand-edited file) —
    // revisiting an already-seen id ends the walk without reaching styleId, so it correctly reports "no
    // cycle introduced by this edit" rather than hanging or throwing.
    private static bool CreatesBasedOnCycle(TextDocument doc, string styleId, string candidateBasedOnId)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var current = candidateBasedOnId;
        while (current is not null && visited.Add(current))
        {
            if (string.Equals(current, styleId, StringComparison.Ordinal))
                return true;
            if (!doc.Styles.TryGetValue(current, out var style))
                return false;
            current = style.BasedOnStyleId;
        }
        return false;
    }

    private static bool NameInUse(TextDocument doc, string name)
    {
        foreach (var style in doc.Styles.Values)
        {
            if (string.Equals(style.Name, name, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
