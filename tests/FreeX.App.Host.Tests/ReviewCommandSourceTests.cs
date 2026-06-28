using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class ReviewCommandSourceTests
{

    [Fact]
    public void ReviewCommandHandlers_RouteThroughExpectedPlannersDialogsAndServices()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ReviewCommands.cs");
        var normalizedSource = source.Replace("\r\n", "\n", StringComparison.Ordinal);

        source.Should().Contain("SpellCheckWorkflowPlanner.ScanWorksheet(");
        source.Should().Contain("SpellCheckWorkflowPlanner.BuildReplaceAllCommand(");
        source.Should().Contain("SpellCheckWorkflowPlanner.BuildReplacementCommand(");
        source.Should().Contain("WorkbookStatisticsService.GetStatistics(_workbook)");
        source.Should().Contain("AccessibilityCheckerService.FindIssues(_workbook)");
        source.Should().Contain("DrawingTargetResolver.GetTargetAltTextObject(sheet, SheetGrid.SelectedRange?.Start, preferredKind)");
        source.Should().Contain("DrawingObjectCommandPlanner.BuildAltTextCommand(");
        source.Should().NotContain("AltTextTargetResolver.Resolve(");
        source.Should().NotContain("AltTextObjectKind.");
        source.Should().Contain("CommentNavigationPlanner.GetDefaultCommentText(sheet.Comments, addr)");
        source.Should().Contain("SheetGrid.BeginNoteInlineEdit(addr, addr.ToA1(), defaultText);");
        source.Should().Contain("SheetGrid.BeginThreadedCommentInlineEdit(addr, addr.ToA1(), existing);");
        source.Should().Contain("SheetGrid_NoteInlineEditSubmitted");
        source.Should().Contain("SheetGrid_ThreadedCommentInlineEditSubmitted");
        source.Should().Contain("e.KeepOpen = true;");
        source.Should().Contain("e.ErrorMessage = LocalizeCommandErrorMessage(outcome.ErrorMessage);");
        source.Should().Contain("case GridThreadedCommentEditAction.EditReply");
        source.Should().Contain("new UpdateThreadedCommentReplyCommand(");
        normalizedSource.Should().Contain("result.ReplyEditText,\n                            result.IsResolved");
        source.Should().Contain("case GridThreadedCommentEditAction.DeleteReply");
        source.Should().Contain("new DeleteThreadedCommentReplyCommand(");
        normalizedSource.Should().Contain("replyIndex,\n                            result.IsResolved");
        source.Should().NotContain("new ThreadedCommentDialog(addr.ToA1(), existing)");
        source.Should().Contain("CommentNavigationPlanner.OrderedThreadedCommentAddresses(sheet.ThreadedComments)");
        source.Should().Contain("CommentListWindow.CreateThreadedCommentItems(sheet.ThreadedComments)");
        source.Should().Contain("ShowOrRefreshCommentListWindow(");
        source.Should().Contain("new CommentListWindow(title, items, NavigateToCell) { Owner = this }");
        source.Should().Contain("window.Show();");
        source.Should().Contain("CommentNavigationPlanner.OrderedNoteAddresses(sheet.Comments)");
        source.Should().Contain("CommentListWindow.CreateNoteItems(sheet.Comments)");
        source.Should().NotContain("CommentNavigationPlanner.OrderedCommentAddresses(sheet.Comments, sheet.ThreadedComments)");
        source.Should().NotContain("CommentNavigationPlanner.FormatCommentList(sheet.Comments, sheet.ThreadedComments)");
        source.Should().NotContain("_messageService.ShowInfo(text, UiText.Get(\"MainWindowMessage_CommentsTitle\"))");
        source.Should().NotContain("_messageService.ShowInfo(text, UiText.Get(\"MainWindow_Text_Notes\"))");
        source.Should().Contain("ProtectionDialogPlanner.CreateSheetResult(");
        source.Should().Contain("SheetProtectionPermissionLabels.GetDefaultSelectedSheetPermissions()");
        source.Should().Contain("string? unprotectPassword = null;");
        source.Should().Contain("sheet.IsProtected && !TryConfirmSheetUnprotectPassword(sheet, out unprotectPassword)");
        source.Should().Contain("private bool TryConfirmSheetUnprotectPassword(Sheet sheet, out string? password)");
        source.Should().Contain("_workbook.IsStructureProtected");
        source.Should().Contain("TryConfirmWorkbookUnprotectPassword(out pwd)");
        source.Should().Contain("ProtectionPasswordHelper.VerifyStoredPassword(storedPassword, password)");
        source.Should().Contain("UiText.Get(\"MainWindowMessage_ReviewPasswordIncorrect\")");
        UiText.Get("MainWindowMessage_ReviewPasswordIncorrect").Should().Be("The password you supplied is not correct.");
        source.Should().Contain("SheetProtectionWorkflow.CreateCommand(sheet, result)");
        source.Should().Contain("WorkbookProtectionWorkflow.CreateCommand(_workbook, pwd)");
        source.Should().Contain("new AllowEditRangeDialog(");
        source.Should().Contain("TryExecuteCommand(command, \"Allow Users to Edit Ranges\")");
        source.Should().Contain("_messageService.ShowInfo(successMessage, UiText.Get(\"MainWindowMessage_AllowEditRangesTitle\"))");
        source.Should().Contain("WorkbookShareReadinessPlanner.CreatePlan(");
        source.Should().Contain("WorkbookShareSurface.WindowsShare");
        source.Should().Contain("WorkbookShareReadinessPlanKind.SaveAsBeforeShare");
        source.Should().NotContain("ShareWorkbookPlanner.CreatePlan(_currentFilePath)");
        source.Should().Contain("_shareService.ShareFileAsync(this, sharePath, _workbook.Name)");
    }

}
