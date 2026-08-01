using System;
using System.Collections.Generic;
using System.Linq;

namespace FreeW.Core.Model;

/// <summary>
/// Which document the comparison result is shown in — Word's "Show changes in:" radio-button group.
/// FreeW always produces a new result document (the compare engine never mutates its inputs), so
/// <see cref="NewDocument"/> is the only substantively different option; the other two are kept for
/// round-trip fidelity so the dialog setting can be persisted.
/// </summary>
public enum CompareShowChangesIn
{
    /// <summary>Show the blackline result in a new, separate document (FreeW's only real option).</summary>
    NewDocument = 0,
    /// <summary>Word also supports loading the result back into the original document.</summary>
    Original = 1,
    /// <summary>Word also supports loading the result back into the revised document.</summary>
    Revised = 2
}

/// <summary>
/// Configuration options for <see cref="DocumentCompare.Compare"/> that mirror Word's "Comparison Settings"
/// expansion in the Compare Documents dialog. Every flag defaults to <c>true</c> (on), matching Word's
/// default — all change types are tracked. When a flag is <c>false</c> the corresponding kind of
/// difference is silently excluded from the output (no revision marks for it).
/// <para>
/// <see cref="Insertions"/>, <see cref="Deletions"/>, <see cref="CaseChanges"/>,
/// <see cref="Whitespace"/>, and <see cref="Formatting"/> affect FreeW's current comparison engine.
/// Formatting revisions are emitted when a paragraph's text and run boundaries are unchanged, so each
/// revised run can retain a precise previous-format snapshot. Unique unchanged paragraphs moved to a new
/// position receive paired move revisions. <see cref="Comments"/> preserves the original threads that
/// remain anchored in deleted whole-paragraph content, while allowing that deleted-side markup to be
/// omitted when comment comparison is disabled.
/// </para>
/// </summary>
public sealed class CompareSettings
{
    /// <summary>Track inserted text. Default: <c>true</c>.</summary>
    public bool Insertions { get; init; } = true;

    /// <summary>Track deleted text. Default: <c>true</c>.</summary>
    public bool Deletions { get; init; } = true;

    /// <summary>
    /// Track unique unchanged paragraphs that moved to a new position. Ambiguous or edited paragraphs
    /// remain ordinary deletion/insertion pairs. Default: <c>true</c>.
    /// </summary>
    public bool Moves { get; init; } = true;

    /// <summary>
    /// Preserve review-comment threads anchored in deleted original paragraphs. When disabled, removes
    /// only those deleted-side anchors; revised comments remain part of the comparison result. Default:
    /// <c>true</c>.
    /// </summary>
    public bool Comments { get; init; } = true;

    /// <summary>
    /// Track format-only changes in text-identical paragraphs. FreeW emits native run-format revisions
    /// when corresponding runs retain the same text boundaries. Default: <c>true</c>.
    /// </summary>
    public bool Formatting { get; init; } = true;

    /// <summary>Track case changes as differences. Default: <c>true</c>.</summary>
    public bool CaseChanges { get; init; } = true;

    /// <summary>Track whitespace changes as differences. Default: <c>true</c>.</summary>
    public bool Whitespace { get; init; } = true;

    /// <summary>Which document to show the result in. Default: <see cref="CompareShowChangesIn.NewDocument"/>.</summary>
    public CompareShowChangesIn ShowChangesIn { get; init; } = CompareShowChangesIn.NewDocument;

    /// <summary>The default settings — all change types enabled, result in a new document.</summary>
    public static readonly CompareSettings Default = new();
}

