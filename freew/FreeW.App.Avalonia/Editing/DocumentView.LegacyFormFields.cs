using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Editing;

public sealed partial class DocumentView
{
    /// <summary>
    /// Developer &gt; Legacy Tools: inserts a Word 97-2003 Text / Check Box / Drop-Down form field at the
    /// caret, with the same defaults and naming as the WPF editor. The Avalonia renderer shows and
    /// round-trips form fields (their result text) but does not yet offer in-place fill-in, the Options
    /// dialog or form-field shading.
    /// </summary>
    public void InsertLegacyFormField(LegacyFormFieldKind kind)
    {
        if (IsEditingLocked)
            return;

        var (field, text) = LegacyFormFieldInteractionPlanner.CreateInsertion(kind, _doc);
        InsertComplexField(field, text);
    }

    /// <summary>Developer &gt; Legacy Tools &gt; Reset Form Fields: one undoable reset of every field.</summary>
    public void ResetLegacyFormFields()
    {
        if (!RestrictEditingPolicy.Allows(RestrictEditingOperationKind.FormFieldEdit))
            return;

        _bus.Execute(new ResetLegacyFormFieldsCommand());
        InvalidateVisual();
    }
}
