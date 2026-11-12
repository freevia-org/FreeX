using System.Collections;
using FreeW.App.Presentation.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

/// <summary>
/// Body-story Find and Replace computed in ONE pass over the model: every flow (a run of consecutive
/// top-level paragraphs, or the paragraphs of one table cell, nested tables included) is projected
/// once, all its matches are found once, and Replace All applies them back to front so earlier
/// offsets stay valid. Nothing here re-projects or re-renders between replacements.
/// </summary>
/// <remarks>
/// Inside a flow the paragraph mark between two paragraphs is a matchable
/// <see cref="FindSpecialCodes.ParagraphMark"/>; the flow's last mark is a
/// <see cref="FindSpecialCodes.CellMark"/> nothing matches. Manual page and column breaks are
/// matchable zero-width characters. Text that mirrors an object, and tracked-deleted text, is
/// masked so it can be neither matched nor rewritten.
/// </remarks>
public static class FindReplaceBodyPlanner
{
    /// <summary>Counts every body match Replace All would visit.</summary>
    public static int CountMatches(TextDocument document, FindReplaceSearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(request);

        var criteria = request.ToCriteria();
        if (criteria.IsEmpty)
            return 0;

        var count = 0;
        foreach (var unit in Units(document.Blocks))
        {
            foreach (var flow in unit.Flows())
                count += FlowProjection.Build(document, flow.Paragraphs, criteria.HasFormat).FindAll(criteria).Count;
        }

        return count;
    }

    /// <summary>
    /// Plans Replace All over the body without touching <paramref name="document"/>: edited blocks
    /// are deep clones, so the returned command swaps the block list and undo swaps it back.
    /// </summary>
    /// <param name="restrictTo">
    /// When set, only matches that start inside this body range are replaced (Replace All within a
    /// selection). A table lying wholly between the range's blocks is included.
    /// </param>
    public static FindReplaceBodyReplaceAllCommand PlanReplaceAll(
        TextDocument document,
        FindReplaceReplaceRequest request,
        FindReplaceRevisionContext? tracked = null,
        DocumentTextRange? restrictTo = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(request);

        var before = document.Blocks.ToList();
        var after = new List<Block>(before.Count);
        var criteria = request.ToCriteria();
        var replaced = 0;
        if (criteria.IsEmpty)
            return new FindReplaceBodyReplaceAllCommand(before, before, 0);

        var range = restrictTo?.Normalize();
        foreach (var unit in Units(before))
        {
            if (!unit.MayBeInside(range)
                || !unit.Flows().Any(flow =>
                    FlowProjection.Build(document, flow.Paragraphs, criteria.HasFormat)
                        .FindAll(criteria)
                        .Any()))
            {
                after.AddRange(unit.Blocks);
                continue;
            }

            // Work on a deep clone so the original blocks stay exactly as they were for undo.
            var clone = unit.Clone();
            foreach (var flow in clone.Flows().ToList())
            {
                var projection = FlowProjection.Build(document, flow.Paragraphs, criteria.HasFormat);
                var matches = projection.FindAll(criteria);
                var joined = new Dictionary<Paragraph, Paragraph>(ReferenceEqualityComparer.Instance);
                for (var i = matches.Count - 1; i >= 0; i--)
                {
                    var match = matches[i];
                    if (range is { } limit && !projection.StartsInside(match.Start, flow, clone, limit))
                        continue;
                    if (ApplyMatch(document, flow.Container, projection, match, request, tracked, joined))
                        replaced++;
                }
            }

            after.AddRange(clone.Blocks);
        }

        return new FindReplaceBodyReplaceAllCommand(before, replaced > 0 ? after : before, replaced);
    }

    // ---- units and flows ---------------------------------------------------------------------

    /// <summary>
    /// A top-level stretch that is searched and cloned together: one run of consecutive
    /// paragraphs, one table, or one other block (which has no text flows).
    /// </summary>
    private sealed class Unit(List<Block> blocks, int firstBlockIndex)
    {
        public List<Block> Blocks { get; } = blocks;

        public int FirstBlockIndex { get; } = firstBlockIndex;

        public bool IsParagraphRun => Blocks[0] is Paragraph;

        public Unit Clone() =>
            new(
                Blocks.Select(block => DocumentModelCloner.CloneBlock(block, RevisionClonePolicy.Preserve)).ToList(),
                FirstBlockIndex);

