using FreeW.Core.Model;

namespace FreeW.App.Presentation.Editing;

/// <summary>Snapshots revision-bearing model state so resolution remains one undoable edit.</summary>
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
        ParagraphFormatRevision? FormatRevision);

    private List<ParagraphSnapshot>? _snapshot;

    public abstract string Label { get; }

    protected abstract bool Resolve(TextDocument document);

    public void Apply(IDocumentCommandContext context)
    {
        var paragraphs = EnumerateParagraphs(context.Document).ToList();
        _snapshot = paragraphs
            .Select(paragraph => new ParagraphSnapshot(
                paragraph,
                paragraph.Runs.ToList(),
                paragraph.Runs.Select(run => new RunMarks(
                    run.Revision,
                    run.RevisionAuthor,
                    run.RevisionDateXml,
                    run.FormatRevision,
                    run.Formatting)).ToList(),
                paragraph.Formatting,
                paragraph.ParagraphFormatRevision))
            .ToList();

        if (!Resolve(context.Document))
            _snapshot = null;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_snapshot is null)
            return;

        foreach (var snapshot in _snapshot)
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
        }

        _snapshot = null;
    }

    private static IEnumerable<Paragraph> EnumerateParagraphs(TextDocument document)
    {
        foreach (var block in document.Blocks)
        {
            if (block is Paragraph paragraph)
            {
                yield return paragraph;
                continue;
            }

            if (block is not Table table)
                continue;

            foreach (var cellParagraph in table.Rows
                         .SelectMany(row => row.Cells)
                         .SelectMany(cell => cell.Paragraphs))
            {
                yield return cellParagraph;
            }
        }
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
