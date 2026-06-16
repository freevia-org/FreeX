using System.Globalization;

namespace FreeW.Core.Model;

/// <summary>
/// One entry in a document's heading outline: the body block index of the heading paragraph, its
/// outline <see cref="Level"/> (Title = 0, Heading1 = 1, Heading2 = 2, …), the heading's plain text,
/// and the originating <see cref="StyleId"/>. Pure data, produced by <see cref="DocumentOutline"/>.
/// </summary>
/// <param name="BlockIndex">Index of the paragraph in <see cref="TextDocument.Blocks"/> (document order).</param>
/// <param name="Level">Outline depth: 0 for Title, 1 for Heading 1, 2 for Heading 2, and so on.</param>
/// <param name="Text">The heading paragraph's plain text (may be empty for a blank heading).</param>
/// <param name="StyleId">The style id that classified this paragraph as a heading/title.</param>
public readonly record struct OutlineEntry(int BlockIndex, int Level, string Text, string StyleId);

/// <summary>
/// Pure, WPF-free extraction of a document's heading outline (the navigation-pane model). Lives in
/// the model project so it is unit-testable without any UI. The outline is the sequence of body
/// paragraphs whose <see cref="Paragraph.StyleId"/> is a recognised heading/title style, in document
/// order, each carrying a level derived from the style:
/// <list type="bullet">
/// <item><c>Title</c> → level 0.</item>
/// <item><c>HeadingN</c> → level N (e.g. <c>Heading1</c> → 1, <c>Heading2</c> → 2).</item>
/// </list>
/// Non-heading paragraphs, table content, and paragraphs with an unrecognised/absent style are
/// excluded. The block index is the paragraph's position in <see cref="TextDocument.Blocks"/>, so a
/// consumer can map an entry back to the matching block in document order.
/// </summary>
public static class DocumentOutline
{
    private const string TitleStyleId = "Title";
    private const string HeadingPrefix = "Heading";

    /// <summary>
    /// Builds the heading outline for <paramref name="document"/>: body paragraphs whose style is a
    /// title or heading, in document order, with their derived level, text, and style id. Returns an
    /// empty list for a document with no headings (or an empty document).
    /// </summary>
    public static IReadOnlyList<OutlineEntry> Of(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var entries = new List<OutlineEntry>();
        var blocks = document.Blocks;
        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i] is not Paragraph paragraph)
                continue;
            if (TryGetLevel(paragraph.StyleId, out var level))
                entries.Add(new OutlineEntry(i, level, paragraph.PlainText, paragraph.StyleId!));
        }
        return entries;
    }

    /// <summary>
    /// Returns true and the outline level when <paramref name="styleId"/> names a heading/title style:
    /// <c>Title</c> → 0, and <c>HeadingN</c> → N for a positive integer N. Any other (or null) style id
    /// is not an outline heading.
    /// </summary>
    public static bool TryGetLevel(string? styleId, out int level)
    {
        level = 0;
        if (string.IsNullOrEmpty(styleId))
            return false;

        if (string.Equals(styleId, TitleStyleId, StringComparison.Ordinal))
            return true; // Title sits at the top of the outline (level 0).

        if (styleId.StartsWith(HeadingPrefix, StringComparison.Ordinal)
            && int.TryParse(
                styleId.AsSpan(HeadingPrefix.Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var headingNumber)
            && headingNumber > 0)
        {
            level = headingNumber;
            return true;
        }

        return false;
    }
}
