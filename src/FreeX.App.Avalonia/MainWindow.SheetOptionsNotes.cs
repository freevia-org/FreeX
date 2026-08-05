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
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private const double ReviewCommentListCellColumnWidth = 80;

    private Window? _commentListWindow;
    private Action<IReadOnlyList<SheetCommentEntry>>? _refreshCommentListWindow;

    private static AvaloniaCompactDialogChromeStyle SheetOptionsDialogChromeStyle => new(FormulaBarFontFamily);

    // Page Layout ▸ Sheet Options ▸ Gridlines / Headings, and Review ▸ Show Notes.
    //
    // In Excel the Page Layout "Gridlines" and "Headings" Sheet Options expose two
    // sub-toggles each (View + Print). The View tab already wires the on-screen view
    // toggles (view.gridlines -> ToggleShowGridlines, view.headings -> ToggleShowHeadings,
    // backed by WorkbookSession.SetShowGridlines / SetShowHeadings).
    //
    // The print side IS modeled: Sheet.PrintGridlines / Sheet.PrintHeadings are real,
    // editable fields persisted via SetPrintOptionsCommand. We therefore present the same
    // two-checkbox popup Excel does (View / Print), so the Page Layout buttons control BOTH
    // the view setting and the print setting, with undo/redo for the print half (the view
    // half routes through the existing session toggles which also support undo).

    /// <summary>
    /// Page Layout ▸ Sheet Options ▸ Gridlines. Two-checkbox popup: View + Print.
    /// View half reuses SetShowGridlines; Print half routes through a narrow print-options command
    /// so it participates in undo/redo without rebuilding the full Page Setup state.
    /// </summary>
    private async Task ShowGridlinesSheetOptionsAsync() =>
        await ShowSheetOptionTwoToggleAsync(
            title: UiText.Get("ShellLoc_GridlinesTitle"),
            label: UiText.Get("ShellLoc_GridlinesTitle"),
            getView: () => _session.IsShowingGridlines,
            getPrint: () => _session.ActiveSheet.PrintGridlines,
            setView: showView =>
            {
                if (showView == _session.IsShowingGridlines)
                    return true;
                var result = _session.SetShowGridlines(showView);
                if (!result.Success)
                    ShowEditIssue(result.ErrorMessage ?? UiText.Get("ShellLoc_GridlinesFailed"));
                return result.Success;
            },
            planPrintCommand: print => CreatePageLayoutCommandSession().PlanPrintGridlines(
                print,
                _session.ActiveSheet.PrintHeadings));

    /// <summary>
    /// Page Layout ▸ Sheet Options ▸ Headings. Two-checkbox popup: View + Print.
    /// </summary>
    private async Task ShowHeadingsSheetOptionsAsync() =>
        await ShowSheetOptionTwoToggleAsync(
            title: UiText.Get("ShellLoc_HeadingsTitle"),
            label: UiText.Get("ShellLoc_HeadingsTitle"),
            getView: () => _session.IsShowingHeadings,
            getPrint: () => _session.ActiveSheet.PrintHeadings,
            setView: showView =>
            {
                if (showView == _session.IsShowingHeadings)
                    return true;
                var result = _session.SetShowHeadings(showView);
                if (!result.Success)
                {
                    ShowEditIssue(result.ErrorMessage ?? UiText.Get("ShellLoc_HeadingsFailed"));
                    return false;
                }

                RefreshViewportSizeForZoom();
                return true;
            },
            planPrintCommand: print => CreatePageLayoutCommandSession().PlanPrintHeadings(
                _session.ActiveSheet.PrintGridlines,
                print));

    private async Task ShowSheetOptionTwoToggleAsync(
        string title,
        string label,
        Func<bool> getView,
        Func<bool> getPrint,
        Func<bool, bool> setView,
        Func<bool, PageLayoutCommandExecutionPlan> planPrintCommand)
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        ClearSelectedDrawingObject();

        var viewCheck = new CheckBox { Content = UiText.Get("ShellLoc_SheetOptionView"), IsChecked = getView() };
        ApplySheetOptionCheckBoxChrome(viewCheck);
        AutomationProperties.SetAutomationId(viewCheck, "SheetOptionViewCheck");
        var printCheck = new CheckBox { Content = UiText.Get("ShellLoc_SheetOptionPrint"), IsChecked = getPrint() };
        ApplySheetOptionCheckBoxChrome(printCheck);
        AutomationProperties.SetAutomationId(printCheck, "SheetOptionPrintCheck");

        var ok = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true, MinWidth = 84 };
        ApplySheetOptionButtonChrome(ok, 84, isDefault: true);
        AutomationProperties.SetAutomationId(ok, "SheetOptionOkButton");
        var cancel = new Button
        {
            Content = UiText.Get("Common_Cancel"),
            IsCancel = true,
            MinWidth = 84,
        };
        ApplySheetOptionButtonChrome(cancel, 84);

        var buttonRow = AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0, 14, 0, 0));

        var dialog = new Window
        {
            Title = title,
            Width = 280,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Children =
                {
                    new TextBlock { Text = label, FontWeight = FontWeight.SemiBold, FontSize = 12, FontFamily = FormulaBarFontFamily, Margin = new Thickness(0, 0, 0, 8) },
                    viewCheck,
                    printCheck,
                    buttonRow,
                },
            },
        };
        AutomationProperties.SetAutomationId(dialog, "SheetOptionDialog");

        ok.Click += (_, _) => dialog.Close(true);
        cancel.Click += (_, _) => dialog.Close(false);

        var confirmed = await dialog.ShowDialog<bool>(this);
        if (!confirmed)
            return;

        var wantView = viewCheck.IsChecked == true;
        var wantPrint = printCheck.IsChecked == true;

        // View half via the existing session toggles.
        if (wantView != getView() && !setView(wantView))
            return;

        // Print half via a rebuilt page-setup command (undo/redo aware).
        if (wantPrint != getPrint())
        {
            var plan = planPrintCommand(wantPrint);
            var result = _session.ExecuteReviewCommand(plan.Command);
            if (!result.Success)
            {
                ShowEditIssue(result.ErrorMessage ?? UiText.Get("ShellLoc_CouldNotUpdatePrintOptions"));
                return;
            }
        }

        RefreshShell(UiText.Format(
            "ShellLoc_SheetOptionStatus",
            label,
            wantView ? UiText.Get("ShellLoc_OnState") : UiText.Get("ShellLoc_OffState"),
            wantPrint ? UiText.Get("ShellLoc_OnState") : UiText.Get("ShellLoc_OffState")));
    }

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
        listBox.ItemTemplate = new FuncDataTemplate<SheetCommentEntry>(
            (entry, _) => BuildCommentListRow(entry),
            supportsRecycling: true);
        var visibleComments = new ObservableCollection<SheetCommentEntry>();
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

        IReadOnlyList<SheetCommentEntry> currentComments = comments;

        void RefreshList(IReadOnlyList<SheetCommentEntry> refreshedComments)
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

    private static List<SheetCommentEntry> CollectThreadedComments(Sheet sheet)
    {
        var entries = new List<SheetCommentEntry>();

        foreach (var (address, comment) in sheet.ThreadedComments)
        {
            entries.Add(new SheetCommentEntry(
                address,
                address.ToA1(),
                CommentNavigationPlanner.FormatThreadedComment(comment)));
        }

        entries.Sort(static (x, y) => x.Address.CompareTo(y.Address));
        return entries;
    }

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

    private static Grid BuildCommentListRow(SheetCommentEntry entry)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions($"{ReviewCommentListCellColumnWidth},*"),
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
            MinHeight = 24,
        };
        AddCommentListRowCell(row, entry.Cell, $"ReviewCommentListCell_{entry.Address.ToA1()}", 0);
        AddCommentListRowCell(row, entry.Text, $"ReviewCommentListText_{entry.Address.ToA1()}", 1);
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

    private readonly record struct SheetCommentEntry(CellAddress Address, string Cell, string Text);

    // ── Visual chrome helpers (SheetOptions / Show Comments dialog) ──────────

    /// <summary>
    /// Applies standard SheetOption-dialog button chrome (Height=24, FontSize=12, white background, grey/blue border).
    /// </summary>
    private static void ApplySheetOptionButtonChrome(Button button, double minWidth, bool isDefault = false)
        => AvaloniaCompactDialogChrome.ApplyButton(button, SheetOptionsDialogChromeStyle, minWidth, isDefault);

    /// <summary>
    /// Applies standard SheetOption-dialog check-box chrome (MinHeight=20, MaxHeight=20, FontSize=12).
    /// </summary>
    private static void ApplySheetOptionCheckBoxChrome(CheckBox checkBox)
    {
        StripContentMnemonic(checkBox);
        checkBox.MinHeight = 20;
        checkBox.MaxHeight = 20;
        AvaloniaCompactDialogChrome.ApplyCheckBox(checkBox, SheetOptionsDialogChromeStyle);
    }

    /// <summary>
    /// Applies standard Show Comments list-box row chrome (MinHeight=24 per row, FontSize=12).
    /// </summary>
    private static void ApplySheetOptionListBoxStyle(ListBox listBox)
        => AvaloniaCompactDialogChrome.ApplyListBox(listBox, SheetOptionsDialogChromeStyle);
}
