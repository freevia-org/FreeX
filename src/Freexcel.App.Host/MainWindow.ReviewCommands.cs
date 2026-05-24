using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Freexcel.Core.Commands;
using Freexcel.Core.IO;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public partial class MainWindow
{
    private void SpellCheckBtn_Click(object sender, RoutedEventArgs e)
    {
        var ignoredWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ignoredIssues = new HashSet<(CellAddress Address, string Word)>();

        while (true)
        {
            var issues = SpellCheckService.FindIssues(_workbook, _currentSheetId)
                .Where(issue => !ignoredWords.Contains(issue.Word))
                .Where(issue => !ignoredIssues.Contains((issue.Address, issue.Word)))
                .ToList();
            if (issues.Count == 0)
            {
                MessageBox.Show("Spelling check is complete.", "Spell Check", MessageBoxButton.OK, MessageBoxImage.Information);
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
                var edits = BuildSpellCheckReplaceAllEdits(issues, issue.Word, replacement);
                if (edits.Count > 0 && !TryExecuteSpellCheckEdits(edits))
                    return;

                UpdateViewport();
                RefreshStatusBar();
                continue;
            }

            var corrected = SpellCheckService.ApplyCorrection(issue, replacement);
            if (!TryExecuteSpellCheckEdits([(issue.Address, Cell.FromValue(new TextValue(corrected)))]))
                return;

            UpdateViewport();
            RefreshStatusBar();
        }
    }

    private static IReadOnlyList<(CellAddress Address, Cell NewCell)> BuildSpellCheckReplaceAllEdits(
        IReadOnlyList<SpellingIssue> issues,
        string word,
        string replacement) =>
        issues
            .Where(issue => string.Equals(issue.Word, word, StringComparison.OrdinalIgnoreCase))
            .GroupBy(issue => issue.Address)
            .Select(group =>
            {
                var issue = group.First();
                var corrected = SpellCheckService.ApplyCorrection(issue, replacement);
                return (issue.Address, Cell.FromValue(new TextValue(corrected)));
            })
            .ToList();

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
        dialog.ShowDialog();
    }

    private void SetAltTextBtn_Click(object sender, RoutedEventArgs e)
    {
        var target = GetTargetAltTextObject(_currentSheetId);
        if (target is null)
        {
            MessageBox.Show("No picture, shape, or text box is anchored at the selected cell.",
                "Alt Text", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new TextEntryDialog("Alt Text", "Alt text:", target.AltText ?? "") { Owner = this };
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
        var dialog = new TextEntryDialog("Comment", $"Comment for {addr.ToA1()}:", defaultText) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Comment",
                SheetGrid.SelectedRange.Value,
                currentRange => new SetCommentCommand(_currentSheetId, currentRange.Start, dialog.Result.Text)))
            return;

        UpdateViewport();
        MessageBox.Show($"Comment added to {addr.ToA1()}.", "Comment", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ReviewNewThreadedCommentBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is null) return;
        var addr = SheetGrid.SelectedRange.Value.Start;
        var sheet = _workbook.GetSheet(_currentSheetId);
        var defaultText = sheet is null || !sheet.ThreadedComments.TryGetValue(addr, out var existing)
            ? string.Empty
            : existing.Text;
        var dialog = new TextEntryDialog("Threaded Comment", $"Threaded comment for {addr.ToA1()}:", defaultText) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Threaded Comment",
                SheetGrid.SelectedRange.Value,
                currentRange => new SetThreadedCommentCommand(_currentSheetId, currentRange.Start, dialog.Result.Text)))
            return;

        UpdateViewport();
        MessageBox.Show($"Threaded comment added to {addr.ToA1()}.", "Threaded Comment", MessageBoxButton.OK, MessageBoxImage.Information);
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
            MessageBox.Show("No comments on this sheet.", "Comments", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var text = CommentNavigationPlanner.FormatCommentList(sheet.Comments, sheet.ThreadedComments);
        MessageBox.Show(text, "Comments", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void NavigateComment(bool previous)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null || sheet.Comments.Count == 0 && sheet.ThreadedComments.Count == 0)
        {
            MessageBox.Show("No comments on this sheet.", "Comments", MessageBoxButton.OK, MessageBoxImage.Information);
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
            var dialog = new PasswordProtectionDialog("Protect Sheet", "Password (optional):") { Owner = this };
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

        MessageBox.Show(action.SuccessMessage, action.Title, MessageBoxButton.OK, MessageBoxImage.Information);
        RefreshSheetProtectionUi();
    }

    private void ProtectWorkbookBtn_Click(object sender, RoutedEventArgs e)
    {
        string? pwd = null;
        if (!_workbook.IsStructureProtected)
        {
            var dialog = new PasswordProtectionDialog("Protect Workbook", "Password (optional):") { Owner = this };
            if (dialog.ShowDialog() != true) return;
            pwd = dialog.Password;
        }

        var action = WorkbookProtectionWorkflow.CreateCommand(_workbook, pwd);
        if (!TryExecuteCommand(action.Command, action.Title))
            return;

        MessageBox.Show(action.SuccessMessage, action.Title, MessageBoxButton.OK, MessageBoxImage.Information);
        RefreshWorkbookProtectionUi();
        RefreshSheetTabs();
    }
    private void AllowEditRangesBtn_Click(object sender, RoutedEventArgs e)
    {
        var defaultRange = SheetGrid.SelectedRange?.ToString() ?? "A1:A1";
        var dialog = new AllowEditRangeDialog(_currentSheetId, defaultRange) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        if (!TryExecuteCommand(new AllowEditRangeCommand(_currentSheetId, dialog.Range), "Allow Edit Ranges"))
            return;

        MessageBox.Show($"{dialog.Range} can now be edited while this sheet is protected.",
            "Allow Edit Ranges", MessageBoxButton.OK, MessageBoxImage.Information);
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
            MessageBox.Show(
                $"Failed to open Windows Share:\n{ex.Message}",
                "Share Workbook",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void HelpOnlineBtn_Click(object sender, RoutedEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        { FileName = AppInfo.HelpUrl, UseShellExecute = true });
    }

    private void AboutBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowOwnedMessage(
            AppInfo.AboutText,
            "About Freexcel", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void SendFeedbackBtn_Click(object sender, RoutedEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        { FileName = AppInfo.FeedbackUrl, UseShellExecute = true });
    }
}
