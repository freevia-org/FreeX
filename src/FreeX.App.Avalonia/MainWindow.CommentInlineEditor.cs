using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

using Free.Shared.Drawing;
using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.Comments;
using FreeX.App.Presentation.Shell;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private const double InlineCommentEditorWidth = 300;
    private const double InlineCommentEditorNewHeight = 230;
    // An existing thread's card (conversation, edit box, optional reply picker, reply box, resolve
    // toggle, thread actions, Save/Cancel) measures far taller than a new comment's. These are the
    // measured content heights; the card auto-sizes up to them instead of clipping the bottom
    // controls out of a fixed 300 DIP box.
    private const double InlineCommentEditorExistingHeight = 400;
    private const double InlineCommentEditorExistingWithRepliesHeight = 540;

    private CellAddress? _inlineThreadedCommentEditAddress;
    private ThreadedComment? _inlineThreadedCommentEditExisting;
    private CellAddress? _inlineNoteEditAddress;
    private string _inlineNoteEditInitialText = string.Empty;
    private bool _inlineNotePreserveSelection;
    private bool _inlineThreadedCommentPreserveSelection;
    private TextBox? _inlineNoteEditBox;
    private TextBlock? _inlineNoteError;
    private TextBox? _inlineThreadedCommentRootBox;
    private TextBox? _inlineThreadedCommentReplyBox;
    private TextBox? _inlineThreadedCommentSelectedReplyBox;
    private ComboBox? _inlineThreadedCommentReplySelector;
    private Button? _inlineThreadedCommentUpdateReplyButton;
    private Button? _inlineThreadedCommentDeleteReplyButton;
    private CheckBox? _inlineThreadedCommentResolvedBox;
    private TextBlock? _inlineThreadedCommentError;

    private void BeginNoteInlineEdit() => BeginNoteInlineEditCore(null, preserveSelection: false);

    private void BeginNoteInlineEditAt(CellAddress address, bool preserveSelection) =>
        BeginNoteInlineEditCore(address, preserveSelection);

    private void BeginNoteInlineEditCore(CellAddress? address, bool preserveSelection)
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;

        var target = CreateReviewSessionController(address).GetSelectedNoteTarget();
        if (target is null)
        {
            RefreshShell(UiText.Get("MainLoc_ReviewTargetNotSelected"));
            return;
        }

        ClearSelectedDrawingObject();
        ClearInlineThreadedCommentEditorState();
        EnsureInlineEditorAddressVisible(target.Address);
        _inlineNoteEditAddress = target.Address;
        _inlineNotePreserveSelection = preserveSelection;
        _inlineNoteEditInitialText = target.NoteText;
        RefreshShell(UiText.Get("MainLoc_Ready"));
        Dispatcher.UIThread.Post(FocusInlineNoteEditor, DispatcherPriority.Input);
    }

    private void BeginThreadedCommentInlineEdit(ThreadedComment? existing) =>
        BeginThreadedCommentInlineEditCore(null, existing, preserveSelection: false);

    private void BeginThreadedCommentInlineEditAt(CellAddress address, ThreadedComment? existing, bool preserveSelection) =>
        BeginThreadedCommentInlineEditCore(address, existing, preserveSelection);

    private void BeginThreadedCommentInlineEditCore(CellAddress? address, ThreadedComment? existing, bool preserveSelection)
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;

        var target = CreateReviewSessionController(address).GetSelectedThreadedCommentTarget();
        if (target is null)
        {
            RefreshShell(UiText.Get("MainLoc_ReviewTargetNotSelected"));
            return;
        }

        ClearSelectedDrawingObject();
        ClearInlineNoteEditorState();
        EnsureInlineEditorAddressVisible(target.Address);
        _inlineThreadedCardPassive = false;
        _inlineThreadedCardDismissedAddress = null;
        _inlineThreadedCommentEditAddress = target.Address;
        _inlineThreadedCommentPreserveSelection = preserveSelection;
        _inlineThreadedCommentEditExisting = existing ?? target.ThreadedComment;
        RefreshShell(UiText.Get("MainLoc_Ready"));
        Dispatcher.UIThread.Post(FocusInlineThreadedCommentEditor, DispatcherPriority.Input);
    }

    // Excel shows a cell's thread card as soon as the cell is selected, without taking keyboard
    // focus (WPF: GridView passive card). It is re-derived from the active cell on every overlay
    // build and becomes a normal editor once the user focuses into it.
    private bool _inlineThreadedCardPassive;
    private CellAddress? _inlineThreadedCardDismissedAddress;

    private void SyncPassiveThreadedCommentCard()
    {
        var wasPassive = _inlineThreadedCardPassive;
        if (!wasPassive && (_inlineThreadedCommentEditAddress is not null || _inlineNoteEditAddress is not null))
            return;

        if (wasPassive)
            ClearInlineThreadedCommentEditorState();
        if (_inlineNoteEditAddress is not null)
            return;

        var address = _session.ActiveCell;
        if (_inlineThreadedCardDismissedAddress is { } dismissed && dismissed != address)
            _inlineThreadedCardDismissedAddress = null;
        if (_inlineThreadedCardDismissedAddress == address)
            return;

        if (!_session.ActiveSheet.ThreadedComments.TryGetValue(address, out var thread) ||
            ThreadedCommentActivationPlanner.Decide(
                CommentActivationSource.Selection,
                CellCommentDisplayKind.ThreadedComment) != CommentActivationAction.OpenCard)
        {
            return;
        }

        _inlineThreadedCommentEditAddress = address;
        _inlineThreadedCommentPreserveSelection = true;
        _inlineThreadedCommentEditExisting = thread;
        _inlineThreadedCardPassive = true;
    }

    /// <summary>Clicking the purple corner indicator opens the editable card with the reply box focused.</summary>
    private void OpenThreadedCommentCardFromIndicator(CellAddress address)
    {
        // WPF TryOpenThreadedCardFromIndicatorClick: an already-open (focused) editor wins; only a
        // passive selection card is replaced by the focused one.
        if (_inlineNoteEditAddress is not null ||
            (_inlineThreadedCommentEditAddress is not null && !_inlineThreadedCardPassive))
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_isOpening || _isSaving || _inlineNoteEditAddress is not null ||
                address.Sheet != _session.ActiveSheet.Id ||
                !_session.ActiveSheet.ThreadedComments.TryGetValue(address, out var thread))
            {
                return;
            }

            ClearInlineThreadedCommentEditorState();
            _inlineThreadedCardDismissedAddress = null;
            _inlineThreadedCommentEditAddress = address;
            _inlineThreadedCommentPreserveSelection = true;
            _inlineThreadedCommentEditExisting = thread;
            RefreshShell(UiText.Get("MainLoc_Ready"));
            Dispatcher.UIThread.Post(FocusInlineThreadedCommentEditor, DispatcherPriority.Input);
        }, DispatcherPriority.Input);
    }

    private void EnsureInlineEditorAddressVisible(CellAddress address)
    {
        var rowVisible = _session.Viewport.RowMetrics.Any(metric => metric.Row == address.Row);
        var columnVisible = _session.Viewport.ColMetrics.Any(metric => metric.Col == address.Col);
        if (rowVisible && columnVisible)
            return;

        var origin = _session.ViewportOrigin;
        var topRow = rowVisible ? origin.TopRow : address.Row;
        var leftColumn = columnVisible ? origin.LeftCol : address.Col;
        _session.SetViewportOrigin(topRow, leftColumn);
    }

    private void AddNoteInlineEditorOverlay(
        Canvas overlay,
        ViewportModel viewport,
        bool showHeadings,
        double zoomFactor)
    {
        ClearInlineNoteControlReferences();
        if (_inlineNoteEditAddress is not { } address ||
            !TryGetDisplayedCellBounds(viewport, address, showHeadings, zoomFactor,
                out var cellLeft, out var cellTop, out var cellWidth, out var cellHeight))
        {
            // WPF dismisses the comment popup as soon as its anchor leaves the viewport.
            // Clearing the state prevents the editor from reappearing after a later rebuild
            // without another explicit New/Edit Note command.
            if (_inlineNoteEditAddress is not null)
                ClearInlineNoteEditorState();
            return;
        }

        var placement = CommentPreviewPlacementPlanner.Calculate(
            new LayoutRect(cellLeft, cellTop, cellWidth, cellHeight),
            new CommentPreviewLayoutSize(
                Math.Max(0, overlay.Width),
                Math.Max(0, overlay.Height)),
            new CommentPreviewLayoutSize(
                InlineCommentEditorWidth,
                InlineCommentEditorNewHeight));

        var editor = new Border
        {
            Width = placement.Width,
            MaxHeight = placement.MaxHeight,
            Background = new SolidColorBrush(Color.FromRgb(255, 255, 225)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(158, 151, 113)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            BoxShadow = new BoxShadows(new BoxShadow
            {
                OffsetX = 2,
                OffsetY = 2,
                Blur = 8,
                Color = Color.FromArgb(56, 0, 0, 0),
            }),
            Child = BuildInlineNotePanel(address),
        };
        AutomationProperties.SetAutomationId(editor, "WorksheetNoteInlineEditor");
        AutomationProperties.SetName(editor, UiText.Get("GridInlineComment_NoteAutomationName"));
        Canvas.SetLeft(editor, placement.HorizontalOffset);
        Canvas.SetTop(editor, placement.VerticalOffset);
        overlay.Children.Add(editor);
    }

    private StackPanel BuildInlineNotePanel(CellAddress address)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = UiText.Format("GridInlineComment_NoteTitleFormat", FormatCellReference(address)),
            FontFamily = FormulaBarFontFamily,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.Black,
            Margin = new Thickness(0, 0, 0, 5),
        });

        _inlineNoteEditBox = new TextBox
        {
            Text = _inlineNoteEditInitialText,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 76,
            MaxHeight = 132,
            FontFamily = FormulaBarFontFamily,
            FontSize = 12,
            Padding = new Thickness(5),
        };
        AvaloniaCompactDialogChrome.ApplyTextBox(
            _inlineNoteEditBox,
            InlineCommentEditorChromeStyle,
            fixedHeight: false);
        _inlineNoteEditBox.VerticalContentAlignment = AvaloniaVerticalAlignment.Top;
        _inlineNoteEditBox.SetValue(
            ScrollViewer.VerticalScrollBarVisibilityProperty,
            global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        _inlineNoteEditBox.SetValue(
            ScrollViewer.HorizontalScrollBarVisibilityProperty,
            global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        AutomationProperties.SetAutomationId(_inlineNoteEditBox, "GridNoteInlineTextBox");
        AutomationProperties.SetName(_inlineNoteEditBox, UiText.Get("GridInlineComment_NoteAutomationName"));
        _inlineNoteEditBox.KeyDown += InlineNoteTextBoxKeyDown;
        panel.Children.Add(_inlineNoteEditBox);

        _inlineNoteError = new TextBlock
        {
            IsVisible = false,
            Foreground = new SolidColorBrush(Color.FromRgb(178, 34, 34)),
            FontFamily = FormulaBarFontFamily,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 0),
        };
        AutomationProperties.SetAutomationId(_inlineNoteError, "GridNoteInlineError");
        panel.Children.Add(_inlineNoteError);

        var save = CreateInlineCommentButton(
            InlineCommentSaveText,
            "GridCommentInlineSaveButton",
            SubmitNoteInlineEdit,
            isDefault: true);
        var cancel = CreateInlineCommentButton(
            InlineCommentCancelText,
            "GridCommentInlineCancelButton",
            CancelNoteInlineEdit,
            isCancel: true);
        panel.Children.Add(CreateInlineCommentActionRow(save, cancel));
        return panel;
    }

    private void AddThreadedCommentInlineEditorOverlay(
        Canvas overlay,
        ViewportModel viewport,
        bool showHeadings,
        double zoomFactor)
    {
        SyncPassiveThreadedCommentCard();
        ClearInlineThreadedCommentControlReferences();
        if (_inlineThreadedCommentEditAddress is not { } address ||
            !TryGetDisplayedCellBounds(viewport, address, showHeadings, zoomFactor,
                out var cellLeft, out var cellTop, out _, out var cellHeight))
        {
            return;
        }

        var editorHeight = _inlineThreadedCommentEditExisting is null
            ? InlineCommentEditorNewHeight
            : ResolveExistingThreadCardHeight(_inlineThreadedCommentEditExisting);
        var left = Math.Clamp(cellLeft, 0, Math.Max(0, overlay.Width - InlineCommentEditorWidth));
        var top = cellTop + cellHeight + 2;
        if (top + editorHeight > overlay.Height && cellTop > editorHeight + 2)
            top = cellTop - editorHeight - 2;
        top = Math.Clamp(top, 0, Math.Max(0, overlay.Height - editorHeight));

        var editor = new Border
        {
            Width = InlineCommentEditorWidth,
            // A new comment keeps the fixed box; an existing thread sizes to its content (capped at
            // the measured maximum) so Save/Cancel and the thread actions are never clipped.
            MinHeight = _inlineThreadedCommentEditExisting is null ? editorHeight : 0,
            MaxHeight = editorHeight,
            Background = new SolidColorBrush(Color.FromRgb(255, 255, 225)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(158, 151, 113)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            BoxShadow = new BoxShadows(new BoxShadow
            {
                OffsetX = 2,
                OffsetY = 2,
                Blur = 8,
                Color = Color.FromArgb(56, 0, 0, 0),
            }),
            Child = BuildInlineThreadedCommentPanel(),
        };
        editor.AddHandler(
            InputElement.GotFocusEvent,
            (_, _) => _inlineThreadedCardPassive = false);
        AutomationProperties.SetAutomationId(editor, "WorksheetThreadedCommentInlineEditor");
        AutomationProperties.SetName(editor, UiText.Get("ThreadedComment_CommentAutomationName"));
        Canvas.SetLeft(editor, left);
        Canvas.SetTop(editor, top);
        overlay.Children.Add(editor);
    }

    internal static double ResolveExistingThreadCardHeight(ThreadedComment existing) =>
        existing.Replies.Count > 0
            ? InlineCommentEditorExistingWithRepliesHeight
            : InlineCommentEditorExistingHeight;

    private StackPanel BuildInlineThreadedCommentPanel()
    {
        var existing = _inlineThreadedCommentEditExisting;
        var cellRef = _inlineThreadedCommentEditAddress is { } address
            ? FormatCellReference(address)
            : string.Empty;
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = UiText.Format("GridInlineComment_CommentTitleFormat", cellRef),
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
                MaxHeight = 92,
                VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 0, 0, 6),
            });
        }

        _inlineThreadedCommentRootBox = CreateInlineCommentTextBox(
            existing?.Text ?? string.Empty,
            existing is null ? "ThreadedComment_CommentAutomationName" : "ThreadedComment_EditCommentAutomationName",
            "GridThreadedCommentRootBox",
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
                "GridThreadedCommentReplyBox",
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

        if (existing is not null)
            panel.Children.Add(BuildInlineThreadActionRow(existing));

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

        var save = CreateInlineCommentButton(
            existing is null ? InlineCommentSaveText : InlineCommentApplyText,
            "GridCommentInlineSaveButton",
            SubmitThreadedCommentInlineEdit,
            isDefault: true);
        var cancel = CreateInlineCommentButton(
            InlineCommentCancelText,
            "GridCommentInlineCancelButton",
            CancelThreadedCommentInlineEdit,
            isCancel: true);
        panel.Children.Add(CreateInlineCommentActionRow(save, cancel));
        return panel;
    }

    // Excel card actions: Resolve thread / Reopen thread and Delete thread (sheet-protection aware).
    private StackPanel BuildInlineThreadActionRow(ThreadedComment existing)
    {
        var card = _inlineThreadedCommentEditAddress is { } address
            ? ThreadedCommentCardPlanner.BuildCard(_session.ActiveSheet, address)
            : null;
        var toggleText = (card?.ToggleResolvedLabel
            ?? Free.Shared.Localization.LocalizedTextDescriptor.Resource(existing.IsResolved
                ? "ThreadedCommentCard_ReopenThread"
                : "ThreadedCommentCard_ResolveThread")).Resolve(UiText.Get, UiText.Format);
        var toggle = CreateInlineCommentButton(
            toggleText,
            "GridThreadedCommentToggleResolvedButton",
            () => ApplyInlineThreadedCommentResult(new ThreadedCommentDialogResult(null, null, !existing.IsResolved)),
            width: 112);
        toggle.IsEnabled = card?.CanToggleResolved ?? true;
        var delete = CreateInlineCommentButton(
            UiText.Get("ThreadedCommentCard_DeleteThread"),
            "GridThreadedCommentDeleteThreadButton",
            () => ApplyInlineThreadedCommentResult(new ThreadedCommentDialogResult(
                null, null, existing.IsResolved, ThreadedCommentDialogAction.DeleteThread)),
            width: 112);
        delete.IsEnabled = card?.CanDeleteThread ?? true;

        var row = AvaloniaCompactDialogChrome.CreateActionRow(
            [toggle, delete],
            new Thickness(0, 6, 0, 0),
            InlineCommentEditorChromeStyle);
        row.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left;
        if (card?.ReadOnlyNotice is { } notice)
        {
            if (_inlineThreadedCommentRootBox is not null) _inlineThreadedCommentRootBox.IsReadOnly = true;
            if (_inlineThreadedCommentReplyBox is not null) _inlineThreadedCommentReplyBox.IsReadOnly = true;
            var wrap = new StackPanel();
            wrap.Children.Add(row);
            wrap.Children.Add(new TextBlock
            {
                Text = notice.Resolve(UiText.Get, UiText.Format),
                FontFamily = FormulaBarFontFamily,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(178, 34, 34)),
                Margin = new Thickness(0, 4, 0, 0),
            });
            return wrap;
        }

        return row;
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
            maxHeight: 74,
            selectedReply: true);
        _inlineThreadedCommentUpdateReplyButton = CreateInlineCommentButton(
            UiText.Get("ThreadedComment_UpdateReplyButton"),
            "GridThreadedCommentUpdateReplyButton",
            SubmitThreadedCommentReplyEdit,
            width: 104);
        _inlineThreadedCommentDeleteReplyButton = CreateInlineCommentButton(
            UiText.Get("ThreadedComment_DeleteReplyButton"),
            "GridThreadedCommentDeleteReplyButton",
            SubmitThreadedCommentReplyDelete,
            width: 104);
        _inlineThreadedCommentReplySelector.SelectionChanged += (_, _) => PopulateInlineSelectedReplyText(existing);
        _inlineThreadedCommentSelectedReplyBox.TextChanged += (_, _) => UpdateInlineSelectedReplyActionState(existing);
        _inlineThreadedCommentReplySelector.SelectedIndex = 0;

        panel.Children.Add(CreateInlineCommentLabel(UiText.Get("ThreadedComment_SelectReplyLabel"), _inlineThreadedCommentReplySelector));
        panel.Children.Add(_inlineThreadedCommentReplySelector);
        panel.Children.Add(CreateInlineCommentLabel(UiText.Get("ThreadedComment_SelectedReplyTextLabel"), _inlineThreadedCommentSelectedReplyBox, 5));
        panel.Children.Add(_inlineThreadedCommentSelectedReplyBox);
        var replyActionRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left,
            Spacing = 6,
            Margin = new Thickness(0, 5, 0, 0),
        };
        replyActionRow.Children.Add(_inlineThreadedCommentUpdateReplyButton);
        replyActionRow.Children.Add(_inlineThreadedCommentDeleteReplyButton);
        panel.Children.Add(replyActionRow);
        PopulateInlineSelectedReplyText(existing);
        return panel;
    }

    private TextBox CreateInlineCommentTextBox(
        string text,
        string nameResourceKey,
        string automationId,
        double minHeight,
        double maxHeight,
        bool selectedReply = false)
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
        AvaloniaCompactDialogChrome.ApplyTextBox(box, InlineCommentEditorChromeStyle, fixedHeight: false);
        box.VerticalContentAlignment = AvaloniaVerticalAlignment.Top;
        box.SetValue(
            ScrollViewer.VerticalScrollBarVisibilityProperty,
            global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        box.SetValue(
            ScrollViewer.HorizontalScrollBarVisibilityProperty,
            global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        AutomationProperties.SetName(box, UiText.Get(nameResourceKey));
        AutomationProperties.SetAutomationId(box, automationId);
        if (selectedReply)
            box.KeyDown += InlineThreadedCommentSelectedReplyKeyDown;
        else
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

    private static Button CreateInlineCommentButton(
        string text,
        string automationId,
        Action action,
        bool isDefault = false,
        bool isCancel = false,
        double width = 72)
    {
        var button = new Button
        {
            Content = text,
            Width = width,
            MinWidth = width,
            IsDefault = isDefault,
            IsCancel = isCancel,
        };
        AvaloniaCompactDialogChrome.ApplyButton(button, CommentDialogChromeStyle, width, isDefault);
        // Deliberately shorter than a dialog button: this is the anchored inline editor, a compact
        // popup surface rather than a dialog, and AvaloniaReviewCommentInlineRuntimeTests pins it.
        button.Height = 24;
        button.MinHeight = 24;
        button.MaxHeight = 24;
        AutomationProperties.SetAutomationId(button, automationId);
        button.Click += (_, _) => action();
        return button;
    }

    private static AvaloniaCompactDialogChromeStyle InlineCommentEditorChromeStyle =>
        CommentDialogChromeStyle with
        {
            TextBoxPadding = new Thickness(5),
            ActionSpacing = 6,
        };

    private static StackPanel CreateInlineCommentActionRow(Button save, Button cancel) =>
        AvaloniaCompactDialogChrome.CreateActionRow(
            [save, cancel],
            new Thickness(0, 8, 0, 0),
            InlineCommentEditorChromeStyle);

    private static string InlineCommentSaveText =>
        UiText.CreateAutomationName(UiText.Get("MainWindow_AutomationName_Save"));

    private static string InlineCommentApplyText =>
        UiText.CreateAutomationName(UiText.Get("Common_Apply"));

    private static string InlineCommentCancelText =>
        UiText.CreateAutomationName(UiText.Get("Common_Cancel"));

    private static Border BuildInlineThreadMessage(string author, string text, DateTimeOffset? createdAtUtc, bool isRoot)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 5) };
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
            Margin = new Thickness(8, 2, 0, 0),
        });
        return new Border
        {
            Child = panel,
            Background = new SolidColorBrush(isRoot ? Color.FromRgb(0xF0, 0xF4, 0xF8) : Colors.White),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(7, 5, 7, 5),
            Margin = new Thickness(0, 0, 0, 4),
        };
    }

    private void InlineThreadedCommentTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            CancelThreadedCommentInlineEdit();
            e.Handled = true;
        }
        else if (IsCommandEnterCommit(e))
        {
            SubmitThreadedCommentInlineEdit();
            e.Handled = true;
        }
    }

    private void InlineNoteTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            CancelNoteInlineEdit();
            e.Handled = true;
        }
        else if (IsCommandEnterCommit(e))
        {
            SubmitNoteInlineEdit();
            e.Handled = true;
        }
    }

    private void SubmitNoteInlineEdit()
    {
        var text = (_inlineNoteEditBox?.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            ShowInlineNoteError(UiText.Get("GridInlineComment_EnterNoteMessage"));
            _inlineNoteEditBox?.Focus();
            return;
        }

        if (_inlineNoteEditAddress is not { } address)
            return;

        var result = CreateReviewSessionController(address, _inlineNotePreserveSelection).ApplyNote(text);
        if (!result.Success)
        {
            ShowInlineNoteError(result.ErrorMessage ?? UiText.Get("Comment_NoteFailed"));
            _inlineNoteEditBox?.Focus();
            return;
        }

        ClearInlineNoteEditorState();
        ApplyReviewRefreshPlan(result.RefreshPlan, UiText.Format("Comment_NoteUpdated", FormatCellReference(address)));
        FocusShellRegion(ShellFocusTarget.Worksheet);
    }

    private void InlineThreadedCommentSelectedReplyKeyDown(object? sender, KeyEventArgs e)
    {
        if (IsCommandEnterCommit(e) &&
            _inlineThreadedCommentUpdateReplyButton?.IsEnabled == true)
        {
            SubmitThreadedCommentReplyEdit();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            CancelThreadedCommentInlineEdit();
            e.Handled = true;
        }
    }

    private void SubmitThreadedCommentInlineEdit()
    {
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
        if (_inlineThreadedCommentEditAddress is not { } address)
            return;
        var mutation = CreateReviewSessionController(address, _inlineThreadedCommentPreserveSelection)
            .ApplyThreadedComment(result);
        if (!mutation.Success)
        {
            ShowInlineThreadedCommentError(mutation.ErrorMessage ?? UiText.Get("Comment_CommentFailed"));
            return;
        }

        ClearInlineThreadedCommentEditorState();
        ApplyReviewRefreshPlan(mutation.RefreshPlan, UiText.Format("Comment_CommentUpdated", FormatCellReference(address)));
        FocusShellRegion(ShellFocusTarget.Worksheet);
    }

    private void CancelThreadedCommentInlineEdit()
    {
        _inlineThreadedCardDismissedAddress = _inlineThreadedCommentEditAddress;
        ClearInlineThreadedCommentEditorState();
        RefreshShell(UiText.Get("MainLoc_Ready"));
        FocusShellRegion(ShellFocusTarget.Worksheet);
    }

    private void CancelNoteInlineEdit()
    {
        ClearInlineNoteEditorState();
        RefreshShell(UiText.Get("MainLoc_Ready"));
        FocusShellRegion(ShellFocusTarget.Worksheet);
    }

    private void ShowInlineNoteError(string message)
    {
        if (_inlineNoteError is null)
            return;

        _inlineNoteError.Text = message;
        _inlineNoteError.IsVisible = true;
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

    private void FocusInlineNoteEditor()
    {
        if (_inlineNoteEditBox is not { } editor)
            return;

        editor.Focus();
        var caret = editor.Text?.Length ?? 0;
        editor.CaretIndex = caret;
        editor.SelectionStart = caret;
        editor.SelectionEnd = caret;
    }

    private void ClearInlineNoteEditorState()
    {
        _inlineNoteEditAddress = null;
        _inlineNotePreserveSelection = false;
        _inlineNoteEditInitialText = string.Empty;
        ClearInlineNoteControlReferences();
    }

    private void ClearInlineNoteControlReferences()
    {
        _inlineNoteEditBox = null;
        _inlineNoteError = null;
    }

    private void ClearInlineThreadedCommentEditorState()
    {
        _inlineThreadedCardPassive = false;
        _inlineThreadedCommentEditAddress = null;
        _inlineThreadedCommentPreserveSelection = false;
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