/// <summary>
/// Pure, WPF-free document comparison ("Compare Documents"). Diffs an <c>original</c> against a
/// <c>revised</c> document and produces a NEW document representing <c>revised</c> with the differences
/// marked as tracked changes (see <see cref="Run.Revision"/>): text only in revised is marked
/// <see cref="RevisionKind.Inserted"/>, text only in original is marked <see cref="RevisionKind.Deleted"/>
/// (kept in the result so it renders struck-through), and unchanged text stays an ordinary run.
///
/// Granularity is two-level and deterministic: a paragraph-level LCS matches paragraphs by their plain
/// text, then each paired-but-changed paragraph is diffed at word granularity with a second LCS so only
/// the changed words carry insertion/deletion marks. The author is stamped onto every produced revision;
/// the date is supplied by the caller (never <c>DateTime.Now</c>) so the helper stays pure/deterministic.
/// </summary>
public static class DocumentCompare
{
    /// <summary>
    /// Compare <paramref name="original"/> against <paramref name="revised"/> and return a new document
    /// that is <paramref name="revised"/> with the differences expressed as tracked changes attributed to
    /// <paramref name="author"/>. <paramref name="dateXml"/> is the W3CDTF revision timestamp to stamp on
    /// every produced revision (pass null to leave the date unset); it is never auto-generated here.
    /// Only paragraph blocks are compared at word granularity; non-paragraph blocks (e.g. tables) in
    /// <paramref name="revised"/> are carried through unchanged.
    /// </summary>
    public static TextDocument Compare(
        TextDocument original,
        TextDocument revised,
        string author,
        string? dateXml = null) => Compare(original, revised, author, dateXml, CompareSettings.Default);

    /// <summary>
    /// Compare <paramref name="original"/> against <paramref name="revised"/> with the given
    /// <paramref name="settings"/> (which change types to track). <paramref name="settings"/> with
    /// <see cref="CompareSettings.Insertions"/> and/or <see cref="CompareSettings.Deletions"/> false will
    /// suppress the corresponding revision marks in the output. Disabling
    /// <see cref="CompareSettings.CaseChanges"/> and/or <see cref="CompareSettings.Whitespace"/> ignores only
    /// those differences while preserving revised text in the result. When <see cref="CompareSettings.Formatting"/>
    /// is enabled, format-only changes in text-identical paragraphs become native run-format revisions.
    /// When <see cref="CompareSettings.Moves"/> is enabled, unique unchanged paragraphs moved to a new
    /// position receive paired Word move revisions. When <see cref="CompareSettings.Comments"/> is enabled,
    /// comment threads anchored in deleted whole-paragraph content are retained; disabling it removes only
    /// those deleted-side anchors while retaining the revised document's comments.
    /// </summary>
    public static TextDocument Compare(
        TextDocument original,
        TextDocument revised,
        string author,
        string? dateXml,
        CompareSettings settings)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(revised);
        ArgumentNullException.ThrowIfNull(author);
        ArgumentNullException.ThrowIfNull(settings);

        var result = new TextDocument();
        // Carry over the revised document's defaults, styles and page setup so the result renders like it.
        CopyDocumentShell(revised, result);

        var originalParagraphs = original.Blocks.OfType<Paragraph>().ToList();
        var revisedBlocks = revised.Blocks;
        var revisedParagraphs = revisedBlocks.OfType<Paragraph>().ToList();

        // Paragraph-level LCS over plain text picks the "anchors": revised paragraphs whose text is exactly
        // an original paragraph's. Anchors copy through unchanged; the unmatched paragraphs that fall in the
        // gaps between anchors are paired up (and word-diffed) or, when unpaired, marked whole-insert/delete.
        var matches = LongestCommonSubsequence(
            originalParagraphs.Select(p => ComparisonKey(p.PlainText, settings)).ToList(),
            revisedParagraphs.Select(p => ComparisonKey(p.PlainText, settings)).ToList());

        var revisedAnchorToOriginal = new Dictionary<int, int>();
        foreach (var (originalIndex, revisedIndex) in matches)
            revisedAnchorToOriginal[revisedIndex] = originalIndex;

        var moveIds = FindWholeParagraphMoves(originalParagraphs, revisedParagraphs, matches, settings);

        // Drive the walk off the revised block order so non-paragraph blocks keep their place. Each revised
        // paragraph is either an anchor (identical to some original) or part of a "gap" since the previous
        // anchor; we buffer gap paragraphs and resolve them against the original gap when we hit the next
        // anchor (or the end). prevOriginalAnchor tracks how far into the original list we have consumed.
        var prevOriginalAnchor = -1; // index in originalParagraphs of the last consumed anchor
        var gapRevised = new List<(Paragraph Paragraph, int Index)>();
        var revisedParagraphOrdinal = 0;

