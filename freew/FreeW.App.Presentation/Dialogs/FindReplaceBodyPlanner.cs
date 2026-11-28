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
                count += FlowProjection.Build(document, flow.Paragraphs, criteria).FindAll(criteria).Count;
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
            return new FindReplaceBodyReplaceAllCommand(before, 0);

        var range = restrictTo?.Normalize();
        foreach (var unit in Units(before))
        {
            if (!unit.MayBeInside(range)
                || !unit.Flows().Any(flow =>
                    FlowProjection.Build(document, flow.Paragraphs, criteria)
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
                var projection = FlowProjection.Build(document, flow.Paragraphs, criteria);
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

        return new FindReplaceBodyReplaceAllCommand(replaced > 0 ? after : before, replaced);
    }

    /// <summary>
    /// Plans the replacement of ONE match, located as Find Next reports it (a body paragraph span, a span
    /// in a table cell, or one that runs across paragraph marks), with exactly the edit Replace All makes
    /// to it: the same projection, the same replacement expansion, and Replace-with formatting applied to
    /// the written text (or to the kept text when the Replace with box is empty). Nothing in
    /// <paramref name="document"/> is touched; the returned command swaps the block list like
    /// <see cref="PlanReplaceAll"/>'s, and replaces nothing when the location is no longer a match.
    /// </summary>
    public static FindReplaceBodyReplaceAllCommand PlanReplaceMatch(
        TextDocument document,
        FindReplaceReplaceRequest request,
        FindReplaceMatch location,
        FindReplaceRevisionContext? tracked = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(request);

        var before = document.Blocks.ToList();
        var none = new FindReplaceBodyReplaceAllCommand(before, 0, ReplaceLabel);
        var criteria = request.ToCriteria();
        if (criteria.IsEmpty || location.IsInHeaderFooter || location.Block < 0 || location.Block >= before.Count)
            return none;

        var unit = Units(before).First(candidate =>
            location.Block >= candidate.FirstBlockIndex && location.Block < candidate.FirstBlockIndex + candidate.Blocks.Count);
        var clone = unit.Clone();

        Flow? flow;
        int firstParagraph;
        int lastParagraph;
        if (unit.IsParagraphRun)
        {
            if (location.IsInTableCell)
                return none;

            flow = clone.Flows().FirstOrDefault();
            firstParagraph = location.Block - unit.FirstBlockIndex;
            lastParagraph = (location.EndBlock ?? location.Block) - unit.FirstBlockIndex;
        }
        else
        {
            if (clone.Blocks[0] is not Table table
                || location.TableRow is not { } row
                || row < 0
                || row >= table.Rows.Count
                || location.TableCol is not { } column
                || TableGridProjection.StartingAt(table.Rows[row], column)?.Cell is not { } cell
                || location.TableParagraphIndex is not { } paragraphIndex)
            {
                return none;
            }

            flow = clone.Flows().FirstOrDefault(candidate => ReferenceEquals(candidate.Container, cell.Paragraphs));
            firstParagraph = paragraphIndex;
            lastParagraph = location.EndParagraphIndex ?? paragraphIndex;
        }

        if (flow is null)
            return none;

        var projection = FlowProjection.Build(document, flow.Paragraphs, criteria);
        var endOffset = location.EndOffset ?? location.Start + location.Length;
        foreach (var match in projection.FindAll(criteria))
        {
            var first = projection.ParagraphAt(match.Start);
            var end = match.Start + match.Length;
            var last = projection.ParagraphAt(end);
            if (first != firstParagraph
                || last != lastParagraph
                || projection.Maps[first].ToPlain(match.Start) != location.Start
                || projection.Maps[last].ToPlain(end) != endOffset
                || (location.MarkRunIndex is { } markRun
                    && projection.Maps[first].FirstMarkRun(match.Start, end) != markRun))
            {
                continue;
            }

            if (!ApplyMatch(document, flow.Container, projection, match, request, tracked, new(ReferenceEqualityComparer.Instance)))
                return none;

            var after = new List<Block>(before.Count);
            foreach (var other in Units(before))
            {
                if (ReferenceEquals(other.Blocks[0], unit.Blocks[0]))
                    after.AddRange(clone.Blocks);
                else
                    after.AddRange(other.Blocks);
            }

            return new FindReplaceBodyReplaceAllCommand(after, 1, ReplaceLabel);
        }

        return none;
    }

    /// <summary>
    /// Names the object a textless match (see <see cref="FindReplaceMatch.IsMarkOnly"/>) covers, for a host
    /// that can place the match only by offset and so cannot tell two objects at one offset apart (the WPF
    /// surface selects nothing for a comment mark). <paramref name="ordinal"/> is the match's position among
    /// the textless matches of <paramref name="request"/> at that same offset, in document order; the
    /// returned location carries the run index of that match's first and last object, so
    /// <see cref="PlanReplaceMatch"/> consumes that one. A location that is not a textless body, table-cell
    /// or spanning-free match, or whose ordinal no match answers, comes back unchanged.
    /// </summary>
    public static FindReplaceMatch WithMarkRun(
        TextDocument document,
        FindReplaceSearchRequest request,
        FindReplaceMatch location,
        int ordinal)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(request);

        if (location.IsMarkOnly
            || location.Length != 0
            || location.IsSpanning
            || ordinal < 0
            || FindReplaceDialogPlanner.ResolveParagraph(document, location) is not { } paragraph)
        {
            return location;
        }

        var criteria = request.ToCriteria();
        if (criteria.IsEmpty)
            return location;

        var objects = MatchParagraph(document, paragraph, criteria, out _)
            .Where(match => match.PlainLength == 0 && match.FirstMarkRunIndex >= 0 && match.PlainStart == location.Start)
            .ToList();
        return ordinal < objects.Count
            ? location with
            {
                MarkRunIndex = objects[ordinal].FirstMarkRunIndex,
                MarkEndRunIndex = objects[ordinal].LastMarkRunIndex,
            }
            : location;
    }

    /// <summary>
    /// The matches that run across at least one paragraph mark, each located by where it starts and ends
    /// (see <see cref="FindReplaceMatch.IsSpanning"/>), in document order. Only text a caret can be
    /// placed in is covered: top-level paragraph runs and the cells of top-level tables (a match never
    /// crosses a cell edge); nested tables are left to the other body walkers.
    /// </summary>
    public static IReadOnlyList<FindReplaceMatch> FindParagraphSpanningMatches(
        TextDocument document,
        FindReplaceSearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(request);

        var criteria = request.ToCriteria();
        var result = new List<FindReplaceMatch>();
        if (criteria.IsEmpty)
            return result;

        foreach (var unit in Units(document.Blocks))
        {
            if (unit.IsParagraphRun)
            {
                if (unit.Blocks.Count > 1)
                {
                    var paragraphs = unit.Blocks.Cast<Paragraph>().ToList();
                    AddSpanning(
                        FlowProjection.Build(document, paragraphs, criteria),
                        criteria,
                        result,
                        (first, last, start, end) => new FindReplaceMatch(
                            unit.FirstBlockIndex + first,
                            start,
                            end.Length,
                            EndBlock: unit.FirstBlockIndex + last,
                            EndOffset: end.Offset));
                }
            }
            else if (unit.Blocks[0] is Table table)
            {
                for (var row = 0; row < table.Rows.Count; row++)
                {
                    foreach (var projected in TableGridProjection.ProjectRow(table.Rows[row]))
                    {
                        if (projected.Cell.Paragraphs.Count < 2)
                            continue;

                        var rowIndex = row;
                        var column = projected.StartColumn;
                        AddSpanning(
                            FlowProjection.Build(document, projected.Cell.Paragraphs.ToList(), criteria),
                            criteria,
                            result,
                            (first, last, start, end) => new FindReplaceMatch(
                                unit.FirstBlockIndex,
                                start,
                                end.Length,
                                rowIndex,
                                column,
                                first,
                                EndParagraphIndex: last,
                                EndOffset: end.Offset));
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Whether a paragraph holds a run the haystack represents by an object mark (a note or comment
    /// reference, an inline graphic, or - when the search uses <c>^d</c> - a field). Such a paragraph
    /// must be searched through <see cref="FindInParagraph"/>, not as bare <see cref="Paragraph.PlainText"/>.
    /// </summary>
    public static bool HasObjectMarks(Paragraph paragraph, FindCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        var fieldMarks = FindSpecialCodes.RequiresFieldMarks(criteria.Term, criteria.Options);
        return paragraph.Runs.Any(run =>
            FormattedTextSearch.StructuralMarkFor(run) is not null
            || (fieldMarks && FormattedTextSearch.IsField(run)));
    }

    /// <summary>
    /// The matches that lie inside one paragraph, as spans over <see cref="Paragraph.PlainText"/>,
    /// found in the same haystack Replace All uses (so an object mark is one matchable character and a
    /// note reference's number is not text). A match with no PlainText extent -- a graphic or a comment
    /// mark -- cannot be expressed as a selectable span and is left out.
    /// </summary>
    public static IReadOnlyList<(int Start, int Length)> FindInParagraph(
        TextDocument document,
        Paragraph paragraph,
        FindCriteria criteria) =>
        MatchParagraph(document, paragraph, criteria, out _)
            .Where(match => match.PlainLength > 0)
            .Select(match => (match.PlainStart, match.PlainLength))
            .ToList();

    /// <summary>
    /// One match inside one paragraph, in both coordinate systems: the projected haystack (where an object
    /// mark is one character) and <see cref="Paragraph.PlainText"/>. A match that covers nothing but marks
    /// with no text of their own (a graphic, a comment mark) has <see cref="PlainLength"/> zero and is
    /// addressed by <see cref="FirstMarkRunIndex"/>: the index in <see cref="Paragraph.Runs"/> of the first
    /// mark run it covers (<see cref="LastMarkRunIndex"/> the last), -1 when it covers none.
    /// </summary>
    public readonly record struct ParagraphMatch(
        int HayStart,
        int HayLength,
        int PlainStart,
        int PlainLength,
        int FirstMarkRunIndex = -1,
        int LastMarkRunIndex = -1);

    /// <summary>
    /// Every match in one paragraph's projected haystack (the same one Replace All searches, so an object
    /// mark is one matchable character and a note reference's number is not text), including matches that
    /// have no <see cref="Paragraph.PlainText"/> extent. <paramref name="haystack"/> is the projected text
    /// the <see cref="ParagraphMatch.HayStart"/> offsets index; it ends in the flow-end mark nothing matches.
    /// </summary>
    public static IReadOnlyList<ParagraphMatch> MatchParagraph(
        TextDocument document,
        Paragraph paragraph,
        FindCriteria criteria,
        out string haystack)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(paragraph);
        ArgumentNullException.ThrowIfNull(criteria);

        var projection = FlowProjection.Build(document, [paragraph], criteria);
        haystack = projection.Text;
        var map = projection.Maps[0];
        var matches = new List<ParagraphMatch>();
        foreach (var (start, length) in projection.FindAll(criteria))
        {
            var from = map.ToPlain(start);
            var to = map.ToPlain(start + length);
            var first = -1;
            var last = -1;
            foreach (var piece in map.Pieces)
            {
                if (piece.RunIndex < 0 || piece.Hay < start || piece.Hay >= start + length)
                    continue;
                if (first < 0)
                    first = piece.RunIndex;
                last = piece.RunIndex;
            }

            matches.Add(new ParagraphMatch(start, length, from, Math.Max(0, to - from), first, last));
        }

        return matches;
    }

    /// <summary>
    /// The <see cref="Paragraph.PlainText"/> offset of a haystack offset (see <see cref="MatchParagraph"/>) of
    /// one paragraph, for a host that places its caret by text offset.
    /// </summary>
    public static int HaystackToPlain(TextDocument document, Paragraph paragraph, FindCriteria criteria, int hayOffset)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(paragraph);
        ArgumentNullException.ThrowIfNull(criteria);
        return FlowProjection.Build(document, [paragraph], criteria).Maps[0].ToPlain(Math.Max(0, hayOffset));
    }

    /// <summary>
    /// Replaces the match at haystack span [<paramref name="hayStart"/>, <paramref name="hayStart"/> +
    /// <paramref name="hayLength"/>) of one paragraph (see <see cref="MatchParagraph"/>) in place, with
    /// exactly the edit Replace All makes to it: the object marks it covers are consumed, the replacement
    /// text and formatting are written, and Track Changes strikes text through. For a paragraph of a story
    /// whose flow is that paragraph alone. Returns false, leaving the paragraph as it was, when the span is
    /// no longer a match or cannot be replaced (a match over a field's cached result, or over an object
    /// while tracking).
    /// </summary>
    public static bool ReplaceInParagraph(
        TextDocument document,
        Paragraph paragraph,
        FindReplaceReplaceRequest request,
        int hayStart,
        int hayLength,
        FindReplaceRevisionContext? tracked = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(paragraph);
        ArgumentNullException.ThrowIfNull(request);

        var criteria = request.ToCriteria();
        if (criteria.IsEmpty)
            return false;

        var projection = FlowProjection.Build(document, [paragraph], criteria);
        var match = projection.FindAll(criteria).FirstOrDefault(candidate =>
            candidate.Start == hayStart && candidate.Length == hayLength);
        return match.Length > 0
            && ApplyMatch(
                document,
                new List<Paragraph> { paragraph },
                projection,
                match,
                request,
                tracked,
                new Dictionary<Paragraph, Paragraph>(ReferenceEqualityComparer.Instance));
    }
    private static void AddSpanning(
        FlowProjection projection,
        FindCriteria criteria,
        List<FindReplaceMatch> result,
        Func<int, int, int, (int Offset, int Length), FindReplaceMatch> locate)
    {
        foreach (var (start, length) in projection.FindAll(criteria))
        {
            var first = projection.ParagraphAt(start);
            var last = projection.ParagraphAt(start + length);
            if (last > first)
            {
                result.Add(locate(
                    first,
                    last,
                    projection.Maps[first].ToPlain(start),
                    (projection.Maps[last].ToPlain(start + length), length)));
            }
        }
    }

    private const string ReplaceLabel = "Replace";

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

    /// <summary>
    /// One stretch of a paragraph's haystack. A zero-width piece is a single structural character that
    /// stands for a run (a break, a note or comment mark, a graphic, the start of a field); its
    /// <paramref name="MarkOrdinal"/> is that run's position among the paragraph's removable mark runs.
    /// </summary>
    private readonly record struct Piece(
        int Hay, int HayLength, int Plain, bool ZeroWidth, int MarkOrdinal = -1, int RunIndex = -1);

    /// <summary>
    /// Whether a run is one the haystack represents by a structural character a Replace can consume:
    /// manual breaks, note / comment reference marks, inline graphics and (when the search projects
    /// field marks) fields. Tracked-deleted runs are masked instead, so are never removable.
    /// </summary>
    private static bool IsMarkRun(Run run, bool fieldMarks) =>
        run.IsPageBreak
        || run.IsColumnBreak
        || (run.Revision != RevisionKind.Deleted
            && (FormattedTextSearch.StructuralMarkFor(run) is not null
                || (fieldMarks && FormattedTextSearch.IsField(run))));

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

        /// <summary>The index in the paragraph's runs of the first mark run in haystack span [start, end), or -1.</summary>
        public int FirstMarkRun(int hayStart, int hayEnd)
        {
            foreach (var piece in Pieces)
            {
                if (piece.RunIndex >= 0 && piece.Hay >= hayStart && piece.Hay < hayEnd)
                    return piece.RunIndex;
            }

            return -1;
        }

        /// <summary>
        /// Removes the mark runs a match covers (breaks, note / comment marks, graphics, fields). They
        /// are addressed by ordinal among the paragraph's mark runs, highest first: matches are
        /// applied back to front, so the ordinals of earlier marks stay valid even after later edits
        /// have rebuilt the run objects. Returns what was removed, so a caller that then declines the
        /// match can put it back with <see cref="Restore"/>, and the PlainText width removed.
        /// </summary>
        public (List<(int Index, Run Run)> Removed, int PlainWidth) RemoveMarkRunsIn(
            Paragraph paragraph,
            int hayStart,
            int hayEnd,
            bool fieldMarks)
        {
            var removed = new List<(int Index, Run Run)>();
            var width = 0;
            foreach (var ordinal in Pieces
                         .Where(piece => piece.MarkOrdinal >= 0 && piece.Hay >= hayStart && piece.Hay < hayEnd)
                         .Select(piece => piece.MarkOrdinal)
                         .OrderByDescending(ordinal => ordinal))
            {
                var seen = -1;
                for (var index = 0; index < paragraph.Runs.Count; index++)
                {
                    var run = paragraph.Runs[index];
                    if (IsMarkRun(run, fieldMarks) && ++seen == ordinal)
                    {
                        removed.Add((index, run));
                        width += run.Text.Length;
                        paragraph.Runs.RemoveAt(index);
                        break;
                    }
                }
            }

            return (removed, width);
        }

        /// <summary>Puts back runs <see cref="RemoveMarkRunsIn"/> removed (lowest index first).</summary>
        public static void Restore(Paragraph paragraph, List<(int Index, Run Run)> removed)
        {
            foreach (var (index, run) in removed.OrderBy(entry => entry.Index))
                paragraph.Runs.Insert(Math.Min(index, paragraph.Runs.Count), run);
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

        /// <summary>
        /// Whether this projection carries <see cref="FindSpecialCodes.FieldStart"/> /
        /// <see cref="FindSpecialCodes.FieldEnd"/> around each field (only when the search uses <c>^d</c>).
        /// </summary>
        public bool FieldMarks { get; private init; }

        public static FlowProjection Build(TextDocument document, List<Paragraph> paragraphs, FindCriteria criteria)
        {
            var resolveFormatting = criteria.HasFormat;
            var fieldMarks = FindSpecialCodes.RequiresFieldMarks(criteria.Term, criteria.Options);
            var projection = new FlowProjection { FieldMarks = fieldMarks };
            for (var index = 0; index < paragraphs.Count; index++)
            {
                var paragraph = paragraphs[index];
                var map = new ParagraphMap(paragraph, projection._text.Length);
                var plain = 0;
                var marks = 0;
                var runIndex = -1;
                foreach (var run in paragraph.Runs)
                {
                    runIndex++;
                    if (run.IsPageBreak || run.IsColumnBreak)
                    {
                        var mark = run.IsPageBreak ? FindSpecialCodes.ManualPageBreak : FindSpecialCodes.ColumnBreak;
                        map.Pieces.Add(new Piece(projection._text.Length, 1, plain, ZeroWidth: true, marks++, runIndex));
                        projection.Add(new FindTextSegment(mark.ToString(), RunFormatting.Default, paragraph.StyleId, true));
                        plain += run.Text.Length;
                        continue;
                    }

                    if (run.Revision != RevisionKind.Deleted && FormattedTextSearch.StructuralMarkFor(run) is { } objectMark)
                    {
                        // One haystack character stands for the whole run, whatever text it mirrors.
                        map.Pieces.Add(new Piece(projection._text.Length, 1, plain, ZeroWidth: true, marks++, runIndex));
                        projection.Add(new FindTextSegment(objectMark.ToString(), RunFormatting.Default, paragraph.StyleId, true));
                        plain += run.Text.Length;
                        continue;
                    }

                    var text = run.Text;
                    var isFieldRun = fieldMarks && run.Revision != RevisionKind.Deleted && FormattedTextSearch.IsField(run);
                    if (isFieldRun)
                    {
                        // The field's cached result sits between its start and end marks, so ^d spans
                        // the whole field while ordinary text still reads through it when no mark is projected.
                        map.Pieces.Add(new Piece(projection._text.Length, 1, plain, ZeroWidth: true, marks++, runIndex));
                        projection.Add(new FindTextSegment(
                            FindSpecialCodes.FieldStart.ToString(), RunFormatting.Default, paragraph.StyleId, true));
                        if (text.Length > 0)
                        {
                            map.Pieces.Add(new Piece(projection._text.Length, text.Length, plain, ZeroWidth: false));
                            projection.Add(new FindTextSegment(
                                text,
                                resolveFormatting
                                    ? FindFormat.EffectiveFormatting(document, paragraph, run)
                                    : run.Formatting,
                                paragraph.StyleId));
                            plain += text.Length;
                        }

                        map.Pieces.Add(new Piece(projection._text.Length, 1, plain, ZeroWidth: true));
                        projection.Add(new FindTextSegment(
                            FindSpecialCodes.FieldEnd.ToString(), RunFormatting.Default, paragraph.StyleId, true));
                        continue;
                    }

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
                    EndMark(paragraph, last: index == paragraphs.Count - 1).ToString(),
                    RunFormatting.Default,
                    paragraph.StyleId,
                    IsStructural: true));
            }

            return projection;
        }

        /// <summary>
        /// The character that ends a paragraph in the haystack: a section break for the paragraph that
        /// ends a section (the break terminates it, as in a Word document), otherwise a paragraph mark,
        /// or the cell mark nothing matches when it is the last paragraph of its flow.
        /// </summary>
        private static char EndMark(Paragraph paragraph, bool last) =>
            paragraph.SectionBreak is not null
                ? FindSpecialCodes.SectionBreakMark
                : last ? FindSpecialCodes.CellMark : FindSpecialCodes.ParagraphMark;

        /// <summary>True when a haystack span covers a character that stands for an object (not text).</summary>
        public bool CoversObjectMark(int start, int length) =>
            Text.AsSpan(start, length).IndexOfAny(
                [FindSpecialCodes.GraphicMark, FindSpecialCodes.FootnoteMark, FindSpecialCodes.EndnoteMark,
                 FindSpecialCodes.CommentMark, FindSpecialCodes.FieldStart]) >= 0;

        /// <summary>True when the haystack character at <paramref name="hay"/> is the section break that ends the flow.</summary>
        public bool IsFlowFinalSectionBreak(int hay) =>
            hay == Text.Length - 1 && Text[hay] == FindSpecialCodes.SectionBreakMark;

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

        // A section break that ends its flow has no paragraph after it to join, so it cannot be
        // consumed. Under Track Changes an object cannot be struck through either (only text can).
        if (end == projection.Text.Length && projection.IsFlowFinalSectionBreak(end - 1))
            return false;
        if (tracked is not null && projection.CoversObjectMark(match.Start, match.Length))
            return false;

        // The mark runs the match covers (breaks, note / comment marks, graphics, fields) are consumed
        // with it. They are taken out first so the text check below sees only what is left; if the
        // match is then declined they go back exactly where they were.
        var removedMarks = new List<(Paragraph Paragraph, List<(int Index, Run Run)> Removed)>();
        int? markRunIndex = null;
        if (tracked is null)
        {
            for (var index = last; index >= first; index--)
            {
                var slot = index - first;
                var (removed, width) = maps[index].RemoveMarkRunsIn(
                    ranges[slot].Paragraph, match.Start, end, projection.FieldMarks);
                removedMarks.Add((ranges[slot].Paragraph, removed));
                ranges[slot] = (ranges[slot].Paragraph, ranges[slot].From, Math.Max(ranges[slot].From, ranges[slot].To - width));

                // A match that covered only objects in its first paragraph (no text of its own there): the
                // replacement takes the place of the first object it consumed among the runs, as in Word,
                // not the front of every object that shares its offset.
                if (index == first && removed.Count > 0 && ranges[slot].To <= ranges[slot].From)
                    markRunIndex = removed.Min(entry => entry.Index);
            }
        }

        // A field's text is a cached result and an object has none; leave such a match alone.
        if (ranges.Any(range =>
                range.To > range.From
                && !RevisionEditPlanner.CanReplacePlainTextRange(range.Paragraph, range.From, range.To - range.From)))
        {
            foreach (var (paragraph, removed) in removedMarks)
                ParagraphMap.Restore(paragraph, removed);
            return false;
        }

        var target = ranges[0].Paragraph;
        var offset = ranges[0].From;
        var pieces = plan.Text.Split(FindSpecialCodes.ParagraphMark);
        var inserted = new List<(Paragraph Paragraph, int From, int To)>();
        var firstPiece = 0;

        // A manual page / column break in the replacement is a run, not text: the text before the first
        // one is written in place, the rest (breaks included) is inserted after it.
        var firstBreak = pieces[0].AsSpan().IndexOfAny(FindSpecialCodes.ManualPageBreak, FindSpecialCodes.ColumnBreak);
        var firstRemainder = firstBreak < 0 ? string.Empty : pieces[0][firstBreak..];
        var firstText = firstBreak < 0 ? pieces[0] : pieces[0][..firstBreak];

        // New text takes the formatting of the first character it replaces, as in Word.
        var covered = ranges.FirstOrDefault(range => range.To > range.From);
        var inherited = covered.Paragraph is not null
            ? RevisionEditPlanner.FormattingAtOffset(covered.Paragraph, covered.From + 1)
            : RevisionEditPlanner.FormattingAtOffset(target, offset);

        if (tracked is null)
        {
            for (var i = ranges.Count - 1; i >= 0; i--)
            {
                var range = ranges[i];
                if (range.To <= range.From)
                    continue;

                // The first piece is written in place so it keeps the replaced run's hyperlink,
                // character style and formatting; everything else the match covered is removed.
                var text = i == 0 ? firstText : string.Empty;
                RevisionEditPlanner.TryReplacePlainTextRange(range.Paragraph, range.From, range.To - range.From, text);
                if (i == 0)
                {
                    firstPiece = 1;
                    offset = range.From + text.Length;
                    if (text.Length > 0)
                        inserted.Add((range.Paragraph, range.From, offset));
                    if (firstRemainder.Length > 0)
                        offset = InsertPiece(range.Paragraph, offset, firstRemainder, inherited, null, inserted);
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

            offset = InsertPiece(
                target,
                offset,
                pieces[i],
                inherited,
                tracked,
                inserted,
                i == firstPiece && i == 0 ? markRunIndex : null);
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

    /// <summary>
    /// Writes one paragraph's worth of replacement at <paramref name="offset"/>: its text, and a run for
    /// each manual page / column break in it (<see cref="FindSpecialCodes.ManualPageBreak"/> /
    /// <see cref="FindSpecialCodes.ColumnBreak"/>). Returns the PlainText offset after it, and records
    /// each stretch of written text in <paramref name="inserted"/> so Replace-with formatting reaches it.
    /// </summary>
    private static int InsertPiece(
        Paragraph target,
        int offset,
        string piece,
        RunFormatting inherited,
        FindReplaceRevisionContext? tracked,
        List<(Paragraph Paragraph, int From, int To)> inserted,
        int? atRunIndex = null)
    {
        if (atRunIndex is null
            && piece.AsSpan().IndexOfAny(FindSpecialCodes.ManualPageBreak, FindSpecialCodes.ColumnBreak) < 0)
        {
            var after = tracked is null
                ? RevisionEditPlanner.InsertText(target, offset, piece, inherited)
                : RevisionEditPlanner.InsertTrackedText(target, offset, piece, inherited, tracked.Author, tracked.DateXml);
            inserted.Add((target, after - piece.Length, after));
            return after;
        }

        // The runs are built in order and inserted as one block: inserting them one at a time by offset
        // would put each text run before the zero-width break run written just ahead of it.
        var block = new List<Run>();
        var index = 0;
        while (index < piece.Length)
        {
            var next = piece.AsSpan(index).IndexOfAny(FindSpecialCodes.ManualPageBreak, FindSpecialCodes.ColumnBreak);
            var textLength = next < 0 ? piece.Length - index : next;
            Run run;
            if (textLength > 0)
            {
                run = new Run(piece.Substring(index, textLength), inherited);
                index += textLength;
            }
            else
            {
                var page = piece[index] == FindSpecialCodes.ManualPageBreak;
                run = new Run(string.Empty, inherited) { IsPageBreak = page, IsColumnBreak = !page };
                index++;
            }

            if (tracked is not null)
            {
                run.Revision = RevisionKind.Inserted;
                run.RevisionAuthor = tracked.Author;
                run.RevisionDateXml = tracked.DateXml;
            }

            block.Add(run);
        }

        int cursor;
        if (atRunIndex is { } runIndex)
        {
            // The match consumed objects that sat at this offset: the written runs take the first consumed
            // object's place among the runs, so the objects that stay keep their order around them. The
            // PlainText offset is the same wherever among zero-width runs they land.
            target.Runs.InsertRange(Math.Clamp(runIndex, 0, target.Runs.Count), block);
            cursor = Math.Clamp(offset, 0, target.PlainText.Length - block.Sum(run => run.Text.Length));
        }
        else
        {
            cursor = RevisionEditPlanner.InsertRunAtOffset(target, offset, block[0]);
            var at = 0;
            while (at < target.Runs.Count && !ReferenceEquals(target.Runs[at], block[0]))
                at++;
            for (var i = 1; i < block.Count; i++)
                target.Runs.Insert(at + i, block[i]);
        }

        foreach (var run in block)
        {
            if (run.Text.Length == 0)
                continue;
            inserted.Add((target, cursor, cursor + run.Text.Length));
            cursor += run.Text.Length;
        }

        return cursor;
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
/// one in which every block that had a match is an edited deep clone, and undo swaps back the list
/// that was there.
/// </summary>
/// <remarks>
/// The list to restore is captured when the command is APPLIED, not when it is planned. The plan is
/// made against one document state, but undo has to return whatever the document held immediately
/// before this command ran -- on a redo the body may have been re-read from the editing surface
/// into new block objects since. The planned originals are never mutated (edits are made on
/// clones), so restoring the captured list restores the document exactly.
/// </remarks>
public sealed class FindReplaceBodyReplaceAllCommand(
    IReadOnlyList<Block> after,
    int replacedCount,
    string label = "Replace All") : IDocumentCommand
{
    private List<Block>? _previous;

    public string Label => label;

    /// <summary>How many body matches the command replaces.</summary>
    public int ReplacedCount => replacedCount;

    public bool HasEffect(IDocumentCommandContext context) => replacedCount > 0;

    public void Apply(IDocumentCommandContext context)
    {
        _previous = [.. context.Document.Blocks];
        Set(context.Document, after);
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_previous is null)
            return;

        Set(context.Document, _previous);
        _previous = null;
    }

    private static void Set(TextDocument document, IReadOnlyList<Block> blocks)
    {
        document.Blocks.Clear();
        document.Blocks.AddRange(blocks);
    }
}
