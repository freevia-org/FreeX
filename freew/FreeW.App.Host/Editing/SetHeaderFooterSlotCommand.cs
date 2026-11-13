using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Host.Editing;

/// <summary>
/// W10: puts an edited header/footer story into its section slot as an undoable edit. Undo puts back the
/// exact <see cref="HeaderFooter"/> instance the slot held (or none), so the document is exactly what it
/// was. Used by the paged editor's in-page header/footer regions.
/// </summary>
internal sealed class SetHeaderFooterSlotCommand(
    SectionHeadersFooters owner,
    HeaderFooterSlotKind slot,
    HeaderFooter? value) : IDocumentCommand
{
    private HeaderFooter? _previous;
    private bool _applied;

    public string Label => "Edit Header/Footer";

    public DocumentCommandMutationKind MutationKind => DocumentCommandMutationKind.BodyText;

    public bool HasEffect(IDocumentCommandContext context) =>
        !ReferenceEquals(HeaderFooterDialogPlanner.GetSlot(owner, slot), value);

    public void Apply(IDocumentCommandContext context)
    {
        if (!_applied)
            _previous = HeaderFooterDialogPlanner.GetSlot(owner, slot);
        HeaderFooterDialogPlanner.SetSlot(owner, slot, value);
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context) =>
        HeaderFooterDialogPlanner.SetSlot(owner, slot, _previous);
}
