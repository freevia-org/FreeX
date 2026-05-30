using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public enum ThreadedCommentDialogAction
{
    ApplyThread,
    EditReply,
    DeleteReply
}

public sealed record ThreadedCommentDialogResult(
    string? RootText,
    string? ReplyText,
    bool IsResolved,
    ThreadedCommentDialogAction Action = ThreadedCommentDialogAction.ApplyThread,
    int? ReplyIndex = null,
    string? ReplyEditText = null);

public sealed class ThreadedCommentDialog : Window
{
    private readonly TextBox _rootBox = new() { AcceptsReturn = true, MinLines = 3, MaxLines = 6 };
    private readonly TextBox _replyBox = new() { AcceptsReturn = true, MinLines = 3, MaxLines = 6 };
    private readonly ComboBox _replySelector = new() { MinWidth = 180 };
    private readonly TextBox _selectedReplyBox = new() { AcceptsReturn = true, MinLines = 2, MaxLines = 5 };
    private readonly CheckBox _resolveBox;

    public ThreadedCommentDialogResult Result { get; private set; } = new(null, null, false);

    public ThreadedCommentDialog(string cellRef, ThreadedComment? existing)
    {
        Title = $"Comment - {cellRef}";
        Width = 480;
        MinHeight = 280;
        MaxHeight = 600;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        _resolveBox = new CheckBox
        {
            Content = "_Mark as Resolved",
            IsChecked = existing?.IsResolved ?? false,
            Margin = new Thickness(0, 4, 0, 8)
        };

        var root = new DockPanel { Margin = new Thickness(12) };

        var ok = new Button { Content = existing is null ? "_Add" : "_Reply", IsDefault = true, Width = 80, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Ca_ncel", IsCancel = true, Width = 80 };
        AutomationProperties.SetName(ok, existing is null ? "Add comment" : "Reply to comment");
        AutomationProperties.SetAutomationId(ok, existing is null ? "ThreadedCommentAddButton" : "ThreadedCommentReplyButton");
        AutomationProperties.SetHelpText(ok, existing is null ? "Add the threaded comment." : "Add a reply to the threaded comment.");
        AutomationProperties.SetName(cancel, "Cancel");
        AutomationProperties.SetAutomationId(cancel, "ThreadedCommentCancelButton");
        AutomationProperties.SetHelpText(cancel, "Close the comment dialog without applying changes.");
        ok.Click += (_, _) => SubmitThreadedCommentDialog(existing);
        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };
        btnRow.Children.Add(ok);
        btnRow.Children.Add(cancel);
        DockPanel.SetDock(btnRow, Dock.Bottom);
        root.Children.Add(btnRow);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = existing is not null && existing.Replies.Count > 0 ? 180 : 300
        };
        var threadPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        if (existing is not null)
        {
            threadPanel.Children.Add(BuildMessage(existing.Author, existing.Text, isRoot: true));
            foreach (var reply in existing.Replies)
                threadPanel.Children.Add(BuildMessage(reply.Author, reply.Text, isRoot: false));
        }
        scroll.Content = threadPanel;