        public bool MayBeInside(DocumentTextRange? range)
        {
            if (range is not { } limit)
                return true;

            var last = FirstBlockIndex + Blocks.Count - 1;
            return IsParagraphRun
                ? last >= limit.Anchor.BlockIndex && FirstBlockIndex <= limit.Active.BlockIndex
                : FirstBlockIndex > limit.Anchor.BlockIndex && FirstBlockIndex < limit.Active.BlockIndex;
        }

        public IEnumerable<Flow> Flows()
        {
            if (IsParagraphRun)
            {
                // The unit's own list is the container: paragraphs split or merged here are spliced
                // back into the document's block list when the unit is emitted.
                yield return new Flow(Blocks, Blocks.Cast<Paragraph>().ToList());
                yield break;
            }

            if (Blocks[0] is Table table)
            {
                foreach (var flow in TableFlows(table))
                    yield return flow;
            }
        }

        private static IEnumerable<Flow> TableFlows(Table table)
        {
            foreach (var row in table.Rows)
            {
                foreach (var cell in row.Cells)
                {
                    if (cell.Paragraphs.Count > 0)
                        yield return new Flow(cell.Paragraphs, cell.Paragraphs.ToList());
                    foreach (var nested in cell.NestedTables)
                    {
                        foreach (var flow in TableFlows(nested))
                            yield return flow;
                    }
                }
            }
        }
    }

    /// <summary>One flow: its paragraphs in order and the list they live in.</summary>
    private sealed record Flow(IList Container, List<Paragraph> Paragraphs);

    private static IEnumerable<Unit> Units(IReadOnlyList<Block> blocks)
    {
        var index = 0;
        while (index < blocks.Count)
        {
            var start = index;
            var unit = new List<Block> { blocks[index++] };
            if (unit[0] is Paragraph)
            {
                while (index < blocks.Count && blocks[index] is Paragraph)
                    unit.Add(blocks[index++]);
            }

            yield return new Unit(unit, start);
        }
    }

    // ---- projection --------------------------------------------------------------------------

    private readonly record struct Piece(int Hay, int HayLength, int Plain, bool ZeroWidth, int BreakOrdinal = -1);

    private sealed class ParagraphMap(Paragraph paragraph, int hayStart)
    {
        public Paragraph Paragraph { get; } = paragraph;

        public int HayStart { get; } = hayStart;

        public List<Piece> Pieces { get; } = [];

        public int PlainLength { get; set; }

        /// <summary>The <see cref="Paragraph.PlainText"/> offset of a haystack offset in this paragraph.</summary>
        public int ToPlain(int hay)
        {
            foreach (var piece in Pieces)
            {
                if (hay < piece.Hay)
                    return piece.Plain;
                if (hay < piece.Hay + piece.HayLength)
                    return piece.ZeroWidth ? piece.Plain : piece.Plain + (hay - piece.Hay);
            }

            return PlainLength;
        }

        /// <summary>
        /// Removes the manual breaks a match covers. They are addressed by ordinal among the
        /// paragraph's break runs, highest first: matches are applied back to front, so the ordinals
        /// of earlier breaks stay valid even after later edits have rebuilt the run objects.
        /// </summary>
        public void RemoveBreaksIn(Paragraph paragraph, int hayStart, int hayEnd)
        {
            foreach (var ordinal in Pieces
                         .Where(piece => piece.BreakOrdinal >= 0 && piece.Hay >= hayStart && piece.Hay < hayEnd)
                         .Select(piece => piece.BreakOrdinal)
                         .OrderByDescending(ordinal => ordinal))
            {
                var seen = -1;
                for (var index = 0; index < paragraph.Runs.Count; index++)
                {
                    var run = paragraph.Runs[index];
                    if ((run.IsPageBreak || run.IsColumnBreak) && ++seen == ordinal)
                    {
                        paragraph.Runs.RemoveAt(index);
                        break;
                    }
                }
            }
        }
    }

    private sealed class FlowProjection
    {
        private readonly List<FindTextSegment> _segments = [];
        private readonly List<ParagraphMap> _maps = [];
        private readonly System.Text.StringBuilder _text = new();
        private string? _cachedText;

        public string Text => _cachedText ??= _text.ToString();

        public IReadOnlyList<ParagraphMap> Maps => _maps;

