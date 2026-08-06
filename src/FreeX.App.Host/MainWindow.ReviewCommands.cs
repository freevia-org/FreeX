using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Free.Shared.AppServices;
using FreeX.App.Presentation.Accessibility;
using FreeX.App.Presentation.Comments;
using FreeX.App.Presentation.DrawingUI;
using FreeX.App.Services;
using FreeX.App.UI;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private CommentListWindow? _reviewCommentsWindow;
    private CommentListWindow? _reviewNotesWindow;

    private void SpellCheckBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryCommitPendingSpellCheckEdit())
            return;

        var controller = new SpellCheckSessionController(new SpellCheckSessionAdapter(
            () => _workbook,
            () => _currentSheetId,
            () => _options.SpellCheckCustomDictionaryWords,
            command =>
            {
                var success = TryExecuteCommand(command, "Spell Check", out var outcome);
                return new SpellCheckCommandExecutionResult(
                    success,
                    outcome.ErrorMessage,
                    outcome.IsNoOp);
            },
            () => AppOptionsStore.Save(_options)));
        var transition = controller.Start();

        while (transition.RequiresReview)
        {
            var issue = transition.Issue!;
            SetActiveCell(issue.Address);
            EnsureCellVisible(issue.Address);
            UpdateViewport();
            RefreshSpellCheckEditorState(issue.Address);

            var dialog = new SpellCheckDialog(issue.Word, issue.Suggestion) { Owner = this };
            if (dialog.ShowDialog() != true)
            {
                controller.Apply(new(SpellCheckSessionAction.Stop));
                return;
            }

            transition = controller.Apply(dialog.Result);
            if (dialog.Result.Action is SpellCheckSessionAction.Change or SpellCheckSessionAction.ChangeAll &&
                transition.Status != SpellCheckSessionStatus.Failed)
            {
                RefreshSpellCheckEditorState(issue.Address);
                UpdateViewport();
                RefreshStatusBar();
            }
        }

        if (transition.Status == SpellCheckSessionStatus.Complete)
        {
            _messageService.ShowInfo(
                UiText.Get("MainWindowMessage_SpellCheckComplete"),
                UiText.Get("MainWindowMessage_SpellCheckTitle"));
        }
    }

    private void RefreshSpellCheckEditorState(CellAddress address)
    {
        HideInlineEditor(commit: false);
        ClearFormulaRangeEntryState();
        var sheet = _workbook.GetSheet(address.Sheet);
        SetFormulaBarSelectionText(FormatFormulaBarText(sheet?.GetCell(address), address));
    }

    private void WorkbookStatisticsBtn_Click(object sender, RoutedEventArgs e)
    {
        var statistics = WorkbookStatisticsService.GetStatistics(_workbook);
        var dialog = new WorkbookStatisticsDialog(statistics) { Owner = this };
        dialog.ShowDialog();
    }

    private void AccessibilityCheckerBtn_Click(object sender, RoutedEventArgs e)
    {
        var issues = AccessibilityCheckerService.FindIssues(_workbook);
        var dialog = new AccessibilityCheckerDialog(issues) { Owner = this };
        if (dialog.ShowDialog() == true)
            NavigateToCell(AccessibilityCheckerDialogPlanner.GetNavigationTarget(dialog.Result!.Issue));
    }

    private void SetAltTextBtn_Click(object sender, RoutedEventArgs e)
    {
        var target = GetTargetAltTextObject(_currentSheetId);
        if (target is null)
        {
            _messageService.ShowInfo(
                UiText.Get("MainWindowMessage_AltTextNoObjectAtSelection"),
                UiText.Get("MainWindowMessage_AltTextTitle"));
            return;
        }

        var dialog = new TextEntryDialog(
            UiText.Get("MainWindowMessage_AltTextTitle"),
            UiText.Get("MainWindowMessage_AltTextLabel"),
            target.AltText ?? "") { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Alt Text",
                sheetId =>
                {
                    var groupedTarget = GetTargetAltTextObject(sheetId, target.Kind);
                    return DrawingObjectFormatCommandPolicy.BuildAltTextCommand(
                        sheetId,
                        target.Kind,
                        groupedTarget?.Id ?? Guid.Empty,
                        dialog.Result.Text);
                }))
        {
            return;
        }

        SetActiveCell(target.Anchor);
        EnsureCellVisible(target.Anchor);
        UpdateViewport();
        RefreshStatusBar();
    }

    private DrawingObjectAltTextTarget? GetTargetAltTextObject(
        SheetId sheetId,
        DrawingObjectTargetKind? preferredKind = null)
    {
        var sheet = _workbook.GetSheet(sheetId);
        if (sheet is null)
            return null;

        return DrawingTargetResolver.GetTargetAltTextObject(sheet, SheetGrid.SelectedRange?.Start, preferredKind);
    }

    private void ReviewNewCommentBtn_Click(object sender, RoutedEventArgs e)
    {
        var target = ReviewSessionController.GetSelectedNoteTarget();
        if (target is null) return;

        EnsureCellVisible(target.Address);
        UpdateViewport();
        SheetGrid.BeginNoteInlineEdit(target.Address, target.Address.ToA1(), target.NoteText);
    }

    private void ReviewNewThreadedCommentBtn_Click(object sender, RoutedEventArgs e)
    {
        var target = ReviewSessionController.GetSelectedThreadedCommentTarget();
        if (target is null) return;

        EnsureCellVisible(target.Address);
        UpdateViewport();
        SheetGrid.BeginThreadedCommentInlineEdit(target.Address, target.Address.ToA1(), target.ThreadedComment);
    }

    private void SheetGrid_NoteInlineEditSubmitted(object? sender, GridNoteInlineEditSubmittedEventArgs e)
    {
        var result = ReviewSessionController.ApplyNote(e.Text);
        if (!result.Success)
        {
            e.KeepOpen = true;
            e.ErrorMessage = result.ErrorMessage;
            return;
        }

        ApplyReviewRefreshPlan(result.RefreshPlan);
    }

    private void SheetGrid_ThreadedCommentInlineEditSubmitted(object? sender, GridThreadedCommentInlineEditSubmittedEventArgs e)
    {
        var result = e.Result;
        var mutation = ReviewSessionController.ApplyThreadedComment(
            new ThreadedCommentDialogResult(
                result.RootText,
                result.ReplyText,
                result.IsResolved,
                result.Action switch
                {
                    GridThreadedCommentEditAction.EditReply => ThreadedCommentDialogAction.EditReply,
                    GridThreadedCommentEditAction.DeleteReply => ThreadedCommentDialogAction.DeleteReply,
                    _ => ThreadedCommentDialogAction.ApplyThread,
                },
                result.ReplyIndex,
                result.ReplyEditText));
        if (!mutation.Success)
        {
            e.KeepOpen = true;
            e.ErrorMessage = mutation.ErrorMessage;
            return;
        }

        ApplyReviewRefreshPlan(mutation.RefreshPlan);
    }

    private GridRange ResolveInlineCommentEditRange(CellAddress address)
    {
        if (SheetGrid.SelectedRange is { } selectedRange && selectedRange.Contains(address))
            return selectedRange;

        return new GridRange(address, address);
    }

    private void ReviewDeleteCommentBtn_Click(object sender, RoutedEventArgs e)
    {
        var result = ReviewSessionController.DeleteNote();
        if (!result.Success)
            return;

        ApplyReviewRefreshPlan(result.RefreshPlan);
    }

    private void ReviewDeleteThreadedCommentBtn_Click(object sender, RoutedEventArgs e)
    {
        var result = ReviewSessionController.DeleteThreadedComment();
        if (!result.Success)
            return;

        ApplyReviewRefreshPlan(result.RefreshPlan);
    }

    private void ReviewPrevCommentBtn_Click(object sender, RoutedEventArgs e)
    {
        NavigateThreadedComment(previous: true);
    }

    private void ReviewNextCommentBtn_Click(object sender, RoutedEventArgs e)
    {
        NavigateThreadedComment(previous: false);
    }

    private void ReviewShowCommentsBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null || sheet.ThreadedComments.Count == 0)
        {
            _messageService.ShowInfo(
                UiText.Get("MainWindowMessage_NoCommentsOnSheet"),
                UiText.Get("MainWindowMessage_CommentsTitle"));
            return;
        }

        var items = CommentListWindow.CreateThreadedCommentItems(sheet.ThreadedComments);
        _reviewCommentsWindow = ShowOrRefreshCommentListWindow(
            _reviewCommentsWindow,
            UiText.Get("MainWindowMessage_CommentsTitle"),
            items,
            window => _reviewCommentsWindow = window);
    }

    private void ReviewPrevNoteBtn_Click(object sender, RoutedEventArgs e)
    {
        NavigateNote(previous: true);
    }

    private void ReviewNextNoteBtn_Click(object sender, RoutedEventArgs e)
    {
        NavigateNote(previous: false);
    }

    private void ReviewShowNotesBtn_Click(object sender, RoutedEventArgs e)
    {
        // Review tab "Show Notes" — pin/unpin all notes on the sheet (toggle-all).
        ExecuteShowAllNotes();
    }

    private void ReviewConvertNotesToCommentsBtn_Click(object sender, RoutedEventArgs e)
    {
        var result = ReviewSessionController.ConvertNotesToComments();
        if (!result.Success)
            return;

        ApplyReviewRefreshPlan(result.RefreshPlan);
    }

    private CommentListWindow ShowOrRefreshCommentListWindow(
        CommentListWindow? window,
        string title,
        IReadOnlyList<CommentListWindowItem> items,
        Action<CommentListWindow?> setWindow)
    {
        if (window is null || !window.IsLoaded)
        {
            window = new CommentListWindow(title, items, NavigateToCell) { Owner = this };
            window.Closed += (_, _) => setWindow(null);
            window.Show();
            return window;
        }

        window.Refresh(items);
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Activate();
        return window;
    }

    private void ExecuteShowHideNote(CellAddress address)
    {
        var result = ReviewSessionController.ToggleNoteVisibility(address);
        if (!result.Success)
            return;

        ApplyReviewRefreshPlan(result.RefreshPlan);
    }

    private void ExecuteShowAllNotes()
    {
        var result = ReviewSessionController.ToggleAllNotesVisibility();
        if (!result.Success)
            return;

        ApplyReviewRefreshPlan(result.RefreshPlan);
    }

    private void NavigateThreadedComment(bool previous)
    {
        var result = ReviewSessionController.NavigateThreadedComment(previous);
        if (!result.Success || result.Target is not { } target)
        {
            _messageService.ShowInfo(
                UiText.Get("MainWindowMessage_NoCommentsOnSheet"),
                UiText.Get("MainWindowMessage_CommentsTitle"));
            return;
        }

        EnsureCellVisible(target);
        ApplyReviewRefreshPlan(result.RefreshPlan);
    }

    private void NavigateNote(bool previous)
    {
        var result = ReviewSessionController.NavigateNote(previous);
        if (!result.Success || result.Target is not { } target)
        {
            _messageService.ShowInfo(
                UiText.Get("MainWindowMessage_NoCommentsOnSheet"),
                UiText.Get("MainWindow_Text_Notes"));
            return;
        }

        EnsureCellVisible(target);
        ApplyReviewRefreshPlan(result.RefreshPlan);
    }

    private void RefreshReviewCommentNoteCommandStates()
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var selectedAddress = SheetGrid.SelectedRange?.Start;
        var hasSelection = selectedAddress is not null;
        var selectedHasThreadedComment = selectedAddress is { } threadedAddress &&
            SheetHasThreadedCommentAtSelection(sheet, threadedAddress);
        var selectedHasNote = selectedAddress is { } noteAddress &&
            SheetHasNoteAtSelection(sheet, noteAddress);
        var hasAnyThreadedComments = (sheet?.ThreadedComments.Count ?? 0) > 0;
        var hasAnyNotes = (sheet?.Comments.Count ?? 0) > 0;

        // Enablement flows through the neutral RibbonStateStore to the rendered ribbon buttons
        // (keyed by CommandName), so no hidden backplane control is needed.
        _ribbonState.SetEnabled("New Comment", hasSelection);
        _ribbonState.SetEnabled("Delete Comment", selectedHasThreadedComment);
        _ribbonState.SetEnabled("Previous Comment", hasAnyThreadedComments);
        _ribbonState.SetEnabled("Next Comment", hasAnyThreadedComments);

        _ribbonState.SetEnabled("New Note", hasSelection);
        _ribbonState.SetEnabled("Edit Note", selectedHasNote);
        _ribbonState.SetEnabled("Delete Note", selectedHasNote);
        _ribbonState.SetEnabled("Previous Note", hasAnyNotes);
        _ribbonState.SetEnabled("Next Note", hasAnyNotes);
        _ribbonState.SetEnabled("Convert to Comments", hasAnyNotes);
    }

    private static bool SheetHasThreadedCommentAtSelection(Sheet? sheet, CellAddress address) =>
        sheet?.ThreadedComments.ContainsKey(address) == true;

    private static bool SheetHasNoteAtSelection(Sheet? sheet, CellAddress address) =>
        sheet?.Comments.ContainsKey(address) == true;

    private void RefreshOpenReviewCommentNoteWindows()
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null)
            return;

        _reviewCommentsWindow?.Refresh(CommentListWindow.CreateThreadedCommentItems(sheet.ThreadedComments));
        _reviewNotesWindow?.Refresh(CommentListWindow.CreateNoteItems(sheet.Comments));
    }

    private void ProtectSheetBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;

        var state = ProtectionSession.ProjectSheet(sheet);
        string? unprotectPassword = null;
        if (state.IsProtected && !TryConfirmSheetUnprotectPassword(sheet, out unprotectPassword))
            return;

        var options = state.Options with { Password = unprotectPassword };
        if (!state.IsProtected)
        {
            var dialog = new PasswordProtectionDialog(
                UiText.Get("MainWindowMessage_ProtectSheetTitle"),
                UiText.Get("MainWindowMessage_OptionalPasswordLabel")) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            options = ProtectSheetOptions.FromCorePermissions(
                dialog.SelectedSheetPermissions,
                dialog.Password,
                dialog.Password);
        }

        var outcome = ProtectionSession.ExecuteSheet(sheet, options);
        if (!outcome.Success)
            return;

        _messageService.ShowInfo(
            UiText.Get(outcome.SuccessMessageResourceKey),
            UiText.Get(outcome.TitleResourceKey));
        RefreshSheetTabs();
    }

    private bool TryConfirmSheetUnprotectPassword(Sheet sheet, out string? password) =>
        TryConfirmUnprotectPassword(
            sheet.ProtectionPassword,
            UiText.Get("Protection_UnprotectSheetTitle"),
            out password);

    private void ProtectWorkbookBtn_Click(object sender, RoutedEventArgs e)
    {
        var state = ProtectionSession.ProjectWorkbook();
        string? pwd = null;
        if (!state.IsStructureProtected)
        {
            var dialog = new PasswordProtectionDialog(
                UiText.Get("MainWindowMessage_ProtectWorkbookTitle"),
                UiText.Get("MainWindowMessage_OptionalPasswordLabel")) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            pwd = dialog.Password;
        }
        else if (!TryConfirmWorkbookUnprotectPassword(out pwd))
        {
            return;
        }

        var outcome = ProtectionSession.ExecuteWorkbook(pwd);
        if (!outcome.Success)
            return;

        _messageService.ShowInfo(
            UiText.Get(outcome.SuccessMessageResourceKey),
            UiText.Get(outcome.TitleResourceKey));
        RefreshWorkbookProtectionUi();
        RefreshSheetTabs();
    }

    private bool TryConfirmWorkbookUnprotectPassword(out string? password) =>
        TryConfirmUnprotectPassword(
            _workbook.StructureProtectionPassword,
            UiText.Get("Protection_UnprotectWorkbookTitle"),
            out password);

    private bool TryConfirmUnprotectPassword(string? storedPassword, string title, out string? password)
    {
        password = null;
        if (string.IsNullOrEmpty(storedPassword))
            return true;

        var dialog = new PasswordProtectionDialog(
            title,
            UiText.Get("Protection_Password2")) { Owner = this };
        if (dialog.ShowDialog() != true)
            return false;

        password = dialog.Password;
        if (ProtectionPasswordHelper.VerifyStoredPassword(storedPassword, password))
            return true;

        _messageService.ShowWarning(UiText.Get("MainWindowMessage_ReviewPasswordIncorrect"), title);
        return false;
    }

    private void AllowEditRangesBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null)
            return;

        var defaultRange = SheetGrid.SelectedRange?.ToString() ?? "A1:A1";
        AllowEditRangeDialog? dialog = null;
        dialog = new AllowEditRangeDialog(
            _currentSheetId,
            defaultRange,
            sheet.AllowEditRanges,
            request => ApplyAllowEditRangeSelection(dialog, request),
            sheet.AllowEditRangePasswords) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        var plan = AllowEditRangePlanner.CreateCommandPlan(
            _currentSheetId,
            dialog.Result,
            dialog.RangePassword,
            dialog.RangePasswordChanged,
            sheet.AllowEditRangePasswords);
        if (plan is null)
            return;

        var successMessage = plan switch
        {
            { Action: AllowEditRangeAction.Add, Range: { } range } =>
                UiText.Format("MainWindowMessage_AllowEditRangeAdded", range),
            { Action: AllowEditRangeAction.Modify, Range: { } range } =>
                UiText.Format("MainWindowMessage_AllowEditRangeModified", range),
            { Action: AllowEditRangeAction.Remove, Range: { } range } =>
                UiText.Format("MainWindowMessage_AllowEditRangeRemoved", range),
            _ => UiText.Get("MainWindowMessage_AllowEditRangesCleared")
        };

        if (!TryExecuteCommand(plan.Command, "Allow Users to Edit Ranges"))
            return;

        _messageService.ShowInfo(successMessage, UiText.Get("MainWindowMessage_AllowEditRangesTitle"));
    }

    private void ApplyAllowEditRangeSelection(AllowEditRangeDialog? dialog, AllowEditRangeSelectionRequest request)
    {
        if (dialog is null)
            return;

        BeginDialogRangeSelection(
            dialog,
            request.CollapseDialog,
            selectedRange => dialog.ApplyRangeSelection(FormatRangeReference(selectedRange.Start, selectedRange.End)));
    }

    private async void ShareWorkbookBtn_Click(object sender, RoutedEventArgs e) => await ShareWorkbookAsync();

    private async Task ShareWorkbookAsync()
    {
        var plan = WorkbookShareReadinessPlanner.CreatePlan(
            _currentFilePath,
            WorkbookShareSurface.WindowsShare);
        if (plan.Kind == WorkbookShareReadinessPlanKind.SaveAsBeforeShare)
        {
            if (!await SaveWorkbookWithDialogAsync())
                return;
        }
        else if (FileSavePlanner.TryResolveExistingPath(plan.Path, _fileAdapters, out var target))
        {
            if (!await SaveWorkbookToTargetAsync(target!))
                return;
        }

        var sharePath = plan.Kind == WorkbookShareReadinessPlanKind.ShareExistingFile
            ? plan.Path
            : _currentFilePath;
        if (string.IsNullOrWhiteSpace(sharePath))
            return;

        try
        {
            await _shareService.ShareFileAsync(this, sharePath, _workbook.Name);
            // Return to the workbook after a successful share instead of leaving the user
            // stranded in the File backstage (Issue 118).
            if (IsStartScreenVisible())
                HideStartScreen();
        }
        catch (Exception ex)
        {
            _messageService.ShowError(
                UiText.Format("MainWindowMessage_ShareWorkbookFailed", ex.Message),
                UiText.Get("MainWindowMessage_ShareWorkbookTitle"));
        }
    }

    private void HelpOnlineBtn_Click(object sender, RoutedEventArgs e)
    {
        OpenExternalHelpLink(AppInfo.HelpUrl, UiText.Get("MainWindowMessage_HelpOnlineTitle"));
    }

    private async void CheckForUpdatesBtn_Click(object sender, RoutedEventArgs e)
    {
        RecordDiagnosticEvent("update_check_opened", new Dictionary<string, string?>
        {
            ["source"] = "help"
        });

        if (!App.TryGetServices(out var services))
            return;

        var updates = services.GetService<FreeX.App.Services.Updates.IUpdateService>();
        if (updates is null) return;

        var result = await updates.CheckAndDownloadAsync();
        switch (result.State)
        {
            case FreeX.App.Services.Updates.UpdateState.ReadyToApply:
                ShowUpdateReady(result.AvailableVersion);
                break;
            case FreeX.App.Services.Updates.UpdateState.UpToDate:
                ShowUpToDate();
                break;
            default: // Unavailable — fall back to the releases page
                OpenExternalHelpLink(updates.ReleasesPageUrl, UiText.Get("MainWindowMessage_CheckForUpdatesTitle"));
                break;
        }
    }

    private void AboutBtn_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AboutDialog();
        ShowOwnedDialog(dialog);
    }

    private void LegalNoticesBtn_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new LegalNoticesDialog();
        ShowOwnedDialog(dialog);
    }

    private void FeedbackBtn_Click(object sender, RoutedEventArgs e)
    {
        var context = CreateIssueReportContext();
        _diagnostics?.RecordEvent("report_issue_opened", new Dictionary<string, string?>
        {
            ["source"] = "help"
        });

        OpenExternalHelpLink(AppIssueReporter.CreateIssueUrl(context), UiText.Get("MainWindowMessage_FeedbackTitle"));
    }

    private void CopyDiagnosticsBtn_Click(object sender, RoutedEventArgs e)
    {
        var context = CreateIssueReportContext();
        var diagnosticsText = AppIssueReporter.CreateDiagnosticsText(context);

        try
        {
            System.Windows.Clipboard.SetText(diagnosticsText);
            _diagnostics?.RecordEvent("diagnostics_copied", new Dictionary<string, string?>
            {
                ["source"] = "help"
            });
            ShowOwnedMessage(
                UiText.Get("MainWindowMessage_DiagnosticsCopied"),
                UiText.Get("MainWindowMessage_CopyDiagnosticsTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ShowOwnedMessage(
                UiText.Format("MainWindowMessage_DiagnosticsCopyFailed", ex.Message),
                UiText.Get("MainWindowMessage_CopyDiagnosticsTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private AppIssueReportContext CreateIssueReportContext()
    {
        return AppIssueReporter.CreateContext(
            AppInfo.FeedbackUrl,
            _diagnosticsMetadata,
            _diagnosticsOptions.IsEnabled);
    }

    private void OpenExternalHelpLink(string url, string title)
    {
        var result = ExternalUrlLauncher.Open(url);
        if (result == ExternalUrlLaunchResult.Launched)
            return;

        var reason = result == ExternalUrlLaunchResult.BlockedScheme
            ? UiText.Get("MainWindowMessage_ExternalLinkBlockedScheme")
            : UiText.Get("MainWindowMessage_ExternalLinkCouldNotBeOpened");
        ShowOwnedMessage(
            UiText.Format("MainWindowMessage_ExternalLinkOpenFailed", url, reason),
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }
}
