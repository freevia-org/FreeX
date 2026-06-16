namespace FreeW.Core.Model;

/// <summary>Insert a block (paragraph or table) at an index in the document body.</summary>
public sealed class InsertBlockCommand(int index, Block block) : IDocumentCommand
{
    public string Label => block is Table ? "Insert Table" : "Insert Paragraph";

    public void Apply(IDocumentCommandContext context) =>
        context.Document.Blocks.Insert(index, block);

    public void Revert(IDocumentCommandContext context) =>
        context.Document.Blocks.RemoveAt(index);
}

/// <summary>Insert a paragraph at a block index.</summary>
public sealed class InsertParagraphCommand(int index, Paragraph paragraph) : IDocumentCommand
{
    public string Label => "Insert Paragraph";

    public void Apply(IDocumentCommandContext context) =>
        context.Document.Blocks.Insert(index, paragraph);

    public void Revert(IDocumentCommandContext context) =>
        context.Document.Blocks.RemoveAt(index);
}

/// <summary>Remove the block at an index (restores it on undo).</summary>
public sealed class DeleteParagraphCommand(int index) : IDocumentCommand
{
    private Block? _removed;

    public string Label => "Delete Paragraph";

    public void Apply(IDocumentCommandContext context)
    {
        _removed = context.Document.Blocks[index];
        context.Document.Blocks.RemoveAt(index);
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_removed is not null)
            context.Document.Blocks.Insert(index, _removed);
    }
}

/// <summary>Replace a paragraph's formatting, snapshotting the previous value for undo.</summary>
public sealed class SetParagraphFormattingCommand(int index, ParagraphFormatting formatting) : IDocumentCommand
{
    private ParagraphFormatting? _previous;

    public string Label => "Paragraph Formatting";

    public void Apply(IDocumentCommandContext context)
    {
        var paragraph = ParagraphAt(context, index);
        _previous = paragraph.Formatting;
        paragraph.Formatting = formatting;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_previous is not null)
            ParagraphAt(context, index).Formatting = _previous;
    }

    private static Paragraph ParagraphAt(IDocumentCommandContext context, int index) =>
        (Paragraph)context.Document.Blocks[index];
}

/// <summary>Replace one run's formatting, snapshotting the previous value for undo.</summary>
public sealed class SetRunFormattingCommand(int paragraphIndex, int runIndex, RunFormatting formatting) : IDocumentCommand
{
    private RunFormatting? _previous;

    public string Label => "Character Formatting";

    public void Apply(IDocumentCommandContext context)
    {
        var run = ((Paragraph)context.Document.Blocks[paragraphIndex]).Runs[runIndex];
        _previous = run.Formatting;
        run.Formatting = formatting;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_previous is not null)
            ((Paragraph)context.Document.Blocks[paragraphIndex]).Runs[runIndex].Formatting = _previous;
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
        var runs = ((Paragraph)context.Document.Blocks[paragraphIndex]).Runs;
        _previous = runs.Select(r => r.Formatting).ToArray();
        foreach (var run in runs)
            run.Formatting = transform(run.Formatting);
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_previous is null)
            return;
        var runs = ((Paragraph)context.Document.Blocks[paragraphIndex]).Runs;
        for (var i = 0; i < runs.Count && i < _previous.Length; i++)
            runs[i].Formatting = _previous[i];
    }
}