        public static FlowProjection Build(TextDocument document, List<Paragraph> paragraphs, bool resolveFormatting)
        {
            var projection = new FlowProjection();
            for (var index = 0; index < paragraphs.Count; index++)
            {
                var paragraph = paragraphs[index];
                var map = new ParagraphMap(paragraph, projection._text.Length);
                var plain = 0;
                var breaks = 0;
                foreach (var run in paragraph.Runs)
                {
                    if (run.IsPageBreak || run.IsColumnBreak)
                    {
                        var mark = run.IsPageBreak ? FindSpecialCodes.ManualPageBreak : FindSpecialCodes.ColumnBreak;
                        map.Pieces.Add(new Piece(projection._text.Length, 1, plain, ZeroWidth: true, breaks++));
                        projection.Add(new FindTextSegment(mark.ToString(), RunFormatting.Default, paragraph.StyleId, true));
                        plain += run.Text.Length;
                        continue;
                    }

                    var text = run.Text;
                    if (text.Length == 0)
                        continue;

                    map.Pieces.Add(new Piece(projection._text.Length, text.Length, plain, ZeroWidth: false));
                    projection.Add(
                        FormattedTextSearch.IsObjectMirror(run) || run.Revision == RevisionKind.Deleted
                            ? new FindTextSegment(
                                new string(FindSpecialCodes.ObjectPlaceholder, text.Length),
                                RunFormatting.Default,
                                paragraph.StyleId,
                                IsStructural: true)
                            : new FindTextSegment(
                                text,
                                resolveFormatting
                                    ? FindFormat.EffectiveFormatting(document, paragraph, run)
                                    : run.Formatting,
                                paragraph.StyleId));
                    plain += text.Length;
                }

                map.PlainLength = plain;
                projection._maps.Add(map);
                projection.Add(new FindTextSegment(
                    (index < paragraphs.Count - 1 ? FindSpecialCodes.ParagraphMark : FindSpecialCodes.CellMark).ToString(),
                    RunFormatting.Default,
                    paragraph.StyleId,
                    IsStructural: true));
            }

            return projection;
        }

        public IReadOnlyList<(int Start, int Length)> FindAll(FindCriteria criteria) =>
            FormattedTextSearch.FindAll(_segments, criteria);

        /// <summary>The paragraph a haystack offset falls in (its mark counts as its last character).</summary>
        public int ParagraphAt(int hay)
        {
            var lo = 0;
            var hi = _maps.Count - 1;
            while (lo < hi)
            {
                var mid = (lo + hi + 1) / 2;
                if (_maps[mid].HayStart <= hay)
                    lo = mid;
                else
                    hi = mid - 1;
            }

            return lo;
        }

        public bool StartsInside(int hay, Flow flow, Unit unit, DocumentTextRange limit)
        {
            // A table is only reached when it lies wholly inside the range (Unit.MayBeInside).
            if (!unit.IsParagraphRun)
                return true;

            var index = ParagraphAt(hay);
            var position = new DocumentTextPosition(unit.FirstBlockIndex + index, _maps[index].ToPlain(hay));
            return Compare(position, limit.Anchor) >= 0 && Compare(position, limit.Active) < 0;
        }

        private void Add(FindTextSegment segment)
        {
            _segments.Add(segment);
            _text.Append(segment.Text);
            _cachedText = null;
        }

        private static int Compare(DocumentTextPosition left, DocumentTextPosition right) =>
            left.BlockIndex != right.BlockIndex
                ? left.BlockIndex.CompareTo(right.BlockIndex)
                : left.Offset.CompareTo(right.Offset);
    }

    // ---- applying one match ------------------------------------------------------------------

