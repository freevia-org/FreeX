using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Editing;

/// <summary>
/// AV-HFEDIT: Splices the paragraph list of a header/footer slot — replaces the single paragraph at
/// <paramref name="firstParaIndex"/> with the paragraphs produced by <paramref name="buildReplacement"/>.
/// Supports a paragraph break (split one → two). Analogous to <see cref="SpliceCellParagraphsCommand"/>
/// but addresses a <see cref="SectionHeadersFooters"/> slot.
///
/// <paramref name="buildReplacement"/> is invoked at Apply time (after the store is resolved) so it can
/// read the live source paragraph; it returns the replacement paragraphs.
/// </summary>
internal sealed class SpliceHeaderFooterParagraphsCommand(
    int sectionIndex,
    bool useFinalSectionStore,
    int slot,
    int firstParaIndex,
    Func<IReadOnlyList<Paragraph>> buildReplacement) : IDocumentCommand
{
    private List<Paragraph>? _removed;
    private int _insertedCount;

    public string Label => "Edit header/footer";

    public void Apply(IDocumentCommandContext context)
    {
        var hf = ResolveSlot(context.Document);
        if (hf is null)
            return;
        var paras = hf.Paragraphs;
        var at = Math.Clamp(firstParaIndex, 0, Math.Max(0, paras.Count - 1));
        if (paras.Count == 0)
            return;
        var replacement = buildReplacement();
        _removed = paras.GetRange(at, 1);
        paras.RemoveAt(at);
        paras.InsertRange(at, replacement);
        _insertedCount = replacement.Count;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_removed is null)
            return;
        var hf = ResolveSlot(context.Document);
        if (hf is null)
            return;
        var paras = hf.Paragraphs;
        var at = Math.Clamp(firstParaIndex, 0, paras.Count);
        var count = Math.Clamp(_insertedCount, 0, paras.Count - at);
        paras.RemoveRange(at, count);
        paras.InsertRange(at, _removed);
    }

    private HeaderFooter? ResolveSlot(TextDocument doc)
    {
        SectionHeadersFooters? store;
        if (useFinalSectionStore)
            store = doc.FinalSectionHeadersFooters;
        else if (sectionIndex < 0 || sectionIndex >= doc.Sections.Count)
            store = doc.FinalSectionHeadersFooters;
        else
            store = doc.Sections[sectionIndex].HeadersFooters;

        return slot switch
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
}
