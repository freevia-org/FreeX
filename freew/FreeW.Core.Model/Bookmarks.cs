namespace FreeW.Core.Model;

/// <summary>
/// One bookmark target in a document. Body bookmarks use their position in
/// <see cref="TextDocument.Blocks"/>; table-cell bookmarks additionally carry an exact logical cell and
/// paragraph address. Pure data, produced by <see cref="Bookmarks"/>, so native hosts do not need to
/// rescan the model to resolve a target.
/// </summary>
/// <param name="Name">The bookmark name (the paragraph's <see cref="Paragraph.BookmarkName"/>).</param>
/// <param name="BlockIndex">Index of the marked paragraph or containing table in <see cref="TextDocument.Blocks"/>.</param>
/// <param name="TableRowIndex">Logical table row for a cell bookmark, or null for a body paragraph.</param>
/// <param name="TableGridColumnIndex">Logical grid column for a cell bookmark, or null for a body paragraph.</param>
/// <param name="TableParagraphIndex">Paragraph index within the cell, or null for a body paragraph.</param>
/// <param name="Offset">Text offset within the target paragraph. Paragraph bookmarks currently start at zero.</param>
public readonly record struct BookmarkLocation(
    string Name,
    int BlockIndex,
    int? TableRowIndex = null,
    int? TableGridColumnIndex = null,
    int? TableParagraphIndex = null,
    int Offset = 0)
{
    public bool IsTableLocation =>
        TableRowIndex.HasValue &&
        TableGridColumnIndex.HasValue &&
        TableParagraphIndex.HasValue;
}

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
    /// containing top-level <see cref="Table"/> block, while logical row/grid-column/cell-paragraph
    /// coordinates provide the exact native-caret target. Returns an empty list for a document with no
    /// bookmarks (or an empty document). Deterministic (preserves block order; does not deduplicate).
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
                    for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
                    {
                        foreach (var projectedCell in TableGridProjection.ProjectRow(table.Rows[rowIndex]))
                        {
                            for (var paragraphIndex = 0;
                                 paragraphIndex < projectedCell.Cell.Paragraphs.Count;
                                 paragraphIndex++)
                            {
                                AddLocations(
                                    projectedCell.Cell.Paragraphs[paragraphIndex],
                                    i,
                                    locations,
                                    rowIndex,
                                    projectedCell.StartColumn,
                                    paragraphIndex);
                            }
                        }
                    }

                    break;
            }
        }
        return locations;
    }

    /// <summary>
    /// Resolves <paramref name="name"/> to the first exact bookmark target in document order. The returned
    /// location is suitable for either renderer: body bookmarks carry a block index, while table-cell
    /// bookmarks also carry their logical row, grid column, and paragraph. Matching is ordinal; a null or
    /// empty name resolves to no target.
    /// </summary>
    public static BookmarkLocation? FindLocation(TextDocument doc, string? name)
    {
        ArgumentNullException.ThrowIfNull(doc);
        if (string.IsNullOrEmpty(name))
            return null;

        foreach (var location in List(doc))
        {
            if (string.Equals(location.Name, name, StringComparison.Ordinal))
                return location;
        }

        return null;
    }

    private static void AddLocations(
        Paragraph paragraph,
        int blockIndex,
        List<BookmarkLocation> locations,
        int? tableRowIndex = null,
        int? tableGridColumnIndex = null,
        int? tableParagraphIndex = null)
    {
        foreach (var name in paragraph.BookmarkNames)
        {
            if (!string.IsNullOrEmpty(name))
            {
                locations.Add(new BookmarkLocation(
                    name,
                    blockIndex,
                    tableRowIndex,
                    tableGridColumnIndex,
                    tableParagraphIndex));
            }
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

    /// <summary>
    /// Resolves a <see cref="BookmarkLocation"/> (as produced by <see cref="List"/>) back to its exact
    /// target paragraph: a body paragraph directly for a non-table location, or — for a table-cell
    /// location — the specific cell paragraph identified by logical row, grid column, and paragraph index
    /// (walking the same <see cref="TableGridProjection"/> shape as <see cref="List"/>). Returns null when
    /// the location no longer resolves (stale index, row/column out of range, etc.).
    /// </summary>
    public static Paragraph? ResolveLocation(TextDocument doc, BookmarkLocation location)
    {
        ArgumentNullException.ThrowIfNull(doc);
        if (location.BlockIndex < 0 || location.BlockIndex >= doc.Blocks.Count)
            return null;

        var block = doc.Blocks[location.BlockIndex];
        if (!location.IsTableLocation)
            return block as Paragraph;

        if (block is not Table table
            || location.TableRowIndex is not { } rowIndex
            || rowIndex < 0 || rowIndex >= table.Rows.Count)
        {
            return null;
        }

        foreach (var projectedCell in TableGridProjection.ProjectRow(table.Rows[rowIndex]))
        {
            if (projectedCell.StartColumn != location.TableGridColumnIndex)
                continue;

            var paragraphIndex = location.TableParagraphIndex!.Value;
            return paragraphIndex >= 0 && paragraphIndex < projectedCell.Cell.Paragraphs.Count
                ? projectedCell.Cell.Paragraphs[paragraphIndex]
                : null;
        }

        return null;
    }

    /// <summary>
    /// Removes exactly the bookmark <em>instance</em> at <paramref name="location"/> — the one specific
    /// paragraph it targets — leaving any other paragraph that happens to carry the same name (a document
    /// can have duplicate bookmark names, e.g. imported from a source that didn't enforce uniqueness)
    /// untouched. Contrast with <see cref="RemoveBookmark"/>, which clears every paragraph sharing that
    /// name document-wide. Used by the Bookmark Manager's Delete action so removing one duplicate-named
    /// entry never silently destroys a different one. A null/empty name, or a location that no longer
    /// resolves, is a no-op.
    /// </summary>
    public static void RemoveBookmarkAt(TextDocument doc, BookmarkLocation location)
    {
        ArgumentNullException.ThrowIfNull(doc);
        if (string.IsNullOrEmpty(location.Name))
            return;

        if (ResolveLocation(doc, location) is { } paragraph)
            RemoveBookmarkName(paragraph, location.Name);
    }
}