    private static bool ApplyMatch(
        TextDocument document,
        IList container,
        FlowProjection projection,
        (int Start, int Length) match,
        FindReplaceReplaceRequest request,
        FindReplaceRevisionContext? tracked,
        Dictionary<Paragraph, Paragraph> joined)
    {
        var end = match.Start + match.Length;
        var first = projection.ParagraphAt(match.Start);
        var last = projection.ParagraphAt(end);
        var maps = projection.Maps;
        var found = projection.Text.Substring(match.Start, match.Length);
        var plan = FindReplaceDialogPlanner.PlanReplacement(request, found);

        // The PlainText range the match covers in each paragraph it touches.
        var ranges = new List<(Paragraph Paragraph, int From, int To)>();
        for (var index = first; index <= last; index++)
        {
            var map = maps[index];
            ranges.Add((
                Current(map.Paragraph, joined),
                index == first ? map.ToPlain(match.Start) : 0,
                index == last ? map.ToPlain(end) : map.PlainLength));
        }

        if (!plan.ReplacesText)
        {
            Reformat(document, ranges, plan.Format!, tracked);
            return true;
        }

        // A field's text is a cached result and an object has none; leave such a match alone.
        if (ranges.Any(range =>
                range.To > range.From
                && !RevisionEditPlanner.CanReplacePlainTextRange(range.Paragraph, range.From, range.To - range.From)))
        {
            return false;
        }

        var target = ranges[0].Paragraph;
        var offset = ranges[0].From;
        var pieces = plan.Text.Split(FindSpecialCodes.ParagraphMark);
        var inserted = new List<(Paragraph Paragraph, int From, int To)>();
        var firstPiece = 0;

        // New text takes the formatting of the first character it replaces, as in Word.
        var covered = ranges.FirstOrDefault(range => range.To > range.From);
        var inherited = covered.Paragraph is not null
            ? RevisionEditPlanner.FormattingAtOffset(covered.Paragraph, covered.From + 1)
            : RevisionEditPlanner.FormattingAtOffset(target, offset);

        if (tracked is null)
        {
            for (var index = last; index >= first; index--)
                maps[index].RemoveBreaksIn(ranges[index - first].Paragraph, match.Start, end);

            for (var i = ranges.Count - 1; i >= 0; i--)
            {
                var range = ranges[i];
                if (range.To <= range.From)
                    continue;

                // The first piece is written in place so it keeps the replaced run's hyperlink,
                // character style and formatting; everything else the match covered is removed.
                var text = i == 0 ? pieces[0] : string.Empty;
                RevisionEditPlanner.TryReplacePlainTextRange(range.Paragraph, range.From, range.To - range.From, text);
                if (i == 0)
                {
                    firstPiece = 1;
                    offset = range.From + text.Length;
                    if (text.Length > 0)
                        inserted.Add((range.Paragraph, range.From, offset));
                }
            }
        }
        else
        {
            // Tracked: the originals stay, struck through, and a consumed paragraph mark is marked
            // deleted instead of the paragraphs being merged.
            for (var i = ranges.Count - 1; i >= 0; i--)
            {
                var range = ranges[i];
                if (range.To > range.From)
                {
                    var deleted = RevisionEditPlanner.DeleteRangeAsRevision(
                        range.Paragraph, range.From, range.To, tracked.Author, tracked.DateXml);
                    if (i == 0)
                        offset = deleted.CaretOffset;
                }

                if (i < ranges.Count - 1)
                {
                    range.Paragraph.MarkRevision = RevisionKind.Deleted;
                    range.Paragraph.MarkRevisionAuthor = tracked.Author;
                    range.Paragraph.MarkRevisionDateXml = tracked.DateXml;
                }
            }
        }

        // Insert the rest of the replacement, turning each paragraph mark in it into a real
        // paragraph break.
        for (var i = firstPiece; i < pieces.Length; i++)
        {
            if (i > 0)
            {
                target = Split(container, target, offset);
                offset = 0;
                if (tracked is not null)
                {
                    target.MarkRevision = RevisionKind.Inserted;
                    target.MarkRevisionAuthor = tracked.Author;
                    target.MarkRevisionDateXml = tracked.DateXml;
                }
            }

            if (pieces[i].Length == 0)
                continue;

            var after = tracked is null
                ? RevisionEditPlanner.InsertText(target, offset, pieces[i], inherited)
                : RevisionEditPlanner.InsertTrackedText(target, offset, pieces[i], inherited, tracked.Author, tracked.DateXml);
            inserted.Add((target, after - pieces[i].Length, after));
            offset = after;
        }

        if (plan.Format is { } format)
        {
            if (inserted.Count == 0)
                inserted.Add((target, offset, offset));
            Reformat(document, inserted, format, tracked);
        }

        // The match consumed paragraph marks: what is left of its first paragraph (ending in the
        // last piece of the replacement) joins the paragraph the match ended in.
        if (tracked is null && ranges.Count > 1)
        {
            for (var i = 1; i < ranges.Count - 1; i++)
                container.Remove(ranges[i].Paragraph);
            JoinInto(container, target, ranges[^1].Paragraph, joined);
        }

        return true;
    }

    /// <summary>The paragraph that now holds <paramref name="paragraph"/>'s text (see <see cref="JoinInto"/>).</summary>
    private static Paragraph Current(Paragraph paragraph, Dictionary<Paragraph, Paragraph> joined)
    {
        while (joined.TryGetValue(paragraph, out var survivor))
            paragraph = survivor;
        return paragraph;
    }

