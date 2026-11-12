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
    /// Every guarded built-in style id: the authoritative gallery plus supplemental Caption / Index /
    /// Table-of-Figures and deeper outline styles seeded by their dedicated tooling.
    /// </summary>
    public static readonly IReadOnlySet<string> BuiltInStyleIds = BuiltInStyles.Gallery
        .Select(descriptor => descriptor.Id)
        .Concat(
        [
            "Heading5", "Heading6",
            "Caption",
            "IndexHeading", "IndexEntry",
            "TableOfFiguresHeading", "TableOfFiguresEntry",
        ])
        .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// True when <paramref name="styleId"/> names a guarded built-in style. Gallery membership is derived
    /// into <see cref="BuiltInStyleIds"/>, so the delete guard cannot drift from styles the app can seed.
    /// </summary>
    public static bool IsBuiltIn(string styleId) =>
        styleId is not null && BuiltInStyleIds.Contains(styleId);

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
        string? nextStyleId = null,
        StyleType type = StyleType.Paragraph,
        bool quickFormat = true)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(para);
        if (type is not (StyleType.Paragraph or StyleType.Character))
            throw new ArgumentOutOfRangeException(nameof(type), type, "Only paragraph and character styles can be authored.");
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            throw new ArgumentException("Style name must be non-empty.", nameof(name));

        var id = GenerateUniqueId(doc, trimmed);
        var uniqueName = MakeNameUnique(doc, trimmed);
        // Word only lets a character style inherit from another character style; a paragraph based-on
        // is dropped like any other invalid reference.
        var basedOn = basedOnId is { Length: > 0 }
            && doc.Styles.TryGetValue(basedOnId, out var basedOnStyle)
            && (type != StyleType.Character || basedOnStyle.Type == StyleType.Character)
            ? basedOnId
            : null;
        if (type == StyleType.Character)
        {
            // A character style carries no paragraph formatting and no follow-on style.
            para = ParagraphFormatting.Default;
            nextStyleId = null;
        }
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
            Type = type,
            BasedOnStyleId = basedOn,
            NextStyleId = next,
            QuickFormat = quickFormat,
            Run = run,
            Paragraph = para,
        };
        doc.Styles[id] = style;
        return style;
    }

    /// <summary>The suffix Word gives the character half of a linked style ("Heading 1 Char").</summary>
    public const string LinkedCharacterStyleSuffix = " Char";

    /// <summary>
    /// Create Word's "Linked (paragraph and character)" style: a paragraph style plus its paired
    /// <c>"&lt;name&gt; Char"</c> character style, each naming the other through
    /// <see cref="DocumentStyle.LinkedStyleId"/> (<c>w:link</c>). Applied with a caret it formats the
    /// paragraph; applied to a text selection it formats just those characters through the character half.
    /// The character half carries the same run formatting, is based on the linked character style of the
    /// paragraph half's based-on style (when that one is itself linked), and is kept out of the gallery
    /// because Word shows a linked pair as one entry. Returns the paragraph half.
    /// </summary>
    public static DocumentStyle CreateLinkedStyle(
        TextDocument doc,
        string name,
        string? basedOnId,
        RunFormatting run,
        ParagraphFormatting para,
        string? nextStyleId = null,
        bool quickFormat = true)
    {
        var paragraphStyle = CreateStyle(doc, name, basedOnId, run, para, nextStyleId, StyleType.Paragraph, quickFormat);
        var characterBasedOn = paragraphStyle.BasedOnStyleId is { } parentId
            ? FindLinkedCharacterStyle(doc, parentId)?.Id
            : null;
        var characterStyle = CreateStyle(
            doc,
            paragraphStyle.Name + LinkedCharacterStyleSuffix,
            characterBasedOn,
            run,
            ParagraphFormatting.Default,
            nextStyleId: null,
            StyleType.Character,
            quickFormat: false);
        doc.Styles[characterStyle.Id] = new DocumentStyle(characterStyle) { LinkedStyleId = paragraphStyle.Id };
        var linkedParagraph = new DocumentStyle(paragraphStyle) { LinkedStyleId = characterStyle.Id };
        doc.Styles[paragraphStyle.Id] = linkedParagraph;
        return linkedParagraph;
    }

    /// <summary>
    /// The character half of the linked pair <paramref name="styleId"/> heads, or null when the style
    /// is not a paragraph style whose <c>w:link</c> names a character style.
    /// </summary>
    public static DocumentStyle? FindLinkedCharacterStyle(TextDocument doc, string styleId)
    {
        ArgumentNullException.ThrowIfNull(doc);
        return styleId is not null
            && doc.Styles.TryGetValue(styleId, out var style)
            && style.Type == StyleType.Paragraph
            && style.LinkedStyleId is { Length: > 0 } linkedId
            && doc.Styles.TryGetValue(linkedId, out var linked)
            && linked.Type == StyleType.Character
            ? linked
            : null;
    }

    /// <summary>
    /// True when <paramref name="style"/> is the character half of a linked pair. Word presents the pair
    /// as a single style named after the paragraph half, so style pickers hide this half.
    /// </summary>
    public static bool IsLinkedCharacterHalf(TextDocument doc, DocumentStyle style)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(style);
        return style.Type == StyleType.Character
            && style.LinkedStyleId is { Length: > 0 } linkedId
            && doc.Styles.TryGetValue(linkedId, out var linked)
            && linked.Type == StyleType.Paragraph;
    }

    /// <summary>
    /// Re-derive the character half of a linked pair from its paragraph half after the paragraph half
    /// was modified or renamed: a linked style is one style to the user, so its run formatting and name
    /// ("&lt;name&gt; Char") follow the paragraph half. No-op (returns null) for an unlinked style.
    /// </summary>
    public static DocumentStyle? SyncLinkedCharacterStyle(TextDocument doc, string paragraphStyleId)
    {
        ArgumentNullException.ThrowIfNull(doc);
        if (FindLinkedCharacterStyle(doc, paragraphStyleId) is not { } linked
            || !doc.Styles.TryGetValue(paragraphStyleId, out var paragraphStyle))
        {
            return null;
        }

        var desiredName = paragraphStyle.Name + LinkedCharacterStyleSuffix;
        var name = string.Equals(linked.Name, desiredName, StringComparison.Ordinal)
            ? linked.Name
            : MakeNameUnique(desiredName, NamesExcept(doc, linked.Id));
        var updated = new DocumentStyle(linked) { Name = name, Run = paragraphStyle.Run };
        doc.Styles[linked.Id] = updated;
        return updated;
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
        bool clearNext = false,
        bool? quickFormat = null)
    {
        ArgumentNullException.ThrowIfNull(doc);
        if (styleId is null || !doc.Styles.TryGetValue(styleId, out var existing))
            return null;

        var newName = existing.Name;
        if (name is { } candidate)
        {
            var trimmed = candidate.Trim();
            // A rename may not collide with another style's display name (Word refuses two styles sharing
            // a w:name); a colliding rename takes the same " N" suffix a colliding create does.
            if (trimmed.Length > 0 && !string.Equals(trimmed, existing.Name, StringComparison.Ordinal))
                newName = MakeNameUnique(trimmed, NamesExcept(doc, styleId));
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
        // The copy constructor preserves the structural data the modify dialog does not edit (table
        // borders, preserved numbering, the w:link pairing, preserved table style XML), so modifying a
        // style read from a docx does not silently drop any of it on the next save.
        var updated = new DocumentStyle(existing)
        {
            Name = newName,
            BasedOnStyleId = newBasedOn,
            NextStyleId = newNext,
            QuickFormat = quickFormat ?? existing.QuickFormat,
            Run = run ?? existing.Run,
            Paragraph = para ?? existing.Paragraph,
        };
        doc.Styles[styleId] = updated;
        return updated;
    }

    /// <summary>
    /// Remove the custom style <paramref name="styleId"/> from the catalog. Returns false (and removes nothing)
    /// when the id is a guarded built-in (see <see cref="IsBuiltIn"/>) or is not present. Returns
    /// true when a custom style was removed. Paragraphs still referencing the removed id fall back to the
    /// document default formatting (an unknown StyleId resolves to nothing), mirroring Word. Any other style
    /// whose <see cref="DocumentStyle.BasedOnStyleId"/> named the removed style is "promoted" onto the
    /// removed style's own based-on (mirroring Word's own delete-style behaviour), so it keeps inheriting
    /// from the deleted style's parent instead of being left with a dangling reference that would silently
    /// drop the whole inherited chain from every paragraph/run still using that child style.
    /// </summary>
    public static bool DeleteStyle(TextDocument doc, string styleId)
    {
        ArgumentNullException.ThrowIfNull(doc);
        if (styleId is null || IsBuiltIn(styleId) || !doc.Styles.TryGetValue(styleId, out var removed))
            return false;

        var promotedBasedOn = removed.BasedOnStyleId;
        foreach (var child in doc.Styles.Values.ToArray())
        {
            if (!string.Equals(child.BasedOnStyleId, styleId, StringComparison.Ordinal))
                continue;

            // DocumentStyle's BasedOnStyleId is init-only, so replace the entry rather than mutate it.
            doc.Styles[child.Id] = new DocumentStyle(child) { BasedOnStyleId = promotedBasedOn };
        }

        return doc.Styles.Remove(styleId);
    }

    /// <summary>
    /// Word's Delete Style: remove the custom style <paramref name="styleId"/> (and, for a linked pair,
    /// its character half) and leave nothing pointing at it. Paragraphs formatted with a removed
    /// paragraph style return to the default paragraph style (Normal), which is what Word does, and runs
    /// carrying a removed character style drop it; other styles' follow-on (<c>w:next</c>) and link
    /// (<c>w:link</c>) references to a removed id are cleared, and children are promoted onto the removed
    /// style's own based-on exactly as <see cref="DeleteStyle"/> does. Returns the removed ids (empty when
    /// the style is built-in or absent, in which case nothing changes).
    /// </summary>
    public static IReadOnlyList<string> DeleteStyleAndReassign(TextDocument doc, string styleId)
    {
        ArgumentNullException.ThrowIfNull(doc);
        if (styleId is null || IsBuiltIn(styleId) || !doc.Styles.ContainsKey(styleId))
            return [];

        var removed = new List<string>(2) { styleId };
        if (FindLinkedCharacterStyle(doc, styleId) is { } linkedHalf
            && !IsBuiltIn(linkedHalf.Id)
            && string.Equals(linkedHalf.LinkedStyleId, styleId, StringComparison.Ordinal))
        {
            removed.Add(linkedHalf.Id);
        }

        foreach (var id in removed)
            DeleteStyle(doc, id);

        var removedSet = removed.ToHashSet(StringComparer.Ordinal);
        foreach (var style in doc.Styles.Values.ToArray())
        {
            var dropNext = style.NextStyleId is { } next && removedSet.Contains(next);
            var dropLink = style.LinkedStyleId is { } link && removedSet.Contains(link);
            if (!dropNext && !dropLink)
                continue;
            doc.Styles[style.Id] = new DocumentStyle(style)
            {
                NextStyleId = dropNext ? null : style.NextStyleId,
                LinkedStyleId = dropLink ? null : style.LinkedStyleId,
            };
        }

        foreach (var paragraph in EnumerateStyledParagraphs(doc))
        {
            if (paragraph.StyleId is { } paragraphStyleId && removedSet.Contains(paragraphStyleId))
                paragraph.StyleId = null;
            foreach (var run in paragraph.Runs)
            {
                if (run.StyleId is { } runStyleId && removedSet.Contains(runStyleId))
                    run.StyleId = null;
            }
        }

        return removed;
    }

    /// <summary>
    /// Every paragraph a style reference can live on, in one deterministic order: body (including table
    /// cells and nested tables), headers/footers, footnotes, endnotes, and the text boxes inside them.
    /// Shared by <see cref="DeleteStyleAndReassign"/> and the undo snapshot of the command that drives it,
    /// so both always see the same paragraphs in the same order.
    /// </summary>
    public static IEnumerable<Paragraph> EnumerateStyledParagraphs(TextDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        return TextDocumentStoryTraversal.EnumerateParagraphs(
            doc,
            TextDocumentStorySubset.All,
            TextDocumentStoryTraversalOptions.IncludeTextBoxes
            | TextDocumentStoryTraversalOptions.IncludeShapeTextBoxes
            | TextDocumentStoryTraversalOptions.IncludeNestedTables);
    }

    private static HashSet<string> NamesExcept(TextDocument doc, string styleId) =>
        new(
            doc.Styles.Values
                .Where(style => !string.Equals(style.Id, styleId, StringComparison.Ordinal))
                .Select(style => style.Name),
            StringComparer.OrdinalIgnoreCase);

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

    /// <summary>
    /// Disambiguate <paramref name="name"/> against every existing style's Name in <paramref name="doc"/>
    /// (case-insensitively — Word treats style names as case-insensitively unique), appending " 2", " 3", …
    /// until unique. Word itself uses this same "&lt;name&gt; N" convention (e.g. pasting a style from
    /// another document that already has "Heading 1 Char" produces "Heading 1 Char 2"), so this mirrors real
    /// Word behaviour rather than inventing a FreeW-only convention. Public because <see cref="DocumentCompare"/>'s
    /// style union reuses this exact convention: when both sides of a comparison independently defined a
    /// custom style under the same Name but different ids, the losing side's copy is disambiguated with this
    /// rather than dropped or added verbatim (which would give styles.xml two <c>&lt;w:style&gt;</c> elements
    /// sharing one <c>&lt;w:name&gt;</c>).
    /// </summary>
    public static string MakeNameUnique(TextDocument doc, string name)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var usedNames = new HashSet<string>(
            doc.Styles.Values.Select(style => style.Name),
            StringComparer.OrdinalIgnoreCase);
        return MakeNameUnique(name, usedNames);
    }

    /// <summary>
    /// Claims <paramref name="name"/> or its first available Word-style <c>" N"</c> suffix from
    /// <paramref name="usedNames"/>. The supplied set is updated with the returned name.
    /// </summary>
    internal static string MakeNameUnique(string name, ISet<string> usedNames)
    {
        ArgumentNullException.ThrowIfNull(usedNames);
        if (usedNames.Add(name))
            return name;

        for (var n = 2; ; n++)
        {
            var candidate = name + " " + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (usedNames.Add(candidate))
                return candidate;
        }
    }

    /// <summary>
    /// Finds the id of the style in <paramref name="doc"/> whose display <see cref="DocumentStyle.Name"/>
    /// matches <paramref name="name"/> under Word's case-insensitive name comparison (the same comparison
    /// <see cref="NameInUse"/> and <see cref="MakeNameUnique"/> use), or null when no style currently carries
    /// that name. Word never allows two styles to share a display name in styles.xml (see
    /// <see cref="CreateStyle"/>'s remarks), so this is the one shared lookup every catalog-merging call site
    /// (<see cref="DocumentMerge"/>'s TransferStyles, <see cref="DocumentCompare"/>'s style union) consults
    /// before adding a style under a possibly-new id, so a name collision under a genuinely new id is caught
    /// the same way a same-id collision already was.
    /// </summary>
    public static string? FindStyleIdByName(TextDocument doc, string name)
    {
        ArgumentNullException.ThrowIfNull(doc);
        foreach (var style in doc.Styles.Values)
            if (string.Equals(style.Name, name, StringComparison.OrdinalIgnoreCase))
                return style.Id;
        return null;
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

    private static bool NameInUse(TextDocument doc, string name) => FindStyleIdByName(doc, name) is not null;
}
