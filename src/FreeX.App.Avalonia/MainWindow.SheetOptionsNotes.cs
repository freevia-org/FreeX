using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.Comments;
using FreeX.Core.Model;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private const double ReviewCommentListCellColumnWidth = 80;

    private readonly Border _commentsPaneHost = new()
    {
        IsVisible = false,
        Width = 320,
        Background = Brushes.White,
        BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
        BorderThickness = new Thickness(1, 0, 0, 0),
    };

    private Action<IReadOnlyList<CommentListRowPlan>>? _refreshCommentListWindow;
    private double? _commentsPaneUserWidth;

    private static AvaloniaCompactDialogChromeStyle SheetOptionsDialogChromeStyle => new(FormulaBarFontFamily);

    /// <summary>True while the docked threaded Comments pane column is showing.</summary>
    internal bool IsCommentsPaneOpen => _commentsPaneHost.IsVisible;

    /// <summary>
    /// Review ▸ Show Comments — toggles the docked Comments pane: a real layout column right of the
    /// grid (the grid shrinks), resizable with its left-edge grip, closed with its X button or by
    /// pressing Show Comments again. Lists every threaded comment on the active sheet (cell ref +
    /// author + text). Double-click a row or use Go To to select that cell. Legacy notes remain a
    /// separate WPF-style Show Notes toggle-all command.
    /// </summary>
    private Task ShowCommentsListAsync()
    {
        if (_isOpening || _isSaving)
            return Task.CompletedTask;

        if (!TryCommitPendingFormulaEdit())
            return Task.CompletedTask;

        ClearSelectedDrawingObject();

        // Show Comments is a toggle (Excel): pressing it while the pane is open closes it.
        if (ThreadedCommentActivationPlanner.ShouldClosePaneOnToggle(IsCommentsPaneOpen))
        {
            CloseCommentsPane();
            return Task.CompletedTask;
        }

        var sheet = _session.ActiveSheet;
        var comments = CollectThreadedComments(sheet);
        if (comments.Count == 0)
        {
            ShowEditIssue(UiText.Get("MainWindowMessage_NoCommentsOnSheet"));
            return Task.CompletedTask;
        }

        var surface = BuildCommentsListSurface(comments, closeRequested: null);
        _commentsPaneHost.Child = BuildCommentsPaneChrome(surface.Content);
        var hostWidth = CommentsPaneHostWidth();
        _commentsPaneHost.Width = ThreadedCommentActivationPlanner.ClampPaneWidth(
            _commentsPaneUserWidth ?? ThreadedCommentActivationPlanner.DefaultPaneWidth(hostWidth),
            hostWidth);
        _refreshCommentListWindow = surface.Refresh;
        _commentsPaneHost.IsVisible = true;
        Dispatcher.UIThread.Post(surface.FocusList, DispatcherPriority.Input);
        return Task.CompletedTask;
    }

    private void ConfigureCommentsPaneHost()
    {
        AutomationProperties.SetAutomationId(_commentsPaneHost, "ReviewCommentsPane");
        AutomationProperties.SetName(_commentsPaneHost, UiText.Get("MainWindowMessage_CommentsTitle"));
    }

    internal void CloseCommentsPane(bool restoreGridFocus = true)
    {
        if (!_commentsPaneHost.IsVisible && _commentsPaneHost.Child is null)
            return;

        _commentsPaneHost.IsVisible = false;
        _commentsPaneHost.Child = null;
        _refreshCommentListWindow = null;
        if (restoreGridFocus)
            _sheetGridHost.Focus();
    }

    private double CommentsPaneHostWidth() =>
        (_commentsPaneHost.Parent as Control)?.Bounds.Width is > 0 and var width ? width : Bounds.Width;

    private void ClampCommentsPaneWidth()
    {
        if (!_commentsPaneHost.IsVisible)
            return;

        _commentsPaneHost.Width = ThreadedCommentActivationPlanner.ClampPaneWidth(
            _commentsPaneHost.Width, CommentsPaneHostWidth());
    }

    private Control BuildCommentsPaneChrome(Control content)
    {
        var title = UiText.Get("MainWindowMessage_CommentsTitle");
        var closeButton = new Button
        {
            Content = "X",
            Width = 22,
            Height = 22,
            Padding = new Thickness(0),
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            HorizontalContentAlignment = AvaloniaHorizontalAlignment.Center,
            VerticalContentAlignment = AvaloniaVerticalAlignment.Center,
        };
        AutomationProperties.SetAutomationId(closeButton, "ReviewCommentsPaneCloseButton");
        AutomationProperties.SetName(closeButton, UiText.Get("ReviewCommentList_CloseButtonAutomationName"));
        closeButton.Click += (_, _) => CloseCommentsPane();

        var header = new DockPanel { Margin = new Thickness(10, 8, 10, 0), LastChildFill = true };
        DockPanel.SetDock(closeButton, Dock.Right);
        header.Children.Add(closeButton);
        header.Children.Add(new TextBlock
        {
            Text = title,
            FontFamily = FormulaBarFontFamily,
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
        });

        var body = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(header, Dock.Top);
        body.Children.Add(header);
        body.Children.Add(content);

        var grip = new Border
        {
            Width = 6,
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.SizeWestEast),
        };
        AutomationProperties.SetAutomationId(grip, "ReviewCommentsPaneResizeGrip");
        AutomationProperties.SetName(grip, title);
        var dragging = false;
        var dragStartX = 0d;
        var dragStartWidth = 0d;
        grip.PointerPressed += (_, args) =>
        {
            if (!args.GetCurrentPoint(grip).Properties.IsLeftButtonPressed)
                return;

            dragging = true;
            dragStartX = args.GetPosition(this).X;
            dragStartWidth = _commentsPaneHost.Width;
            args.Pointer.Capture(grip);
            args.Handled = true;
        };
        grip.PointerMoved += (_, args) =>
        {
            if (!dragging)
                return;

            // Docked on the right: dragging left (negative delta) widens the pane.
            var requested = dragStartWidth - (args.GetPosition(this).X - dragStartX);
            _commentsPaneHost.Width = ThreadedCommentActivationPlanner.ClampPaneWidth(requested, CommentsPaneHostWidth());
            _commentsPaneUserWidth = _commentsPaneHost.Width;
            args.Handled = true;
        };
        grip.PointerReleased += (_, args) =>
        {
            if (!dragging)
                return;

            dragging = false;
            args.Pointer.Capture(null);
            args.Handled = true;
        };

        var root = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(grip, Dock.Left);
        root.Children.Add(grip);
        root.Children.Add(body);
        return root;
    }

    private sealed record CommentsListSurface(
        Control Content,
        Action<IReadOnlyList<CommentListRowPlan>> Refresh,
        Action FocusList);

    /// <summary>
    /// Opens the Comments list as a floating owned window (the surface the dialog-parity capture
    /// harness and its modeless-contract checks drive). The production command docks the pane
    /// instead; both host the same <see cref="BuildCommentsListSurface"/> content.
    /// </summary>
    internal Task ShowCommentsListFloatingWindowAsync()
    {
        var comments = CollectThreadedComments(_session.ActiveSheet);
        Window? dialog = null;
        var surface = BuildCommentsListSurface(comments, closeRequested: () => dialog?.Close());
        dialog = new Window
        {
            Title = UiText.Get("MainWindowMessage_CommentsTitle"),
            Width = 520,
            Height = 380,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Content = surface.Content,
        };
        AutomationProperties.SetAutomationId(dialog, "ReviewCommentListWindow");
        ShowOwnedModelessWindow(dialog, surface.FocusList, () => { });
        return Task.CompletedTask;
    }

    /// <summary>
    /// Builds the Comments list content shared by the docked pane and the floating window: All /
    /// Active / Resolved filter, live summary, two-column list and per-thread Resolve/Reopen, Delete
    /// and Go To actions, driven by the shared ThreadedCommentCardPlanner view-model.
    /// </summary>
    /// <param name="closeRequested">When set, a Close button is added to the action row (floating window).</param>
    private CommentsListSurface BuildCommentsListSurface(
        IReadOnlyList<CommentListRowPlan> comments,
        Action? closeRequested)
    {
        var listBox = new ListBox { MinHeight = 240, MinWidth = 240 };
        ApplySheetOptionListBoxStyle(listBox);
        AutomationProperties.SetAutomationId(listBox, "ReviewCommentList");
        AutomationProperties.SetName(listBox, UiText.Get("MainWindowMessage_CommentsTitle"));
        AutomationProperties.SetHelpText(listBox, UiText.Get("ReviewCommentList_ListHelpText"));
        listBox.ItemTemplate = new FuncDataTemplate<CommentListRowPlan>(
            (entry, _) => BuildCommentListRow(entry),
            supportsRecycling: true);
        var visibleComments = new ObservableCollection<CommentListRowPlan>();
        listBox.ItemsSource = visibleComments;

        var emptyText = new TextBlock
        {
            Text = UiText.Get("MainWindowMessage_NoCommentsOnSheet"),
            Foreground = Brush(110, 110, 110),
            IsVisible = comments.Count == 0,
            Margin = new Thickness(0, 0, 0, 8),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
        };

        var goToButton = new Button { Content = UiText.Get("ReviewCommentList_OpenButton"), MinWidth = 84, IsEnabled = false };
        ApplySheetOptionButtonChrome(goToButton, 84);
        AutomationProperties.SetAutomationId(goToButton, "ReviewCommentListOpenButton");

        IReadOnlyList<CommentListRowPlan> currentComments = comments;

        // Excel Comments pane parity: All / Active / Resolved filter, a live summary and per-thread
        // Resolve/Reopen + Delete, driven by the shared ThreadedCommentCardPlanner view-model.
        var filterBox = new ComboBox { MinWidth = 110 };
        filterBox.Items.Add(UiText.Get("ThreadedCommentPane_FilterAll"));
        filterBox.Items.Add(UiText.Get("ThreadedCommentPane_FilterActive"));
        filterBox.Items.Add(UiText.Get("ThreadedCommentPane_FilterResolved"));
        filterBox.SelectedIndex = 0;
        AvaloniaCompactDialogChrome.ApplyComboBox(filterBox, SheetOptionsDialogChromeStyle);
        AutomationProperties.SetAutomationId(filterBox, "ReviewCommentListFilter");
        AutomationProperties.SetName(filterBox, UiText.Get("MainWindowMessage_CommentsTitle"));
        var summaryText = new TextBlock
        {
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            Foreground = Brush(110, 110, 110),
            Margin = new Thickness(0, 0, 0, 6),
        };
        AutomationProperties.SetAutomationId(summaryText, "ReviewCommentListSummary");
        var toggleResolvedButton = new Button { Content = UiText.Get("ThreadedCommentCard_ResolveThread"), MinWidth = 112, IsEnabled = false };
        ApplySheetOptionButtonChrome(toggleResolvedButton, 112);
        AutomationProperties.SetAutomationId(toggleResolvedButton, "ReviewCommentListToggleResolvedButton");
        var deleteThreadButton = new Button { Content = UiText.Get("ThreadedCommentCard_DeleteThread"), MinWidth = 104, IsEnabled = false };
        ApplySheetOptionButtonChrome(deleteThreadButton, 104);
        AutomationProperties.SetAutomationId(deleteThreadButton, "ReviewCommentListDeleteThreadButton");

        ThreadedCommentPaneFilter CurrentFilter() => filterBox.SelectedIndex switch
        {
            1 => ThreadedCommentPaneFilter.Active,
            2 => ThreadedCommentPaneFilter.Resolved,
            _ => ThreadedCommentPaneFilter.All,
        };

        void UpdateThreadButtons()
        {
            var index = listBox.SelectedIndex;
            var sheetNow = _session.ActiveSheet;
            if (index < 0 || index >= currentComments.Count ||
                ThreadedCommentCardPlanner.BuildCard(sheetNow, currentComments[index].Address) is not { } card)
            {
                toggleResolvedButton.IsEnabled = false;
                deleteThreadButton.IsEnabled = false;
                return;
            }

            toggleResolvedButton.Content = card.ToggleResolvedLabel.Resolve(UiText.Get, UiText.Format);
            toggleResolvedButton.IsEnabled = card.CanToggleResolved;
            deleteThreadButton.IsEnabled = card.CanDeleteThread;
        }

        void RefreshList(IReadOnlyList<CommentListRowPlan> refreshedComments)
        {
            var paneSheet = _session.ActiveSheet;
            var pane = ThreadedCommentCardPlanner.BuildPane(paneSheet, CurrentFilter());
            var visibleAddresses = pane.Rows.Select(row => row.Address).ToHashSet();
            refreshedComments = refreshedComments.Where(row => visibleAddresses.Contains(row.Address)).ToList();
            summaryText.Text = UiText.Format("ThreadedCommentPane_SummaryFormat", pane.ActiveCount, pane.ResolvedCount);
            var selectedAddress = listBox.SelectedIndex >= 0 && listBox.SelectedIndex < currentComments.Count
                ? currentComments[listBox.SelectedIndex].Address
                : (CellAddress?)null;
            currentComments = refreshedComments;
            visibleComments.Clear();
            foreach (var comment in currentComments)
                visibleComments.Add(comment);
            listBox.UpdateLayout();
            listBox.SelectedIndex = selectedAddress is { } address
                ? currentComments.ToList().FindIndex(comment => comment.Address.Equals(address))
                : -1;
            if (listBox.SelectedIndex < 0 && currentComments.Count > 0)
                listBox.SelectedIndex = 0;
            emptyText.IsVisible = currentComments.Count == 0;
            goToButton.IsEnabled = listBox.SelectedIndex >= 0 && listBox.SelectedIndex < currentComments.Count;
            UpdateThreadButtons();
        }

        void ApplyThreadAction(bool delete)
        {
            var index = listBox.SelectedIndex;
            if (index < 0 || index >= currentComments.Count)
                return;

            var address = currentComments[index].Address;
            if (ThreadedCommentCardPlanner.BuildCard(_session.ActiveSheet, address) is not { } card)
                return;

            var result = ReviewSessionController.ApplyThreadedCommentCardAction(
                address,
                delete ? ThreadedCommentCardAction.DeleteThread : card.ToggleResolvedAction);
            if (!result.Success)
            {
                ShowEditIssue(result.ErrorMessage ?? UiText.Get("Comment_CommentFailed"));
                return;
            }

            ApplyReviewRefreshPlan(
                result.RefreshPlan,
                UiText.Format("Comment_CommentUpdated", FormatCellReference(address)));
        }

        void GoToSelected()
        {
            var index = listBox.SelectedIndex;
            if (index < 0 || index >= currentComments.Count)
                return;

            _session.SelectCell(currentComments[index].Address);
            RefreshShell(UiText.Format("ShellLoc_SelectedCell", FormatCellReference(currentComments[index].Address)));
        }

        listBox.SelectionChanged += (_, _) =>
        {
            goToButton.IsEnabled = listBox.SelectedIndex >= 0 && listBox.SelectedIndex < currentComments.Count;
            UpdateThreadButtons();
        };
        filterBox.SelectionChanged += (_, _) => RefreshList(CollectThreadedComments(_session.ActiveSheet));
        toggleResolvedButton.Click += (_, _) => ApplyThreadAction(delete: false);
        deleteThreadButton.Click += (_, _) => ApplyThreadAction(delete: true);
        listBox.DoubleTapped += (_, _) => GoToSelected();
        listBox.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Enter)
                return;

            GoToSelected();
            args.Handled = true;
        };
        goToButton.Click += (_, _) => GoToSelected();

        var actionButtons = new List<Button> { toggleResolvedButton, deleteThreadButton, goToButton };
        if (closeRequested is not null)
        {
            var closeButton = new Button
            {
                Content = UiText.Get("ReviewCommentList_CloseButton"),
                IsCancel = true,
                MinWidth = 84,
            };
            ApplySheetOptionButtonChrome(closeButton, 84);
            AutomationProperties.SetAutomationId(closeButton, "ReviewCommentListCloseButton");
            closeButton.Click += (_, _) => closeRequested();
            actionButtons.Add(closeButton);
        }

        // A wrapping row so the actions stay reachable in the narrow docked pane.
        var bottomRow = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
        };
        foreach (var actionButton in actionButtons)
        {
            actionButton.Margin = new Thickness(4, 2, 0, 2);
            bottomRow.Children.Add(actionButton);
        }
        DockPanel.SetDock(bottomRow, Dock.Bottom);
        DockPanel.SetDock(emptyText, Dock.Top);

        var header = BuildCommentListHeader();
        DockPanel.SetDock(header, Dock.Top);
        var filterRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 6) };
        filterRow.Children.Add(filterBox);
        filterRow.Children.Add(summaryText);
        summaryText.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        summaryText.Margin = new Thickness(0);
        DockPanel.SetDock(filterRow, Dock.Top);

        var content = new DockPanel
        {
            Margin = new Thickness(10),
            Children = { bottomRow, emptyText, filterRow, header, listBox },
        };

        RefreshList(comments);
        return new CommentsListSurface(content, RefreshList, () => listBox.Focus());
    }

    private static IReadOnlyList<CommentListRowPlan> CollectThreadedComments(Sheet sheet) =>
        CommentNavigationPlanner.CreateThreadedCommentRows(sheet.ThreadedComments);

    private static Grid BuildCommentListHeader()
    {
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions($"{ReviewCommentListCellColumnWidth},*"),
            MinHeight = 24,
            Background = Brush(242, 242, 242),
        };
        AddCommentListHeaderCell(
            header,
            UiText.Get("ReviewCommentList_CellColumnHeader"),
            "ReviewCommentListCellHeader",
            0);
        AddCommentListHeaderCell(
            header,
            UiText.Get("ReviewCommentList_TextColumnHeader"),
            "ReviewCommentListTextHeader",
            1);
        return header;
    }

    private static void AddCommentListHeaderCell(Grid header, string text, string automationId, int column)
    {
        var label = new TextBlock
        {
            Text = text,
            FontFamily = FormulaBarFontFamily,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            Margin = new Thickness(6, 0),
        };
        AutomationProperties.SetAutomationId(label, automationId);
        AutomationProperties.SetName(label, text);
        Grid.SetColumn(label, column);
        header.Children.Add(label);
    }

    private static Grid BuildCommentListRow(CommentListRowPlan entry)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions($"{ReviewCommentListCellColumnWidth},*"),
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
            MinHeight = 24,
        };
        AddCommentListRowCell(row, entry.Cell, $"ReviewCommentListCell_{entry.Cell}", 0);
        AddCommentListRowCell(row, entry.Text, $"ReviewCommentListText_{entry.Cell}", 1);
        return row;
    }

    private static void AddCommentListRowCell(Grid row, string text, string automationId, int column)
    {
        var label = new TextBlock
        {
            Text = text,
            FontFamily = FormulaBarFontFamily,
            FontSize = 12,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            Margin = new Thickness(6, 0),
        };
        AutomationProperties.SetAutomationId(label, automationId);
        Grid.SetColumn(label, column);
        row.Children.Add(label);
    }

    // ── Visual chrome helpers (SheetOptions / Show Comments dialog) ──────────

    /// <summary>
    /// Applies standard SheetOption-dialog button chrome (Height=24, FontSize=12, white background, grey/blue border).
    /// </summary>
    private static void ApplySheetOptionButtonChrome(Button button, double minWidth, bool isDefault = false)
        => AvaloniaCompactDialogChrome.ApplyButton(button, SheetOptionsDialogChromeStyle, minWidth, isDefault);

    /// <summary>
    /// Applies standard Show Comments list-box row chrome (MinHeight=24 per row, FontSize=12).
    /// </summary>
    private static void ApplySheetOptionListBoxStyle(ListBox listBox)
        => AvaloniaCompactDialogChrome.ApplyListBox(listBox, SheetOptionsDialogChromeStyle);
}
