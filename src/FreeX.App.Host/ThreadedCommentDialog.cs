using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FreeX.App.Presentation.Comments;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed class ThreadedCommentDialog : Window
{
    private readonly TextBox _rootBox = new() { AcceptsReturn = true, MinLines = 3, MaxLines = 6 };
    private readonly TextBox _replyBox = new() { AcceptsReturn = true, MinLines = 3, MaxLines = 6 };
    private readonly ComboBox _replySelector = new() { MinWidth = 180 };
    private readonly TextBox _selectedReplyBox = new() { AcceptsReturn = true, MinLines = 2, MaxLines = 5 };
    private readonly Button _updateReplyButton = new() { Width = 112, Margin = new Thickness(0, 8, 8, 0) };
    private readonly Button _deleteReplyButton = new() { Width = 112, Margin = new Thickness(0, 8, 0, 0) };
    private readonly CheckBox _resolveBox;

    public ThreadedCommentDialogResult Result { get; private set; } = new(null, null, false);

    public ThreadedCommentDialog(string cellRef, ThreadedComment? existing)
    {
        Title = UiText.Format("ThreadedComment_TitleFormat", cellRef);
        Width = 480;
        MinHeight = 280;
        MaxHeight = 600;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        _resolveBox = new CheckBox
        {
            Content = UiText.Get("ThreadedComment_MarkAsResolved"),
            IsChecked = existing?.IsResolved ?? false,
            Margin = new Thickness(0, 4, 0, 8)
        };

        var root = new DockPanel { Margin = new Thickness(12) };

        var ok = new Button { Content = existing is null ? UiText.Get("ThreadedComment_AddButton") : UiText.Get("ThreadedComment_ReplyButton"), IsDefault = true, Width = 80, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = UiText.Get("ThreadedComment_CancelButton"), IsCancel = true, Width = 80 };
        AutomationProperties.SetName(ok, existing is null ? UiText.Get("ThreadedComment_AddCommentAutomationName") : UiText.Get("ThreadedComment_ReplyToCommentAutomationName"));
        AutomationProperties.SetAutomationId(ok, existing is null ? "ThreadedCommentAddButton" : "ThreadedCommentReplyButton");
        AutomationProperties.SetHelpText(ok, existing is null ? UiText.Get("ThreadedComment_AddCommentHelpText") : UiText.Get("ThreadedComment_ReplyToCommentHelpText"));
        AutomationProperties.SetName(cancel, UiText.CreateAutomationName(UiText.Cancel));
        AutomationProperties.SetAutomationId(cancel, "ThreadedCommentCancelButton");
        AutomationProperties.SetHelpText(cancel, UiText.Get("ThreadedComment_CancelHelpText"));
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
            threadPanel.Children.Add(BuildMessage(existing.Author, existing.Text, existing.CreatedAtUtc, isRoot: true));
            foreach (var reply in existing.Replies)
                threadPanel.Children.Add(BuildMessage(reply.Author, reply.Text, reply.CreatedAtUtc, isRoot: false));
        }
        scroll.Content = threadPanel;

        var inner = new StackPanel();
        inner.Children.Add(scroll);
        _rootBox.Text = existing?.Text ?? "";
        AutomationProperties.SetName(_rootBox, existing is null ? UiText.Get("ThreadedComment_CommentAutomationName") : UiText.Get("ThreadedComment_EditCommentAutomationName"));
        AutomationProperties.SetAutomationId(_rootBox, "ThreadedCommentRootBox");
        AutomationProperties.SetHelpText(_rootBox, existing is null ? UiText.Get("ThreadedComment_CommentHelpText") : UiText.Get("ThreadedComment_EditCommentHelpText"));
        inner.Children.Add(new Label { Content = existing is null ? UiText.Get("ThreadedComment_CommentLabel") : UiText.Get("ThreadedComment_EditCommentLabel"), Target = _rootBox, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 2) });
        inner.Children.Add(_rootBox);
        if (existing is not null)
        {
            if (existing.Replies.Count > 0)
                inner.Children.Add(BuildSelectedReplyEditor(existing));

            inner.Children.Add(new Label { Content = UiText.Get("ThreadedComment_ReplyLabel"), Target = _replyBox, Padding = new Thickness(0), Margin = new Thickness(0, 8, 0, 2) });
            AutomationProperties.SetName(_replyBox, UiText.Get("ThreadedComment_ReplyAutomationName"));
            AutomationProperties.SetAutomationId(_replyBox, "ThreadedCommentReplyBox");
            AutomationProperties.SetHelpText(_replyBox, UiText.Get("ThreadedComment_ReplyHelpText"));
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
        AutomationProperties.SetName(_resolveBox, UiText.Get("ThreadedComment_MarkAsResolvedAutomationName"));
        AutomationProperties.SetAutomationId(_resolveBox, "ThreadedCommentResolvedBox");
        AutomationProperties.SetHelpText(_resolveBox, UiText.Get("ThreadedComment_MarkAsResolvedHelpText"));
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
            ShowInvalidThreadedCommentWarning(error ?? UiText.Get("ThreadedComment_EnterCommentMessage"), _rootBox);
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
        var success = ThreadedCommentDialogPlanner.TryCreateResult(existing, rootText, replyText, isResolved, out result, out var errorCode);
        error = GetThreadedCommentDialogErrorMessage(errorCode);
        return success;
    }

    public static bool TryCreateReplyEditResult(
        ThreadedComment? existing,
        int replyIndex,
        string? replyText,
        out ThreadedCommentDialogResult result,
        out string? error) =>
        TryCreateReplyEditResult(existing, replyIndex, replyText, existing?.IsResolved ?? false, out result, out error);

    public static bool TryCreateReplyEditResult(
        ThreadedComment? existing,
        int replyIndex,
        string? replyText,
        bool isResolved,
        out ThreadedCommentDialogResult result,
        out string? error)
    {
        var success = ThreadedCommentDialogPlanner.TryCreateReplyEditResult(existing, replyIndex, replyText, isResolved, out result, out var errorCode);
        error = GetThreadedCommentDialogErrorMessage(errorCode);
        return success;
    }

    public static bool TryCreateReplyDeleteResult(
        ThreadedComment? existing,
        int replyIndex,
        out ThreadedCommentDialogResult result,
        out string? error) =>
        TryCreateReplyDeleteResult(existing, replyIndex, existing?.IsResolved ?? false, out result, out error);

    public static bool TryCreateReplyDeleteResult(
        ThreadedComment? existing,
        int replyIndex,
        bool isResolved,
        out ThreadedCommentDialogResult result,
        out string? error)
    {
        var success = ThreadedCommentDialogPlanner.TryCreateReplyDeleteResult(existing, replyIndex, isResolved, out result, out var errorCode);
        error = GetThreadedCommentDialogErrorMessage(errorCode);
        return success;
    }

    public static ThreadedCommentDialogResult CreateResult(
        ThreadedComment? existing,
        string? rootText,
        string? replyText,
        bool isResolved) =>
        ThreadedCommentDialogPlanner.CreateResult(existing, rootText, replyText, isResolved);

    private StackPanel BuildSelectedReplyEditor(ThreadedComment existing)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        AutomationProperties.SetName(_replySelector, UiText.Get("ThreadedComment_ReplyToEditOrDeleteAutomationName"));
        AutomationProperties.SetAutomationId(_replySelector, "ThreadedCommentReplySelector");
        AutomationProperties.SetHelpText(_replySelector, UiText.Get("ThreadedComment_ReplySelectorHelpText"));
        for (var i = 0; i < existing.Replies.Count; i++)
        {
            var item = new ComboBoxItem { Content = FormatReplyChoice(i, existing.Replies[i]) };
            AutomationProperties.SetName(item, FormatReplyAutomationName(i, existing.Replies[i]));
            _replySelector.Items.Add(item);
        }

        _replySelector.SelectionChanged += (_, _) => PopulateSelectedReplyText(existing);
        _replySelector.SelectedIndex = 0;
        panel.Children.Add(new Label { Content = UiText.Get("ThreadedComment_SelectReplyLabel"), Target = _replySelector, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 2) });
        panel.Children.Add(_replySelector);

        AutomationProperties.SetName(_selectedReplyBox, UiText.Get("ThreadedComment_SelectedReplyTextAutomationName"));
        AutomationProperties.SetAutomationId(_selectedReplyBox, "ThreadedCommentSelectedReplyBox");
        AutomationProperties.SetHelpText(_selectedReplyBox, UiText.Get("ThreadedComment_SelectedReplyTextHelpText"));
        _selectedReplyBox.TextChanged += (_, _) => UpdateSelectedReplyActionState(existing);
        _selectedReplyBox.PreviewKeyDown += (_, e) =>
        {
            if (_updateReplyButton.IsEnabled && Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Enter)
            {
                SubmitThreadedCommentReplyEdit(existing);
                e.Handled = true;
            }
        };
        panel.Children.Add(new Label { Content = UiText.Get("ThreadedComment_SelectedReplyTextLabel"), Target = _selectedReplyBox, Padding = new Thickness(0), Margin = new Thickness(0, 8, 0, 2) });
        panel.Children.Add(_selectedReplyBox);

        _updateReplyButton.Content = UiText.Get("ThreadedComment_UpdateReplyButton");
        _deleteReplyButton.Content = UiText.Get("ThreadedComment_DeleteReplyButton");
        AutomationProperties.SetName(_updateReplyButton, UiText.Get("ThreadedComment_UpdateSelectedReplyAutomationName"));
        AutomationProperties.SetAutomationId(_updateReplyButton, "ThreadedCommentUpdateReplyButton");
        AutomationProperties.SetHelpText(_updateReplyButton, UiText.Get("ThreadedComment_UpdateSelectedReplyHelpText"));
        AutomationProperties.SetName(_deleteReplyButton, UiText.Get("ThreadedComment_DeleteSelectedReplyAutomationName"));
        AutomationProperties.SetAutomationId(_deleteReplyButton, "ThreadedCommentDeleteReplyButton");
        AutomationProperties.SetHelpText(_deleteReplyButton, UiText.Get("ThreadedComment_DeleteSelectedReplyHelpText"));
        _updateReplyButton.Click += (_, _) => SubmitThreadedCommentReplyEdit(existing);
        _deleteReplyButton.Click += (_, _) => SubmitThreadedCommentReplyDelete(existing);

        var actionRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
        actionRow.Children.Add(_updateReplyButton);
        actionRow.Children.Add(_deleteReplyButton);
        panel.Children.Add(actionRow);
        PopulateSelectedReplyText(existing);
        return panel;
    }

    private void PopulateSelectedReplyText(ThreadedComment existing)
    {
        var replyIndex = _replySelector.SelectedIndex;
        _selectedReplyBox.Text = ThreadedCommentDialogPlanner.IsValidReplyIndex(existing, replyIndex)
            ? existing.Replies[replyIndex].Text
            : "";
        UpdateSelectedReplyActionState(existing);
    }

    private void UpdateSelectedReplyActionState(ThreadedComment existing)
    {
        var hasSelection = ThreadedCommentDialogPlanner.IsValidReplyIndex(existing, _replySelector.SelectedIndex);
        _deleteReplyButton.IsEnabled = hasSelection;
        _updateReplyButton.IsEnabled = hasSelection && !string.IsNullOrWhiteSpace(_selectedReplyBox.Text);
    }

    private void SubmitThreadedCommentReplyEdit(ThreadedComment existing)
    {
        if (!TryCreateReplyEditResult(existing, _replySelector.SelectedIndex, _selectedReplyBox.Text, _resolveBox.IsChecked == true, out var result, out var error))
        {
            ShowInvalidThreadedCommentWarning(error ?? UiText.Get("ThreadedComment_EnterReplyMessage"), _selectedReplyBox);
            return;
        }

        Result = result;
        DialogResult = true;
    }

    private void SubmitThreadedCommentReplyDelete(ThreadedComment existing)
    {
        if (!TryCreateReplyDeleteResult(existing, _replySelector.SelectedIndex, _resolveBox.IsChecked == true, out var result, out var error))
        {
            ShowInvalidThreadedCommentWarning(error ?? UiText.Get("ThreadedComment_SelectReplyMessage"), _selectedReplyBox);
            return;
        }

        Result = result;
        DialogResult = true;
    }

    private static string FormatReplyChoice(int index, CommentReply reply) =>
        ThreadedCommentDialogPlanner.FormatReplyChoice(index, reply);

    private static string FormatReplyAutomationName(int index, CommentReply reply) =>
        UiText.Format(
            "ThreadedComment_ReplyAutomationNameFormat",
            index + 1,
            ThreadedCommentDialogPlanner.FormatMessageHeading(reply.Author, reply.CreatedAtUtc),
            ThreadedCommentDialogPlanner.SummarizeReplyText(reply.Text));

    private static Border BuildMessage(string author, string text, DateTimeOffset? createdAtUtc, bool isRoot)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        panel.Children.Add(new TextBlock
        {
            Text = FormatMessageHeading(author, createdAtUtc),
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

    private static string FormatMessageHeading(string author, DateTimeOffset? createdAtUtc)
        => ThreadedCommentDialogPlanner.FormatMessageHeading(author, createdAtUtc);

    private static string? GetThreadedCommentDialogErrorMessage(ThreadedCommentDialogValidationError error) =>
        ThreadedCommentDialogPlanner.DescribeValidationError(error)?.Message.Resolve(UiText.Get, UiText.Format);

    private void ShowInvalidThreadedCommentWarning(string message, TextBox target)
    {
        DialogFocus.ShowWarningAndFocus(this, message, Title, target);
    }
}
