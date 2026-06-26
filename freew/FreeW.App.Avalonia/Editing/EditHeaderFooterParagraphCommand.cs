using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Editing;

/// <summary>
/// AV-HFEDIT: Mutates the runs of a single paragraph inside a per-section header/footer slot, snapshotting
/// the prior runs for undo. Analogous to <see cref="ReplaceCellParagraphRunsCommand"/> but addresses a
/// paragraph inside a <see cref="SectionHeadersFooters"/> store rather than a table cell.
///
/// Addressing: (sectionIndex → section in <c>Document.Sections</c>, useFinalSectionStore → target the
/// document-level <see cref="TextDocument.FinalSectionHeadersFooters"/> instead, slot → which of the six
/// header/footer slots, paraIndex → paragraph within the slot). The <paramref name="rebuild"/> action
/// mutates the paragraph in-place (same contract as <see cref="ReplaceCellParagraphRunsCommand"/>).
/// </summary>
internal sealed class EditHeaderFooterParagraphCommand(
    int sectionIndex,
    bool useFinalSectionStore,
    int slot,
    int paraIndex,
    Action<Paragraph> rebuild) : IDocumentCommand
{
    private List<Run>? _previous;

    public string Label => "Edit header/footer";

    public void Apply(IDocumentCommandContext context)
    {
        if (!TryGetParagraph(context, out var para))
            return;
        _previous = [.. para.Runs];
        rebuild(para);
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_previous is null || !TryGetParagraph(context, out var para))
            return;
        para.Runs.Clear();
        para.Runs.AddRange(_previous);
    }

    private bool TryGetParagraph(IDocumentCommandContext context, out Paragraph para)
    {
        para = null!;
        var store = ResolveStore(context.Document);
        if (store is null)
            return false;
        var hf = SlotOf(store, slot);
        if (hf is null || paraIndex < 0 || paraIndex >= hf.Paragraphs.Count)
            return false;
        para = hf.Paragraphs[paraIndex];
        return true;
    }

    private SectionHeadersFooters? ResolveStore(TextDocument doc)
    {
        if (useFinalSectionStore)
            return doc.FinalSectionHeadersFooters;
        if (sectionIndex < 0 || sectionIndex >= doc.Sections.Count)
            return doc.FinalSectionHeadersFooters;
        return doc.Sections[sectionIndex].HeadersFooters;
    }

    // Slot ordinals mirror DocumentView.HfSlot: 0 Header, 1 Footer, 2 FirstHeader, 3 FirstFooter,
    // 4 EvenHeader, 5 EvenFooter.
    private static HeaderFooter? SlotOf(SectionHeadersFooters store, int slot) => slot switch
    {
        0 => store.Header,
        1 => store.Footer,
        2 => store.FirstHeader,
        3 => store.FirstFooter,
        4 => store.EvenHeader,
        5 => store.EvenFooter,
        _ => null,
    };
}
