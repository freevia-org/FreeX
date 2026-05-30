using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private void SpellCheckBtn_Click(object sender, RoutedEventArgs e)
    {
        var ignoredWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ignoredIssues = new HashSet<(CellAddress Address, string Word)>();

        while (true)
        {
            var issues = SpellCheckWorkflowPlanner.FilterIssues(
                SpellCheckService.FindIssues(_workbook, _currentSheetId),
                ignoredWords,
                ignoredIssues);
            if (issues.Count == 0)
            {
                _messageService.ShowInfo(
                    UiText.Get("MainWindowMessage_SpellCheckComplete"),
                    UiText.Get("MainWindowMessage_SpellCheckTitle"));
                return;
            }

            var issue = issues[0];
            SetActiveCell(issue.Address);
            EnsureCellVisible(issue.Address);
            UpdateViewport();

            var dialog = new SpellCheckDialog(issue.Word, issue.Suggestion) { Owner = this };
            if (dialog.ShowDialog() != true)
                return;

            if (dialog.Result.Action == SpellCheckDialogAction.Ignore)
            {
                ignoredIssues.Add((issue.Address, issue.Word));
                continue;
            }

            if (dialog.Result.Action is SpellCheckDialogAction.IgnoreAll or SpellCheckDialogAction.Add)
            {
                ignoredWords.Add(issue.Word);
                continue;
            }

            var replacement = dialog.Result.Replacement ?? issue.Suggestion;

            if (dialog.Result.Action == SpellCheckDialogAction.ReplaceAll)
            {
                var edits = SpellCheckWorkflowPlanner.BuildReplaceAllEdits(issues, issue.Word, replacement);
                if (edits.Count > 0 && !TryExecuteSpellCheckEdits(edits))
                    return;

                UpdateViewport();
                RefreshStatusBar();
                continue;
            }

            if (!TryExecuteSpellCheckEdits([SpellCheckWorkflowPlanner.BuildReplacementEdit(issue, replacement)]))
                return;

            UpdateViewport();
            RefreshStatusBar();
        }
    }

    private bool TryExecuteSpellCheckEdits(IReadOnlyList<(CellAddress Address, Cell NewCell)> edits) =>
        TryExecuteCommand(new EditCellsCommand(_currentSheetId, edits), "Spell Check");

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
            NavigateToCell(AccessibilityCheckerDialog.GetNavigationTarget(dialog.Result!.Issue));
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
                    return target.Kind switch
                    {
                        AltTextObjectKind.Picture => new SetPictureAltTextCommand(sheetId, groupedTarget?.Id ?? Guid.Empty, dialog.Result.Text),
                        AltTextObjectKind.Shape => new SetDrawingShapeAltTextCommand(sheetId, groupedTarget?.Id ?? Guid.Empty, dialog.Result.Text),
                        _ => new SetTextBoxAltTextCommand(sheetId, groupedTarget?.Id ?? Guid.Empty, dialog.Result.Text)
                    };
                }))
        {
            return;
        }

        SetActiveCell(target.Anchor);
        EnsureCellVisible(target.Anchor);
        UpdateViewport();
        RefreshStatusBar();
    }

    private AltTextObjectTarget? GetTargetAltTextObject(SheetId sheetId, AltTextObjectKind? preferredKind = null)
    {
        var sheet = _workbook.GetSheet(sheetId);
        if (sheet is null)
            return null;

        return AltTextTargetResolver.Resolve(sheet, SheetGrid.SelectedRange?.Start, preferredKind);
    }

    private void ReviewNewCommentBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is null) return;
        var addr = SheetGrid.SelectedRange.Value.Start;
        var sheet = _workbook.GetSheet(_currentSheetId);
        var defaultText = sheet is null
            ? string.Empty
            : CommentNavigationPlanner.GetDefaultCommentText(sheet.Comments, addr);
        var dialog = new TextEntryDialog(
            UiText.Get("MainWindowMessage_CommentTitle"),
            UiText.Format("MainWindowMessage_CommentForCellLabel", addr.ToA1()),
            defaultText) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Comment",
                SheetGrid.SelectedRange.Value,
                currentRange => new SetCommentCommand(_currentSheetId, currentRange.Start, dialog.Result.Text)))
            return;

        UpdateViewport();
        _messageService.ShowInfo(
            UiText.Format("MainWindowMessage_CommentAdded", addr.ToA1()),
            UiText.Get("MainWindowMessage_CommentTitle"));
    }

    private void ReviewNewThreadedCommentBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is null) return;
        var addr = SheetGrid.SelectedRange.Value.Start;
        var sheet = _workbook.GetSheet(_currentSheetId);
        ThreadedComment? existing = null;
        sheet?.ThreadedComments.TryGetValue(addr, out existing);
        var dialog = new ThreadedCommentDialog(addr.ToA1(), existing) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        var result = dialog.Result;
        var range = SheetGrid.SelectedRange.Value;
        var changed = false;

        if (existing is null && result.ReplyText is not null)
        {
            changed = TryExecuteRepeatableCurrentRangeCommand(
                "Threaded Comment",
                range,
                r => new SetThreadedCommentCommand(_currentSheetId, r.Start, result.ReplyText));
        }
        else if (existing is not null)
        {
            switch (result.Action)
            {
                case ThreadedCommentDialogAction.EditReply when result.ReplyIndex is { } replyIndex && result.ReplyEditText is not null:
                    changed = TryExecuteRepeatableCurrentRangeCommand(
                        "Edit Comment Reply",
                        range,
                        r => new UpdateThreadedCommentReplyCommand(
                            _currentSheetId,
                            r.Start,
                            replyIndex,
                            result.ReplyEditText));
                    break;
                case ThreadedCommentDialogAction.DeleteReply when result.ReplyIndex is { } replyIndex:
                    changed = TryExecuteRepeatableCurrentRangeCommand(
                        "Delete Comment Reply",
                        range,
                        r => new DeleteThreadedCommentReplyCommand(
                            _currentSheetId,
                            r.Start,
                            replyIndex));
                    break;
                default:
                    var hasThreadChange =
                        result.RootText is not null ||
                        result.ReplyText is not null ||
                        result.IsResolved != existing.IsResolved;
                    if (hasThreadChange)
                    {
                        changed = TryExecuteRepeatableCurrentRangeCommand(
                            "Edit Comment",
                            range,
                            r => new ApplyThreadedCommentChangesCommand(
                                _currentSheetId,
                                r.Start,
                                result.RootText,
                                result.ReplyText,
                                result.IsResolved));
                    }

                    break;
            }
        }

        if (changed) UpdateViewport();
    }

    private void ReviewDeleteCommentBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is null) return;
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Comment",
                SheetGrid.SelectedRange.Value,
                currentRange => new DeleteCommentCommand(_currentSheetId, currentRange.Start)))
            return;

        UpdateViewport();
    }

    private void ReviewDeleteThreadedCommentBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is null) return;
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Threaded Comment",
                SheetGrid.SelectedRange.Value,
                currentRange => new DeleteThreadedCommentCommand(_currentSheetId, currentRange.Start)))
            return;

        UpdateViewport();
    }

    private void ReviewPrevCommentBtn_Click(object sender, RoutedEventArgs e)
    {
        NavigateComment(previous: true);
    }

    private void ReviewNextCommentBtn_Click(object sender, RoutedEventArgs e)
    {
        NavigateComment(previous: false);
    }

    private void ReviewShowCommentsBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null || sheet.Comments.Count == 0 && sheet.ThreadedComments.Count == 0)
        {
            _messageService.ShowInfo(
                UiText.Get("MainWindowMessage_NoCommentsOnSheet"),
                UiText.Get("MainWindowMessage_CommentsTitle"));
            return;
        }

        var text = CommentNavigationPlanner.FormatCommentList(sheet.Comments, sheet.ThreadedComments);
        _messageService.ShowInfo(text, UiText.Get("MainWindowMessage_CommentsTitle"));
    }

    private void NavigateComment(bool previous)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null || sheet.Comments.Count == 0 && sheet.ThreadedComments.Count == 0)
        {
            _messageService.ShowInfo(
                UiText.Get("MainWindowMessage_NoCommentsOnSheet"),
                UiText.Get("MainWindowMessage_CommentsTitle"));
            return;
        }

        var comments = CommentNavigationPlanner.OrderedCommentAddresses(sheet.Comments, sheet.ThreadedComments);
        var current = SheetGrid.SelectedRange?.Start ?? comments[0];
        var target = CommentNavigationPlanner.FindNext(comments, current, previous);

        SetActiveCell(target);
        EnsureCellVisible(target);
        UpdateViewport();
    }

    private void ProtectSheetBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;

        var result = ProtectionDialogPlanner.CreateSheetResult(sheet, password: null);
        if (!sheet.IsProtected)
        {
            var dialog = new PasswordProtectionDialog(
                UiText.Get("MainWindowMessage_ProtectSheetTitle"),
                UiText.Get("MainWindowMessage_OptionalPasswordLabel")) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            result = ProtectionDialogPlanner.CreateSheetResult(
                sheet,
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
        RefreshSheetProtectionUi();
    }

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

        var action = WorkbookProtectionWorkflow.CreateCommand(_workbook, pwd);
        if (!TryExecuteCommand(action.Command, action.Title))
            return;

        _messageService.ShowInfo(action.SuccessMessage, action.Title);
        RefreshWorkbookProtectionUi();
        RefreshSheetTabs();
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
            request => ApplyAllowEditRangeSelection(dialog, request)) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        IWorkbookCommand? command = null;
        string? successMessage = null;
        switch (dialog.Result)
        {
            case { Action: AllowEditRangeDialogAction.Add, Range: { } range }:
                command = new AllowEditRangeCommand(_currentSheetId, range);
                successMessage = UiText.Format("MainWindowMessage_AllowEditRangeAdded", range);
                break;
            case { Action: AllowEditRangeDialogAction.Remove, Range: { } range }:
                command = new RemoveAllowEditRangeCommand(_currentSheetId, range);
                successMessage = UiText.Format("MainWindowMessage_AllowEditRangeRemoved", range);
                break;
            case { Action: AllowEditRangeDialogAction.Clear }:
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
        if (dialog is null || SheetGrid.SelectedRange is not { } selectedRange)
            return;

        if (request.CollapseDialog)
            dialog.Hide();

        try
        {
            dialog.ApplyRangeSelection(FormatRangeReference(selectedRange.Start, selectedRange.End));
        }
        finally
        {
            if (request.CollapseDialog)
            {
                dialog.Show();
                dialog.Activate();
            }
        }
    }

    private async void ShareWorkbookBtn_Click(object sender, RoutedEventArgs e) => await ShareWorkbookAsync();

    private async Task ShareWorkbookAsync()
    {
        var plan = ShareWorkbookPlanner.CreatePlan(_currentFilePath);
        if (plan.Kind == ShareWorkbookPlanKind.SaveAsBeforeShare)
        {
            if (!await SaveWorkbookWithDialogAsync())
                return;
        }
        else if (FileSavePlanner.TryResolveExistingPath(plan.Path, _fileAdapters, out var target))
        {
            if (!await SaveWorkbookToTargetAsync(target!))
                return;
        }

        if (string.IsNullOrWhiteSpace(_currentFilePath))
            return;

        try
        {
            await _shareService.ShareFileAsync(this, _currentFilePath, _workbook.Name);
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

    private void CheckForUpdatesBtn_Click(object sender, RoutedEventArgs e)
    {
        RecordDiagnosticEvent("update_check_opened", new Dictionary<string, string?>
        {
            ["source"] = "help"
        });

        OpenExternalHelpLink(AppUpdateSource.CreateDefault().ReleasePageUrl, UiText.Get("MainWindowMessage_CheckForUpdatesTitle"));
    }

    private void AboutBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowOwnedMessage(
            AppInfo.AboutText,
            UiText.Get("MainWindowMessage_AboutFreeXTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void LegalNoticesBtn_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new LegalNoticesDialog();
        ShowOwnedDialog(dialog);
    }

    private void SendFeedbackBtn_Click(object sender, RoutedEventArgs e)
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
