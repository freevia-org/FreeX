using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>Replaces one owned header/footer slot while retaining its previous value for Undo.</summary>
public sealed class ReplaceHeaderFooterSlotCommand(
    int sectionIndex,
    HeaderFooterSlotKind slot,
    HeaderFooter? replacement,
    string label = "Edit header/footer") : IDocumentCommand
{
    private HeaderFooter? _previous;

    public string Label => label;

    public void Apply(IDocumentCommandContext context)
    {
        var store = context.Document.Sections[sectionIndex].HeadersFooters;
        _previous = HeaderFooterDialogPlanner.GetSlot(store, slot);
        HeaderFooterDialogPlanner.SetSlot(store, slot, replacement);
    }

    public void Revert(IDocumentCommandContext context) =>
        HeaderFooterDialogPlanner.SetSlot(context.Document.Sections[sectionIndex].HeadersFooters, slot, _previous);
}
