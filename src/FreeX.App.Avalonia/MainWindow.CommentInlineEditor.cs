using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.Comments;
using FreeX.App.Presentation.Shell;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private const double InlineCommentEditorWidth = 300;
    private const double InlineCommentEditorNewHeight = 230;
    private const double InlineCommentEditorExistingHeight = 300;

    private CellAddress? _inlineThreadedCommentEditAddress;
    private ThreadedComment? _inlineThreadedCommentEditExisting;
    private TextBox? _inlineThreadedCommentRootBox;
    private TextBox? _inlineThreadedCommentReplyBox;
    private TextBox? _inlineThreadedCommentSelectedReplyBox;
    private ComboBox? _inlineThreadedCommentReplySelector;
    private Button? _inlineThreadedCommentUpdateReplyButton;
    private Button? _inlineThreadedCommentDeleteReplyButton;
    private CheckBox? _inlineThreadedCommentResolvedBox;
    private TextBlock? _inlineThreadedCommentError;

    private void BeginThreadedCommentInlineEdit(ThreadedComment? existing)
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;

        var target = ReviewSessionController.GetSelectedThreadedCommentTarget();
        if (target is null)
        {
            RefreshShell(UiText.Get("MainLoc_ReviewTargetNotSelected"));
            return;
        }

        ClearSelectedDrawingObject();
        _inlineThreadedCommentEditAddress = target.Address;
        _inlineThreadedCommentEditExisting = existing ?? target.ThreadedComment;
        RefreshShell("Ready");
        Dispatcher.UIThread.Post(FocusInlineThreadedCommentEditor, DispatcherPriority.Input);
    }

    private void AddThreadedCommentInlineEditorOverlay(
        Canvas overlay,
        ViewportModel viewport,
        bool showHeadings,
        double zoomFactor)
    {
        ClearInlineThreadedCommentControlReferences();
        if (_inlineThreadedCommentEditAddress is not { } address ||
            !TryGetDisplayedCellBounds(viewport, address, showHeadings, zoomFactor,
                out var cellLeft, out var cellTop, out _, out var cellHeight))
        {
            return;
        }

        var editorHeight = _inlineThreadedCommentEditExisting is null
            ? InlineCommentEditorNewHeight
            : InlineCommentEditorExistingHeight;
        var left = Math.Clamp(cellLeft, 0, Math.Max(0, overlay.Width - InlineCommentEditorWidth));
        var top = cellTop + cellHeight + 2;
        if (top + editorHeight > overlay.Height && cellTop > editorHeight + 2)
            top = cellTop - editorHeight - 2;
        top = Math.Clamp(top, 0, Math.Max(0, overlay.Height - editorHeight));

        var editor = new Border
        {
            Width = InlineCommentEditorWidth,
            MinHeight = editorHeight,
            MaxHeight = editorHeight,
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(158, 151, 113)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6),
            Child = BuildInlineThreadedCommentPanel(),
        };
        AutomationProperties.SetAutomationId(editor, "WorksheetThreadedCommentInlineEditor");
        AutomationProperties.SetName(editor, UiText.Get("ThreadedComment_CommentAutomationName"));
        Canvas.SetLeft(editor, left);
        Canvas.SetTop(editor, top);
        overlay.Children.Add(editor);
    }

    private StackPanel BuildInlineThreadedCommentPanel()
    {
        var existing = _inlineThreadedCommentEditExisting;
        var cellRef = _inlineThreadedCommentEditAddress is { } address
            ? FormatCellReference(address)
            : string.Empty;
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = $"Comment - {cellRef}",
            FontFamily = FormulaBarFontFamily,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.Black,
            Margin = new Thickness(0, 0, 0, 5),
        });

        if (existing is not null)
        {
            var conversation = new StackPanel();
            conversation.Children.Add(BuildInlineThreadMessage(existing.Author, existing.Text, existing.CreatedAtUtc, isRoot: true));
            foreach (var reply in existing.Replies)
                conversation.Children.Add(BuildInlineThreadMessage(reply.Author, reply.Text, reply.CreatedAtUtc, isRoot: false));
            panel.Children.Add(new ScrollViewer
            {
                Content = conversation,
                MaxHeight = 82,
                VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 0, 0, 6),
            });
        }

        _inlineThreadedCommentRootBox = CreateInlineCommentTextBox(
            existing?.Text ?? string.Empty,
            existing is null ? "ThreadedComment_CommentAutomationName" : "ThreadedComment_EditCommentAutomationName",
            "ThreadedCommentRootBox",
            minHeight: 54,
            maxHeight: 96);
        panel.Children.Add(CreateInlineCommentLabel(
            existing is null ? UiText.Get("ThreadedComment_CommentLabel") : UiText.Get("ThreadedComment_EditCommentLabel"),
            _inlineThreadedCommentRootBox));
        panel.Children.Add(_inlineThreadedCommentRootBox);

        if (existing is not null)
        {
            if (existing.Replies.Count > 0)
                panel.Children.Add(BuildInlineReplyEditor(existing));

            _inlineThreadedCommentReplyBox = CreateInlineCommentTextBox(
                string.Empty,
                "ThreadedComment_ReplyAutomationName",
                "ThreadedCommentReplyBox",
                minHeight: 42,
                maxHeight: 74);
            panel.Children.Add(CreateInlineCommentLabel(UiText.Get("ThreadedComment_ReplyLabel"), _inlineThreadedCommentReplyBox, 6));
            panel.Children.Add(_inlineThreadedCommentReplyBox);
        }

        _inlineThreadedCommentResolvedBox = new CheckBox
        {
            Content = UiText.Get("ThreadedComment_MarkAsResolved"),
            IsChecked = existing?.IsResolved ?? false,
            Margin = new Thickness(0, 5, 0, 0),
        };
        AvaloniaCompactDialogChrome.ApplyCheckBox(_inlineThreadedCommentResolvedBox, CommentDialogChromeStyle);
        AutomationProperties.SetAutomationId(_inlineThreadedCommentResolvedBox, "GridThreadedCommentResolvedBox");
        panel.Children.Add(_inlineThreadedCommentResolvedBox);

        _inlineThreadedCommentError = new TextBlock
        {
            IsVisible = false,
            Foreground = new SolidColorBrush(Color.FromRgb(178, 34, 34)),
            FontFamily = FormulaBarFontFamily,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 0),
        };
        AutomationProperties.SetAutomationId(_inlineThreadedCommentError, "GridThreadedCommentInlineError");
        panel.Children.Add(_inlineThreadedCommentError);

        var cancel = CreateInlineCommentButton(UiText.Get("ThreadedComment_CancelButton"), "GridThreadedCommentInlineCancelButton", CancelThreadedCommentInlineEdit);
        var save = CreateInlineCommentButton(
            existing is null ? UiText.Get("ThreadedComment_AddButton") : UiText.Get("ThreadedComment_ReplyButton"),
            "GridThreadedCommentInlineSaveButton",
            SubmitThreadedCommentInlineEdit);
        panel.Children.Add(AvaloniaCompactDialogChrome.CreateActionRow([cancel, save], new Thickness(0, 7, 0, 0)));
        return panel;
    }

    private StackPanel BuildInlineReplyEditor(ThreadedComment existing)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 5, 0, 0) };
        _inlineThreadedCommentReplySelector = new ComboBox { MinWidth = 180 };
        AvaloniaCompactDialogChrome.ApplyComboBox(_inlineThreadedCommentReplySelector, CommentDialogChromeStyle);
        AutomationProperties.SetAutomationId(_inlineThreadedCommentReplySelector, "GridThreadedCommentReplySelector");
        for (var i = 0; i < existing.Replies.Count; i++)
        {
            var item = new ComboBoxItem { Content = ThreadedCommentDialogPlanner.FormatReplyChoice(i, existing.Replies[i]) };
            _inlineThreadedCommentReplySelector.Items.Add(item);
        }

        _inlineThreadedCommentSelectedReplyBox = CreateInlineCommentTextBox(
            string.Empty,
            "ThreadedComment_SelectedReplyTextAutomationName",
            "GridThreadedCommentSelectedReplyBox",
            minHeight: 42,
            maxHeight: 74);
        _inlineThreadedCommentUpdateReplyButton = CreateInlineCommentButton(
            UiText.Get("ThreadedComment_UpdateReplyButton"),
            "GridThreadedCommentUpdateReplyButton",
            SubmitThreadedCommentReplyEdit);
        _inlineThreadedCommentDeleteReplyButton = CreateInlineCommentButton(
            UiText.Get("ThreadedComment_DeleteReplyButton"),
            "GridThreadedCommentDeleteReplyButton",
            SubmitThreadedCommentReplyDelete);
        _inlineThreadedCommentReplySelector.SelectionChanged += (_, _) => PopulateInlineSelectedReplyText(existing);
        _inlineThreadedCommentSelectedReplyBox.TextChanged += (_, _) => UpdateInlineSelectedReplyActionState(existing);
        _inlineThreadedCommentReplySelector.SelectedIndex = 0;

        panel.Children.Add(CreateInlineCommentLabel(UiText.Get("ThreadedComment_SelectReplyLabel"), _inlineThreadedCommentReplySelector));
        panel.Children.Add(_inlineThreadedCommentReplySelector);
        panel.Children.Add(CreateInlineCommentLabel(UiText.Get("ThreadedComment_SelectedReplyTextLabel"), _inlineThreadedCommentSelectedReplyBox, 5));
        panel.Children.Add(_inlineThreadedCommentSelectedReplyBox);
        panel.Children.Add(AvaloniaCompactDialogChrome.CreateActionRow(
            [_inlineThreadedCommentUpdateReplyButton, _inlineThreadedCommentDeleteReplyButton],
            new Thickness(0, 5, 0, 0)));
        PopulateInlineSelectedReplyText(existing);
        return panel;
    }

    private TextBox CreateInlineCommentTextBox(
        string text,
        string nameResourceKey,
        string automationId,
        double minHeight,
        double maxHeight)
    {
        var box = new TextBox
        {
            Text = text,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = minHeight,
            MaxHeight = maxHeight,
            FontFamily = FormulaBarFontFamily,
            FontSize = 12,
            Padding = new Thickness(5),
        };
        AvaloniaCompactDialogChrome.ApplyTextBox(box, CommentDialogChromeStyle, fixedHeight: false);
        AutomationProperties.SetName(box, UiText.Get(nameResourceKey));
        AutomationProperties.SetAutomationId(box, automationId);
        box.KeyDown += InlineThreadedCommentTextBoxKeyDown;
        return box;
    }

    private static Label CreateInlineCommentLabel(string text, Control target, double topMargin = 0) => new()
    {
        Content = text,
        Target = target,
        Padding = new Thickness(0),
        Margin = new Thickness(0, topMargin, 0, 2),
        FontFamily = FormulaBarFontFamily,
        FontSize = 11,
    };

    private static Button CreateInlineCommentButton(string text, string automationId, Action action)
    {
        var button = new Button { Content = text, MinWidth = 84, IsDefault = text != UiText.Get("ThreadedComment_CancelButton") };
        AvaloniaCompactDialogChrome.ApplyButton(button, CommentDialogChromeStyle, 84, button.IsDefault);
        AutomationProperties.SetAutomationId(button, automationId);
        button.Click += (_, _) => action();
        return button;
    }

    private static Border BuildInlineThreadMessage(string author, string text, DateTimeOffset? createdAtUtc, bool isRoot)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = ThreadedCommentDialogPlanner.FormatMessageHeading(author, createdAtUtc),
            FontFamily = FormulaBarFontFamily,
            FontSize = 10,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(isRoot ? Color.FromRgb(0x1F, 0x49, 0x7D) : Color.FromRgb(0x40, 0x40, 0x40)),
        });
        panel.Children.Add(new TextBlock
        {
            Text = text,
            FontFamily = FormulaBarFontFamily,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(7, 2, 0, 0),
        });
        return new Border
        {
            Child = panel,
            Background = new SolidColorBrush(isRoot ? Color.FromRgb(0xF0, 0xF4, 0xF8) : Colors.White),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 4),
            Margin = new Thickness(0, 0, 0, 3),
        };
    }

    private void InlineThreadedCommentTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            CancelThreadedCommentInlineEdit();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.Control)
        {
            SubmitThreadedCommentInlineEdit();
            e.Handled = true;
        }
    }

    private void SubmitThreadedCommentInlineEdit()
    {
        if (_inlineThreadedCommentEditExisting is { } existing &&
            _inlineThreadedCommentReplySelector?.SelectedIndex >= 0 &&
            _inlineThreadedCommentReplyBox is null &&
            _inlineThreadedCommentSelectedReplyBox?.IsFocused == true)
        {
            SubmitThreadedCommentReplyEdit();
            return;
        }

        if (!ThreadedCommentDialogPlanner.TryCreateResult(
                _inlineThreadedCommentEditExisting,
                _inlineThreadedCommentRootBox?.Text,
                _inlineThreadedCommentReplyBox?.Text,
                _inlineThreadedCommentResolvedBox?.IsChecked == true,
                out var result,
                out var error))
        {
            ShowInlineThreadedCommentError(DescribeValidationError(error));
            (_inlineThreadedCommentEditExisting is null ? _inlineThreadedCommentRootBox : _inlineThreadedCommentReplyBox ?? _inlineThreadedCommentRootBox)?.Focus();
            return;
        }

        ApplyInlineThreadedCommentResult(result);
    }

    private void SubmitThreadedCommentReplyEdit()
    {
        if (_inlineThreadedCommentEditExisting is not { } existing)
        {
            ShowInlineThreadedCommentError(UiText.Get("ThreadedComment_NoThreadedCommentAvailableMessage"));
            _inlineThreadedCommentSelectedReplyBox?.Focus();
            return;
        }

        if (!ThreadedCommentDialogPlanner.TryCreateReplyEditResult(
                existing,
                _inlineThreadedCommentReplySelector?.SelectedIndex ?? -1,
                _inlineThreadedCommentSelectedReplyBox?.Text,
                _inlineThreadedCommentResolvedBox?.IsChecked == true,
                out var result,
                out var error))
        {
            ShowInlineThreadedCommentError(DescribeValidationError(error));
            _inlineThreadedCommentSelectedReplyBox?.Focus();
            return;
        }

        ApplyInlineThreadedCommentResult(result);
    }

    private void SubmitThreadedCommentReplyDelete()
    {
        if (_inlineThreadedCommentEditExisting is not { } existing)
        {
            ShowInlineThreadedCommentError(UiText.Get("ThreadedComment_NoThreadedCommentAvailableMessage"));
            _inlineThreadedCommentSelectedReplyBox?.Focus();
            return;
        }

        if (!ThreadedCommentDialogPlanner.TryCreateReplyDeleteResult(
                existing,
                _inlineThreadedCommentReplySelector?.SelectedIndex ?? -1,
                _inlineThreadedCommentResolvedBox?.IsChecked == true,
                out var result,
                out var error))
        {
            ShowInlineThreadedCommentError(DescribeValidationError(error));
            _inlineThreadedCommentSelectedReplyBox?.Focus();
            return;
        }

        ApplyInlineThreadedCommentResult(result);
    }

    private void ApplyInlineThreadedCommentResult(ThreadedCommentDialogResult result)
    {
        var address = _inlineThreadedCommentEditAddress;
        var mutation = ReviewSessionController.ApplyThreadedComment(result);
        if (!mutation.Success)
        {
            ShowInlineThreadedCommentError(mutation.ErrorMessage ?? UiText.Get("Comment_CommentFailed"));
            return;
        }

        ClearInlineThreadedCommentEditorState();
        ApplyReviewRefreshPlan(mutation.RefreshPlan, address is { } cell
            ? UiText.Format("Comment_CommentUpdated", FormatCellReference(cell))
            : UiText.Get("Comment_CommentUpdated"));
        FocusShellRegion(ShellFocusTarget.Worksheet);
    }

    private void CancelThreadedCommentInlineEdit()
    {
        ClearInlineThreadedCommentEditorState();
        RefreshShell("Ready");
        FocusShellRegion(ShellFocusTarget.Worksheet);
    }

    private void ShowInlineThreadedCommentError(string message)
    {
        if (_inlineThreadedCommentError is null)
            return;

        _inlineThreadedCommentError.Text = message;
        _inlineThreadedCommentError.IsVisible = true;
    }

    private void PopulateInlineSelectedReplyText(ThreadedComment existing)
    {
        var index = _inlineThreadedCommentReplySelector?.SelectedIndex ?? -1;
        if (_inlineThreadedCommentSelectedReplyBox is not null)
        {
            _inlineThreadedCommentSelectedReplyBox.Text =
                ThreadedCommentDialogPlanner.IsValidReplyIndex(existing, index)
                    ? existing.Replies[index].Text
                    : string.Empty;
        }
        UpdateInlineSelectedReplyActionState(existing);
    }

    private void UpdateInlineSelectedReplyActionState(ThreadedComment existing)
    {
        var selected = ThreadedCommentDialogPlanner.IsValidReplyIndex(
            existing,
            _inlineThreadedCommentReplySelector?.SelectedIndex ?? -1);
        if (_inlineThreadedCommentDeleteReplyButton is not null)
            _inlineThreadedCommentDeleteReplyButton.IsEnabled = selected;
        if (_inlineThreadedCommentUpdateReplyButton is not null)
            _inlineThreadedCommentUpdateReplyButton.IsEnabled = selected &&
                !string.IsNullOrWhiteSpace(_inlineThreadedCommentSelectedReplyBox?.Text);
    }

    private void FocusInlineThreadedCommentEditor()
    {
        if (_inlineThreadedCommentEditAddress is null)
            return;

        (_inlineThreadedCommentEditExisting is null
            ? _inlineThreadedCommentRootBox
            : _inlineThreadedCommentReplyBox ?? _inlineThreadedCommentRootBox)?.Focus();
    }

    private void ClearInlineThreadedCommentEditorState()
    {
        _inlineThreadedCommentEditAddress = null;
        _inlineThreadedCommentEditExisting = null;
        ClearInlineThreadedCommentControlReferences();
    }

    private void ClearInlineThreadedCommentControlReferences()
    {
        _inlineThreadedCommentRootBox = null;
        _inlineThreadedCommentReplyBox = null;
        _inlineThreadedCommentSelectedReplyBox = null;
        _inlineThreadedCommentReplySelector = null;
        _inlineThreadedCommentUpdateReplyButton = null;
        _inlineThreadedCommentDeleteReplyButton = null;
        _inlineThreadedCommentResolvedBox = null;
        _inlineThreadedCommentError = null;
    }
}