        var inner = new StackPanel();
        inner.Children.Add(scroll);
        _rootBox.Text = existing?.Text ?? "";
        AutomationProperties.SetName(_rootBox, existing is null ? "Comment" : "Edit comment");
        AutomationProperties.SetAutomationId(_rootBox, "ThreadedCommentRootBox");
        AutomationProperties.SetHelpText(_rootBox, existing is null ? "Enter the threaded comment text." : "Edit the root comment text.");
        inner.Children.Add(new Label { Content = existing is null ? "_Comment:" : "Edit _comment:", Target = _rootBox, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 2) });
        inner.Children.Add(_rootBox);
        if (existing is not null)
        {
            if (existing.Replies.Count > 0)
                inner.Children.Add(BuildSelectedReplyEditor(existing));

            inner.Children.Add(new Label { Content = "Repl_y:", Target = _replyBox, Padding = new Thickness(0), Margin = new Thickness(0, 8, 0, 2) });
            AutomationProperties.SetName(_replyBox, "Reply");
            AutomationProperties.SetAutomationId(_replyBox, "ThreadedCommentReplyBox");
            AutomationProperties.SetHelpText(_replyBox, "Enter an optional reply to the threaded comment. Press Ctrl+Enter to reply.");
            _replyBox.PreviewKeyDown += (_, e) =>
            {
                if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Enter)
                {
                    SubmitThreadedCommentDialog(existing);
                    e.Handled = true;
                }
            };
            inner.Children.Add(_replyBox);
        }
        AutomationProperties.SetName(_resolveBox, "Mark as resolved");
        AutomationProperties.SetAutomationId(_resolveBox, "ThreadedCommentResolvedBox");
        AutomationProperties.SetHelpText(_resolveBox, "Mark the threaded comment as resolved.");
        inner.Children.Add(_resolveBox);
        root.Children.Add(inner);

        Content = root;
        Loaded += (_, _) =>
        {
            var target = existing is null ? _rootBox : _replyBox;
            target.Focus();
            Keyboard.Focus(target);
        };
    }

    private void SubmitThreadedCommentDialog(ThreadedComment? existing)
    {
        if (!TryCreateResult(existing, _rootBox.Text, _replyBox.Text, _resolveBox.IsChecked == true, out var result, out var error))
        {
            ShowInvalidThreadedCommentWarning(error ?? "Enter a comment.", _rootBox);
            return;
        }

        Result = result;
        DialogResult = true;
    }

    public static bool TryCreateResult(
        ThreadedComment? existing,
        string? rootText,
        string? replyText,
        bool isResolved,
        out ThreadedCommentDialogResult result,
        out string? error)
    {
        result = CreateResult(existing, rootText, replyText, isResolved);
        if (existing is not null && string.IsNullOrWhiteSpace(rootText))
        {
            error = "Enter a comment.";
            return false;
        }

        if (existing is null && string.IsNullOrWhiteSpace(result.ReplyText))
        {
            error = "Enter a comment.";
            return false;
        }

        error = null;
        return true;
    }

    public static bool TryCreateReplyEditResult(
        ThreadedComment? existing,
        int replyIndex,
        string? replyText,
        out ThreadedCommentDialogResult result,
        out string? error)
    {
        result = new ThreadedCommentDialogResult(
            null,
            null,
            existing?.IsResolved ?? false,
            ThreadedCommentDialogAction.EditReply,
            replyIndex,
            (replyText ?? "").Trim());
        if (existing is null)
        {
            error = "No threaded comment is available.";
            return false;
        }

        if (!IsValidReplyIndex(existing, replyIndex))
        {
            error = "Select a reply.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(result.ReplyEditText))
        {
            error = "Enter a reply.";
            return false;
        }

        error = null;
        return true;
    }

    public static bool TryCreateReplyDeleteResult(
        ThreadedComment? existing,
        int replyIndex,
        out ThreadedCommentDialogResult result,
        out string? error)
    {
        result = new ThreadedCommentDialogResult(
            null,
            null,
            existing?.IsResolved ?? false,
            ThreadedCommentDialogAction.DeleteReply,
            replyIndex);
        if (existing is null)
        {
            error = "No threaded comment is available.";
            return false;
        }

        if (!IsValidReplyIndex(existing, replyIndex))
        {
            error = "Select a reply.";
            return false;
        }

        error = null;
        return true;
    }

    public static ThreadedCommentDialogResult CreateResult(
        ThreadedComment? existing,
        string? rootText,
        string? replyText,
        bool isResolved)
    {
        var trimmedRoot = (rootText ?? "").Trim();
        var trimmedReply = (replyText ?? "").Trim();
        if (existing is null)
        {
            return new ThreadedCommentDialogResult(
                null,
                string.IsNullOrWhiteSpace(trimmedRoot) ? null : trimmedRoot,
                isResolved);
        }

        var rootEdit = !string.IsNullOrWhiteSpace(trimmedRoot)
            && !string.Equals(trimmedRoot, existing.Text, StringComparison.Ordinal)
                ? trimmedRoot
                : null;
        return new ThreadedCommentDialogResult(
            rootEdit,
            string.IsNullOrWhiteSpace(trimmedReply) ? null : trimmedReply,
            isResolved);
    }

    private StackPanel BuildSelectedReplyEditor(ThreadedComment existing)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        AutomationProperties.SetName(_replySelector, "Reply to edit or delete");
        AutomationProperties.SetAutomationId(_replySelector, "ThreadedCommentReplySelector");
        AutomationProperties.SetHelpText(_replySelector, "Select a threaded comment reply to edit or delete.");
        for (var i = 0; i < existing.Replies.Count; i++)
        {
            var item = new ComboBoxItem { Content = FormatReplyChoice(i, existing.Replies[i]) };
            AutomationProperties.SetName(item, FormatReplyAutomationName(i, existing.Replies[i]));
            _replySelector.Items.Add(item);
        }

        _replySelector.SelectionChanged += (_, _) => PopulateSelectedReplyText(existing);
        _replySelector.SelectedIndex = 0;
        panel.Children.Add(new Label { Content = "Select re_ply:", Target = _replySelector, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 2) });
        panel.Children.Add(_replySelector);

        AutomationProperties.SetName(_selectedReplyBox, "Selected reply text");
        AutomationProperties.SetAutomationId(_selectedReplyBox, "ThreadedCommentSelectedReplyBox");
        AutomationProperties.SetHelpText(_selectedReplyBox, "Edit the selected reply text before choosing Update Reply.");
        panel.Children.Add(new Label { Content = "Selected reply te_xt:", Target = _selectedReplyBox, Padding = new Thickness(0), Margin = new Thickness(0, 8, 0, 2) });
        panel.Children.Add(_selectedReplyBox);

        var updateReply = new Button { Content = "_Update Reply", Width = 112, Margin = new Thickness(0, 8, 8, 0) };
        var deleteReply = new Button { Content = "_Delete Reply", Width = 112, Margin = new Thickness(0, 8, 0, 0) };
        AutomationProperties.SetName(updateReply, "Update selected reply");
        AutomationProperties.SetAutomationId(updateReply, "ThreadedCommentUpdateReplyButton");
        AutomationProperties.SetHelpText(updateReply, "Update the selected threaded comment reply.");
        AutomationProperties.SetName(deleteReply, "Delete selected reply");
        AutomationProperties.SetAutomationId(deleteReply, "ThreadedCommentDeleteReplyButton");
        AutomationProperties.SetHelpText(deleteReply, "Delete the selected threaded comment reply.");
        updateReply.Click += (_, _) => SubmitThreadedCommentReplyEdit(existing);
        deleteReply.Click += (_, _) => SubmitThreadedCommentReplyDelete(existing);

        var actionRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
        actionRow.Children.Add(updateReply);
        actionRow.Children.Add(deleteReply);
        panel.Children.Add(actionRow);
        PopulateSelectedReplyText(existing);
        return panel;
    }

    private void PopulateSelectedReplyText(ThreadedComment existing)
    {
        var replyIndex = _replySelector.SelectedIndex;
        _selectedReplyBox.Text = IsValidReplyIndex(existing, replyIndex)
            ? existing.Replies[replyIndex].Text
            : "";
    }

    private void SubmitThreadedCommentReplyEdit(ThreadedComment existing)
    {
        if (!TryCreateReplyEditResult(existing, _replySelector.SelectedIndex, _selectedReplyBox.Text, out var result, out var error))
        {
            ShowInvalidThreadedCommentWarning(error ?? "Enter a reply.", _selectedReplyBox);
            return;
        }

        Result = result;
        DialogResult = true;
    }

    private void SubmitThreadedCommentReplyDelete(ThreadedComment existing)
    {
        if (!TryCreateReplyDeleteResult(existing, _replySelector.SelectedIndex, out var result, out var error))
        {
            ShowInvalidThreadedCommentWarning(error ?? "Select a reply.", _selectedReplyBox);
            return;
        }

        Result = result;
        DialogResult = true;
    }

    private static bool IsValidReplyIndex(ThreadedComment comment, int replyIndex) =>
        replyIndex >= 0 && replyIndex < comment.Replies.Count;

    private static string FormatReplyChoice(int index, CommentReply reply) =>
        $"{index + 1}. {reply.Author}: {SummarizeReplyText(reply.Text)}";

    private static string FormatReplyAutomationName(int index, CommentReply reply) =>
        $"Reply {index + 1} by {reply.Author}: {SummarizeReplyText(reply.Text)}";

    private static string SummarizeReplyText(string text)
    {
        var normalized = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= 60 ? normalized : normalized[..57] + "...";
    }

    private static Border BuildMessage(string author, string text, bool isRoot)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        panel.Children.Add(new TextBlock
        {
            Text = author,
            FontWeight = FontWeights.SemiBold,
            FontSize = 11,
            Foreground = new SolidColorBrush(isRoot ? Color.FromRgb(0x1F, 0x49, 0x7D) : Color.FromRgb(0x40, 0x40, 0x40))
        });
        panel.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(8, 2, 0, 0)
        });
        return new Border
        {
            Child = panel,
            Background = new SolidColorBrush(isRoot ? Color.FromRgb(0xF0, 0xF4, 0xF8) : Colors.White),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 4)
        };
    }

    private void ShowInvalidThreadedCommentWarning(string message, TextBox target)
    {
        DialogMessageHelper.ShowWarning(this, message, Title);
        target.Focus();
        target.SelectAll();
        Keyboard.Focus(target);
    }
}
