using System.Collections.Generic;
using System.Linq;
using FreeW.App.Presentation.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Editing;

/// <summary>
/// AV-REVIEW: undoable wrappers around the portable tracked-change resolution model
/// (<see cref="TrackChanges"/> and <see cref="RevisionList"/>) so accept/reject of revisions rides the
/// same undo/redo bus as the rest of the Avalonia editor.
///
/// <para>
/// The model accept/reject operations mutate paragraphs in place — they either remove a run (insertion
/// rejected / deletion accepted) or clear a kept run's revision metadata (and, for a tracked formatting
/// change rejected, restore the previous formatting). To make this undoable without a fragile per-field
/// deep clone of every <see cref="Run"/>, each command snapshots — for every body paragraph — the exact
/// ordered list of run objects plus the handful of fields the resolution can touch
/// (<see cref="Run.Revision"/>, author/date, <see cref="Run.FormatRevision"/>, <see cref="Run.Formatting"/>,
/// and <see cref="Paragraph.Formatting"/>/<see cref="Paragraph.ParagraphFormatRevision"/>). Revert restores
/// the original membership and those fields verbatim, so a single Undo brings every resolved revision back.
/// </para>
/// </summary>
internal abstract class RevisionResolveCommandBase : IDocumentCommand
{
    // Per-paragraph snapshot: the original ordered runs plus each run's resolvable marks, and the
    // paragraph's own formatting + format-revision (for w:pPrChange reject).
    private sealed record RunMarks(
        RevisionKind Revision,
        string? Author,
        string? DateXml,
        FormatRevision? FormatRevision,
        RunFormatting Formatting);

    private sealed record ParagraphSnapshot(
        Paragraph Paragraph,
        List<Run> Runs,
        List<RunMarks> Marks,
        ParagraphFormatting Formatting,
        ParagraphFormatRevision? FormatRevision);

    private List<ParagraphSnapshot>? _snapshot;
    private bool _applied;

    public abstract string Label { get; }

    /// <summary>The model mutation this command performs (accept/reject — single or all).</summary>
    protected abstract void Resolve(TextDocument document);

    public void Apply(IDocumentCommandContext context)
    {
        var paragraphs = EnumerateParagraphs(context.Document).ToList();
        _snapshot = paragraphs
            .Select(p => new ParagraphSnapshot(
                p,
                p.Runs.ToList(),
                p.Runs.Select(r => new RunMarks(r.Revision, r.RevisionAuthor, r.RevisionDateXml, r.FormatRevision, r.Formatting)).ToList(),
                p.Formatting,
                p.ParagraphFormatRevision))
            .ToList();

        Resolve(context.Document);
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || _snapshot is null)
            return;

        foreach (var snap in _snapshot)
        {
            // Restore each run's resolvable marks on the original run objects.
            for (var i = 0; i < snap.Runs.Count; i++)
            {
                var run = snap.Runs[i];
                var marks = snap.Marks[i];
                run.Revision = marks.Revision;
                run.RevisionAuthor = marks.Author;
                run.RevisionDateXml = marks.DateXml;
                run.FormatRevision = marks.FormatRevision;
                run.Formatting = marks.Formatting;
            }

            // Restore membership + order (some runs may have been removed by accept/reject).
            snap.Paragraph.Runs.Clear();
            snap.Paragraph.Runs.AddRange(snap.Runs);

            // Restore paragraph-level formatting + tracked paragraph-formatting change.
            snap.Paragraph.Formatting = snap.Formatting;
            snap.Paragraph.ParagraphFormatRevision = snap.FormatRevision;
        }

        _applied = false;
    }

    // The same body-paragraph walk TrackChanges/RevisionList use (top-level + table-cell paragraphs),
    // so the snapshot covers exactly the paragraphs the resolution can mutate.
    private static IEnumerable<Paragraph> EnumerateParagraphs(TextDocument document)
    {
        foreach (var block in document.Blocks)
        {
            if (block is Paragraph paragraph)
                yield return paragraph;
            else if (block is Table table)
                foreach (var row in table.Rows)
                    foreach (var cell in row.Cells)
                        foreach (var cellParagraph in cell.Paragraphs)
                            yield return cellParagraph;
        }
    }
}

