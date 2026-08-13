using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>Undoable run edit for one paragraph in a section header/footer slot.</summary>
public sealed class EditHeaderFooterParagraphCommand(
    int sectionIndex,
    bool useFinalSectionStore,
    int slot,
    int paragraphIndex,
    Action<Paragraph> rebuild) : IDocumentCommand
{
    private List<Run>? _previous;

    public string Label => "Edit header/footer";

    public void Apply(IDocumentCommandContext context)
    {
        if (!TryGetParagraph(context.Document, out var paragraph))
            return;
        _previous = paragraph.Runs
            .Select(run => RevisionEditPlanner.CloneRunWithText(run, run.Text))
            .ToList();
        rebuild(paragraph);
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_previous is null || !TryGetParagraph(context.Document, out var paragraph))
            return;
        paragraph.Runs.Clear();
        paragraph.Runs.AddRange(_previous.Select(
            run => RevisionEditPlanner.CloneRunWithText(run, run.Text)));
    }

    private bool TryGetParagraph(TextDocument document, out Paragraph paragraph)
    {
        paragraph = null!;
        var story = HeaderFooterCommandAddress.ResolveStory(
            document,
            sectionIndex,
            useFinalSectionStore,
            slot);
        if (story is null || paragraphIndex < 0 || paragraphIndex >= story.Paragraphs.Count)
            return false;
        paragraph = story.Paragraphs[paragraphIndex];
        return true;
    }
}
