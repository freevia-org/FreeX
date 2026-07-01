namespace FreeX.App.Presentation.DrawingUI;

public enum TextBoxInlineEditKey
{
    Other,
    Escape,
    Enter,
    Return,
    Tab
}

public enum TextBoxInlineEditKeyAction
{
    None,
    Commit,
    Cancel
}

public sealed record TextBoxInlineEditCommitPlan(
    string Text,
    bool TextChanged);

public static class TextBoxInlineEditPlanner
{
    public const string CommitCommandTitle = "Edit Text Box";

    public static TextBoxInlineEditKeyAction PlanKeyDown(
        TextBoxInlineEditKey key,
        bool hasModifiers) =>
        key switch
        {
            TextBoxInlineEditKey.Escape when !hasModifiers => TextBoxInlineEditKeyAction.Cancel,
            TextBoxInlineEditKey.Enter or TextBoxInlineEditKey.Return when !hasModifiers => TextBoxInlineEditKeyAction.Commit,
            TextBoxInlineEditKey.Tab => TextBoxInlineEditKeyAction.Commit,
            _ => TextBoxInlineEditKeyAction.None
        };

    public static TextBoxInlineEditCommitPlan CreateCommitPlan(
        string? originalText,
        string editedText) =>
        new(editedText, !string.Equals(originalText, editedText, StringComparison.Ordinal));

    public static bool ShouldCommitLostFocus(
        bool editorVisible,
        bool editorHasKeyboardFocus,
        bool editorHasLogicalFocus) =>
        editorVisible &&
        !editorHasKeyboardFocus &&
        !editorHasLogicalFocus;
}
