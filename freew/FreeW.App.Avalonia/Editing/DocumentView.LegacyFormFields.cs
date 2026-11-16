using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Editing;

public sealed partial class DocumentView
{
    /// <summary>
    /// Developer &gt; Legacy Tools: inserts a Word 97-2003 Text / Check Box / Drop-Down form field at the
    /// caret, with the same defaults and naming as the WPF editor. The Avalonia renderer shows and
    /// round-trips form fields (their result text); form-field fill-in and shading remain separate gaps.
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

    internal (ComplexField Field, string Text, LegacyFormField FormField)? LegacyFormFieldOptionsTargetAtCaret()
    {
        var run = ComplexFieldRunAtCaret();
        return run?.ComplexField is { FormField: { } formField } field
            ? (field, run.Text, formField)
            : null;
    }

    internal bool ApplyLegacyFormFieldOptions(ComplexField target, LegacyFormField updated)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(updated);
        if (!RestrictEditingPolicy.Allows(RestrictEditingOperationKind.BodyTextEdit))
            return false;

        foreach (var story in DocumentFieldStories.Enumerate(_doc))
        {
            foreach (var run in story.Paragraph.Runs)
            {
                if (!ReferenceEquals(run.ComplexField, target) || run.ComplexField?.FormField is not { } current)
                    continue;

                var text = LegacyFormFieldInteractionPlanner.TextAfterOptions(current, updated, run.Text);
                var command = new SetLegacyFormFieldCommand(target, text, updated);
                _bus.Execute(command);
                if (command.AppliedField is null)
                    return false;

                InvalidateLayoutAndVisual();
                return true;
            }
        }

        return false;
    }
}
