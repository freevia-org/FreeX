using System;
using System.Collections.Generic;
using System.Linq;

namespace FreeW.Core.Model;

/// <summary>
/// Pure, WPF-free "Combine Documents" (Word's Review › Compare › Combine): merge the revisions of two
/// reviewers — <c>revisedA</c> (author <c>authorA</c>) and <c>revisedB</c> (author <c>authorB</c>) — that
/// were both edited from the same <c>original</c> base, into ONE document whose differences are expressed
/// as tracked changes (<see cref="Run.Revision"/>) <em>preserving each reviewer's own authorship</em>: a
/// word A changed is attributed to <c>authorA</c>, a word B changed to <c>authorB</c>. The result opens
/// with full markup and can be Accepted/Rejected per author, exactly like a real combined document.
///
/// The combine is built by reusing the existing legal-blackline engine: <see cref="DocumentCompare"/>
/// produces base→A (authored to A); that blackline already carries A's text as ordinary+inserted runs, so
/// comparing A's text against B and layering B's edits (authored to B) on top yields a single document that
/// holds <em>both</em> authors' tracked insertions and deletions. Deterministic and input-non-mutating;
/// the <c>dateXml</c> revision timestamp is supplied by the caller (never <see cref="DateTime.Now"/>).
/// </summary>
public static class DocumentCombine
{
    /// <summary>
    /// Combine the two reviewers' edits of a shared <paramref name="original"/> base into one document
    /// carrying both authors' tracked changes. <paramref name="revisedA"/>/<paramref name="authorA"/> and
    /// <paramref name="revisedB"/>/<paramref name="authorB"/> are the two reviewed copies and their authors.
    /// <paramref name="dateXml"/> is the W3CDTF timestamp stamped on every produced revision (null to leave
    /// the date unset). The result renders like <paramref name="revisedA"/>'s shell (defaults/styles/page).
    /// </summary>
    public static TextDocument Combine(
        TextDocument original,
        TextDocument revisedA,
        string authorA,
        TextDocument revisedB,
        string authorB,
        string? dateXml = null)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(revisedA);
        ArgumentNullException.ThrowIfNull(authorA);
        ArgumentNullException.ThrowIfNull(revisedB);
        ArgumentNullException.ThrowIfNull(authorB);

        // Step 1: base→A blackline. This carries A's revisions attributed to authorA; accepting it would
        // yield exactly revisedA's text, so its "A view" (what survives an accept) equals revisedA.
        var blacklineA = DocumentCompare.Compare(original, revisedA, authorA, dateXml);

        // Step 2: layer B's edits on top. We compare A's surviving text (revisedA) against revisedB and walk
        // B's word-level changes paragraph by paragraph, re-marking only the runs B touched as authorB while
        // leaving A's existing marks intact. Because both comparisons are anchored on the same base text,
        // a paragraph A left unchanged but B changed gets B's marks; a paragraph both changed keeps A's
        // deletion/insertion runs and additionally carries B's, each under its own author.
        var blacklineB = DocumentCompare.Compare(revisedA, revisedB, authorB, dateXml);

