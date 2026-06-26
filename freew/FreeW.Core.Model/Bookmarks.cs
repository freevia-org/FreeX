namespace FreeW.Core.Model;

/// <summary>
/// One bookmark target in a document: the bookmark <see cref="Name"/> and the body block index of the
/// paragraph it marks (its position in <see cref="TextDocument.Blocks"/>, in document order). Pure
/// data, produced by <see cref="Bookmarks"/>; a consumer can map an entry back to the matching block
/// (e.g. to scroll/caret to it) via the index.
/// </summary>
/// <param name="Name">The bookmark name (the paragraph's <see cref="Paragraph.BookmarkName"/>).</param>
/// <param name="BlockIndex">Index of the marked paragraph in <see cref="TextDocument.Blocks"/>.</param>
public readonly record struct BookmarkLocation(string Name, int BlockIndex);

/// <summary>
/// Pure, WPF-free helpers over a document's bookmark targets (paragraphs carrying a
/// <see cref="Paragraph.BookmarkName"/>). Lives in the model project so it is unit-testable without any
/// UI: <see cref="List"/> enumerates the named paragraphs in document order, and
/// <see cref="RemoveBookmark"/> clears a bookmark by name. Both operate only on the existing
/// <see cref="Paragraph.BookmarkName"/> field — no model-shape or docx I/O changes.
/// </summary>
public static class Bookmarks
{
    /// <summary>
    /// Lists the document's bookmark targets in document order: every body paragraph whose
    /// <see cref="Paragraph.BookmarkName"/> is a non-empty name, paired with its block index in
    /// <see cref="TextDocument.Blocks"/>. Returns an empty list for a document with no bookmarks (or an
    /// empty document). Deterministic (preserves block order; does not deduplicate).
    /// </summary>
    public static IReadOnlyList<BookmarkLocation> List(TextDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        var locations = new List<BookmarkLocation>();
        var blocks = doc.Blocks;
        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i] is Paragraph paragraph)
            {
                foreach (var name in paragraph.BookmarkNames)
                {
                    if (!string.IsNullOrEmpty(name))
                        locations.Add(new BookmarkLocation(name, i));
                }
            }
        }
        return locations;
    }

    /// <summary>
    /// Clears the bookmark named <paramref name="name"/> from the document: sets
    /// <see cref="Paragraph.BookmarkName"/> to null on every body paragraph carrying that name (an
    /// ordinal match), leaving all other paragraphs untouched. A null/empty name matches nothing. The
    /// paragraph and its text are preserved — only the bookmark marker is removed.
    /// </summary>
    public static void RemoveBookmark(TextDocument doc, string name)
    {
        ArgumentNullException.ThrowIfNull(doc);
        if (string.IsNullOrEmpty(name))
            return;

        foreach (var block in doc.Blocks)
        {
            if (block is Paragraph paragraph)
                paragraph.BookmarkNames.RemoveAll(n => string.Equals(n, name, StringComparison.Ordinal));
        }
    }
}