        foreach (var block in revisedBlocks)
        {
            if (block is not Paragraph revisedParagraph)
            {
                // A non-paragraph block ends the current gap region: resolve buffered paragraphs up to the
                // end of the original list, then clone the block through in place.
                ResolveGap(originalParagraphs.Count);
                result.Blocks.Add(CloneBlock(block));
                continue;
            }

            var revisedIndex = revisedParagraphOrdinal++;
            if (revisedAnchorToOriginal.TryGetValue(revisedIndex, out var anchorOriginalIndex))
            {
                // Resolve the gap that precedes this anchor against the original paragraphs sitting between
                // the previous anchor and this one, then copy the anchor (identical text) through unchanged.
                ResolveGap(anchorOriginalIndex);
                result.Blocks.Add(ClonePlainWithFormatRevisions(
                    originalParagraphs[anchorOriginalIndex],
                    revisedParagraph,
                    author,
                    dateXml,
                    settings));
                prevOriginalAnchor = anchorOriginalIndex;
            }
            else
            {
                gapRevised.Add((revisedParagraph, revisedIndex));
            }
        }

        // Resolve the trailing gap (everything after the last anchor) against the remaining originals.
        ResolveGap(originalParagraphs.Count);

        // Whole-paragraph deletions retain the original runs verbatim, including comment anchors. Carry
        // the matching original comment threads so those anchors remain valid when the comparison is saved.
        // If comment comparison is disabled, drop only the deleted-side markers; revised comments continue
        // to describe the resulting document.
        ReconcileDeletedCommentAnchors(original, result, settings.Comments);
        return result;

