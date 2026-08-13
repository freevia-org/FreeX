using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.DrawingUI;

public sealed record TextBoxInlineEditStartPlan(
    Guid TextBoxId,
    string Text);

public sealed record TextBoxInlineEditCommandPlan(
    Guid TextBoxId,
    string Text,
    bool TextChanged,
    IWorkbookCommand? Command);

public sealed record TextBoxInlineEditCancelPlan(
    Guid TextBoxId,
    string OriginalText);

/// <summary>
/// Owns the renderer-neutral lifecycle of an in-place text-box edit. Desktop hosts retain native controls,
/// focus, layout, and command execution while this session owns edit identity, the rollback snapshot, and
/// transition planning.
/// </summary>
public sealed class TextBoxInlineEditSession
{
    private Guid? _editingTextBoxId;
    private string? _originalText;

    public const string CommitCommandTitle = TextBoxInlineEditPlanner.CommitCommandTitle;

    public Guid? EditingTextBoxId => _editingTextBoxId;

    public bool IsActive => _editingTextBoxId is not null;

    public bool IsEditing(Guid textBoxId) => _editingTextBoxId == textBoxId;

    public TextBoxInlineEditStartPlan Begin(TextBoxModel textBox)
    {
        ArgumentNullException.ThrowIfNull(textBox);

        _editingTextBoxId = textBox.Id;
        _originalText = textBox.Text;
        return new TextBoxInlineEditStartPlan(textBox.Id, textBox.Text);
    }

    public TextBoxInlineEditCommandPlan? CreateCommitPlan(
        SheetId sheetId,
        string? editedText)
    {
        if (_editingTextBoxId is not { } textBoxId)
            return null;

        var plan = TextBoxInlineEditPlanner.CreateCommitPlan(
            _originalText,
            editedText ?? string.Empty);
        return new TextBoxInlineEditCommandPlan(
            textBoxId,
            plan.Text,
            plan.TextChanged,
            plan.TextChanged
                ? new SetTextBoxTextCommand(sheetId, textBoxId, plan.Text)
                : null);
    }

    public TextBoxInlineEditCancelPlan? CreateCancelPlan() =>
        _editingTextBoxId is { } textBoxId
            ? new TextBoxInlineEditCancelPlan(textBoxId, _originalText ?? string.Empty)
            : null;

    public TextBoxInlineEditKeyAction PlanKeyDown(
        TextBoxInlineEditKey key,
        bool hasModifiers) =>
        TextBoxInlineEditPlanner.PlanKeyDown(key, hasModifiers);

    public bool ShouldCommitLostFocus(
        bool editorVisible,
        bool editorHasKeyboardFocus,
        bool editorHasLogicalFocus) =>
        IsActive && TextBoxInlineEditPlanner.ShouldCommitLostFocus(
            editorVisible,
            editorHasKeyboardFocus,
            editorHasLogicalFocus);

    public void Complete()
    {
        _editingTextBoxId = null;
        _originalText = null;
    }
}
