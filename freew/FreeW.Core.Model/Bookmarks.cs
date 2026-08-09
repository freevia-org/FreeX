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
/// UI: <see cref="List"/> enumerates the named paragraphs in document order, <see cref="FindParagraph"/>
/// resolves a bookmark name directly to its target paragraph, and <see cref="RemoveBookmark"/> clears a
/// bookmark by name. All three walk the same shape — top-level body paragraphs plus paragraphs nested in
/// table cells (nested tables are not walked: the reader does not yet preserve a table inside a cell) —
/// and operate only on the existing <see cref="Paragraph.BookmarkName"/> field, no model-shape or docx I/O
/// changes.
/// </summary>
public static class Bookmarks
{
    /// <summary>
    /// Lists the document's bookmark targets in document order: every body paragraph whose
    /// <see cref="Paragraph.BookmarkName"/> is a non-empty name, paired with its block index in
    /// <see cref="TextDocument.Blocks"/>. Descends into table cells (and their rows), so a bookmark placed
    /// inside a table is found too; its <see cref="BookmarkLocation.BlockIndex"/> is then the index of the
    /// containing top-level <see cref="Table"/> block (the same convention <c>ComplexFieldEngine</c> uses
    /// for body-paragraph walks), since a cell-nested paragraph has no standalone index into
    /// <see cref="TextDocument.Blocks"/> — callers that need the exact paragraph should use
    /// <see cref="FindParagraph"/> instead. Returns an empty list for a document with no bookmarks (or an
    /// empty document). Deterministic (preserves block order; does not deduplicate).
    /// </summary>
    public static IReadOnlyList<BookmarkLocation> List(TextDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        var locations = new List<BookmarkLocation>();
        var blocks = doc.Blocks;
        for (var i = 0; i < blocks.Count; i++)
        {
            switch (blocks[i])
            {
                case Paragraph paragraph:
                    AddLocations(paragraph, i, locations);
                    break;
                case Table table:
                    foreach (var row in table.Rows)
                        foreach (var cell in row.Cells)
                            foreach (var cellParagraph in cell.Paragraphs)
                                AddLocations(cellParagraph, i, locations);
                    break;
            }
        }
        return locations;
    }

    private static void AddLocations(Paragraph paragraph, int blockIndex, List<BookmarkLocation> locations)
    {
        foreach (var name in paragraph.BookmarkNames)
        {
            if (!string.IsNullOrEmpty(name))
                locations.Add(new BookmarkLocation(name, blockIndex));
        }
    }

    /// <summary>
    /// Finds the body paragraph carrying the bookmark named <paramref name="name"/> — the actual target
    /// paragraph, not just its containing block index. Needed because a bookmark nested in a table cell has
    /// no standalone <see cref="TextDocument.Blocks"/> index (<see cref="List"/> reports the containing
    /// table for those). Walks the same shape as <see cref="List"/> (top-level paragraphs, then each table's
    /// cells in row order) and returns the first match in document order, or null when no paragraph carries
    /// that bookmark (including a null/empty <paramref name="name"/>).
    /// </summary>
    public static Paragraph? FindParagraph(TextDocument doc, string name)
    {
        ArgumentNullException.ThrowIfNull(doc);
        if (string.IsNullOrEmpty(name))
            return null;

        foreach (var block in doc.Blocks)
        {
            switch (block)
            {
                case Paragraph paragraph when HasBookmark(paragraph, name):
                    return paragraph;
                case Table table:
                    foreach (var row in table.Rows)
                        foreach (var cell in row.Cells)
                            foreach (var cellParagraph in cell.Paragraphs)
                                if (HasBookmark(cellParagraph, name))
                                    return cellParagraph;
                    break;
            }
        }
        return null;
    }

    private static bool HasBookmark(Paragraph paragraph, string name) =>
        paragraph.BookmarkNames.Contains(name, StringComparer.Ordinal);

    /// <summary>
    /// Clears the bookmark named <paramref name="name"/> from the document: sets
    /// <see cref="Paragraph.BookmarkName"/> to null on every body paragraph carrying that name (an
    /// ordinal match) — top-level or nested in a table cell — leaving all other paragraphs untouched. A
    /// null/empty name matches nothing. The paragraph and its text are preserved — only the bookmark
    /// marker is removed.
    /// </summary>
    public static void RemoveBookmark(TextDocument doc, string name)
    {
        ArgumentNullException.ThrowIfNull(doc);
        if (string.IsNullOrEmpty(name))
            return;

        foreach (var block in doc.Blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    RemoveBookmarkName(paragraph, name);
                    break;
                case Table table:
                    foreach (var row in table.Rows)
                        foreach (var cell in row.Cells)
                            foreach (var cellParagraph in cell.Paragraphs)
                                RemoveBookmarkName(cellParagraph, name);
                    break;
            }
        }
    }

    private static void RemoveBookmarkName(Paragraph paragraph, string name) =>
        paragraph.BookmarkNames.RemoveAll(n => string.Equals(n, name, StringComparison.Ordinal));
}
