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

using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.Comments;
using FreeX.Core.Model;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private const double ReviewCommentListCellColumnWidth = 80;

    private Window? _commentListWindow;
    private Action<IReadOnlyList<CommentListRowPlan>>? _refreshCommentListWindow;

    private static AvaloniaCompactDialogChromeStyle SheetOptionsDialogChromeStyle => new(FormulaBarFontFamily);

    /// <summary>
    /// Review ▸ Show Comments — list every threaded comment on the active sheet
    /// (cell ref + author + text). Double-click a row or use Go To to select that cell.
    /// Legacy notes remain a separate WPF-style Show Notes toggle-all command.
    /// </summary>
    private Task ShowCommentsListAsync()
    {
        if (_isOpening || _isSaving)
            return Task.CompletedTask;

        if (!TryCommitPendingFormulaEdit())
            return Task.CompletedTask;

        ClearSelectedDrawingObject();

        // Show Comments is a toggle (Excel): pressing it while the pane is open closes it.
        if (_commentListWindow is { IsVisible: true } openPane &&
            ThreadedCommentActivationPlanner.ShouldClosePaneOnToggle(paneOpen: true))
        {
            openPane.Close();
            return Task.CompletedTask;
        }

        var sheet = _session.ActiveSheet;
        var comments = CollectThreadedComments(sheet);
        if (comments.Count == 0)
        {
            ShowEditIssue(UiText.Get("MainWindowMessage_NoCommentsOnSheet"));
            return Task.CompletedTask;
        }

        if (_commentListWindow is { IsVisible: true } existing)
        {
            _refreshCommentListWindow?.Invoke(comments);
            if (existing.WindowState == WindowState.Minimized)
                existing.WindowState = WindowState.Normal;
            existing.Activate();
            return Task.CompletedTask;
        }

        var listBox = new ListBox { MinHeight = 240, MinWidth = 420 };
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
        var closeButton = new Button
        {
            Content = UiText.Get("ReviewCommentList_CloseButton"),
            IsCancel = true,
            MinWidth = 84,
        };
        ApplySheetOptionButtonChrome(closeButton, 84);
        AutomationProperties.SetAutomationId(closeButton, "ReviewCommentListCloseButton");

        var dialog = new Window
        {
            Title = UiText.Get("MainWindowMessage_CommentsTitle"),
            Width = 520,
            Height = 380,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "ReviewCommentListWindow");

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
        closeButton.Click += (_, _) => dialog.Close();

        // The pane docks at 280-400px, narrower than the four buttons side by side, so the row wraps
        // instead of cutting Open/Close off the right edge.
        var bottomRow = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
        };
        foreach (var actionButton in new[] { toggleResolvedButton, deleteThreadButton, goToButton, closeButton })
        {
            actionButton.Margin = new Thickness(8, 0, 0, 6);
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

        dialog.Content = new DockPanel
        {
            Margin = new Thickness(16),
            Children = { bottomRow, emptyText, filterRow, header, listBox },
        };

        RefreshList(comments);
        DockCommentPaneRight(dialog);
        _commentListWindow = dialog;
        _refreshCommentListWindow = RefreshList;
        ShowOwnedModelessWindow(
            dialog,
            () => listBox.Focus(),
            () =>
            {
                if (!ReferenceEquals(_commentListWindow, dialog))
                    return;

                _commentListWindow = null;
                _refreshCommentListWindow = null;
            });
        return Task.CompletedTask;
    }

    /// <summary>
    /// Docks the Comments pane to the right edge of the grid area (Excel's task-pane position) and
    /// keeps it there while the main window moves/resizes; falls back to the floating window when
    /// the grid would be squeezed. Mirrors WPF's DockCommentPaneRight.
    /// </summary>
    private void DockCommentPaneRight(Window pane)
    {
        void Apply()
        {
            if (WindowState == WindowState.Minimized || _sheetGridHost.Bounds.Width <= 0)
                return;

            var scaling = RenderScaling <= 0 ? 1 : RenderScaling;
            var origin = _sheetGridHost.PointToScreen(new Point(0, 0));
            var plan = ThreadedCommentActivationPlanner.PlanPaneDock(
                origin.X / scaling,
                origin.Y / scaling,
                _sheetGridHost.Bounds.Width,
                _sheetGridHost.Bounds.Height,
                contentTopInset: 0);
            if (!plan.IsDocked)
                return;

            pane.WindowStartupLocation = WindowStartupLocation.Manual;
            pane.Width = plan.Width;
            pane.Height = plan.Height;
            pane.Position = new PixelPoint((int)Math.Round(plan.Left * scaling), (int)Math.Round(plan.Top * scaling));
        }

        pane.Opened += (_, _) => Apply();
        EventHandler<PixelPointEventArgs> moved = (_, _) => { if (pane.IsVisible) Apply(); };
        EventHandler<SizeChangedEventArgs> resized = (_, _) => { if (pane.IsVisible) Apply(); };
        PositionChanged += moved;
        SizeChanged += resized;
        pane.Closed += (_, _) =>
        {
            PositionChanged -= moved;
            SizeChanged -= resized;
        };
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
