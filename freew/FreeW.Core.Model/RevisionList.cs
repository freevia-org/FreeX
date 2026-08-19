namespace FreeW.Core.Model;

/// <summary>
/// The kind of tracked change a <see cref="RevisionEntry"/> describes — what Word's Reviewing Pane shows
/// as the revision's verb. <see cref="Insertion"/>/<see cref="Deletion"/> mirror <see cref="RevisionKind"/>;
/// <see cref="Formatting"/> is a tracked formatting change (<c>w:rPrChange</c>) carried on an otherwise
/// ordinary run.
/// </summary>
public enum RevisionEntryKind
{
    Insertion,
    Deletion,
    Formatting
}

/// <summary>
/// One reviewable tracked change, as surfaced by the Reviewing Pane: the change's <see cref="Kind"/>,
/// <see cref="Author"/>, <see cref="DateXml"/> (the raw W3CDTF timestamp, or null), and the affected
/// <see cref="Text"/>. <see cref="BlockIndex"/> is the index of the owning paragraph in the document's
/// body-paragraph walk (top-level paragraphs and those nested in table cells, in order) — a stable handle
/// for click-to-navigate. The owning <see cref="Paragraph"/> and <see cref="Run"/> are carried so a single
/// entry can be accepted or rejected directly. Immutable snapshot: re-enumerate after any accept/reject.
/// </summary>
public sealed record RevisionEntry(
    int BlockIndex,
    RevisionEntryKind Kind,
    string? Author,
    string? DateXml,
    string Text,
    Paragraph Paragraph,
    Run Run);

/// <summary>
/// Pure, unit-testable enumeration and single-revision resolution over the tracked changes carried on the
/// document's runs — the model behind Word's Reviewing Pane and its Accept/Reject (this one) + Previous/Next
/// commands. <see cref="Enumerate"/> lists every revision in reading order; <see cref="Accept"/> and
/// <see cref="Reject"/> resolve exactly one of them (leaving every other revision untouched), reusing the
/// same accept/reject semantics as <see cref="TrackChanges"/>. Nothing here touches the editor or docx layers.
/// </summary>
public static class RevisionList
{
    /// <summary>
    /// Every tracked change in the document body, in reading order: for each body paragraph (top-level and
    /// nested in table cells), each run carrying an insertion/deletion mark and each run carrying a tracked
    /// formatting change, as a <see cref="RevisionEntry"/>. A single run can yield two entries when it is
    /// both inserted/deleted and format-changed (mirroring how <see cref="TrackChanges"/> resolves the two
    /// marks independently). Order matches the Reviewing Pane and drives Previous/Next navigation.
    /// </summary>
    public static IReadOnlyList<RevisionEntry> Enumerate(TextDocument document)
    {
        var entries = new List<RevisionEntry>();
        var blockIndex = 0;
        foreach (var paragraph in EnumerateParagraphs(document))
        {
            foreach (var run in paragraph.Runs)
            {
                if (run.Revision == RevisionKind.Inserted)
                    entries.Add(new RevisionEntry(blockIndex, RevisionEntryKind.Insertion, run.RevisionAuthor, run.RevisionDateXml, run.Text, paragraph, run));
                else if (run.Revision == RevisionKind.Deleted)
                    entries.Add(new RevisionEntry(blockIndex, RevisionEntryKind.Deletion, run.RevisionAuthor, run.RevisionDateXml, run.Text, paragraph, run));

                if (run.FormatRevision is { } format)
                    entries.Add(new RevisionEntry(blockIndex, RevisionEntryKind.Formatting, format.Author, format.DateXml, run.Text, paragraph, run));
            }

            blockIndex++;
        }

        return entries;
    }

    /// <summary>
    /// Accept exactly the change described by <paramref name="entry"/>, leaving every other revision in
    /// place. An insertion becomes ordinary text; a deletion's run is removed; a formatting change keeps the
    /// new formatting and clears its mark. The document is mutated in place. A no-op if the entry's run is no
    /// longer in its paragraph (e.g. the list is stale). Returns true when something was resolved.
    /// </summary>
    public static bool Accept(TextDocument document, RevisionEntry entry) => Resolve(entry, accept: true);

    /// <summary>
    /// Reject exactly the change described by <paramref name="entry"/>, leaving every other revision in
    /// place. An insertion's run is removed; a deletion becomes ordinary text; a formatting change restores
    /// the previous formatting and clears its mark. The document is mutated in place. A no-op if the entry's
    /// run is no longer in its paragraph (stale list). Returns true when something was resolved.
    /// </summary>
    public static bool Reject(TextDocument document, RevisionEntry entry) => Resolve(entry, accept: false);

    // Resolve one entry's mark only. For an insertion/deletion entry we touch the run's Revision mark; for a
    // formatting entry we touch only its FormatRevision (the two are independent, exactly as TrackChanges
    // treats them). This deliberately does NOT clear the other mark on a doubly-marked run, so accepting an
    // insertion on a run that is also format-changed leaves the format revision pending (and vice versa).
    private static bool Resolve(RevisionEntry entry, bool accept)
    {
        var paragraph = entry.Paragraph;
        var run = entry.Run;
        var index = paragraph.Runs.IndexOf(run);
        if (index < 0)
            return false;

        if (entry.Kind == RevisionEntryKind.Formatting)
        {
            if (run.FormatRevision is not { } format)
                return false;
            if (!accept)
                run.Formatting = format.PreviousFormatting;
            run.FormatRevision = null;
            return true;
        }

        // Insertion/deletion. Drop the run when the change is being thrown away (insertion rejected or
        // deletion accepted); otherwise keep the run as ordinary text and clear its revision metadata.
        var dropKind = accept ? RevisionKind.Deleted : RevisionKind.Inserted;
        if (run.Revision == RevisionKind.None)
            return false;

        if (run.Revision == dropKind)
        {
            paragraph.Runs.RemoveAt(index);
        }
        else
        {
            run.Revision = RevisionKind.None;
            run.RevisionAuthor = null;
            run.RevisionDateXml = null;
        }

        return true;
    }

    // Every paragraph reachable in the document body — top-level paragraphs and those nested in table cells
    // (including tables nested inside table cells, to any depth), plus the text-box content of any
    // Run.Shape a run carries (see BodyParagraphWalk) — the same walk TrackChanges/DocxWriter use, so the
    // index is consistent across the reviewing surface.
    private static IEnumerable<Paragraph> EnumerateParagraphs(TextDocument document) =>
        BodyParagraphWalk.Enumerate(document);
}
