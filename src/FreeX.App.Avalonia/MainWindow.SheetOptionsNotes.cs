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

        var goToButton = new Button { Content = UiText.Get("ShellLoc_GoToButton"), MinWidth = 84, IsEnabled = false };
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

        void RefreshList(IReadOnlyList<CommentListRowPlan> refreshedComments)
        {
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
            goToButton.IsEnabled = listBox.SelectedIndex >= 0 && listBox.SelectedIndex < currentComments.Count;
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

        var bottomRow = AvaloniaCompactDialogChrome.CreateActionRow([goToButton, closeButton], new Thickness(0, 10, 0, 0));
        DockPanel.SetDock(bottomRow, Dock.Bottom);
        DockPanel.SetDock(emptyText, Dock.Top);

        var header = BuildCommentListHeader();
        DockPanel.SetDock(header, Dock.Top);

        dialog.Content = new DockPanel
        {
            Margin = new Thickness(16),
            Children = { bottomRow, emptyText, header, listBox },
        };

        RefreshList(comments);
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
