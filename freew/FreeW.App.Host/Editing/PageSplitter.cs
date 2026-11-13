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
/// repeated header rows included). A row cut with <see cref="Cells"/> lies inside row <see cref="Row"/>:
/// the paginator split that row (taller than the space left) and each cell continues at its own cut.
/// </summary>
internal readonly record struct CutLocator(int Paragraph, int Offset, int Row, CellCuts? Cells = null)
{
    internal static CutLocator AtParagraph(int paragraph, int offset) => new(paragraph, offset, -1);

    internal static CutLocator AtRow(int row) => new(-1, 0, row);

    internal static CutLocator InsideRow(int row, CellCuts cells) => new(-1, 0, row, cells);

    internal bool IsRow => Paragraph < 0;

    /// <summary>A cut inside a row (per-cell cuts), not before it.</summary>
    internal bool IsInsideRow => Cells is not null;

    public override string ToString() => IsRow ? (Cells is { } cells ? $"r{Row}[{cells}]" : $"r{Row}") : $"p{Paragraph}+{Offset}";
}

/// <summary>
/// Where each cell of a row split mid-row continues: per cell (in row order) the cell paragraph's ordinal
/// and the content characters before the cut in it (as <see cref="CutLocator.AtParagraph"/> within the
/// cell), or <see cref="End"/> when none of the cell's content is on the later page.
/// </summary>
internal sealed class CellCuts : IEquatable<CellCuts>
{
    internal const int End = int.MaxValue;

    internal CellCuts(IReadOnlyList<(int Paragraph, int Offset)> cells) => Cells = cells;

    internal IReadOnlyList<(int Paragraph, int Offset)> Cells { get; }

    internal static bool IsEnd((int Paragraph, int Offset) cut) => cut.Paragraph == End;

    internal static bool IsStart((int Paragraph, int Offset) cut) => cut is (0, 0);

    public bool Equals(CellCuts? other) => other is not null && Cells.SequenceEqual(other.Cells);

