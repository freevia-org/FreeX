using System.Collections.Generic;
using System.Linq;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Editing;

/// <summary>
/// Undoable command that marks the runs covering a character range of one body paragraph as a tracked
/// change of a given <see cref="RevisionKind"/> (insertion or deletion), splitting partially-covered runs
/// at the boundaries so the mark is exact. Live keystroke-level recording is handled by the edit pipeline.
/// The covered paragraph's run list is snapshotted so Undo restores it exactly.
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