    private static void Reformat(
        TextDocument document,
        IEnumerable<(Paragraph Paragraph, int From, int To)> ranges,
        FindFormat format,
        FindReplaceRevisionContext? tracked)
    {
        foreach (var (paragraph, from, to) in ranges)
        {
            if (format.HasCharacterFormatting && to > from)
            {
                RevisionEditPlanner.ApplyFormattingRange(
                    paragraph,
                    from,
                    to,
                    format.ApplyTo,
                    tracked is null ? null : document,
                    tracked?.Author,
                    tracked?.DateXml);
            }

            if (format.ParagraphStyleId is { } styleId)
                paragraph.StyleId = styleId;
        }
    }

    /// <summary>
    /// Joins <paramref name="head"/> onto the front of <paramref name="survivor"/> and removes it.
    /// The joined paragraph takes the head's style and formatting (it is the paragraph the text
    /// started in) and keeps the survivor's mark, section break included.
    /// </summary>
    /// <remarks>
    /// The head is folded into the survivor rather than the other way round on purpose. Matches are
    /// applied back to front, so in a long chain of joins the survivor is the paragraph that keeps
    /// growing; appending it to each new head would copy the whole accumulated tail every time.
    /// Prepending keeps every offset in the head valid, so earlier matches that still point at the
    /// head are simply redirected through <paramref name="joined"/>.
    /// </remarks>
    private static void JoinInto(
        IList container,
        Paragraph head,
        Paragraph survivor,
        Dictionary<Paragraph, Paragraph> joined)
    {
        var shift = head.Runs.Count;
        survivor.Runs.InsertRange(0, head.Runs);
        for (var i = 0; i < survivor.BookmarkBoundaries.Count; i++)
        {
            var boundary = survivor.BookmarkBoundaries[i];
            survivor.BookmarkBoundaries[i] = boundary with { RunIndex = boundary.RunIndex + shift };
        }

        survivor.BookmarkBoundaries.InsertRange(0, head.BookmarkBoundaries);
        var names = head.BookmarkNames.Concat(survivor.BookmarkNames).Distinct(StringComparer.Ordinal).ToList();
        survivor.BookmarkNames.Clear();
        survivor.BookmarkNames.AddRange(names);
        survivor.Formatting = head.Formatting;
        survivor.StyleId = head.StyleId;
        survivor.PreservedNumbering = head.PreservedNumbering;

        container.Remove(head);
        joined[head] = survivor;
    }
    /// <summary>
    /// Splits <paramref name="paragraph"/> at a PlainText offset and returns the new second half,
    /// inserted after it. Both halves share the style and formatting; the section break stays with
    /// the half that keeps the original paragraph mark.
    /// </summary>
    private static Paragraph Split(IList container, Paragraph paragraph, int offset)
    {
        var tail = RevisionEditPlanner.SplitParagraph(paragraph, offset);
        var index = container.IndexOf(paragraph);
        container.Insert(index < 0 ? container.Count : index + 1, tail);
        return tail;
    }
}

/// <summary>
/// The single undoable edit Replace All makes to the body: it swaps the document's block list for
/// one in which every block that had a match is an edited deep clone. Undo swaps the original list
/// back, so the untouched originals are restored exactly.
/// </summary>
public sealed class FindReplaceBodyReplaceAllCommand(
    IReadOnlyList<Block> before,
    IReadOnlyList<Block> after,
    int replacedCount) : IDocumentCommand
{
    public string Label => "Replace All";

    /// <summary>How many body matches the command replaces.</summary>
    public int ReplacedCount => replacedCount;

    public bool HasEffect(IDocumentCommandContext context) => replacedCount > 0;

    // The block list actually replaced, captured when the edit is applied. Undo must restore what the
    // document held at that moment, not the list handed in at construction: another command may have
    // run in between, and a stale snapshot would silently discard its blocks.
    private List<Block>? _replaced;

    public void Apply(IDocumentCommandContext context)
    {
        _replaced = [.. context.Document.Blocks];
        Set(context.Document, after);
    }

    public void Revert(IDocumentCommandContext context) => Set(context.Document, _replaced ?? before);

    private static void Set(TextDocument document, IReadOnlyList<Block> blocks)
    {
        document.Blocks.Clear();
        document.Blocks.AddRange(blocks);
    }
}