/// <summary>Undoable accept-all: every tracked change resolved as kept text / dropped deletion.</summary>
internal sealed class AcceptAllRevisionsCommand : RevisionResolveCommandBase
{
    public override string Label => "Accept All Changes";
    protected override void Resolve(TextDocument document) => TrackChanges.AcceptAll(document);
}

/// <summary>Undoable reject-all: every tracked change resolved as dropped insertion / restored deletion.</summary>
internal sealed class RejectAllRevisionsCommand : RevisionResolveCommandBase
{
    public override string Label => "Reject All Changes";
    protected override void Resolve(TextDocument document) => TrackChanges.RejectAll(document);
}

/// <summary>
/// Undoable accept of a single revision identified by its position in <see cref="RevisionList.Enumerate"/>
/// reading order. The index is resolved against a fresh enumeration at apply time, so the command stays
/// valid as long as the list is not stale; out-of-range is a no-op.
/// </summary>
internal sealed class AcceptOneRevisionCommand(RevisionTargetDecision target) : RevisionResolveCommandBase
{
    public override string Label => "Accept Change";
    protected override void Resolve(TextDocument document) =>
        target.TryApply(document, RevisionResolutionAction.Accept);
}

/// <summary>Undoable reject of a single revision identified by its reading-order index (see accept).</summary>
internal sealed class RejectOneRevisionCommand(RevisionTargetDecision target) : RevisionResolveCommandBase
{
    public override string Label => "Reject Change";
    protected override void Resolve(TextDocument document) =>
        target.TryApply(document, RevisionResolutionAction.Reject);
}

/// <summary>
/// Undoable command that marks the runs covering a character range of one body paragraph as a tracked
/// change of a given <see cref="RevisionKind"/> (insertion or deletion), splitting partially-covered runs
/// at the boundaries so the mark is exact. Ports the WPF host's MarkRevisionRange. This is how
/// <see cref="DocumentView.MarkSelectionAsRevision"/> turns an existing selection into a recorded revision.
/// (Live keystroke-level recording on type/delete is handled directly in the edit pipeline — AV-TRACKEDIT —
/// by stamping the revision mark onto the inserted/deleted cells through the normal ReplaceParagraphRuns path.)
/// The covered paragraph's run list is snapshotted (deep clone of marks) so Undo restores it exactly.
/// </summary>
internal sealed class MarkRevisionRangeCommand(
    int blockIndex,
    int startOffset,
    int endOffset,
    RevisionKind kind,
    string author,
    string? dateXml) : IDocumentCommand
{
    private List<Run>? _savedRuns;
    private bool _applied;

    public string Label => kind == RevisionKind.Deleted ? "Mark Deletion" : "Mark Insertion";

    public void Apply(IDocumentCommandContext context)
    {
        if (kind == RevisionKind.None ||
            context.Document.Blocks.ElementAtOrDefault(blockIndex) is not Paragraph paragraph)
            return;

        // Snapshot deep clones: MarkRange mutates run objects in place (splits text, stamps Revision),
        // so a shallow copy would share those mutations and break Revert.
        _savedRuns = paragraph.Runs.Select(run => RevisionEditPlanner.CloneRunWithText(run, run.Text)).ToList();
        RevisionEditPlanner.MarkRevisionRange(paragraph, startOffset, endOffset, kind, author, dateXml);
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || _savedRuns is null ||
            context.Document.Blocks.ElementAtOrDefault(blockIndex) is not Paragraph paragraph)
            return;

        paragraph.Runs.Clear();
        paragraph.Runs.AddRange(_savedRuns);
        _applied = false;
    }

}
