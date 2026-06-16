using System.Globalization;

namespace FreeW.Core.Model;

/// <summary>
/// The kind of document object a cross-reference points at. Mirrors the categories the Insert &gt;
/// Cross-reference dialog offers: a <see cref="Heading"/> (from the document outline), a named
/// <see cref="Bookmark"/>, a figure/table <see cref="Caption"/>, or a <see cref="Footnote"/>.
/// </summary>
public enum CrossRefType
{
    Heading,
    Bookmark,
    Caption,
    Footnote
}

/// <summary>
/// One candidate cross-reference target. Pure data produced by <see cref="CrossReferences.Targets"/>.
/// </summary>
/// <param name="Display">
/// The human-readable label shown in the picker (e.g. a heading's text, a bookmark name, a caption's
/// text, or "Footnote 1").
/// </param>
/// <param name="Anchor">
/// The bookmark name this target can be linked to (so the inserted reference can be a clickable
/// internal link), or null when the target has no anchor and must be inserted as plain text.
/// </param>
/// <param name="BlockIndex">
/// The body block index of the originating paragraph (heading/bookmark/caption), or null for targets
/// (footnotes) that are not body blocks.
/// </param>
public readonly record struct CrossRefTarget(string Display, string? Anchor, int? BlockIndex);

/// <summary>
/// Pure, WPF-free enumeration of cross-reference targets and the text a reference inserts. Lives in
/// the model project so it is fully unit-testable without any UI.
/// <para>
/// Targets are derived deterministically from existing document structure:
/// </para>
/// <list type="bullet">
/// <item><see cref="CrossRefType.Heading"/> — the heading paragraphs from <see cref="DocumentOutline"/>.</item>
/// <item><see cref="CrossRefType.Bookmark"/> — body paragraphs carrying a <see cref="Paragraph.BookmarkName"/>.</item>
/// <item><see cref="CrossRefType.Caption"/> — <c>Caption</c>-styled paragraphs (see <see cref="Captions"/>).</item>
/// <item><see cref="CrossRefType.Footnote"/> — the entries in <see cref="TextDocument.Footnotes"/>.</item>
/// </list>
/// A heading or caption target only carries an <see cref="CrossRefTarget.Anchor"/> when its paragraph
/// already has a bookmark name; otherwise the reference is inserted as plain text. Bookmark targets are
/// always anchored (the anchor is the bookmark name itself).
/// </summary>
public static class CrossReferences
{
    /// <summary>
    /// Enumerates the cross-reference targets of <paramref name="type"/> in <paramref name="doc"/>, in
    /// document order (footnotes ordered by ascending id). Returns an empty list when the document has
    /// no targets of that type. Deterministic and side-effect free.
    /// </summary>
    public static IReadOnlyList<CrossRefTarget> Targets(TextDocument doc, CrossRefType type)
    {
        ArgumentNullException.ThrowIfNull(doc);

        return type switch
        {
            CrossRefType.Heading => HeadingTargets(doc),
            CrossRefType.Bookmark => BookmarkTargets(doc),
            CrossRefType.Caption => CaptionTargets(doc),
            CrossRefType.Footnote => FootnoteTargets(doc),
            _ => []
        };
    }

    /// <summary>
    /// The text a reference to <paramref name="target"/> inserts: its <see cref="CrossRefTarget.Display"/>
    /// label (the heading/caption text, the bookmark name, or "Footnote N").
    /// </summary>
    public static string ReferenceText(CrossRefTarget target) => target.Display;

    private static List<CrossRefTarget> HeadingTargets(TextDocument doc)
    {
        var targets = new List<CrossRefTarget>();
        foreach (var entry in DocumentOutline.Of(doc))
        {
            var anchor = AnchorAt(doc, entry.BlockIndex);
            targets.Add(new CrossRefTarget(entry.Text, anchor, entry.BlockIndex));
        }
        return targets;
    }

    private static List<CrossRefTarget> BookmarkTargets(TextDocument doc)
    {
        var targets = new List<CrossRefTarget>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var blocks = doc.Blocks;
        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i] is Paragraph { BookmarkName: { Length: > 0 } name } && seen.Add(name))
                targets.Add(new CrossRefTarget(name, name, i));
        }
        return targets;
    }

    private static List<CrossRefTarget> CaptionTargets(TextDocument doc)
    {
        var targets = new List<CrossRefTarget>();
        var blocks = doc.Blocks;
        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i] is Paragraph paragraph && Captions.IsCaptionParagraph(paragraph))
                targets.Add(new CrossRefTarget(paragraph.PlainText, AnchorAt(doc, i), i));
        }
        return targets;
    }

    private static List<CrossRefTarget> FootnoteTargets(TextDocument doc)
    {
        var targets = new List<CrossRefTarget>();
        foreach (var id in doc.Footnotes.Keys.OrderBy(k => k))
            targets.Add(new CrossRefTarget(FootnoteDisplay(id), Anchor: null, BlockIndex: null));
        return targets;
    }

    private static string FootnoteDisplay(int id) =>
        "Footnote " + id.ToString(CultureInfo.InvariantCulture);

    // The bookmark name on the body paragraph at blockIndex, or null when it carries none.
    private static string? AnchorAt(TextDocument doc, int blockIndex) =>
        blockIndex >= 0 && blockIndex < doc.Blocks.Count
        && doc.Blocks[blockIndex] is Paragraph { BookmarkName: { Length: > 0 } name }
            ? name
            : null;
}
