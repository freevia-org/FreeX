namespace FreeW.Core.Model;

/// <summary>Insert a paragraph at an index.</summary>
public sealed class InsertParagraphCommand(int index, Paragraph paragraph) : IDocumentCommand
{
    public string Label => "Insert Paragraph";

    public void Apply(IDocumentCommandContext context) =>
        context.Document.Paragraphs.Insert(index, paragraph);

    public void Revert(IDocumentCommandContext context) =>
        context.Document.Paragraphs.RemoveAt(index);
}

/// <summary>Remove the paragraph at an index (restores it on undo).</summary>
public sealed class DeleteParagraphCommand(int index) : IDocumentCommand
{
    private Paragraph? _removed;

    public string Label => "Delete Paragraph";

    public void Apply(IDocumentCommandContext context)
    {
        _removed = context.Document.Paragraphs[index];
        context.Document.Paragraphs.RemoveAt(index);
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_removed is not null)
            context.Document.Paragraphs.Insert(index, _removed);
    }
}

/// <summary>Replace a paragraph's formatting, snapshotting the previous value for undo.</summary>
public sealed class SetParagraphFormattingCommand(int index, ParagraphFormatting formatting) : IDocumentCommand
{
    private ParagraphFormatting? _previous;

    public string Label => "Paragraph Formatting";

    public void Apply(IDocumentCommandContext context)
    {
        var paragraph = context.Document.Paragraphs[index];
        _previous = paragraph.Formatting;
        paragraph.Formatting = formatting;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_previous is not null)
            context.Document.Paragraphs[index].Formatting = _previous;
    }
}

/// <summary>Replace one run's formatting, snapshotting the previous value for undo.</summary>
public sealed class SetRunFormattingCommand(int paragraphIndex, int runIndex, RunFormatting formatting) : IDocumentCommand
{
    private RunFormatting? _previous;

    public string Label => "Character Formatting";

    public void Apply(IDocumentCommandContext context)
    {
        var run = context.Document.Paragraphs[paragraphIndex].Runs[runIndex];
        _previous = run.Formatting;
        run.Formatting = formatting;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_previous is not null)
            context.Document.Paragraphs[paragraphIndex].Runs[runIndex].Formatting = _previous;
    }
}

/// <summary>
/// Apply a formatting transform to every run in a paragraph (e.g. toggle bold), snapshotting
/// each run's prior formatting. The building block the ribbon will call for selection-wide format.
/// </summary>
public sealed class FormatParagraphRunsCommand(int paragraphIndex, Func<RunFormatting, RunFormatting> transform) : IDocumentCommand
{
    private RunFormatting[]? _previous;

    public string Label => "Format";

    public void Apply(IDocumentCommandContext context)
    {
        var runs = context.Document.Paragraphs[paragraphIndex].Runs;
        _previous = runs.Select(r => r.Formatting).ToArray();
        foreach (var run in runs)
            run.Formatting = transform(run.Formatting);
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_previous is null)
            return;
        var runs = context.Document.Paragraphs[paragraphIndex].Runs;
        for (var i = 0; i < runs.Count && i < _previous.Length; i++)
            runs[i].Formatting = _previous[i];
    }
}
