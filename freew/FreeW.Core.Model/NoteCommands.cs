namespace FreeW.Core.Model;

/// <summary>
/// Inserts one footnote or endnote and its reference marker into a body paragraph as a single undoable
/// edit. The marker is inserted at the requested plain-text offset, splitting a formatted run when
/// necessary.
/// </summary>
public sealed class InsertNoteCommand(
    int id,
    bool footnote,
    string text,
    int paragraphIndex,
    int textOffset) : IDocumentCommand
{
    private Run[]? _previousRuns;
    private bool _applied;

    public string Label => footnote ? "Insert Footnote" : "Insert Endnote";

    public void Apply(IDocumentCommandContext context)
    {
        if (paragraphIndex < 0
            || paragraphIndex >= context.Document.Blocks.Count
            || context.Document.Blocks[paragraphIndex] is not Paragraph paragraph
            || NoteExists(context.Document))
        {
            return;
        }

        _previousRuns = [.. paragraph.Runs];
        if (footnote)
            context.Document.Footnotes[id] = new Footnote(id, text ?? string.Empty);
        else
            context.Document.Endnotes[id] = new Endnote(id, text ?? string.Empty);

        var marker = footnote ? Run.FootnoteReference(id) : Run.EndnoteReference(id);
        RevisionEditPlanner.InsertRunAtOffset(paragraph, textOffset, marker);
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied
            || _previousRuns is null
            || paragraphIndex < 0
            || paragraphIndex >= context.Document.Blocks.Count
            || context.Document.Blocks[paragraphIndex] is not Paragraph paragraph)
        {
            return;
        }

        if (footnote)
            context.Document.Footnotes.Remove(id);
        else
            context.Document.Endnotes.Remove(id);

        paragraph.Runs.Clear();
        paragraph.Runs.AddRange(_previousRuns);
        _previousRuns = null;
        _applied = false;
    }

    private bool NoteExists(TextDocument document) =>
        footnote ? document.Footnotes.ContainsKey(id) : document.Endnotes.ContainsKey(id);
}

/// <summary>Replaces the rich paragraph content of one footnote or endnote.</summary>
public sealed class ReplaceNoteContentCommand(
    int id,
    bool footnote,
    IReadOnlyList<Paragraph> replacement) : IDocumentCommand
{
    private Paragraph[]? _previous;

    public string Label => footnote ? "Edit Footnote" : "Edit Endnote";

    public void Apply(IDocumentCommandContext context)
    {
        var content = FindContent(context.Document);
        if (content is null)
            return;

        _previous = content.Select(CloneParagraph).ToArray();
        content.Clear();
        if (replacement.Count == 0)
        {
            content.Add(new Paragraph());
            return;
        }

        content.AddRange(replacement.Select(CloneParagraph));
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_previous is null || FindContent(context.Document) is not { } content)
            return;

        content.Clear();
        content.AddRange(_previous.Select(CloneParagraph));
        _previous = null;
    }

    private List<Paragraph>? FindContent(TextDocument document)
    {
        if (footnote)
            return document.Footnotes.TryGetValue(id, out var footnoteValue) ? footnoteValue.Content : null;
        return document.Endnotes.TryGetValue(id, out var endnoteValue) ? endnoteValue.Content : null;
    }

    private static Paragraph CloneParagraph(Paragraph paragraph) =>
        (Paragraph)DocumentMerge.CloneBlock(paragraph);
}

/// <summary>
/// Deletes one footnote or endnote and every matching reference marker in the document body.
/// Undo restores both the rich note content and marker runs, including markers inside table cells.
/// </summary>
public sealed class DeleteNoteCommand(int id, bool footnote) : IDocumentCommand
{
    private Footnote? _footnote;
    private Endnote? _endnote;
    private List<(Paragraph Paragraph, Run[] Runs)>? _paragraphRuns;

    public string Label => footnote ? "Delete Footnote" : "Delete Endnote";

    public void Apply(IDocumentCommandContext context)
    {
        var document = context.Document;
        if (footnote)
        {
            if (!document.Footnotes.Remove(id, out _footnote))
                return;
        }
        else if (!document.Endnotes.Remove(id, out _endnote))
        {
            return;
        }

        _paragraphRuns = [];
        foreach (var paragraph in EnumerateBodyParagraphs(document.Blocks))
        {
            if (!paragraph.Runs.Any(IsMarker))
                continue;

            _paragraphRuns.Add((paragraph, [.. paragraph.Runs]));
            paragraph.Runs.RemoveAll(IsMarker);
        }
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (footnote && _footnote is not null)
            context.Document.Footnotes[id] = _footnote;
        else if (!footnote && _endnote is not null)
            context.Document.Endnotes[id] = _endnote;

        if (_paragraphRuns is not null)
        {
            foreach (var (paragraph, runs) in _paragraphRuns)
            {
                paragraph.Runs.Clear();
                paragraph.Runs.AddRange(runs);
            }
        }

        _footnote = null;
        _endnote = null;
        _paragraphRuns = null;
    }

    private bool IsMarker(Run run) =>
        footnote ? run.FootnoteId == id : run.EndnoteId == id;

    private static IEnumerable<Paragraph> EnumerateBodyParagraphs(IEnumerable<Block> blocks)
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

            foreach (var cellParagraph in table.Rows
                         .SelectMany(row => row.Cells)
                         .SelectMany(cell => cell.Paragraphs))
            {
                yield return cellParagraph;
            }
        }
    }
}