        // Resolve the currently-buffered revised gap paragraphs against the original paragraphs in
        // (prevOriginalAnchor, originalLimit). Paired positionally: each pair is word-diffed; surplus
        // original paragraphs become whole-paragraph deletions, surplus revised ones whole insertions.
        // Deletions are emitted before insertions so removed text reads ahead of the replacement.
        // When settings.Deletions is false, surplus original paragraphs are dropped (not carried as deletions).
        // When settings.Insertions is false, surplus revised paragraphs are copied through unmarked.
        void ResolveGap(int originalLimit)
        {
            var gapOriginal = new List<(Paragraph Paragraph, int Index)>();
            for (var i = prevOriginalAnchor + 1; i < originalLimit && i < originalParagraphs.Count; i++)
                gapOriginal.Add((originalParagraphs[i], i));

            var pairCount = Math.Min(gapOriginal.Count, gapRevised.Count);
            for (var i = 0; i < pairCount; i++)
            {
                var originalEntry = gapOriginal[i];
                var revisedEntry = gapRevised[i];
                var originalMoveId = moveIds.GetOriginalId(originalEntry.Index);
                var revisedMoveId = moveIds.GetRevisedId(revisedEntry.Index);
                if (originalMoveId is null && revisedMoveId is null)
                {
                    result.Blocks.Add(DiffParagraph(
                        originalEntry.Paragraph,
                        revisedEntry.Paragraph,
                        author,
                        dateXml,
                        settings));
                    continue;
                }

                if (settings.Deletions)
                    result.Blocks.Add(MarkWholeParagraph(
                        originalEntry.Paragraph,
                        RevisionKind.Deleted,
                        author,
                        dateXml,
                        originalMoveId));
                if (settings.Insertions)
                    result.Blocks.Add(MarkWholeParagraph(
                        revisedEntry.Paragraph,
                        RevisionKind.Inserted,
                        author,
                        dateXml,
                        revisedMoveId));
            }

            for (var i = pairCount; i < gapOriginal.Count; i++)
            {
                if (settings.Deletions)
                    result.Blocks.Add(MarkWholeParagraph(
                        gapOriginal[i].Paragraph,
                        RevisionKind.Deleted,
                        author,
                        dateXml,
                        moveIds.GetOriginalId(gapOriginal[i].Index)));
                // When deletions are suppressed, the original-only paragraph is simply dropped.
            }

            for (var i = pairCount; i < gapRevised.Count; i++)
            {
                if (settings.Insertions)
                    result.Blocks.Add(MarkWholeParagraph(
                        gapRevised[i].Paragraph,
                        RevisionKind.Inserted,
                        author,
                        dateXml,
                        moveIds.GetRevisedId(gapRevised[i].Index)));
                else
                    result.Blocks.Add(ClonePlain(gapRevised[i].Paragraph)); // carry through unmarked
            }

            prevOriginalAnchor = originalLimit - 1;
            gapRevised.Clear();
        }
    }

    // Word-level diff of two paragraphs whose text differs. Runs an LCS over whitespace-delimited tokens:
    // common tokens become ordinary runs, revised-only tokens become inserted runs, original-only tokens
    // become deleted runs. Tokens keep their trailing spacing so the reconstructed text reads naturally.
    // settings.Insertions/Deletions gate whether those revision kinds appear in the output.
    private static Paragraph DiffParagraph(Paragraph original, Paragraph revised, string author, string? dateXml, CompareSettings settings)
    {
        // Text that differs only in disabled comparison categories copies through verbatim.
        if (string.Equals(
                ComparisonKey(original.PlainText, settings),
                ComparisonKey(revised.PlainText, settings),
                StringComparison.Ordinal))
            return ClonePlain(revised);

        var result = new Paragraph
        {
            BlockContentControl = revised.BlockContentControl,
            BlockCustomXml = revised.BlockCustomXml,
            Formatting = revised.Formatting,
            StyleId = revised.StyleId,
            DropCap = revised.DropCap,
        };
        result.BookmarkNames.AddRange(revised.BookmarkNames);

        var useExactTokens = settings.CaseChanges && settings.Whitespace;
        var originalTokens = useExactTokens
            ? Tokenize(original.PlainText)
            : TokenizeComparisonSegments(original.PlainText);
        var revisedTokens = useExactTokens
            ? Tokenize(revised.PlainText)
            : TokenizeComparisonSegments(revised.PlainText);
        var common = LongestCommonSubsequence(
            originalTokens.Select(token => ComparisonKey(token, settings)).ToList(),
            revisedTokens.Select(token => ComparisonKey(token, settings)).ToList());

        var commonOriginal = new HashSet<int>(common.Select(m => m.OriginalIndex));
        var commonRevised = new HashSet<int>(common.Select(m => m.RevisedIndex));

        var oi = 0;
        var ri = 0;
        var nextMatch = 0;
        while (oi < originalTokens.Count || ri < revisedTokens.Count)
        {
            // At the next aligned common token, both cursors are on a match: emit it as ordinary text.
            if (nextMatch < common.Count
                && oi == common[nextMatch].OriginalIndex
                && ri == common[nextMatch].RevisedIndex)
            {
                AppendRun(result, revisedTokens[ri], RevisionKind.None, author, dateXml);
                oi++;
                ri++;
                nextMatch++;
                continue;
            }

            // Emit original-only tokens (deletions) until we reach the next common original token.
            if (oi < originalTokens.Count && !commonOriginal.Contains(oi))
            {
                // When deletions are suppressed, skip (do not emit the deleted token at all).
                if (settings.Deletions)
                    AppendRun(result, originalTokens[oi], RevisionKind.Deleted, author, dateXml);
                oi++;
                continue;
            }

            // Then emit revised-only tokens (insertions) until we reach the next common revised token.
            if (ri < revisedTokens.Count && !commonRevised.Contains(ri))
            {
                // When insertions are suppressed, emit the token as plain text (no revision mark).
                var kind = settings.Insertions ? RevisionKind.Inserted : RevisionKind.None;
                AppendRun(result, revisedTokens[ri], kind, author, dateXml);
                ri++;
                continue;
            }

            // Defensive: if one side is exhausted but the other still has a "common" token not yet aligned
            // (can't normally happen), advance the lagging cursor so the loop always terminates.
            if (oi < originalTokens.Count)
                oi++;
            else if (ri < revisedTokens.Count)
                ri++;
        }

        BookmarkBoundaryMapper.CopyMapped(
            revised,
            result,
            static run => run.Revision != RevisionKind.Deleted);
        return result;
    }

    // Append one token as a run with the given revision kind. None-kind runs carry no revision metadata.
    private static void AppendRun(Paragraph paragraph, string token, RevisionKind kind, string author, string? dateXml)
    {
        if (token.Length == 0)
            return;
        var run = new Run(token);
        if (kind != RevisionKind.None)
        {
            run.Revision = kind;
            run.RevisionAuthor = author;
            run.RevisionDateXml = dateXml;
        }
        paragraph.Runs.Add(run);
    }

    // Produce a copy of a paragraph with every run marked with one revision kind (whole-paragraph
    // insertion or deletion). Run text/formatting is preserved; the revision metadata is stamped on.
    private static Paragraph MarkWholeParagraph(
        Paragraph source,
        RevisionKind kind,
        string author,
        string? dateXml,
        int? moveRevisionId = null)
    {
        var clone = new Paragraph
        {
            BlockContentControl = source.BlockContentControl,
            BlockCustomXml = source.BlockCustomXml,
            Formatting = source.Formatting,
            StyleId = source.StyleId,
            DropCap = source.DropCap,
        };
        clone.BookmarkNames.AddRange(source.BookmarkNames);
        clone.BookmarkBoundaries.AddRange(source.BookmarkBoundaries);
        // An empty paragraph (no runs) still needs to register as inserted/deleted; the paragraph stays
        // empty in the result but is otherwise carried so block ordering is preserved.
        foreach (var run in source.Runs)
        {
            var copy = CloneRun(run);
            copy.Revision = kind;
            copy.RevisionAuthor = author;
            copy.RevisionDateXml = dateXml;
            copy.MoveRevisionId = moveRevisionId;
            clone.Runs.Add(copy);
        }
        return clone;
    }

    // Word can recognize complex edits as moves. This bounded pass intentionally marks only exact,
    // unique paragraph content outside the LCS anchors; duplicates and edited paragraphs retain the
    // ordinary insertion/deletion behavior rather than risking false move attribution.
    private static WholeParagraphMoveIds FindWholeParagraphMoves(
        IReadOnlyList<Paragraph> original,
        IReadOnlyList<Paragraph> revised,
        IReadOnlyList<(int OriginalIndex, int RevisedIndex)> anchors,
        CompareSettings settings)
    {
        var result = new WholeParagraphMoveIds();
        if (!settings.Moves || !settings.Insertions || !settings.Deletions)
            return result;

        var anchoredOriginal = anchors.Select(anchor => anchor.OriginalIndex).ToHashSet();
        var anchoredRevised = anchors.Select(anchor => anchor.RevisedIndex).ToHashSet();
        var originalByText = original
            .Select((paragraph, index) => (Text: paragraph.PlainText, Index: index))
            .GroupBy(entry => entry.Text, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(entry => entry.Index).ToList(), StringComparer.Ordinal);
        var revisedByText = revised
            .Select((paragraph, index) => (Text: paragraph.PlainText, Index: index))
            .GroupBy(entry => entry.Text, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(entry => entry.Index).ToList(), StringComparer.Ordinal);

        var nextMoveId = 1;
        for (var originalIndex = 0; originalIndex < original.Count; originalIndex++)
        {
            if (anchoredOriginal.Contains(originalIndex))
                continue;

            var text = original[originalIndex].PlainText;
            if (text.Length == 0
                || !originalByText.TryGetValue(text, out var originalMatches)
                || originalMatches.Count != 1
                || !revisedByText.TryGetValue(text, out var revisedMatches)
                || revisedMatches.Count != 1
                || anchoredRevised.Contains(revisedMatches[0]))
                continue;

            result.Add(originalIndex, revisedMatches[0], nextMoveId++);
        }

        return result;
    }

    private sealed class WholeParagraphMoveIds
    {
        private readonly Dictionary<int, int> _original = [];
        private readonly Dictionary<int, int> _revised = [];

        public void Add(int originalIndex, int revisedIndex, int moveId)
        {
            _original[originalIndex] = moveId;
            _revised[revisedIndex] = moveId;
        }

        public int? GetOriginalId(int index) => _original.TryGetValue(index, out var id) ? id : null;

        public int? GetRevisedId(int index) => _revised.TryGetValue(index, out var id) ? id : null;
    }

    // Split text into tokens that each keep their trailing whitespace, so concatenating the tokens
    // reproduces the original text exactly. A run of whitespace attaches to the preceding word token.
    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var i = 0;
        while (i < text.Length)
        {
            var start = i;
            // Consume the word characters (non-whitespace).
            while (i < text.Length && !char.IsWhiteSpace(text[i]))
                i++;
            // Then consume the trailing whitespace so it travels with the word.
            while (i < text.Length && char.IsWhiteSpace(text[i]))
                i++;
            tokens.Add(text.Substring(start, i - start));
        }
        return tokens;
    }

    private static List<string> TokenizeComparisonSegments(string text)
    {
        var tokens = new List<string>();
        var index = 0;
        while (index < text.Length)
        {
            var isWhitespace = char.IsWhiteSpace(text[index]);
            var start = index;
            while (index < text.Length && char.IsWhiteSpace(text[index]) == isWhitespace)
                index++;
            tokens.Add(text[start..index]);
        }

        return tokens;
    }

    private static string ComparisonKey(string text, CompareSettings settings)
    {
        if (!settings.Whitespace && text.All(char.IsWhiteSpace))
            text = " ";
        else if (!settings.Whitespace)
        {
            var normalizedWhitespace = new System.Text.StringBuilder(text.Length);
            var previousWasWhitespace = false;
            foreach (var character in text)
            {
                if (char.IsWhiteSpace(character))
                {
                    if (!previousWasWhitespace)
                        normalizedWhitespace.Append(' ');
                    previousWasWhitespace = true;
                }
                else
                {
                    normalizedWhitespace.Append(character);
                    previousWasWhitespace = false;
                }
            }

            text = normalizedWhitespace.ToString();
        }

        return settings.CaseChanges ? text : text.ToUpperInvariant();
    }

    // Classic dynamic-programming LCS returning the matched index pairs (OriginalIndex, RevisedIndex) in
    // increasing order. Deterministic: ties prefer keeping the left (original) sequence's element.
    private static List<(int OriginalIndex, int RevisedIndex)> LongestCommonSubsequence(
        IReadOnlyList<string> left,
        IReadOnlyList<string> right)
    {
        var n = left.Count;
        var m = right.Count;
        var lengths = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
        {
            for (var j = m - 1; j >= 0; j--)
            {
                lengths[i, j] = string.Equals(left[i], right[j], StringComparison.Ordinal)
                    ? lengths[i + 1, j + 1] + 1
                    : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
            }
        }

        var matches = new List<(int, int)>();
        var x = 0;
        var y = 0;
        while (x < n && y < m)
        {
            if (string.Equals(left[x], right[y], StringComparison.Ordinal))
            {
                matches.Add((x, y));
                x++;
                y++;
            }
            else if (lengths[x + 1, y] >= lengths[x, y + 1])
            {
                x++;
            }
            else
            {
                y++;
            }
        }
        return matches;
    }

    // Copy a document's defaults, style catalog and page geometry so the comparison result renders like
    // the revised document. Body blocks are added by the caller; this only seeds the surrounding shell.
    private static void CopyDocumentShell(TextDocument source, TextDocument target)
    {
        target.DefaultRun = source.DefaultRun;
        target.DefaultParagraph = source.DefaultParagraph;
        foreach (var (id, style) in source.Styles)
            target.Styles[id] = style;

        target.Page.WidthPt = source.Page.WidthPt;
        target.Page.HeightPt = source.Page.HeightPt;
        target.Page.MarginLeftPt = source.Page.MarginLeftPt;
        target.Page.MarginRightPt = source.Page.MarginRightPt;
        target.Page.MarginTopPt = source.Page.MarginTopPt;
        target.Page.MarginBottomPt = source.Page.MarginBottomPt;
        target.Page.Landscape = source.Page.Landscape;
        target.Page.ColumnCount = source.Page.ColumnCount;
        target.Page.ColumnSpacingPt = source.Page.ColumnSpacingPt;
        target.Page.ColumnsLineBetween = source.Page.ColumnsLineBetween;
        target.Page.ColumnWidthsPt = source.Page.ColumnWidthsPt is null ? null : new List<double>(source.Page.ColumnWidthsPt);
        target.Page.PageBorder = source.Page.PageBorder;
        target.Page.Watermark = source.Page.Watermark;
        target.Page.LineNumberMode = source.Page.LineNumberMode;
        target.Page.LineNumberCountBy = source.Page.LineNumberCountBy;
        target.Page.LineNumberStartAt = source.Page.LineNumberStartAt;
        target.Page.AutoHyphenation = source.Page.AutoHyphenation;
        target.Page.VerticalAlignment = source.Page.VerticalAlignment;
        target.Page.DifferentFirstPage = source.Page.DifferentFirstPage;
        foreach (var (id, comment) in source.Comments)
            target.Comments[id] = CloneComment(comment);
        target.Preserved.CopyFrom(source.Preserved);
    }

    // Compared body runs retain their comment ids. Carry the revised document's comment graph as owned
    // model objects as well, otherwise a saved compare result would emit orphaned comment anchors.
    private static Comment CloneComment(Comment source)
        => CloneComment(source, static id => id);

    private static Comment CloneComment(Comment source, Func<int, int> mapId)
    {
        var clone = new Comment(mapId(source.Id))
        {
            Author = source.Author,
            Initials = source.Initials,
            DateXml = source.DateXml,
            Resolved = source.Resolved,
        };
        foreach (var paragraph in source.Content)
            clone.Content.Add(ClonePlain(paragraph));
        foreach (var reply in source.Replies)
            clone.Replies.Add(CloneComment(reply, mapId));
        return clone;
    }

    private static void ReconcileDeletedCommentAnchors(
        TextDocument original,
        TextDocument result,
        bool includeDeletedComments)
    {
        var deletedCommentIds = EnumerateParagraphs(result.Blocks)
            .SelectMany(paragraph => paragraph.Runs)
            .Where(run => run.Revision == RevisionKind.Deleted && run.CommentId is not null)
            .Select(run => run.CommentId!.Value)
            .Distinct()
            .ToList();

        if (!includeDeletedComments)
        {
            foreach (var commentId in deletedCommentIds)
                RemoveDeletedCommentMarkers(result, commentId);
            return;
        }

        var usedIds = result.Comments.Values
            .SelectMany(comment => comment.ThreadInOrder())
            .Select(comment => comment.Id)
            .ToHashSet();

        foreach (var originalId in deletedCommentIds)
        {
            if (!original.Comments.TryGetValue(originalId, out var originalComment))
            {
                // Do not leave an invalid anchor behind when the source document itself lacks its thread.
                RemoveDeletedCommentMarkers(result, originalId);
                continue;
            }

            var idMap = new Dictionary<int, int>();
            foreach (var comment in originalComment.ThreadInOrder())
            {
                var id = comment.Id;
                if (!usedIds.Add(id))
                    id = NextUnusedCommentId(usedIds);
                idMap[comment.Id] = id;
            }

            var copiedId = idMap[originalId];
            result.Comments[copiedId] = CloneComment(originalComment, id => idMap[id]);
            if (copiedId != originalId)
                RemapDeletedCommentMarkers(result, originalId, copiedId);
        }
    }

    private static int NextUnusedCommentId(HashSet<int> usedIds)
    {
        var id = usedIds.Count == 0 ? 0 : usedIds.Max() + 1;
        while (!usedIds.Add(id))
            id++;
        return id;
    }

    private static void RemapDeletedCommentMarkers(TextDocument document, int oldId, int newId)
    {
        foreach (var paragraph in EnumerateParagraphs(document.Blocks))
        foreach (var run in paragraph.Runs)
            if (run.Revision == RevisionKind.Deleted && run.CommentId == oldId)
                run.CommentId = newId;
    }

    private static void RemoveDeletedCommentMarkers(TextDocument document, int commentId)
    {
        foreach (var paragraph in EnumerateParagraphs(document.Blocks))
        {
            for (var index = paragraph.Runs.Count - 1; index >= 0; index--)
            {
                var run = paragraph.Runs[index];
                if (run.Revision != RevisionKind.Deleted || run.CommentId != commentId)
                    continue;

                if (run.IsCommentReference)
                    paragraph.Runs.RemoveAt(index);
                else
                    run.CommentId = null;
            }
        }
    }

    private static IEnumerable<Paragraph> EnumerateParagraphs(IEnumerable<Block> blocks)
    {
        foreach (var block in blocks)
        {
            if (block is Paragraph paragraph)
            {
                yield return paragraph;
                continue;
            }

            if (block is not Table table)
                continue;

            foreach (var cell in table.Rows.SelectMany(row => row.Cells))
            foreach (var cellParagraph in cell.Paragraphs)
                yield return cellParagraph;
        }
    }

    // Clone a paragraph with its runs verbatim and no revision marks (used for unchanged paragraphs).
    private static Paragraph ClonePlain(Paragraph source)
    {
        var clone = new Paragraph
        {
            BlockContentControl = source.BlockContentControl,
            BlockCustomXml = source.BlockCustomXml,
            Formatting = source.Formatting,
            StyleId = source.StyleId,
            DropCap = source.DropCap,
        };
        clone.BookmarkNames.AddRange(source.BookmarkNames);
        clone.BookmarkBoundaries.AddRange(source.BookmarkBoundaries);
        foreach (var run in source.Runs)
            clone.Runs.Add(CloneRun(run));
        return clone;
    }

    // A paragraph-level LCS anchor has matching comparison text. When the source text and run boundaries
    // are also exact, preserve the revised appearance and mark only format differences with w:rPrChange.
    // Mixed text-and-formatting edits stay on the word-diff path, where there is no unambiguous source run
    // snapshot for a formatting revision.
    private static Paragraph ClonePlainWithFormatRevisions(
        Paragraph original,
        Paragraph revised,
        string author,
        string? dateXml,
        CompareSettings settings)
    {
        var clone = ClonePlain(revised);
        if (!settings.Formatting
            || !string.Equals(original.PlainText, revised.PlainText, StringComparison.Ordinal)
            || original.Runs.Count != revised.Runs.Count)
            return clone;

        for (var index = 0; index < revised.Runs.Count; index++)
        {
            var originalRun = original.Runs[index];
            var revisedRun = revised.Runs[index];
            if (originalRun.Text.Length == 0
                || !string.Equals(originalRun.Text, revisedRun.Text, StringComparison.Ordinal)
                || Equals(originalRun.Formatting, revisedRun.Formatting))
                continue;

            clone.Runs[index].FormatRevision = new FormatRevision(
                originalRun.Formatting,
                author,
                dateXml);
        }

        return clone;
    }

    // Shallow-clone a non-paragraph block (e.g. a table) so the result owns its own block instances.
    private static Block CloneBlock(Block block) => block switch
    {
        Paragraph paragraph => ClonePlain(paragraph),
        Table table => CloneTable(table),
        _ => block
    };

    private static Table CloneTable(Table source)
    {
        var clone = new Table
        {
            BlockContentControl = source.BlockContentControl,
            BlockCustomXml = source.BlockCustomXml,
            Formatting = source.Formatting,
            Borders = source.Borders
        };
        clone.ColumnWidthsPt.AddRange(source.ColumnWidthsPt);
        foreach (var row in source.Rows)
        {
            var rowClone = new TableRow();
            foreach (var cell in row.Cells)
            {
                var cellClone = new TableCell
                {
                    ShadingColorHex = cell.ShadingColorHex,
                    WidthPt = cell.WidthPt,
                    GridSpan = cell.GridSpan,
                    VerticalMerge = cell.VerticalMerge
                };
                foreach (var paragraph in cell.Paragraphs)
                    cellClone.Paragraphs.Add(ClonePlain(paragraph));
                rowClone.Cells.Add(cellClone);
            }
            clone.Rows.Add(rowClone);
        }
        return clone;
    }

    // Copy a run's content and marks (preserving formatting, images, links, fields, controls, etc.) while
    // dropping any pre-existing revision metadata — the compare result assigns its own revision marks.
    private static Run CloneRun(Run source) => new(source.Text, source.Formatting)
    {
        Image = source.Image,
        HyperlinkUrl = source.HyperlinkUrl,
        HyperlinkAnchor = source.HyperlinkAnchor,
        HyperlinkTooltip = source.HyperlinkTooltip,
        SubDocument = source.SubDocument,
        FieldKind = source.FieldKind,
        FootnoteId = source.FootnoteId,
        EndnoteId = source.EndnoteId,
        CommentId = source.CommentId,
        IsCommentReference = source.IsCommentReference,
        IsPageBreak = source.IsPageBreak,
        IsColumnBreak = source.IsColumnBreak,
        Control = source.Control,
        Citation = source.Citation,
        CrossReference = source.CrossReference,
        ComplexField = source.ComplexField // immutable record — safe to share
    };
}
