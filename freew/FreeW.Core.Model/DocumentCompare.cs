using System;
using System.Collections.Generic;
using System.Linq;

namespace FreeW.Core.Model;

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
        string? dateXml = null)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(revised);
        ArgumentNullException.ThrowIfNull(author);

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
            originalParagraphs.Select(p => p.PlainText).ToList(),
            revisedParagraphs.Select(p => p.PlainText).ToList());

        var revisedAnchorToOriginal = new Dictionary<int, int>();
        foreach (var (originalIndex, revisedIndex) in matches)
            revisedAnchorToOriginal[revisedIndex] = originalIndex;

        // Drive the walk off the revised block order so non-paragraph blocks keep their place. Each revised
        // paragraph is either an anchor (identical to some original) or part of a "gap" since the previous
        // anchor; we buffer gap paragraphs and resolve them against the original gap when we hit the next
        // anchor (or the end). prevOriginalAnchor tracks how far into the original list we have consumed.
        var prevOriginalAnchor = -1; // index in originalParagraphs of the last consumed anchor
        var gapRevised = new List<Paragraph>();
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
                result.Blocks.Add(ClonePlain(revisedParagraph));
                prevOriginalAnchor = anchorOriginalIndex;
            }
            else
            {
                gapRevised.Add(revisedParagraph);
            }
        }

        // Resolve the trailing gap (everything after the last anchor) against the remaining originals.
        ResolveGap(originalParagraphs.Count);
        return result;

        // Resolve the currently-buffered revised gap paragraphs against the original paragraphs in
        // (prevOriginalAnchor, originalLimit). Paired positionally: each pair is word-diffed; surplus
        // original paragraphs become whole-paragraph deletions, surplus revised ones whole insertions.
        // Deletions are emitted before insertions so removed text reads ahead of the replacement.
        void ResolveGap(int originalLimit)
        {
            var gapOriginal = new List<Paragraph>();
            for (var i = prevOriginalAnchor + 1; i < originalLimit && i < originalParagraphs.Count; i++)
                gapOriginal.Add(originalParagraphs[i]);

            var pairCount = Math.Min(gapOriginal.Count, gapRevised.Count);
            for (var i = 0; i < pairCount; i++)
                result.Blocks.Add(DiffParagraph(gapOriginal[i], gapRevised[i], author, dateXml));

            for (var i = pairCount; i < gapOriginal.Count; i++)
                result.Blocks.Add(MarkWholeParagraph(gapOriginal[i], RevisionKind.Deleted, author, dateXml));

            for (var i = pairCount; i < gapRevised.Count; i++)
                result.Blocks.Add(MarkWholeParagraph(gapRevised[i], RevisionKind.Inserted, author, dateXml));

            prevOriginalAnchor = originalLimit - 1;
            gapRevised.Clear();
        }
    }

    // Word-level diff of two paragraphs whose text differs. Runs an LCS over whitespace-delimited tokens:
    // common tokens become ordinary runs, revised-only tokens become inserted runs, original-only tokens
    // become deleted runs. Tokens keep their trailing spacing so the reconstructed text reads naturally.
    private static Paragraph DiffParagraph(Paragraph original, Paragraph revised, string author, string? dateXml)
    {
        // Identical text: copy the revised paragraph verbatim (no revision marks at all).
        if (string.Equals(original.PlainText, revised.PlainText, StringComparison.Ordinal))
            return ClonePlain(revised);

        var result = new Paragraph
        {
            Formatting = revised.Formatting,
            StyleId = revised.StyleId,
            BookmarkName = revised.BookmarkName
        };

        var originalTokens = Tokenize(original.PlainText);
        var revisedTokens = Tokenize(revised.PlainText);
        var common = LongestCommonSubsequence(originalTokens, revisedTokens);

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
                AppendRun(result, originalTokens[oi], RevisionKind.Deleted, author, dateXml);
                oi++;
                continue;
            }

            // Then emit revised-only tokens (insertions) until we reach the next common revised token.
            if (ri < revisedTokens.Count && !commonRevised.Contains(ri))
            {
                AppendRun(result, revisedTokens[ri], RevisionKind.Inserted, author, dateXml);
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
    private static Paragraph MarkWholeParagraph(Paragraph source, RevisionKind kind, string author, string? dateXml)
    {
        var clone = new Paragraph
        {
            Formatting = source.Formatting,
            StyleId = source.StyleId,
            BookmarkName = source.BookmarkName
        };
        // An empty paragraph (no runs) still needs to register as inserted/deleted; the paragraph stays
        // empty in the result but is otherwise carried so block ordering is preserved.
        foreach (var run in source.Runs)
        {
            var copy = CloneRun(run);
            copy.Revision = kind;
            copy.RevisionAuthor = author;
            copy.RevisionDateXml = dateXml;
            clone.Runs.Add(copy);
        }
        return clone;
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
        target.Page.PageBorder = source.Page.PageBorder;
        target.Page.Watermark = source.Page.Watermark;
    }

    // Clone a paragraph with its runs verbatim and no revision marks (used for unchanged paragraphs).
    private static Paragraph ClonePlain(Paragraph source)
    {
        var clone = new Paragraph
        {
            Formatting = source.Formatting,
            StyleId = source.StyleId,
            BookmarkName = source.BookmarkName
        };
        foreach (var run in source.Runs)
            clone.Runs.Add(CloneRun(run));
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
        var clone = new Table { Formatting = source.Formatting };
        clone.ColumnWidthsPt.AddRange(source.ColumnWidthsPt);
        foreach (var row in source.Rows)
        {
            var rowClone = new TableRow();
            foreach (var cell in row.Cells)
            {
                var cellClone = new TableCell
                {
                    ShadingColorHex = cell.ShadingColorHex,
                    WidthPt = cell.WidthPt
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
        FieldKind = source.FieldKind,
        FootnoteId = source.FootnoteId,
        CommentId = source.CommentId,
        IsCommentReference = source.IsCommentReference,
        Control = source.Control
    };
}
