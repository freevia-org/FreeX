using FreeW.Core.Model;

namespace FreeW.App.Presentation.Editing;

/// <summary>
/// Snapshots revision-bearing model state so resolution remains one undoable edit.
/// TrackChanges.AcceptAll/RejectAll doesn't just clear marks in place — it can structurally remove
/// content: a paragraph whose tracked paragraph-mark resolves to "removed" is merged into (and vanishes
/// from) its containing block/cell-paragraph list, and a table row whose tracked RowRevision resolves to
/// "removed" is dropped from its table entirely (including anywhere this happens inside a header, footer,
/// footnote or endnote, now that TrackChanges walks those too). Undo must put exactly those paragraphs and
/// rows back, not just restore field values on objects that are still there — so this snapshots every
/// paragraph/row-bearing *list* the resolve pass can structurally mutate (by reference, before resolving)
/// alongside each paragraph's and row's own revision-bearing fields, and restores both wholesale on
/// Revert. Restoring the list contents from the pre-resolve snapshot re-inserts any paragraph/row that was
/// merged away or dropped, at its original position, using the SAME object instances (so other state that
/// may reference them, e.g. an active selection, keeps working after undo).
/// </summary>
internal abstract class RevisionResolveCommandBase : IDocumentCommand
{
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
        ParagraphFormatRevision? FormatRevision,
        RevisionKind MarkRevision,
        string? MarkRevisionAuthor,
        string? MarkRevisionDateXml);

    private sealed record RowSnapshot(
        TableRow Row,
        RevisionKind RowRevision,
        string? RowRevisionAuthor,
        string? RowRevisionDateXml);

    private sealed record ListSnapshot<T>(IList<T> Container, List<T> OriginalContents);

    private List<ParagraphSnapshot>? _paragraphSnapshots;
    private List<RowSnapshot>? _rowSnapshots;
    private List<ListSnapshot<Block>>? _blockListSnapshots;
    private List<ListSnapshot<TableRow>>? _rowListSnapshots;
    private List<ListSnapshot<Paragraph>>? _paragraphListSnapshots;

    public abstract string Label { get; }

    /// <summary>
    /// Single-entry resolution (<see cref="ResolveOneRevisionCommand"/>) only ever mutates this one
    /// paragraph's own Runs list -- <see cref="RevisionList"/>.Accept/Reject never merges paragraphs away
    /// or drops table rows the way <see cref="TrackChanges"/>.AcceptAll/RejectAll can -- so undo only needs
    /// this paragraph's fields snapshotted, not a full-document walk. Bulk commands (Accept All/Reject All)
    /// leave this null and fall back to the full structural snapshot below, since those DO resolve via
    /// <see cref="TrackChanges"/> and can merge/drop content anywhere in the document.
    /// </summary>
    protected virtual Paragraph? TargetParagraph => null;

    protected abstract bool Resolve(TextDocument document);

    public void Apply(IDocumentCommandContext context)
    {
        _paragraphSnapshots = [];
        _rowSnapshots = [];
        _blockListSnapshots = [];
        _rowListSnapshots = [];
        _paragraphListSnapshots = [];

        var document = context.Document;
        if (TargetParagraph is { } targetParagraph)
        {
            CaptureParagraph(targetParagraph);
        }
        else
        {
            CaptureBlockList(document.Blocks);
            foreach (var section in document.Sections)
            {
                var headersFooters = section.HeadersFooters;
                CaptureHeaderFooter(headersFooters.Header);
                CaptureHeaderFooter(headersFooters.Footer);
                CaptureHeaderFooter(headersFooters.EvenHeader);
                CaptureHeaderFooter(headersFooters.EvenFooter);
                CaptureHeaderFooter(headersFooters.FirstHeader);
                CaptureHeaderFooter(headersFooters.FirstFooter);
            }
            foreach (var footnote in document.Footnotes.Values)
                CaptureParagraphContainer(footnote.Content);
            foreach (var endnote in document.Endnotes.Values)
                CaptureParagraphContainer(endnote.Content);
        }

        if (!Resolve(document))
            ClearSnapshots();
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_paragraphSnapshots is null)
            return;

        // Restore structural list membership/order first (deepest containers first, though order across
        // distinct list instances doesn't matter — each list is restored independently), so every
        // paragraph/row that resolution merged away or dropped is back before field values are reapplied.
        foreach (var snapshot in _paragraphListSnapshots!)
            ReplaceContents(snapshot.Container, snapshot.OriginalContents);
        foreach (var snapshot in _rowListSnapshots!)
            ReplaceContents(snapshot.Container, snapshot.OriginalContents);
        foreach (var snapshot in _blockListSnapshots!)
            ReplaceContents(snapshot.Container, snapshot.OriginalContents);

        foreach (var rowSnapshot in _rowSnapshots!)
        {
            rowSnapshot.Row.RowRevision = rowSnapshot.RowRevision;
            rowSnapshot.Row.RowRevisionAuthor = rowSnapshot.RowRevisionAuthor;
            rowSnapshot.Row.RowRevisionDateXml = rowSnapshot.RowRevisionDateXml;
        }

        foreach (var snapshot in _paragraphSnapshots)
        {
            for (var index = 0; index < snapshot.Runs.Count; index++)
            {
                var run = snapshot.Runs[index];
                var marks = snapshot.Marks[index];
                run.Revision = marks.Revision;
                run.RevisionAuthor = marks.Author;
                run.RevisionDateXml = marks.DateXml;
                run.FormatRevision = marks.FormatRevision;
                run.Formatting = marks.Formatting;
            }

            snapshot.Paragraph.Runs.Clear();
            snapshot.Paragraph.Runs.AddRange(snapshot.Runs);
            snapshot.Paragraph.Formatting = snapshot.Formatting;
            snapshot.Paragraph.ParagraphFormatRevision = snapshot.FormatRevision;
            snapshot.Paragraph.MarkRevision = snapshot.MarkRevision;
            snapshot.Paragraph.MarkRevisionAuthor = snapshot.MarkRevisionAuthor;
            snapshot.Paragraph.MarkRevisionDateXml = snapshot.MarkRevisionDateXml;
        }

        ClearSnapshots();
    }

    private static void ReplaceContents<T>(IList<T> container, List<T> originalContents)
    {
        container.Clear();
        foreach (var item in originalContents)
            container.Add(item);
    }

    private void ClearSnapshots()
    {
        _paragraphSnapshots = null;
        _rowSnapshots = null;
        _blockListSnapshots = null;
        _rowListSnapshots = null;
        _paragraphListSnapshots = null;
    }

    private void CaptureBlockList(IList<Block> blocks)
    {
        _blockListSnapshots!.Add(new ListSnapshot<Block>(blocks, blocks.ToList()));
        foreach (var block in blocks)
        {
            if (block is Paragraph paragraph)
                CaptureParagraph(paragraph);
            else if (block is Table table)
                CaptureTable(table);
        }
    }

    private void CaptureTable(Table table)
    {
        _rowListSnapshots!.Add(new ListSnapshot<TableRow>(table.Rows, table.Rows.ToList()));
        foreach (var row in table.Rows)
        {
            _rowSnapshots!.Add(new RowSnapshot(row, row.RowRevision, row.RowRevisionAuthor, row.RowRevisionDateXml));
            foreach (var cell in row.Cells)
            {
                CaptureParagraphContainer(cell.Paragraphs);
                foreach (var nestedTable in cell.NestedTables)
                    CaptureTable(nestedTable);
            }
        }
    }

    private void CaptureHeaderFooter(HeaderFooter? headerFooter)
    {
        if (headerFooter is null)
            return;

        CaptureParagraphContainer(headerFooter.Paragraphs);
        if (headerFooter.Table is { } table)
            CaptureTable(table);
    }

    private void CaptureParagraphContainer(IList<Paragraph> paragraphs)
    {
        _paragraphListSnapshots!.Add(new ListSnapshot<Paragraph>(paragraphs, paragraphs.ToList()));
        foreach (var paragraph in paragraphs)
            CaptureParagraph(paragraph);
    }

    private void CaptureParagraph(Paragraph paragraph)
    {
        _paragraphSnapshots!.Add(new ParagraphSnapshot(
            paragraph,
            paragraph.Runs.ToList(),
            paragraph.Runs.Select(run => new RunMarks(
                run.Revision,
                run.RevisionAuthor,
                run.RevisionDateXml,
                run.FormatRevision,
                run.Formatting)).ToList(),
            paragraph.Formatting,
            paragraph.ParagraphFormatRevision,
            paragraph.MarkRevision,
            paragraph.MarkRevisionAuthor,
            paragraph.MarkRevisionDateXml));
    }
}

