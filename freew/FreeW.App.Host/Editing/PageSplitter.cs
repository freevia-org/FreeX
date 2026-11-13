using System.Windows;
using System.Windows.Documents;

using WpfBlock = System.Windows.Documents.Block;
using WpfList = System.Windows.Documents.List;
using WpfParagraph = System.Windows.Documents.Paragraph;
using WpfTable = System.Windows.Documents.Table;

namespace FreeW.App.Host.Editing;

/// <summary>
/// Where a page starts inside a top-level rendered block, independent of which render of the block is
/// looked at: <see cref="Paragraph"/> &gt;= 0 is a paragraph cut (the paragraph's ordinal in the block --
/// 0 for a top-level paragraph, its position among a list's paragraphs otherwise -- and the number of
/// content characters (text characters and embedded objects) before the cut); <see cref="Paragraph"/>
/// &lt; 0 is a table cut before visual row <see cref="Row"/> (rows of every row group, in order,
/// repeated header rows included).
/// </summary>
internal readonly record struct CutLocator(int Paragraph, int Offset, int Row)
{
    internal static CutLocator AtParagraph(int paragraph, int offset) => new(paragraph, offset, -1);

    internal static CutLocator AtRow(int row) => new(-1, 0, row);

    internal bool IsRow => Paragraph < 0;

    public override string ToString() => IsRow ? $"r{Row}" : $"p{Paragraph}+{Offset}";
}

/// <summary>A page (segment-local in a layout pass, global in a run's result) that starts inside rendered block <see cref="Rendered"/>.</summary>
internal readonly record struct RenderedCut(int Page, int Rendered, CutLocator Locator);

/// <summary>
/// The pieces of a top-level block split across pages: the first page shows it up to the first cut,
/// later pages show what lies between consecutive cuts. Each piece is the same render with the content
/// outside it removed structurally -- the ancestors of a cut are kept on both sides (so the piece is a
/// well-formed paragraph, list or table) and nothing else changes. That keeps every piece's symbol
/// offsets a fixed shift of the whole block's (see <see cref="PagePiece"/>), which is what maps a
/// position on a page to the hidden continuous editor and back.
/// </summary>
internal static class PageSplitter
{
    // ── content offsets (text characters and embedded objects; element edges do not count) ──────────

    /// <summary>Content characters between <paramref name="from"/> and <paramref name="to"/>.</summary>
    internal static int ContentOffset(TextPointer from, TextPointer to)
    {
        var count = 0;
        var pointer = from;
        while (pointer is not null && pointer.CompareTo(to) < 0)
        {
            switch (pointer.GetPointerContext(LogicalDirection.Forward))
            {
                case TextPointerContext.Text:
                    var length = Math.Min(pointer.GetTextRunLength(LogicalDirection.Forward), pointer.GetOffsetToPosition(to));
                    count += length;
                    pointer = pointer.GetPositionAtOffset(length, LogicalDirection.Forward);
                    break;
                case TextPointerContext.EmbeddedElement:
                    count++;
                    pointer = pointer.GetPositionAtOffset(1, LogicalDirection.Forward);
                    break;
                case TextPointerContext.None:
                    return count;
                default:
                    pointer = pointer.GetNextContextPosition(LogicalDirection.Forward);
                    break;
            }
        }
        return count;
    }

    /// <summary>
    /// The position <paramref name="offset"/> content characters into <paramref name="element"/>. With
    /// <paramref name="forward"/> the position is pushed past element edges to the start of the next
    /// content (inside the next run); otherwise it stays right after the last counted character (inside
    /// its run). Clamped to the element's content end.
    /// </summary>
    internal static TextPointer PositionAtContentOffset(TextElement element, int offset, bool forward)
    {
        var pointer = element.ContentStart;
        var remaining = Math.Max(0, offset);
        while (pointer.CompareTo(element.ContentEnd) < 0)
        {
            if (remaining == 0 && !forward)
                return pointer;
            var context = pointer.GetPointerContext(LogicalDirection.Forward);
            switch (context)
            {
                case TextPointerContext.Text:
                    var length = pointer.GetTextRunLength(LogicalDirection.Forward);
                    if (remaining < length || (remaining == length && !forward))
                        return pointer.GetPositionAtOffset(remaining, LogicalDirection.Forward)!;
                    remaining -= length;
                    pointer = pointer.GetPositionAtOffset(length, LogicalDirection.Forward)!;
                    break;
                case TextPointerContext.EmbeddedElement:
                    if (remaining == 0)
                        return pointer;
                    remaining--;
                    pointer = pointer.GetPositionAtOffset(1, LogicalDirection.Forward)!;
                    break;
                default:
                    pointer = pointer.GetNextContextPosition(LogicalDirection.Forward) ?? element.ContentEnd;
                    break;
            }
        }
        return element.ContentEnd;
    }

