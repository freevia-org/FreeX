using System.Globalization;

namespace FreeW.Core.Model;

/// <summary>
/// The single canonical walk from a bookmark name to the page it should be reported on, shared by every
/// FreeW feature that needs one: PAGEREF fields (<see cref="ComplexFieldEngine"/>), "As Page Number"
/// cross-references (<see cref="CrossReferences"/>), and INDEX <c>\r</c> entries
/// (<see cref="DocumentIndex"/>). Those three used to each hand-write their own bookmark search and
/// table-row page-offset math, and the copies drifted: only two of the three were ever taught that every
/// row of a table shares one <see cref="TextDocument.Blocks"/> entry, so a bookmark on a table's later row
/// resolved to the same page as its first row wherever the third copy (<see cref="DocumentIndex"/>) was
/// still in use -- an INDEX entry for a bookmark on row 35 reported the table's own starting page. This
/// type is now the only place that search and that row-offset math live.
/// </summary>
public static class BookmarkPageResolution
{
    /// <summary>
    /// Where a bookmark resolves to. <see cref="TableRowIndex"/> is the logical row inside the table at
    /// <see cref="BlockIndex"/> when the bookmark sits on a table row (directly, or in a nested table
    /// inside one of that row's cells); null for a body paragraph outside any table. <see cref="BlockIndex"/>
    /// is -1 for every story that has no <see cref="TextDocument.Blocks"/> address of its own (headers,
    /// footers, footnotes, endnotes, comments) -- only <see cref="DocumentFieldStoryKind.MainDocument"/>
    /// and <see cref="DocumentFieldStoryKind.TextBox"/> (which inherits its anchor paragraph's block index)
    /// carry a usable one.
    /// </summary>
    public readonly record struct Target(
        Paragraph Paragraph,
        int BlockIndex,
        int? TableRowIndex,
        DocumentFieldStoryKind StoryKind);

    /// <summary>
    /// Finds the first paragraph, in story order, carrying <paramref name="name"/> as a bookmark
    /// (<see cref="Paragraph.BookmarkNames"/>). Walks the main document first -- row-aware, via
    /// <see cref="DocumentBodyParagraphs"/>, which (unlike <see cref="DocumentFieldStories"/>) keeps each
    /// table paragraph's logical row -- then every other story <see cref="DocumentFieldStories"/> models:
    /// headers/footers, footnotes, endnotes, text boxes, and comments. Returns null when no paragraph
    /// anywhere carries that bookmark, or when <paramref name="name"/> is null/empty.
    /// </summary>
    public static Target? Find(TextDocument document, string? name)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrEmpty(name))
            return null;

        foreach (var location in DocumentBodyParagraphs.Enumerate(document))
        {
            if (location.Paragraph.BookmarkNames.Contains(name, StringComparer.Ordinal))
            {
                return new Target(
                    location.Paragraph,
                    location.BlockIndex,
                    location.TableParagraph?.RowIndex,
                    DocumentFieldStoryKind.MainDocument);
            }
        }

        foreach (var story in DocumentFieldStories.Enumerate(document))
        {
            if (story.StoryKind == DocumentFieldStoryKind.MainDocument)
                continue; // already covered, row-aware, above -- do not report it a second time
            if (story.Paragraph.BookmarkNames.Contains(name, StringComparer.Ordinal))
                return new Target(story.Paragraph, story.BodyBlockIndex, null, story.StoryKind);
        }

        return null;
    }

    /// <summary>
    /// Counts authored page breaks (manual breaks and page-break-before formatting) between the start of
    /// <paramref name="table"/> and <paramref name="rowIndex"/>, inclusive of a page-break-before that
    /// starts <paramref name="rowIndex"/> itself. A best-effort correction, not full pagination: it catches
    /// an explicit break the author placed inside the table, not a row that lands on a later page purely
    /// from natural row-height overflow (the model has no layout engine, so that page is unknowable here).
    /// Row 0 always returns zero: a page-break-before authored on row 0 is the same break that puts the
    /// table's own block on that page, which the host's <c>pageOf</c>/<c>pageReferenceOf</c> answer for the
    /// table's block index already reflects -- counting it again here would double count. Because row 0's
    /// own break is excluded from row 0's answer, it must also be excluded from the running total used for
    /// every later row; the two hand-written copies this replaces got that second half wrong, so a break
    /// authored on row 0 still leaked into every row after it even though row 0 itself never saw it.
    /// </summary>
    public static int PageBreaksBeforeTableRow(Table table, int rowIndex)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (rowIndex <= 0 || rowIndex >= table.Rows.Count)
            return 0;

        var breaks = 0;
        for (var row = 1; row <= rowIndex; row++)
        {
            foreach (var paragraph in ParagraphsInTableRow(table.Rows[row]))
            {
                if (paragraph.Formatting.PageBreakBefore)
                    breaks++;
                if (row < rowIndex)
                    breaks += paragraph.Runs.Count(run => run.IsPageBreak);
            }
        }

        return breaks;
    }

    /// <summary>Every paragraph directly in a table row's cells, plus (recursively) each cell's nested
    /// tables.</summary>
    private static IEnumerable<Paragraph> ParagraphsInTableRow(TableRow row)
    {
        foreach (var cell in row.Cells)
        {
            foreach (var cellParagraph in cell.Paragraphs)
                yield return cellParagraph;
            foreach (var nestedTable in cell.NestedTables)
                foreach (var nestedRow in nestedTable.Rows)
                    foreach (var nestedParagraph in ParagraphsInTableRow(nestedRow))
                        yield return nestedParagraph;
        }
    }

    /// <summary>
    /// Resolves a found <paramref name="target"/> to the page-number text a PAGEREF field or an "As Page
    /// Number" cross-reference should display: the host's own <paramref name="pageTextOf"/> label when it
    /// supplies one (row offset never applies to an explicit host label, matching the existing convention
    /// for that override), else the host's numeric <paramref name="pageOf"/> page for the target's block,
    /// adjusted by <see cref="PageBreaksBeforeTableRow"/> when the target sits on a table row, else "1"
    /// when no numeric page is known at all (the pure model has no pagination). Callers are expected to
    /// have already excluded targets with no block address of their own (<see cref="Target.BlockIndex"/>
    /// less than zero -- headers/footers/footnotes/endnotes/comments) and fall back to their own cached
    /// text for those, exactly as they do for a bookmark that resolves to no target at all.
    /// </summary>
    public static string ResolvePageText(
        TextDocument document,
        Target target,
        Func<int, int?>? pageOf,
        Func<int, string?>? pageTextOf)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (pageTextOf?.Invoke(target.BlockIndex) is { Length: > 0 } pageText)
            return pageText;

        var page = pageOf?.Invoke(target.BlockIndex);
        if (page is null)
            return "1";

        var rowOffset = target.TableRowIndex is { } rowIndex
            && target.BlockIndex >= 0 && target.BlockIndex < document.Blocks.Count
            && document.Blocks[target.BlockIndex] is Table table
                ? PageBreaksBeforeTableRow(table, rowIndex)
                : 0;

        return Math.Max(1, page.Value + rowOffset).ToString(CultureInfo.InvariantCulture);
    }
}
