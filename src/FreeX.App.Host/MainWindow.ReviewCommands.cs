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

        var customDictionary = SpellCheckWorkflowPlanner.CreateCustomDictionary(_options.SpellCheckCustomDictionaryWords);
        var ignoredWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ignoredIssues = new HashSet<SpellingIssueKey>();

        while (true)
        {
            var scan = SpellCheckWorkflowPlanner.ScanWorksheet(
                _workbook,
                _currentSheetId,
                customDictionary,
                ignoredWords,
                ignoredIssues);
            if (scan.IsComplete)
            {
                _messageService.ShowInfo(
                    UiText.Get("MainWindowMessage_SpellCheckComplete"),
                    UiText.Get("MainWindowMessage_SpellCheckTitle"));
                return;
            }

            var issues = scan.Issues;
            var issue = issues[0];
            SetActiveCell(issue.Address);
            EnsureCellVisible(issue.Address);
            UpdateViewport();
            RefreshSpellCheckEditorState(issue.Address);

            var dialog = new SpellCheckDialog(issue.Word, issue.Suggestion) { Owner = this };
            if (dialog.ShowDialog() != true)
                return;

            if (dialog.Result.Action == SpellCheckDialogAction.Ignore)
            {
                ignoredIssues.Add(SpellCheckWorkflowPlanner.CreateIssueKey(issue));
                continue;
            }

            if (dialog.Result.Action == SpellCheckDialogAction.IgnoreAll)
            {
                ignoredWords.Add(issue.Word);
                continue;
            }

            if (dialog.Result.Action == SpellCheckDialogAction.Add)
            {
                if (SpellCheckWorkflowPlanner.AddCustomDictionaryWord(
                        _options.SpellCheckCustomDictionaryWords,
                        customDictionary,
                        issue.Word))
                    _options.Save();

                continue;
            }

            var replacement = dialog.Result.Replacement ?? issue.Suggestion;

            if (dialog.Result.Action == SpellCheckDialogAction.ReplaceAll)
            {
                var command = SpellCheckWorkflowPlanner.BuildReplaceAllCommand(issues, issue.Word, replacement);
                if (command is not null && !TryExecuteSpellCheckCommand(command))
                    return;

                RefreshSpellCheckEditorState(issue.Address);
                UpdateViewport();
                RefreshStatusBar();
                continue;
            }

            if (!TryExecuteSpellCheckCommand(SpellCheckWorkflowPlanner.BuildReplacementCommand(issue, replacement)))
                return;

            RefreshSpellCheckEditorState(issue.Address);
            UpdateViewport();
            RefreshStatusBar();
        }
    }

    private bool TryExecuteSpellCheckCommand(IWorkbookCommand command) =>
        TryExecuteCommand(command, "Spell Check");

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
        {
            // R127-review-delete-enablement-1: the ribbon button now greys out whenever the
            // active cell has no note (RefreshReviewCommentNoteCommandStates runs on every
            // selection change), but this still fires for reachable no-op paths -- the
            // worksheet context-menu "Delete Note" item and any stale ribbon state -- so
            // surface the failure instead of silently doing nothing (mirrors Avalonia's
            // DeleteActiveCellNote, which calls RefreshShell on failure).
            _messageService.ShowInfo(
                UiText.Get("MainWindowMessage_NoCommentsOnSheet"),
                UiText.Get("MainWindow_Text_Notes"));
            return;
        }

        ApplyReviewRefreshPlan(result.RefreshPlan);
    }

    private void ReviewDeleteThreadedCommentBtn_Click(object sender, RoutedEventArgs e)
    {
        var result = ReviewSessionController.DeleteThreadedComment();
        if (!result.Success)
        {
            // R127-review-delete-enablement-1: see ReviewDeleteCommentBtn_Click above.
            _messageService.ShowInfo(
                UiText.Get("MainWindowMessage_NoCommentsOnSheet"),
                UiText.Get("MainWindowMessage_CommentsTitle"));
            return;
        }

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
        var cmd = new ShowHideCommentCommand(_currentSheetId, address);
        if (TryExecuteCommand(cmd, "Show/Hide Note"))
            UpdateViewport();
    }

    private void ExecuteShowAllNotes()
    {
        var cmd = new ShowAllNotesCommand(_currentSheetId);
        if (TryExecuteCommand(cmd, "Show All Notes"))
            UpdateViewport();
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

        string? unprotectPassword = null;
        if (sheet.IsProtected && !TryConfirmSheetUnprotectPassword(sheet, out unprotectPassword))
            return;

        var result = ProtectionDialogPlanner.CreateSheetResult(
            sheet.IsProtected,
            unprotectPassword,
            SheetProtectionPermissionLabels.GetDefaultSelectedSheetPermissions());
        if (!sheet.IsProtected)
        {
            var dialog = new PasswordProtectionDialog(
                UiText.Get("MainWindowMessage_ProtectSheetTitle"),
                UiText.Get("MainWindowMessage_OptionalPasswordLabel")) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            result = ProtectionDialogPlanner.CreateSheetResult(
                sheet.IsProtected,
                dialog.Password,
                dialog.SelectedSheetPermissions);
        }

        var action = SheetProtectionWorkflow.CreateCommand(sheet, result);
        var outcome = _commandBus.Execute(_workbook.Id, action.Command);
        if (!outcome.Success)
        {
            ShowCommandError(outcome, action.Title);
            return;
        }

        _messageService.ShowInfo(action.SuccessMessage, action.Title);
        RefreshSheetTabs();
    }

    private bool TryConfirmSheetUnprotectPassword(Sheet sheet, out string? password) =>
        TryConfirmUnprotectPassword(
            sheet.ProtectionPassword,
            UiText.Get("Protection_UnprotectSheetTitle"),
            out password);

    private void ProtectWorkbookBtn_Click(object sender, RoutedEventArgs e)
    {
        string? pwd = null;
        if (!_workbook.IsStructureProtected)
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

        var action = WorkbookProtectionWorkflow.CreateCommand(_workbook, pwd);
        if (!TryExecuteCommand(action.Command, action.Title))
            return;

        _messageService.ShowInfo(action.SuccessMessage, action.Title);
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

        IWorkbookCommand? command = null;
        string? successMessage = null;
        switch (dialog.Result)
        {
            case { Action: AllowEditRangeAction.Add, Range: { } range }:
                command = new CompositeWorkbookCommand(
                    "Allow Edit Range",
                    [
                        new AllowEditRangeCommand(_currentSheetId, range),
                        new SetAllowEditRangePasswordCommand(_currentSheetId, range, dialog.RangePassword)
                    ]);
                successMessage = UiText.Format("MainWindowMessage_AllowEditRangeAdded", range);
                break;
            case { Action: AllowEditRangeAction.Modify, PreviousRange: { } previousRange, Range: { } range }:
                var modifyCommands = new List<IWorkbookCommand>
                {
                    new RemoveAllowEditRangeCommand(_currentSheetId, previousRange),
                    new AllowEditRangeCommand(_currentSheetId, range)
                };
                // Only touch the stored password when the user actually typed into the password box
                // this time (RangePasswordChanged); a modify with the box left blank keeps whatever
                // password (if any) the range already had, matching Excel and AllowEditRangeDialog's
                // own contract for RangePasswordChanged.
                if (dialog.RangePasswordChanged)
                {
                    modifyCommands.Add(new SetAllowEditRangePasswordCommand(_currentSheetId, range, dialog.RangePassword));
                }
                else if (!range.Equals(previousRange) && sheet.AllowEditRangePasswords.TryGetValue(previousRange, out var carriedPassword))
                {
                    // The range's key changed (e.g. its bounds were edited) but the password was left
                    // untouched -- carry the existing password over to the new key so it is not lost.
                    modifyCommands.Add(new SetAllowEditRangePasswordCommand(_currentSheetId, range, carriedPassword));
                }
                command = new CompositeWorkbookCommand("Modify Allow Edit Range", modifyCommands);
                successMessage = UiText.Format("MainWindowMessage_AllowEditRangeModified", range);
                break;
            case { Action: AllowEditRangeAction.Remove, Range: { } range }:
                command = new CompositeWorkbookCommand(
                    "Remove Allow Edit Range",
                    [
                        new RemoveAllowEditRangeCommand(_currentSheetId, range),
                        new SetAllowEditRangePasswordCommand(_currentSheetId, range, null)
                    ]);
                successMessage = UiText.Format("MainWindowMessage_AllowEditRangeRemoved", range);
                break;
            case { Action: AllowEditRangeAction.Clear }:
                command = new ClearAllowEditRangesCommand(_currentSheetId);
                successMessage = UiText.Get("MainWindowMessage_AllowEditRangesCleared");
                break;
        }

        if (command is null || successMessage is null)
            return;

        if (!TryExecuteCommand(command, "Allow Users to Edit Ranges"))
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