internal sealed class AcceptAllRevisionsCommand : RevisionResolveCommandBase
{
    public override string Label => "Accept All Changes";

    protected override bool Resolve(TextDocument document)
    {
        if (!TrackChanges.HasRevisions(document))
            return false;
        TrackChanges.AcceptAll(document);
        return true;
    }
}

internal sealed class RejectAllRevisionsCommand : RevisionResolveCommandBase
{
    public override string Label => "Reject All Changes";

    protected override bool Resolve(TextDocument document)
    {
        if (!TrackChanges.HasRevisions(document))
            return false;
        TrackChanges.RejectAll(document);
        return true;
    }
}

internal sealed class ResolveOneRevisionCommand(
    RevisionTargetDecision target,
    RevisionResolutionAction action) : RevisionResolveCommandBase
{
    public override string Label => action == RevisionResolutionAction.Accept
        ? "Accept Change"
        : "Reject Change";

    protected override Paragraph? TargetParagraph => target.Entry.Paragraph;

    protected override bool Resolve(TextDocument document) => target.TryApply(document, action);
}

internal sealed class MarkRevisionRangeCommand(
    int blockIndex,
    int startOffset,
    int endOffset,
    RevisionKind kind,
    string author,
    string? dateXml) : IDocumentCommand
{
    private List<Run>? _savedRuns;

    public string Label => kind == RevisionKind.Deleted ? "Mark Deletion" : "Mark Insertion";

    public void Apply(IDocumentCommandContext context)
    {
        if (kind == RevisionKind.None
            || context.Document.Blocks.ElementAtOrDefault(blockIndex) is not Paragraph paragraph)
        {
            return;
        }

        var savedRuns = paragraph.Runs
            .Select(run => RevisionEditPlanner.CloneRunWithText(run, run.Text))
            .ToList();
        if (!RevisionEditPlanner.MarkRevisionRange(
                paragraph,
                startOffset,
                endOffset,
                kind,
                author,
                dateXml))
        {
            return;
        }

        _savedRuns = savedRuns;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_savedRuns is null
            || context.Document.Blocks.ElementAtOrDefault(blockIndex) is not Paragraph paragraph)
        {
            return;
        }

        paragraph.Runs.Clear();
        paragraph.Runs.AddRange(_savedRuns);
        _savedRuns = null;
    }
}
