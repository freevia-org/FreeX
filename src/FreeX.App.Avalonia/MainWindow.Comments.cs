using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    // New Note / New Comment on the active cell (parity gap: the shell could navigate/clear comments
    // and notes but not create them). Routes through WorkbookSession.SetActiveCellNote /
    // SetActiveCellThreadedComment (SetCommentCommand / SetThreadedCommentCommand) for full undo/redo.

    private async Task ShowNewNoteDialogAsync()
    {
        var text = await ShowCommentTextPromptAsync("New Note", "Note text");
        if (string.IsNullOrWhiteSpace(text))
            return;
        var result = _session.SetActiveCellNote(text);
        RefreshShell(result.Success
            ? $"Added note to {FormatCellReference(_session.ActiveCell)}"
            : result.ErrorMessage ?? "Could not add note.");
    }

    private async Task ShowNewThreadedCommentDialogAsync()
    {
        var text = await ShowCommentTextPromptAsync("New Comment", "Comment text");
        if (string.IsNullOrWhiteSpace(text))
            return;
        var result = _session.SetActiveCellThreadedComment(text);
        RefreshShell(result.Success
            ? $"Added comment to {FormatCellReference(_session.ActiveCell)}"
            : result.ErrorMessage ?? "Could not add comment.");
    }

    private async Task ShowEditNoteDialogAsync()
    {
        var existing = _session.GetActiveCellNote();
        if (existing is null)
        {
            RefreshShell(UiText.Get("Comment_NoNote"));
            return;
        }

        var text = await ShowCommentTextPromptAsync(UiText.Get("Comment_EditNoteTitle"), UiText.Get("Comment_NoteLabel"), existing);
        if (text is null)
            return;
        var result = _session.SetActiveCellNote(text);
        RefreshShell(result.Success
            ? UiText.Format("Comment_NoteUpdated", FormatCellReference(_session.ActiveCell))
            : result.ErrorMessage ?? UiText.Get("Comment_NoteFailed"));
    }

    private async Task ShowEditThreadedCommentDialogAsync()
    {
        var existing = _session.GetActiveCellThreadedCommentText();
        if (existing is null)
        {
            RefreshShell(UiText.Get("Comment_NoComment"));
            return;
        }

        var text = await ShowCommentTextPromptAsync(UiText.Get("Comment_EditCommentTitle"), UiText.Get("Comment_CommentLabel"), existing);
        if (string.IsNullOrWhiteSpace(text))
            return;
        var result = _session.EditActiveCellThreadedComment(text);
        RefreshShell(result.Success
            ? UiText.Format("Comment_CommentUpdated", FormatCellReference(_session.ActiveCell))
            : result.ErrorMessage ?? UiText.Get("Comment_CommentFailed"));
    }

    private void ResolveActiveCellThreadedComment(bool resolved)
    {
        var result = _session.SetActiveCellThreadedCommentResolved(resolved);
        if (!result.Success)
        {
            RefreshShell(result.ErrorMessage ?? UiText.Get("Comment_CommentFailed"));
            return;
        }

        RefreshShell(UiText.Format(
            resolved ? "Comment_Resolved" : "Comment_Unresolved",
            FormatCellReference(_session.ActiveCell)));
    }

    private async Task<string?> ShowCommentTextPromptAsync(string title, string label, string? initialText = null)
    {
        var box = new TextBox { AcceptsReturn = true, MinWidth = 320, MinHeight = 72, TextWrapping = TextWrapping.Wrap, Text = initialText ?? string.Empty };
        AutomationProperties.SetName(box, label);
        AutomationProperties.SetAutomationId(box, "CommentTextBox");

        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 84, Padding = new Thickness(10, 4) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 84, Padding = new Thickness(10, 4), Margin = new Thickness(8, 0, 0, 0) };

        var dialog = new Window
        {
            Title = title,
            Width = 420,
            Height = 220,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(14),
                Children =
                {
                    new TextBlock { Text = label + ":", Margin = new Thickness(0, 0, 0, 6) },
                    box,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Margin = new Thickness(0, 10, 0, 0),
                        Children = { ok, cancel },
                    },
                },
            },
        };

        ok.Click += (_, _) => dialog.Close(box.Text ?? string.Empty);
        cancel.Click += (_, _) => dialog.Close(null);
        return await dialog.ShowDialog<string?>(this);
    }
}
