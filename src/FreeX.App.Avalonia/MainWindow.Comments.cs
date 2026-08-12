using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Ribbon;
using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.Comments;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private static AvaloniaCompactDialogChromeStyle CommentDialogChromeStyle => new(FormulaBarFontFamily);

    // ── Review ▸ Comments/Notes ribbon enablement ───────────────────────────────────────────────
    // Mirrors WPF's RefreshReviewCommentNoteCommandStates (MainWindow.ReviewCommands.cs): Excel greys
    // Delete/navigation/Convert commands based on whether the active cell (or sheet) actually has a
    // note/threaded comment, rather than leaving every Review command permanently enabled and only
    // differentiating via a post-click status message. "New Comment"/"New Note" are gated on having a
    // selection too, for parity with WPF's SheetGrid.SelectedRange?.Start check, even though Avalonia's
    // WorkbookSession.SelectedRange always carries a value (there is always an active cell here).
    private RibbonCommandState GetReviewNewCommentRibbonState() =>
        new(IsEnabled: true);

    private RibbonCommandState GetReviewDeleteCommentRibbonState() =>
        new(IsEnabled: ReviewSessionController.HasThreadedCommentAtSelection());

    private RibbonCommandState GetReviewNavigateCommentRibbonState() =>
        new(IsEnabled: _session.ActiveSheet.ThreadedComments.Count > 0);

    private RibbonCommandState GetReviewNewNoteRibbonState() =>
        new(IsEnabled: true);

    private RibbonCommandState GetReviewNoteAtSelectionRibbonState() =>
        new(IsEnabled: ReviewSessionController.HasNoteAtSelection());

    private RibbonCommandState GetReviewNavigateNoteRibbonState() =>
        new(IsEnabled: _session.ActiveSheet.Comments.Count > 0);

    private RibbonCommandState GetReviewConvertNotesToCommentsRibbonState() =>
        new(IsEnabled: _session.ActiveSheet.Comments.Count > 0);

    // WPF uses the same worksheet-anchored note editor for both New Note and Edit Note.
    // Keep both routes on the shared review-session mutation path for matching undo/redo.
    private Task ShowNewNoteDialogAsync()
    {
        BeginNoteInlineEdit();
        return Task.CompletedTask;
    }

    private Task ShowNewThreadedCommentDialogAsync()
    {
        BeginThreadedCommentInlineEdit(existing: null);
        return Task.CompletedTask;
    }

    private Task ShowEditNoteDialogAsync()
    {
        BeginNoteInlineEdit();
        return Task.CompletedTask;
    }

    private Task ShowEditThreadedCommentDialogAsync()
    {
        var target = ReviewSessionController.GetSelectedThreadedCommentTarget();
        if (target is null || target.ThreadedComment is null)
        {
            RefreshShell(UiText.Get("Comment_NoComment"));
            return Task.CompletedTask;
        }

        BeginThreadedCommentInlineEdit(target.ThreadedComment);
        return Task.CompletedTask;
    }

    private void ResolveActiveCellThreadedComment(bool resolved)
    {
        var result = ReviewSessionController.ResolveThreadedComment(resolved);
        if (!result.Success)
        {
            RefreshShell(result.ErrorMessage ?? UiText.Get("Comment_CommentFailed"));
            return;
        }

        ApplyReviewRefreshPlan(result.RefreshPlan, UiText.Format(
            resolved ? "Comment_Resolved" : "Comment_Unresolved",
            FormatCellReference(_session.ActiveCell)));
    }

    private void ConvertNotesToComments()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var result = ReviewSessionController.ConvertNotesToComments();
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "Convert to Comments failed.");
            return;
        }

        ApplyReviewRefreshPlan(result.RefreshPlan, "Converted notes to comments.");
    }

    private void ToggleActiveCellNoteVisibility()
    {
        var result = ReviewSessionController.ToggleNoteVisibility(_session.ActiveCell);
        if (!result.Success)
        {
            RefreshShell(result.ErrorMessage ?? UiText.Get("Comment_NoNote"));
            return;
        }

        ApplyReviewRefreshPlan(result.RefreshPlan, "Show/Hide Note");
    }

    private void ToggleAllNotesVisibility()
    {
        var result = ReviewSessionController.ToggleAllNotesVisibility();
        if (!result.Success)
        {
            RefreshShell(result.ErrorMessage ?? UiText.Get("Comment_NoNote"));
            return;
        }

        ApplyReviewRefreshPlan(result.RefreshPlan, "Show All Notes");
    }

    // ── Threaded comment dialog: create / edit root / reply / edit-reply / delete-reply ────────
    // Mirrors the WPF host's ThreadedCommentDialog, sharing its
    // portable ThreadedCommentDialogPlanner (validation + result-shaping) so both shells agree on
    // behavior. Previously this shell could only set/replace the root comment text or toggle resolved
    // (WorkbookSession.SetActiveCellThreadedComment / EditActiveCellThreadedComment /
    // SetActiveCellThreadedCommentResolved) with no way to add, edit, or delete a reply — a thread
    // authored (with replies) on Windows was reply-read-only when opened on Linux/macOS. The
    // Add/Update/Delete-reply commands (FreeX.Core.Commands.ThreadedCommentCommands) already exist and
    // are routed through the existing generic WorkbookSession.ExecuteReviewCommand (used just above by
    // ConvertNotesToComments), so no WorkbookSession changes are needed.
    private async Task ShowThreadedCommentDialogAsync()
    {
        var target = ReviewSessionController.GetSelectedThreadedCommentTarget();
        var existing = target?.ThreadedComment;
        var cellRef = FormatCellReference(_session.ActiveCell);
        var dialogResult = await ShowThreadedCommentEditorAsync(cellRef, existing);
        if (dialogResult is null)
            return;

        var mutation = ApplyThreadedCommentDialogResult(existing, dialogResult);
        if (!mutation.Success)
        {
            RefreshShell(mutation.ErrorMessage ?? UiText.Get("Comment_CommentFailed"));
            return;
        }

        ApplyReviewRefreshPlan(mutation.RefreshPlan, existing is null
            ? $"Added comment to {cellRef}"
            : UiText.Format("Comment_CommentUpdated", cellRef));
    }

    private PresentationReviewMutationResult ApplyThreadedCommentDialogResult(
        ThreadedComment? existing,
        ThreadedCommentDialogResult dialogResult)
    {
        _ = existing;
        return ReviewSessionController.ApplyThreadedComment(dialogResult);
    }

    /// <summary>
    /// Shows the threaded-comment editor dialog and returns the user's chosen action as a
    /// <see cref="ThreadedCommentDialogResult"/>, or <c>null</c> if the dialog was cancelled.
    /// </summary>
    private async Task<ThreadedCommentDialogResult?> ShowThreadedCommentEditorAsync(string cellRef, ThreadedComment? existing)
    {
        var style = CommentDialogChromeStyle;
        ThreadedCommentDialogResult? dialogResult = null;
        Window? dialogRef = null;

        void Accept(ThreadedCommentDialogResult result)
        {
            dialogResult = result;
            dialogRef?.Close();
        }

        var rootBox = new TextBox { AcceptsReturn = true, MinWidth = 320, MinHeight = 60, TextWrapping = TextWrapping.Wrap, Text = existing?.Text ?? string.Empty };
        AvaloniaCompactDialogChrome.ApplyTextBox(rootBox, style, fixedHeight: false);
        AutomationProperties.SetName(rootBox, existing is null ? UiText.Get("ThreadedComment_CommentAutomationName") : UiText.Get("ThreadedComment_EditCommentAutomationName"));
        AutomationProperties.SetAutomationId(rootBox, "ThreadedCommentRootBox");
        AutomationProperties.SetHelpText(rootBox, existing is null ? UiText.Get("ThreadedComment_CommentHelpText") : UiText.Get("ThreadedComment_EditCommentHelpText"));

        var replyBox = new TextBox { AcceptsReturn = true, MinWidth = 320, MinHeight = 48, TextWrapping = TextWrapping.Wrap };
        AvaloniaCompactDialogChrome.ApplyTextBox(replyBox, style, fixedHeight: false);
        AutomationProperties.SetName(replyBox, UiText.Get("ThreadedComment_ReplyAutomationName"));
        AutomationProperties.SetAutomationId(replyBox, "ThreadedCommentReplyBox");
        AutomationProperties.SetHelpText(replyBox, UiText.Get("ThreadedComment_ReplyHelpText"));

        var resolveBox = new CheckBox { Content = UiText.Get("ThreadedComment_MarkAsResolved"), IsChecked = existing?.IsResolved ?? false };
        AvaloniaCompactDialogChrome.ApplyCheckBox(resolveBox, style);
        AutomationProperties.SetName(resolveBox, UiText.Get("ThreadedComment_MarkAsResolvedAutomationName"));
        AutomationProperties.SetAutomationId(resolveBox, "ThreadedCommentResolvedBox");
        AutomationProperties.SetHelpText(resolveBox, UiText.Get("ThreadedComment_MarkAsResolvedHelpText"));

        var validationText = new TextBlock();
        AvaloniaCompactDialogChrome.ApplyValidationStatus(validationText, style, new Thickness(0, 4, 0, 0));

        var ok = new Button { Content = existing is null ? UiText.Get("ThreadedComment_AddButton") : UiText.Get("ThreadedComment_ReplyButton"), IsDefault = true };
        var cancel = new Button { Content = UiText.Get("ThreadedComment_CancelButton"), IsCancel = true };
        AvaloniaCompactDialogChrome.ApplyButton(ok, style, 84, isDefault: true);
        AvaloniaCompactDialogChrome.ApplyButton(cancel, style, 84);
        AutomationProperties.SetName(ok, existing is null ? UiText.Get("ThreadedComment_AddCommentAutomationName") : UiText.Get("ThreadedComment_ReplyToCommentAutomationName"));
        AutomationProperties.SetAutomationId(ok, existing is null ? "ThreadedCommentAddButton" : "ThreadedCommentReplyButton");
        AutomationProperties.SetHelpText(ok, existing is null ? UiText.Get("ThreadedComment_AddCommentHelpText") : UiText.Get("ThreadedComment_ReplyToCommentHelpText"));
        AutomationProperties.SetName(cancel, UiText.CreateAutomationName(UiText.Cancel));
        AutomationProperties.SetAutomationId(cancel, "ThreadedCommentCancelButton");
        AutomationProperties.SetHelpText(cancel, UiText.Get("ThreadedComment_CancelHelpText"));

        var dialog = new Window
        {
            Title = UiText.Format("ThreadedComment_TitleFormat", cellRef),
            Width = 480,
            MinHeight = 280,
            MaxHeight = 640,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            CanResize = false,
        };
        dialogRef = dialog;

        var content = new StackPanel { Margin = new Thickness(14) };

        if (existing is not null)
        {
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                MaxHeight = existing.Replies.Count > 0 ? 160 : 260,
                Margin = new Thickness(0, 0, 0, 8),
            };
            var threadPanel = new StackPanel();
            threadPanel.Children.Add(BuildThreadMessage(existing.Author, existing.Text, existing.CreatedAtUtc, isRoot: true));
            foreach (var reply in existing.Replies)
                threadPanel.Children.Add(BuildThreadMessage(reply.Author, reply.Text, reply.CreatedAtUtc, isRoot: false));
            scroll.Content = threadPanel;
            content.Children.Add(scroll);
        }

        content.Children.Add(new Label
        {
            Content = existing is null ? UiText.Get("ThreadedComment_CommentLabel") : UiText.Get("ThreadedComment_EditCommentLabel"),
            Target = rootBox,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 2),
        });
        content.Children.Add(rootBox);

        if (existing is not null)
        {
            if (existing.Replies.Count > 0)
                BuildReplyEditor(content, existing, style, Accept);

            content.Children.Add(new Label
            {
                Content = UiText.Get("ThreadedComment_ReplyLabel"),
                Target = replyBox,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 8, 0, 2),
            });
            content.Children.Add(replyBox);
        }

        content.Children.Add(resolveBox);
        content.Children.Add(validationText);
        content.Children.Add(AvaloniaCompactDialogChrome.CreateActionRow([cancel, ok], new Thickness(0, 10, 0, 0)));

        dialog.Content = content;

        void ShowValidationMessage(string message, TextBox focusTarget)
        {
            validationText.Text = message;
            validationText.IsVisible = true;
            focusTarget.Focus();
        }

        void Submit()
        {
            if (!ThreadedCommentDialogPlanner.TryCreateResult(
                    existing,
                    rootBox.Text,
                    replyBox.Text,
                    resolveBox.IsChecked == true,
                    out var result,
                    out var error))
            {
                ShowValidationMessage(DescribeValidationError(error), rootBox);
                return;
            }

            Accept(result);
        }

        ok.Click += (_, _) => Submit();
        cancel.Click += (_, _) => dialog.Close();
        replyBox.KeyDown += (_, e) =>
        {
            if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.Enter)
            {
                Submit();
                e.Handled = true;
            }
        };

        dialog.Opened += (_, _) => (existing is null ? rootBox : replyBox).Focus();

        await dialog.ShowDialog(this);
        return dialogResult;
    }

    private static void BuildReplyEditor(
        StackPanel content,
        ThreadedComment existing,
        AvaloniaCompactDialogChromeStyle style,
        Action<ThreadedCommentDialogResult> accept)
    {
        var selector = new ComboBox { MinWidth = 200 };
        AvaloniaCompactDialogChrome.ApplyComboBox(selector, style);
        AutomationProperties.SetName(selector, UiText.Get("ThreadedComment_ReplyToEditOrDeleteAutomationName"));
        AutomationProperties.SetAutomationId(selector, ThreadedCommentDialogPlanner.ReplySelectorAutomationId);
        AutomationProperties.SetHelpText(selector, UiText.Get("ThreadedComment_ReplySelectorHelpText"));
        for (var i = 0; i < existing.Replies.Count; i++)
        {
            var descriptor = ThreadedCommentDialogPlanner.DescribeReply(i, existing.Replies[i]);
            var item = new ComboBoxItem { Content = descriptor.ChoiceText };
            AutomationProperties.SetName(item, descriptor.AutomationName.Resolve(UiText.Get, UiText.Format));
            selector.Items.Add(item);
        }

        var selectedReplyBox = new TextBox { AcceptsReturn = true, MinWidth = 320, MinHeight = 48, TextWrapping = TextWrapping.Wrap };
        AvaloniaCompactDialogChrome.ApplyTextBox(selectedReplyBox, style, fixedHeight: false);
        AutomationProperties.SetName(selectedReplyBox, UiText.Get("ThreadedComment_SelectedReplyTextAutomationName"));
        AutomationProperties.SetAutomationId(selectedReplyBox, ThreadedCommentDialogPlanner.SelectedReplyEditorAutomationId);
        AutomationProperties.SetHelpText(selectedReplyBox, UiText.Get("ThreadedComment_SelectedReplyTextHelpText"));

        var updateButton = new Button { Content = UiText.Get("ThreadedComment_UpdateReplyButton") };
        var deleteButton = new Button { Content = UiText.Get("ThreadedComment_DeleteReplyButton") };
        AvaloniaCompactDialogChrome.ApplyButton(updateButton, style, 110);
        AvaloniaCompactDialogChrome.ApplyButton(deleteButton, style, 110);
        AutomationProperties.SetName(updateButton, UiText.Get("ThreadedComment_UpdateSelectedReplyAutomationName"));
        AutomationProperties.SetAutomationId(updateButton, ThreadedCommentDialogPlanner.UpdateReplyAutomationId);
        AutomationProperties.SetHelpText(updateButton, UiText.Get("ThreadedComment_UpdateSelectedReplyHelpText"));
        AutomationProperties.SetName(deleteButton, UiText.Get("ThreadedComment_DeleteSelectedReplyAutomationName"));
        AutomationProperties.SetAutomationId(deleteButton, ThreadedCommentDialogPlanner.DeleteReplyAutomationId);
        AutomationProperties.SetHelpText(deleteButton, UiText.Get("ThreadedComment_DeleteSelectedReplyHelpText"));

        void PopulateSelectedReplyText()
        {
            var index = selector.SelectedIndex;
            selectedReplyBox.Text = ThreadedCommentDialogPlanner.IsValidReplyIndex(existing, index)
                ? existing.Replies[index].Text
                : string.Empty;
            UpdateActionState();
        }

        void UpdateActionState()
        {
            var hasSelection = ThreadedCommentDialogPlanner.IsValidReplyIndex(existing, selector.SelectedIndex);
            deleteButton.IsEnabled = hasSelection;
            updateButton.IsEnabled = hasSelection && !string.IsNullOrWhiteSpace(selectedReplyBox.Text);
        }

        selector.SelectionChanged += (_, _) => PopulateSelectedReplyText();
        selectedReplyBox.TextChanged += (_, _) => UpdateActionState();

        updateButton.Click += (_, _) =>
        {
            if (!ThreadedCommentDialogPlanner.TryCreateReplyEditResult(
                    existing,
                    selector.SelectedIndex,
                    selectedReplyBox.Text,
                    out var result,
                    out _))
            {
                return;
            }

            accept(result);
        };
        deleteButton.Click += (_, _) =>
        {
            if (!ThreadedCommentDialogPlanner.TryCreateReplyDeleteResult(
                    existing,
                    selector.SelectedIndex,
                    out var result,
                    out _))
            {
                return;
            }

            accept(result);
        };

        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        panel.Children.Add(new Label
        {
            Content = UiText.Get("ThreadedComment_SelectReplyLabel"),
            Target = selector,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 2),
        });
        panel.Children.Add(selector);
        panel.Children.Add(new Label
        {
            Content = UiText.Get("ThreadedComment_SelectedReplyTextLabel"),
            Target = selectedReplyBox,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 8, 0, 2),
        });
        panel.Children.Add(selectedReplyBox);
        panel.Children.Add(AvaloniaCompactDialogChrome.CreateActionRow([updateButton, deleteButton], new Thickness(0, 8, 0, 0)));
        content.Children.Add(panel);

        selector.SelectedIndex = 0;
        PopulateSelectedReplyText();
    }

    private static Border BuildThreadMessage(string author, string text, DateTimeOffset? createdAtUtc, bool isRoot)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        panel.Children.Add(new TextBlock
        {
            Text = ThreadedCommentDialogPlanner.FormatMessageHeading(author, createdAtUtc),
            FontWeight = FontWeight.SemiBold,
            FontSize = 11,
            Foreground = new SolidColorBrush(isRoot ? Color.FromRgb(0x1F, 0x49, 0x7D) : Color.FromRgb(0x40, 0x40, 0x40)),
        });
        panel.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(8, 2, 0, 0),
        });
        return new Border
        {
            Child = panel,
            Background = new SolidColorBrush(isRoot ? Color.FromRgb(0xF0, 0xF4, 0xF8) : Colors.White),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 4),
        };
    }

    private static string DescribeValidationError(ThreadedCommentDialogValidationError error) =>
        (ThreadedCommentDialogPlanner.DescribeValidationError(error)
         ?? ThreadedCommentDialogPlanner.DescribeValidationError(ThreadedCommentDialogValidationError.EnterComment)!)
        .Message.Resolve(UiText.Get, UiText.Format);

    private async Task<string?> ShowCommentTextPromptAsync(string title, string label, string? initialText = null)
    {
        var box = new TextBox { AcceptsReturn = true, MinWidth = 320, MinHeight = 72, TextWrapping = TextWrapping.Wrap, Text = initialText ?? string.Empty };
        AvaloniaCompactDialogChrome.ApplyTextBox(box, CommentDialogChromeStyle, fixedHeight: false);
        AutomationProperties.SetName(box, label);
        AutomationProperties.SetAutomationId(box, "CommentTextBox");

        var ok = new Button { Content = UiText.CreateAutomationName(UiText.Get("Common_Ok")), IsDefault = true };
        var cancel = new Button { Content = UiText.CreateAutomationName(UiText.Get("Common_Cancel")), IsCancel = true };
        AvaloniaCompactDialogChrome.ApplyButton(ok, CommentDialogChromeStyle, 84, isDefault: true);
        AvaloniaCompactDialogChrome.ApplyButton(cancel, CommentDialogChromeStyle, 84);

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
                    AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0, 10, 0, 0)),
                },
            },
        };

        ok.Click += (_, _) => dialog.Close(box.Text ?? string.Empty);
        cancel.Click += (_, _) => dialog.Close(null);
        return await dialog.ShowDialog<string?>(this);
    }
}