    // ── finding the cut a paginator made ───────────────────────────────────────────────────────────

    /// <summary>
    /// The first position of <paramref name="block"/> the paginator places on <paramref name="page"/> or
    /// later (page numbers never decrease along the text), or null when the block ends before that page.
    /// </summary>
    internal static TextPointer? FirstPositionOnPage(DynamicDocumentPaginator paginator, WpfBlock block, int page)
    {
        int PageOf(TextPointer position)
        {
            try
            {
                return paginator.GetPageNumber(position);
            }
            catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException)
            {
                return -1;
            }
        }

        var high = block.ContentStart.GetOffsetToPosition(block.ContentEnd);
        if (high <= 0 || PageOf(block.ContentEnd) < page)
            return null;
        if (PageOf(block.ContentStart) >= page)
            return block.ContentStart;

        var low = 0;
        while (high - low > 1)
        {
            var middle = (low + high) / 2;
            var probe = block.ContentStart.GetPositionAtOffset(middle, LogicalDirection.Forward);
            if (probe is not null && PageOf(probe) >= page)
                high = middle;
            else
                low = middle;
        }
        return block.ContentStart.GetPositionAtOffset(high, LogicalDirection.Forward);
    }

    /// <summary>
    /// The render-independent locator of a page that starts at <paramref name="position"/> inside
    /// <paramref name="block"/> (of a layout render), or null when the block cannot be cut there (a cut
    /// before its first content, after its last text, or inside a block kind that is not split).
    /// <paramref name="contentBase"/> is added to a top-level paragraph's offset (a paragraph the layout
    /// already split, whose remainder starts that far into the model paragraph).
    /// </summary>
    internal static CutLocator? Locate(WpfBlock block, TextPointer position, int contentBase = 0)
    {
        switch (block)
        {
            case WpfParagraph paragraph:
            {
                var offset = ContentOffset(paragraph.ContentStart, position);
                if (offset <= 0 || !PaginationEngine.HasText(position, paragraph.ContentEnd))
                    return null;
                return CutLocator.AtParagraph(0, contentBase + offset);
            }
            case WpfList list:
            {
                var paragraphs = ListParagraphs(list);
                for (var j = 0; j < paragraphs.Count; j++)
                {
                    var paragraph = paragraphs[j];
                    if (position.CompareTo(paragraph.ContentStart) <= 0)
                        return j == 0 ? null : CutLocator.AtParagraph(j, 0);
                    if (position.CompareTo(paragraph.ContentEnd) <= 0)
                    {
                        var offset = ContentOffset(paragraph.ContentStart, position);
                        if (offset > 0 && PaginationEngine.HasText(position, paragraph.ContentEnd))
                            return CutLocator.AtParagraph(j, offset);
                        if (offset <= 0)
                            return j == 0 ? null : CutLocator.AtParagraph(j, 0);
                        // Only the paragraph mark spills over: the next paragraph starts the page.
                        return j + 1 < paragraphs.Count ? CutLocator.AtParagraph(j + 1, 0) : null;
                    }
                }
                return null;
            }
            case var _ when SingleTable(block) is { } table:
            {
                var rows = TableRows(table);
                for (var v = 0; v < rows.Count; v++)
                {
                    // The row the position lies in (a row the paginator broke moves whole to the page), or
                    // the first row after it.
                    if (position.CompareTo(rows[v].ContentEnd) <= 0)
                        return v == 0 ? null : CutLocator.AtRow(v);
                }
                return null;
            }
            default:
                return null;
        }
    }

    internal static List<WpfParagraph> ListParagraphs(WpfList list)
    {
        var result = new List<WpfParagraph>();
        void Walk(WpfList current)
        {
            foreach (var item in current.ListItems)
            {
                foreach (var child in item.Blocks)
                {
                    if (child is WpfParagraph paragraph)
                        result.Add(paragraph);
                    else if (child is WpfList nested)
                        Walk(nested);
                }
            }
        }
        Walk(list);
        return result;
    }

    /// <summary>The table <paramref name="block"/> is, or the only block of a section that wraps one (a planned table page segment).</summary>
    internal static WpfTable? SingleTable(WpfBlock block) => block switch
    {
        WpfTable table => table,
        Section { Blocks.Count: 1 } section when section.Blocks.FirstBlock is WpfTable wrapped => wrapped,
        _ => null
    };

    internal static List<TableRow> TableRows(WpfTable table) =>
        table.RowGroups.SelectMany(group => group.Rows).ToList();

    /// <summary>
    /// The cut position <paramref name="locator"/> names in <paramref name="block"/> (any render of it).
    /// A paragraph cut resolves inside text: for the side before the cut (<paramref name="forward"/> false)
    /// right after the last character, for the side after it at the start of the next. A cut at a
    /// paragraph's start, or before a row, resolves before the outermost element that starts there, so
    /// neither side keeps an empty item or row group. Null when the block does not have that content.
    /// </summary>
    internal static TextPointer? Resolve(WpfBlock block, CutLocator locator, bool forward)
    {
        TextElement? startingElement = null;
        switch (block)
        {
            case WpfParagraph paragraph when !locator.IsRow && locator.Paragraph == 0:
                return locator.Offset <= 0 ? null : PositionAtContentOffset(paragraph, locator.Offset, forward);
            case WpfList list when !locator.IsRow:
            {
                var paragraphs = ListParagraphs(list);
                if (locator.Paragraph >= paragraphs.Count)
                    return null;
                if (locator.Offset > 0)
                    return PositionAtContentOffset(paragraphs[locator.Paragraph], locator.Offset, forward);
                startingElement = paragraphs[locator.Paragraph];
                break;
            }
            case var _ when locator.IsRow && SingleTable(block) is { } table:
            {
                var rows = TableRows(table);
                if (locator.Row <= 0 || locator.Row >= rows.Count)
                    return null;
                startingElement = rows[locator.Row];
                break;
            }
            default:
                return null;
        }

        // Climb while the element is the first child of its parent, below the top-level block.
        var element = startingElement;
        while (element.Parent is TextElement parent && !ReferenceEquals(parent, block)
            && FirstChild(parent) is { } first && ReferenceEquals(first, element))
        {
            element = parent;
        }
        return element.ElementStart;
    }

    private static TextElement? FirstChild(TextElement parent) => parent switch
    {
        ListItem item => item.Blocks.FirstBlock,
        WpfList list => list.ListItems.FirstListItem,
        TableRowGroup group => group.Rows.Count > 0 ? group.Rows[0] : null,
        WpfTable table => table.RowGroups.Count > 0 ? table.RowGroups[0] : null,
        Section section => section.Blocks.FirstBlock,
        TableCell cell => cell.Blocks.FirstBlock,
        Span span => span.Inlines.FirstInline,
        WpfParagraph paragraph => paragraph.Inlines.FirstInline,
        _ => null
    };

    // ── structural trimming ──────────────────────────────────────────────────────────────────────────

    /// <summary>Open elements between <paramref name="position"/> and <paramref name="top"/> (exclusive).</summary>
    internal static int Depth(WpfBlock top, TextPointer position)
    {
        var depth = 0;
        var node = position.Parent;
        while (node is TextElement element && !ReferenceEquals(element, top))
        {
            depth++;
            node = element.Parent;
        }
        return depth;
    }

    /// <summary>Symbol offset of <paramref name="position"/> from the start of <paramref name="top"/>'s content.</summary>
    internal static int SymbolOffset(WpfBlock top, TextPointer position) => top.ContentStart.GetOffsetToPosition(position);

    /// <summary>Symbol length of <paramref name="top"/>'s content.</summary>
    internal static int SymbolLength(WpfBlock top) => top.ContentStart.GetOffsetToPosition(top.ContentEnd);

    /// <summary>Removes everything in <paramref name="top"/> after <paramref name="position"/>, keeping its ancestors.</summary>
    internal static void TrimAfter(WpfBlock top, TextPointer position) => Trim(top, position, keepBefore: true);

    /// <summary>Removes everything in <paramref name="top"/> before <paramref name="position"/>, keeping its ancestors.</summary>
    internal static void TrimBefore(WpfBlock top, TextPointer position) => Trim(top, position, keepBefore: false);

    private static void Trim(WpfBlock top, TextPointer position, bool keepBefore)
    {
        var container = position.Parent as TextElement;
        TextPointer boundary;
        if (container is System.Windows.Documents.Run run)
        {
            var offsetInRun = Math.Clamp(run.ContentStart.GetOffsetToPosition(position), 0, run.Text.Length);
            run.Text = keepBefore ? run.Text[..offsetInRun] : run.Text[offsetInRun..];
            boundary = keepBefore ? run.ElementEnd : run.ElementStart;
            container = run.Parent as TextElement;
        }
        else
        {
            boundary = position;
        }

        while (container is not null)
        {
            foreach (var child in Children(container).ToList())
            {
                var remove = keepBefore
                    ? child.ElementStart.CompareTo(boundary) >= 0
                    : child.ElementEnd.CompareTo(boundary) <= 0;
                if (remove)
                    RemoveChild(container, child);
            }
            if (ReferenceEquals(container, top))
                break;
            boundary = keepBefore ? container.ElementEnd : container.ElementStart;
            container = container.Parent as TextElement;
        }
    }

    private static IEnumerable<TextElement> Children(TextElement container) => container switch
    {
        WpfParagraph paragraph => paragraph.Inlines,
        Span span => span.Inlines,
        WpfList list => list.ListItems,
        ListItem item => item.Blocks,
        Section section => section.Blocks,
        WpfTable table => table.RowGroups,
        TableRowGroup group => group.Rows,
        TableRow row => row.Cells,
        TableCell cell => cell.Blocks,
        AnchoredBlock anchored => anchored.Blocks,
        _ => []
    };

    private static void RemoveChild(TextElement container, TextElement child)
    {
        switch (container)
        {
            case WpfParagraph paragraph when child is Inline inline:
                paragraph.Inlines.Remove(inline);
                break;
            case Span span when child is Inline inline:
                span.Inlines.Remove(inline);
                break;
            case WpfList list when child is ListItem item:
                list.ListItems.Remove(item);
                break;
            case ListItem item when child is WpfBlock block:
                item.Blocks.Remove(block);
                break;
            case Section section when child is WpfBlock block:
                section.Blocks.Remove(block);
                break;
            case WpfTable table when child is TableRowGroup group:
                table.RowGroups.Remove(group);
                break;
            case TableRowGroup group when child is TableRow row:
                group.Rows.Remove(row);
                break;
            case TableRow row when child is TableCell cell:
                row.Cells.Remove(cell);
                break;
            case TableCell cell when child is WpfBlock block:
                cell.Blocks.Remove(block);
                break;
            case AnchoredBlock anchored when child is WpfBlock block:
                anchored.Blocks.Remove(block);
                break;
        }
    }
}

/// <summary>
/// A top-level block on a page that is one piece of a block split across pages (see
/// <see cref="PageSplitter"/>). Its content is the whole block's symbols from <c>W</c> on, after
/// <see cref="Open"/> re-opened ancestor elements, and before <see cref="Close"/> closing ones; <c>W</c>
/// of a piece is the previous piece's <c>W</c> plus that piece's span plus <see cref="Gap"/> (element
/// edges between the two cut positions, which the two sides resolve on either side of).
/// </summary>
internal sealed class PagePiece
{
    internal required WpfBlock Block { get; init; }

    internal int Open { get; init; }

    internal int Close { get; set; }

    internal int Gap { get; init; }

    /// <summary>Continues the block that ends the page (or piece) before it.</summary>
    internal bool ContinuesPrevious { get; init; }

    /// <summary>Continues on the next page (or piece).</summary>
    internal bool ContinuesNext { get; set; }

    /// <summary>A continuation of the previous piece's text (the caret crosses the seam within a line).</summary>
    internal bool IsTextSeam { get; init; }

    /// <summary>The symbols of the whole block this piece shows.</summary>
    internal int Span => Math.Max(0, PageSplitter.SymbolLength(Block) - Open - Close);
}