        // The two blacklines share the same anchor text (revisedA), so they have matching ordinary-run
        // skeletons. Merge them paragraph-positionally: emit A's blackline paragraph, then splice in B's
        // insertions/deletions for the same paragraph. To keep this deterministic and simple we rebuild the
        // combined body from B's blackline (which already has the final structure: revisedA's surviving text
        // plus B's marks) and then re-overlay A's revisions onto the runs that came from revisedA.
        return MergeBlacklines(blacklineA, blacklineB, authorA, authorB, dateXml);
    }

    // Merge two blacklines that share the same surviving text (revisedA): blacklineA is base→A (carries A's
    // ins/del, accepts to revisedA) and blacklineB is A→B (carries B's ins/del, rejects to revisedA). We walk
    // their paragraphs positionally — the comparison engine emits one result paragraph per surviving (and
    // deleted) paragraph, so blacklineA and blacklineB have the same paragraph order for the revisedA spine —
    // and merge each pair so the result carries BOTH authors' tracked changes (see MergeParagraph).
    private static TextDocument MergeBlacklines(
        TextDocument blacklineA,
        TextDocument blacklineB,
        string authorA,
        string authorB,
        string? dateXml)
    {
        var result = new TextDocument();
        CopyShell(blacklineB, result);

        var aParagraphs = blacklineA.Blocks.OfType<Paragraph>().ToList();
        var bBlocks = blacklineB.Blocks;
        var bParagraphIndex = 0;

        foreach (var block in bBlocks)
        {
            if (block is not Paragraph bParagraph)
            {
                result.Blocks.Add(DocumentModelCloner.CloneBlock(block, RevisionClonePolicy.Preserve));
                continue;
            }

            var aParagraph = bParagraphIndex < aParagraphs.Count ? aParagraphs[bParagraphIndex] : null;
            bParagraphIndex++;
            result.Blocks.Add(MergeParagraph(aParagraph, bParagraph, authorA, authorB, dateXml));
        }

        return result;
    }

    // Merge one paragraph from A's blackline and the positionally-matching paragraph from B's blackline into
    // a single paragraph carrying both authors' tracked changes.
    //
    // Both blacklines share a common SPINE: the revisedA token sequence (text that survives accepting A and
    // rejecting B). In A's blackline the spine runs are Inserted (new in A) or None (in base and A), with A's
    // Deleted runs (base-only) interleaved. In B's blackline the spine runs are Deleted (B removed an A word)
    // or None (kept by B), with B's Inserted runs (new in B) interleaved. We walk both run lists in lockstep
    // over the spine: at each spine position we emit A's pending deletions, B's pending insertions, then the
    // spine token itself carrying the union of A's and B's marks for that token. This is deterministic and
    // needs no text-equality guessing — alignment is purely positional over the shared spine.
    private static Paragraph MergeParagraph(
        Paragraph? aParagraph,
        Paragraph bParagraph,
        string authorA,
        string authorB,
        string? dateXml)
    {
        var merged = DocumentModelCloner.CloneParagraph(bParagraph, RevisionClonePolicy.Preserve);
        merged.BlockContentControl = bParagraph.BlockContentControl ?? aParagraph?.BlockContentControl;
        merged.BlockCustomXml = bParagraph.BlockCustomXml ?? aParagraph?.BlockCustomXml;
        merged.SpanningFieldStart = bParagraph.SpanningFieldStart ?? aParagraph?.SpanningFieldStart;
        merged.SpanningFieldOwner = bParagraph.SpanningFieldOwner ?? aParagraph?.SpanningFieldOwner;
        merged.EndsSpanningField = bParagraph.EndsSpanningField || aParagraph?.EndsSpanningField == true;
        merged.Runs.Clear();
        merged.BookmarkBoundaries.Clear();

        var aRuns = aParagraph?.Runs ?? new List<Run>();
        var bRuns = bParagraph.Runs;
        var bBoundaryOutputIndices = new int?[bRuns.Count + 1];
        var ai = 0;
        var bi = 0;

        while (ai < aRuns.Count || bi < bRuns.Count)
        {
            bBoundaryOutputIndices[bi] ??= merged.Runs.Count;
            // A's deletions (base-only text struck by A) are off-spine: emit them, attributed to authorA.
            if (ai < aRuns.Count && aRuns[ai].Revision == RevisionKind.Deleted)
            {
                merged.Runs.Add(Stamp(aRuns[ai], RevisionKind.Deleted, authorA, dateXml));
                ai++;
                continue;
            }

            // B's insertions (new in B) are off-spine: emit them, keeping B's authorship/date.
            if (bi < bRuns.Count && bRuns[bi].Revision == RevisionKind.Inserted)
            {
                var bRun = bRuns[bi];
                merged.Runs.Add(Stamp(bRun, RevisionKind.Inserted, bRun.RevisionAuthor ?? authorB, bRun.RevisionDateXml ?? dateXml));
                bi++;
                bBoundaryOutputIndices[bi] ??= merged.Runs.Count;
                continue;
            }

            // Both cursors are now on a spine token (or one side is exhausted). The A side tells us if A
            // inserted this token (vs the base); the B side tells us if B deleted it. Combine the two:
            //   A inserted & B deleted  → B struck text A had added → keep as B-deletion (attributed to B)
            //   A inserted & B kept     → A's insertion (attributed to A)
            //   A kept     & B deleted  → B's deletion (attributed to B)
            //   A kept     & B kept     → ordinary text, no marks
            var aRun = ai < aRuns.Count ? aRuns[ai] : null;
            var bSpine = bi < bRuns.Count ? bRuns[bi] : null;
            if (bSpine is null)
            {
                // A still has spine runs but B is exhausted — carry A's mark through (A-insert or ordinary).
                if (aRun is not null)
                {
                    if (aRun.Revision == RevisionKind.Inserted)
                        merged.Runs.Add(Stamp(aRun, RevisionKind.Inserted, authorA, dateXml));
                    else
                        merged.Runs.Add(Stamp(aRun, RevisionKind.None, null, null));
                    ai++;
                }
                continue;
            }

            var aInserted = aRun is { Revision: RevisionKind.Inserted };
            var bDeleted = bSpine.Revision == RevisionKind.Deleted;

            // The spine token's text/formatting comes from B's blackline (it is the authority for the spine).
            if (bDeleted)
                merged.Runs.Add(Stamp(bSpine, RevisionKind.Deleted, bSpine.RevisionAuthor ?? authorB, bSpine.RevisionDateXml ?? dateXml));
            else if (aInserted)
                merged.Runs.Add(Stamp(bSpine, RevisionKind.Inserted, authorA, dateXml));
            else
                merged.Runs.Add(Stamp(bSpine, RevisionKind.None, null, null));

            if (aRun is not null)
                ai++;
            bi++;
            bBoundaryOutputIndices[bi] ??= merged.Runs.Count;
        }

        bBoundaryOutputIndices[bRuns.Count] ??= merged.Runs.Count;
        merged.BookmarkBoundaries.AddRange(bParagraph.BookmarkBoundaries.Select(boundary => boundary with
        {
            RunIndex = bBoundaryOutputIndices[Math.Clamp(boundary.RunIndex, 0, bRuns.Count)] ?? merged.Runs.Count
        }));

        return merged;
    }

    // Clone a run and stamp it with one revision kind/author/date (clearing the mark when kind is None).
    private static Run Stamp(Run source, RevisionKind kind, string? author, string? dateXml)
    {
        var copy = DocumentModelCloner.CloneRun(source, RevisionClonePolicy.Preserve);
        copy.Revision = kind;
        copy.RevisionAuthor = kind == RevisionKind.None ? null : author;
        copy.RevisionDateXml = kind == RevisionKind.None ? null : dateXml;
        return copy;
    }

    private static void CopyShell(TextDocument source, TextDocument target)
    {
        target.DefaultRun = source.DefaultRun;
        target.DefaultParagraph = source.DefaultParagraph;
        target.DoNotAutoCompressPictures = source.DoNotAutoCompressPictures;
        target.EmbedSystemFonts = source.EmbedSystemFonts;
        target.SaveSubsetFonts = source.SaveSubsetFonts;
        target.PageBordersDoNotSurroundHeader = source.PageBordersDoNotSurroundHeader;
        target.PageBordersDoNotSurroundFooter = source.PageBordersDoNotSurroundFooter;
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
        target.Preserved.CopyFrom(source.Preserved);
    }

}