    public override bool Equals(object? obj) => Equals(obj as CellCuts);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var cell in Cells)
            hash.Add(cell);
        return hash.ToHashCode();
    }

    internal int CompareTo(CellCuts other)
    {
        for (var i = 0; i < Math.Min(Cells.Count, other.Cells.Count); i++)
        {
            var compare = Cells[i].Paragraph != other.Cells[i].Paragraph
                ? Cells[i].Paragraph.CompareTo(other.Cells[i].Paragraph)
                : Cells[i].Offset.CompareTo(other.Cells[i].Offset);
            if (compare != 0)
                return compare;
        }
        return Cells.Count.CompareTo(other.Cells.Count);
    }

    public override string ToString() =>
        string.Join(",", Cells.Select(cell => IsEnd(cell) ? "e" : $"{cell.Paragraph}+{cell.Offset}"));
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
                    // A print-like layout render joins "a/b" with a display-only word joiner the model (and
                    // the editable pages) do not have: it is not content.
                    var text = pointer.GetTextInRun(LogicalDirection.Forward);
                    for (var i = 0; i < length && i < text.Length; i++)
                    {
                        if (text[i] == FreeW.App.Presentation.DocumentView.SlashLineBreakDisplayPlanner.WordJoiner)
                            count--;
                    }
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

    /// <summary>
    /// A paragraph cut <paramref name="offset"/> content characters in. The side before the cut keeps the
    /// empty runs that sit right at it (a manual page/column-break mark, which ends the text before the
    /// page it starts, or another zero-width marker): otherwise neither side would hold them and a commit
    /// of the pages would drop them.
    /// </summary>
    private static TextPointer CutPosition(TextElement paragraph, int offset, bool forward)
    {
        var position = PositionAtContentOffset(paragraph, offset, forward);
        if (forward)
            return position;

        var pointer = position;
        var result = position;
        while (pointer.CompareTo(paragraph.ContentEnd) < 0)
        {
            var context = pointer.GetPointerContext(LogicalDirection.Forward);
            if (context == TextPointerContext.ElementEnd)
            {
                pointer = pointer.GetNextContextPosition(LogicalDirection.Forward)!;
                continue;
            }
            if (context == TextPointerContext.ElementStart
                && pointer.GetAdjacentElement(LogicalDirection.Forward) is System.Windows.Documents.Run { Text.Length: 0 } empty)
            {
                pointer = empty.ElementEnd;
                result = pointer;
                continue;
            }
            break;
        }
        return result;
    }

    // ── finding the cut a paginator made ───────────────────────────────────────────────────────────

    /// <summary>
    /// The first position of <paramref name="block"/> the paginator places on <paramref name="page"/> or
    /// later (page numbers never decrease along the text), or null when the block ends before that page.
    /// </summary>
    internal static TextPointer? FirstPositionOnPage(DynamicDocumentPaginator paginator, TextElement block, int page)
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

        // Fast path: where the paginator says the page starts, when that is inside the block and the
        // position before it is on an earlier page (it probes the page's top-left corner, which is not
        // the first position for right-to-left text or a line opening with a list marker).
        try
        {
            if (paginator.GetPagePosition(paginator.GetPage(page)) is TextPointer start
                && start.CompareTo(block.ContentStart) > 0
                && start.CompareTo(block.ContentEnd) < 0
                && PageOf(start) == page
                && start.GetPositionAtOffset(-1, LogicalDirection.Backward) is { } before
                && PageOf(before) < page)
            {
                return start;
            }
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException)
        {
            // Fall back to the search.
        }

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
            case WpfParagraph or WpfList:
            case Section when SingleTable(block) is null:
            {
                // The layout paragraphs of the block, each with the model paragraph it renders (its ordinal
                // in the block) and where in that paragraph it starts: a pagination render splits a paragraph
                // at inline page/column breaks into fragments that continue it.
                var paragraphs = LayoutParagraphs(block);
                if (paragraphs.Count == 0)
                    return null;
                var bases = new int[paragraphs.Count];
                for (var j = 0; j < paragraphs.Count; j++)
                {
                    bases[j] = paragraphs[j].Continues && j > 0
                        ? bases[j - 1] + ContentOffset(paragraphs[j - 1].Paragraph.ContentStart, paragraphs[j - 1].Paragraph.ContentEnd)
                        : 0;
                }
                if (block is WpfParagraph)
                    bases[0] = contentBase;

                // The cut of a page that starts at layout paragraph j's start.
                CutLocator? StartOf(int j)
                {
                    if (j >= paragraphs.Count)
                        return null;
                    if (!paragraphs[j].Continues)
                        return paragraphs[j].Ordinal == 0 ? null : CutLocator.AtParagraph(paragraphs[j].Ordinal, 0);
                    // A fragment continuing a paragraph: a cut inside it, when text of that paragraph follows.
                    // After only empty fragments (a paragraph opening with a manual break) the cut is at the
                    // paragraph's start: all of its text is on this page (AtParagraph(ordinal, 0)).
                    var last = j;
                    while (last + 1 < paragraphs.Count && paragraphs[last + 1].Continues)
                        last++;
                    // Only empty fragments left (a paragraph ending with a manual break): the page really
                    // starts with the next paragraph of the block, if any.
                    return PaginationEngine.HasText(paragraphs[j].Paragraph.ContentStart, paragraphs[last].Paragraph.ContentEnd)
                        ? CutLocator.AtParagraph(paragraphs[j].Ordinal, bases[j])
                        : StartOf(last + 1);
                }

                for (var j = 0; j < paragraphs.Count; j++)
                {
                    var paragraph = paragraphs[j].Paragraph;
                    if (position.CompareTo(paragraph.ContentStart) <= 0)
                        return StartOf(j);
                    if (position.CompareTo(paragraph.ContentEnd) <= 0)
                    {
                        var offset = ContentOffset(paragraph.ContentStart, position);
                        if (offset > 0 && PaginationEngine.HasText(position, paragraph.ContentEnd))
                            return CutLocator.AtParagraph(paragraphs[j].Ordinal, bases[j] + offset);
                        if (offset <= 0)
                            return StartOf(j);
                        // Only the paragraph mark spills over: the next paragraph starts the page.
                        return StartOf(j + 1);
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

    /// <summary>
    /// The paragraphs of a top-level paragraph, list or inline-break fragment group, each with its model
    /// paragraph's ordinal in the block and whether it continues the paragraph before it (an inline-break
    /// fragment of a pagination render).
    /// </summary>
    private static List<(WpfParagraph Paragraph, int Ordinal, bool Continues)> LayoutParagraphs(WpfBlock block)
    {
        var flat = new List<WpfParagraph>();
        switch (block)
        {
            case WpfParagraph paragraph:
                flat.Add(paragraph);
                break;
            case WpfList list:
                flat.AddRange(ListParagraphs(list));
                break;
            case Section section:
                foreach (var child in section.Blocks)
                {
                    if (child is WpfParagraph paragraph)
                        flat.Add(paragraph);
                    else
                        return [];
                }
                break;
        }

        var result = new List<(WpfParagraph, int, bool)>(flat.Count);
        var ordinal = -1;
        foreach (var paragraph in flat)
        {
            var continues = ordinal >= 0 && DocumentView.IsInlineBreakContinuationFragment(paragraph);
            if (!continues)
                ordinal++;
            result.Add((paragraph, ordinal, continues));
        }
        return result;
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

    // ── a row taller than the space left: split inside it, cell by cell ──────────────────────────────

    /// <summary>
    /// The cut of a page that starts inside a row of table <paramref name="block"/> (any render of it laid
    /// out by <paramref name="paginator"/>): the last row that starts before <paramref name="page"/> when
    /// the paginator continues some of its content on that page, with each cell's first position there.
    /// Null when no row straddles the page start (the table splits between rows, if at all) or the row
    /// cannot be cut cell by cell (a merged cell, or a cell holding anything but paragraphs).
    /// </summary>
    internal static CutLocator? LocateInsideRow(DynamicDocumentPaginator paginator, WpfBlock block, int page)
    {
        if (SingleTable(block) is not { } table)
            return null;
        var rows = TableRows(table);
        if (rows.Count == 0)
            return null;

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

        // Row starts never move back a page: the last row starting before the page.
        int low = -1, high = rows.Count;
        while (high - low > 1)
        {
            var middle = (low + high) / 2;
            var start = FirstContent(rows[middle]) ?? rows[middle].ContentStart;
            var startPage = PageOf(start);
            if (startPage >= 0 && startPage < page)
                low = middle;
            else
                high = middle;
        }
        if (low < 0)
            return null;
        var v = low;
        var row = rows[v];
        if (RowEndPage(paginator, row) < page || !CanSplitRow(rows, v))
            return null;

        var cuts = new List<(int Paragraph, int Offset)>(row.Cells.Count);
        bool anyBefore = false, anyAfter = false;
        foreach (var cell in row.Cells)
        {
            var blocks = cell.Blocks.ToList();
            var position = blocks.Count == 0 ? null : FirstPositionOnPage(paginator, cell, page);
            var cut = position is null ? (CellCuts.End, 0) : LocateInCell(blocks, position, p => PageOf(p) < page);
            cuts.Add(cut);
            if (!CellCuts.IsStart(cut) && blocks.Count > 0)
                anyBefore = true;
            if (!CellCuts.IsEnd(cut))
                anyAfter = true;
        }
        return anyBefore && anyAfter ? CutLocator.InsideRow(v, new CellCuts(cuts)) : null;
    }

    /// <summary>
    /// The last page <paramref name="block"/> reaches. A table's end position is on the page its last
    /// cell ends on, but a row the paginator split runs on as far as its longest cell.
    /// </summary>
    internal static int EndPage(DynamicDocumentPaginator paginator, WpfBlock block)
    {
        var page = SafePageOf(paginator, block.ContentEnd);
        if (SingleTable(block) is { } table && TableRows(table) is { Count: > 0 } rows)
            page = Math.Max(page, RowEndPage(paginator, rows[^1]));
        return page;
    }

    private static int RowEndPage(DynamicDocumentPaginator paginator, TableRow row)
    {
        var page = SafePageOf(paginator, row.ContentEnd);
        foreach (var cell in row.Cells)
        {
            if (LastContent(cell) is { } last)
                page = Math.Max(page, SafePageOf(paginator, last));
        }
        return page;
    }

    private static int SafePageOf(DynamicDocumentPaginator paginator, TextPointer position)
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

    /// <summary>
    /// The cut a page starting at <paramref name="position"/> makes in a cell of <paramref name="blocks"/>:
    /// inside a paragraph (its block ordinal and content offset), or before a block. A block that is not a
    /// paragraph (a picture's container) is never cut: it is on the later page unless
    /// <paramref name="startsEarlier"/> says its start is on an earlier one.
    /// </summary>
    private static (int Paragraph, int Offset) LocateInCell(
        List<WpfBlock> blocks,
        TextPointer position,
        Func<TextPointer, bool> startsEarlier)
    {
        for (var j = 0; j < blocks.Count; j++)
        {
            var block = blocks[j];
            if (position.CompareTo(block.ContentStart) <= 0)
                return (j, 0);
            if (position.CompareTo(block.ContentEnd) > 0)
                continue;
            if (block is not WpfParagraph paragraph)
            {
                return !startsEarlier(block.ContentStart) ? (j, 0)
                    : j + 1 < blocks.Count ? (j + 1, 0) : (CellCuts.End, 0);
            }
            var offset = ContentOffset(paragraph.ContentStart, position);
            if (offset > 0 && PaginationEngine.HasText(position, paragraph.ContentEnd))
                return (j, offset);
            if (offset <= 0)
                return (j, 0);
            // Only the paragraph mark spills over: the next block starts the page.
            return j + 1 < blocks.Count ? (j + 1, 0) : (CellCuts.End, 0);
        }
        return (CellCuts.End, 0);
    }

    /// <summary>Row <paramref name="v"/> holds only unmerged cells of paragraphs, and no cell above spans into it.</summary>
    private static bool CanSplitRow(List<TableRow> rows, int v)
    {
        for (var r = 0; r < v; r++)
        {
            foreach (var cell in rows[r].Cells)
            {
                if (cell.RowSpan > v - r)
                    return false;
            }
        }
        foreach (var cell in rows[v].Cells)
        {
            if (cell.RowSpan != 1)
                return false;
            foreach (var child in cell.Blocks)
            {
                if (child is WpfParagraph paragraph ? DocumentView.IsInlineBreakContinuationFragment(paragraph) : child is not BlockUIContainer)
                    return false;
            }
        }
        return rows[v].Cells.Count > 0;
    }

    /// <summary>The first text (or embedded object) of <paramref name="element"/>.</summary>
    internal static TextPointer? FirstContent(TextElement element)
    {
        for (var pointer = element.ContentStart; pointer is not null && pointer.CompareTo(element.ContentEnd) < 0;
             pointer = pointer.GetNextContextPosition(LogicalDirection.Forward))
        {
            if (pointer.GetPointerContext(LogicalDirection.Forward) is TextPointerContext.Text or TextPointerContext.EmbeddedElement)
                return pointer;
        }
        return null;
    }

    /// <summary>The position before the last text character (or embedded object) of <paramref name="element"/>.</summary>
    private static TextPointer? LastContent(TextElement element)
    {
        for (var pointer = element.ContentEnd; pointer is not null && pointer.CompareTo(element.ContentStart) > 0;
             pointer = pointer.GetNextContextPosition(LogicalDirection.Backward))
        {
            if (pointer.GetPointerContext(LogicalDirection.Backward) is TextPointerContext.Text or TextPointerContext.EmbeddedElement)
                return pointer.GetPositionAtOffset(-1, LogicalDirection.Forward);
        }
        return null;
    }

    /// <summary>
    /// The position a cell cut names in <paramref name="cell"/>: for the side before the cut
    /// (<paramref name="forward"/> false) right after the last character, for the side after it at the
    /// start of the next; a cut at a paragraph's start lies between the cell's paragraphs.
    /// </summary>
    internal static TextPointer ResolveCell(TableCell cell, (int Paragraph, int Offset) cut, bool forward)
    {
        if (CellCuts.IsEnd(cut))
            return cell.ContentEnd;
        var blocks = cell.Blocks.ToList();
        if (cut.Paragraph >= blocks.Count)
            return cell.ContentEnd;
        return cut.Offset > 0 && blocks[cut.Paragraph] is WpfParagraph paragraph
            ? CutPosition(paragraph, cut.Offset, forward)
            : blocks[cut.Paragraph].ElementStart;
    }

    /// <summary>The position before row <paramref name="v"/>, outside any element it is the first child of (below <paramref name="block"/>).</summary>
    private static TextPointer RowStart(WpfBlock block, List<TableRow> rows, int v)
    {
        TextElement element = rows[v];
        while (element.Parent is TextElement parent && !ReferenceEquals(parent, block)
            && FirstChild(parent) is { } first && ReferenceEquals(first, element))
        {
            element = parent;
        }
        return element.ElementStart;
    }

    /// <summary>
    /// Cuts a render of a table (or of a section holding one) to the piece between two row cuts
    /// (<see cref="CutLocator.AtRow"/> or <see cref="CutLocator.InsideRow"/>; null: the table's start or
    /// end). A row cut inside a row keeps the row with each cell cut at its own position: the side before
    /// keeps every cell's content up to its cut, the side after from it (a cell may be left empty). Returns
    /// the piece's position map onto the whole render's symbol offsets, or null when the trimmed piece does
    /// not come out as predicted (it is then left as cut, without a map).
    /// </summary>
    internal static PieceMap? TrimTablePiece(WpfBlock block, CutLocator? from, CutLocator? to)
    {
        if (SingleTable(block) is not { } table)
            return null;
        var rows = TableRows(table);

        // Everything is measured on the untrimmed render first.
        var breaks = new List<(int StopFull, int Close, int ResumeFull, int Open)>();
        var startFull = 0;
        var startOpen = 0;
        var endFull = SymbolLength(block);
        var endClose = 0;

        if (to is { IsRow: true } end)
        {
            if (end.Cells is { } endCells)
            {
                if (end.Row < 0 || end.Row >= rows.Count || rows[end.Row].Cells.Count != endCells.Cells.Count)
                    return null;
                var c = 0;
                foreach (var cell in rows[end.Row].Cells)
                {
                    var at = ResolveCell(cell, endCells.Cells[c++], forward: false);
                    breaks.Add((SymbolOffset(block, at), Depth(cell, at), SymbolOffset(block, cell.ContentEnd), 0));
                }
                var after = rows[end.Row].ElementEnd;
                endFull = SymbolOffset(block, after);
                endClose = Depth(block, after);
            }
            else if (Resolve(block, end, forward: false) is { } at)
            {
                endFull = SymbolOffset(block, at);
                endClose = Depth(block, at);
            }
            else
            {
                return null;
            }
        }

        if (from is { IsRow: true } start)
        {
            if (start.Cells is { } startCells)
            {
                if (start.Row < 0 || start.Row >= rows.Count || rows[start.Row].Cells.Count != startCells.Cells.Count)
                    return null;
                var before = RowStart(block, rows, start.Row);
                startFull = SymbolOffset(block, before);
                startOpen = Depth(block, before);
                var c = 0;
                foreach (var cell in rows[start.Row].Cells)
                {
                    var at = ResolveCell(cell, startCells.Cells[c++], forward: true);
                    breaks.Add((SymbolOffset(block, cell.ContentStart), 0, SymbolOffset(block, at), Depth(cell, at)));
                }
            }
            else if (Resolve(block, start, forward: true) is { } at)
            {
                startFull = SymbolOffset(block, at);
                startOpen = Depth(block, at);
            }
            else
            {
                return null;
            }
        }

        // The map: kept stretches of the whole render, each after the ancestors re-opened before it.
        breaks.Sort((a, b) => a.StopFull != b.StopFull ? a.StopFull.CompareTo(b.StopFull) : a.ResumeFull.CompareTo(b.ResumeFull));
        var segments = new List<PieceMap.Segment>();
        var local = startOpen;
        var full = startFull;
        foreach (var (stopFull, close, resumeFull, open) in breaks)
        {
            if (stopFull < full || stopFull > endFull)
                return null;
            segments.Add(new PieceMap.Segment(local, full, stopFull - full));
            local += stopFull - full + close + open;
            full = resumeFull;
        }
        if (endFull < full)
            return null;
        segments.Add(new PieceMap.Segment(local, full, endFull - full));
        var expectedLength = local + endFull - full + endClose;

        // Trim: the side after the end cut first (positions before it keep their content offsets).
        if (to is { IsRow: true } trimEnd)
        {
            if (trimEnd.Cells is { } endCells)
            {
                var c = 0;
                foreach (var cell in rows[trimEnd.Row].Cells.ToList())
                    Trim(cell, ResolveCell(cell, endCells.Cells[c++], forward: false), keepBefore: true);
                TrimAfter(block, rows[trimEnd.Row].ElementEnd);
            }
            else if (Resolve(block, trimEnd, forward: false) is { } at)
            {
                TrimAfter(block, at);
            }
        }
        if (from is { IsRow: true } trimStart)
        {
            if (trimStart.Cells is { } startCells)
            {
                var c = 0;
                foreach (var cell in rows[trimStart.Row].Cells.ToList())
                    Trim(cell, ResolveCell(cell, startCells.Cells[c++], forward: true), keepBefore: false);
                TrimBefore(block, RowStart(block, rows, trimStart.Row));
            }
            else if (Resolve(block, trimStart, forward: true) is { } at)
            {
                TrimBefore(block, at);
            }
        }

        return SymbolLength(block) == expectedLength ? new PieceMap(segments) : null;
    }

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
                return locator.Offset <= 0 ? null : CutPosition(paragraph, locator.Offset, forward);
            case WpfList list when !locator.IsRow:
            {
                var paragraphs = ListParagraphs(list);
                if (locator.Paragraph >= paragraphs.Count)
                    return null;
                if (locator.Offset > 0)
                    return CutPosition(paragraphs[locator.Paragraph], locator.Offset, forward);
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
    internal static int Depth(TextElement top, TextPointer position)
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

    private static void Trim(TextElement top, TextPointer position, bool keepBefore)
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

    /// <summary>
    /// A piece of a table split inside a row: its content is not one stretch of the whole block (each
    /// cell of the split row shows its own share), so positions map through this instead of
    /// <see cref="Open"/>/<see cref="Gap"/>. Every piece of such a block has one (offsets are absolute).
    /// </summary>
    internal PieceMap? Map { get; init; }

    /// <summary>The piece's first row continues the previous piece's last row cell by cell.</summary>
    internal System.Windows.Documents.TableRow? RowContinuedFromPrevious { get; init; }

    /// <summary>The piece's last row continues cell by cell on the next piece.</summary>
    internal System.Windows.Documents.TableRow? RowContinuedOnNext { get; init; }

    /// <summary>Per cell of <see cref="RowContinuedFromPrevious"/>: its text continues a paragraph of the previous piece's cell mid-line.</summary>
    internal bool[]? CellTextSeamsFromPrevious { get; init; }

    /// <summary>Per cell of <see cref="RowContinuedOnNext"/>: its text continues mid-line in the next piece's cell.</summary>
    internal bool[]? CellTextSeamsOnNext { get; init; }
}

/// <summary>
/// Maps symbol offsets of a page piece onto those of the whole (unsplit) block it was cut from: an ordered
/// list of stretches the piece keeps, each at its own local offset. Positions in what the piece re-opens
/// (the ancestors of a cut) map to the nearest kept position.
/// </summary>
internal sealed class PieceMap(IReadOnlyList<PieceMap.Segment> segments)
{
    internal readonly record struct Segment(int Local, int Full, int Length);

    internal IReadOnlyList<Segment> Segments { get; } = segments;

    /// <summary>The whole-block offset of local offset <paramref name="local"/>.</summary>
    internal int ToFull(int local)
    {
        if (Segments.Count == 0)
            return 0;
        var segment = Segments[0];
        foreach (var candidate in Segments)
        {
            if (candidate.Local > local)
                break;
            segment = candidate;
        }
        return segment.Full + Math.Clamp(local - segment.Local, 0, segment.Length);
    }

    /// <summary>
    /// The local offset of whole-block offset <paramref name="full"/> and how far it is from what the piece
    /// keeps (0: the piece shows it).
    /// </summary>
    internal (int Local, int Distance) ToLocal(int full)
    {
        var best = (Local: 0, Distance: int.MaxValue);
        foreach (var segment in Segments)
        {
            if (full >= segment.Full && full <= segment.Full + segment.Length)
                return (segment.Local + full - segment.Full, 0);
            var distance = full < segment.Full ? segment.Full - full : full - segment.Full - segment.Length;
            if (distance < best.Distance)
                best = (full < segment.Full ? segment.Local : segment.Local + segment.Length, distance);
        }
        return best;
    }
}
