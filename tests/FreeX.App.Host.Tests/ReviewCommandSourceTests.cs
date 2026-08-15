using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class ReviewCommandSourceTests
{

    [Fact]
    public void NewThreadedComment_UsesSelectedCellInlineEditorAndSharedSubmitRoute()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ReviewCommands.cs");

        source.Should().Contain("var target = ReviewSessionController.GetSelectedThreadedCommentTarget();");
        source.Should().Contain("SheetGrid.BeginThreadedCommentInlineEdit(target.Address, target.Address.ToA1(), target.ThreadedComment);");
        source.Should().Contain("SheetGrid_ThreadedCommentInlineEditSubmitted");
        source.Should().Contain("ReviewSessionController.ApplyThreadedComment(");
        source.Should().NotContain("new ThreadedCommentDialog(addr.ToA1(), existing)");
    }

    [Fact]
    public void WpfInlineCommentEditor_ExposesSharedAutomationAndButtonContracts()
    {
        var source = DialogSourceTestSupport.ReadAppUiSources("GridView.CommentPreview.cs");

        source.Should().Contain("GridThreadedCommentRootBox");
        source.Should().Contain("GridThreadedCommentReplyBox");
        source.Should().Contain("GridCommentInlineSaveButton");
        source.Should().Contain("GridCommentInlineCancelButton");
        source.Should().Contain("UiText.Get(existing is null");
        source.Should().Contain("? \"GridInlineComment_SaveButton\"");
        source.Should().Contain(": \"GridInlineComment_ApplyButton\"");
        source.Should().Contain("Content = UiText.Get(\"GridInlineComment_CancelButton\")");
        source.Should().Contain("row.Children.Add(saveButton);");
        source.Should().Contain("row.Children.Add(cancelButton);");
        source.Should().Contain("Width = 72");
        source.Should().Contain("MinHeight = 24");
        source.Should().Contain("SubmitThreadedCommentReplyEdit();");
    }

    [Fact]
    public void ReviewCommandHandlers_RouteThroughExpectedPlannersDialogsAndServices()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ReviewCommands.cs");
        var controllerSource = DialogSourceTestSupport.ReadHostSources("MainWindow.ReviewSessionController.cs");

        source.Should().Contain("new SpellCheckSessionController(new SpellCheckSessionAdapter(");
        source.Should().Contain("transition = controller.Apply(dialog.Result);");
        source.Should().NotContain("SpellCheckWorkflowPlanner.ScanWorksheet(");
        source.Should().NotContain("SpellCheckWorkflowPlanner.BuildReplaceAllCommand(");
        source.Should().NotContain("SpellCheckWorkflowPlanner.BuildReplacementCommand(");
        source.Should().Contain("WorkbookStatisticsService.GetStatistics(_workbook)");
        source.Should().Contain("AccessibilityCheckerService.FindIssues(_workbook)");
        source.Should().Contain("DrawingTargetResolver.GetTargetAltTextObject(sheet, SheetGrid.SelectedRange?.Start, preferredKind)");
        source.Should().Contain("DrawingObjectFormatCommandPolicy.BuildAltTextCommand(");
        source.Should().NotContain("DrawingObjectCommandPlanner.BuildAltTextCommand(");
        source.Should().NotContain("AltTextTargetResolver.Resolve(");
        source.Should().NotContain("AltTextObjectKind.");
        source.Should().Contain("ReviewSessionController.GetSelectedNoteTarget()");
        source.Should().Contain("ReviewSessionController.GetSelectedThreadedCommentTarget()");
        source.Should().Contain("SheetGrid.BeginNoteInlineEdit(target.Address, target.Address.ToA1(), target.NoteText);");
        source.Should().Contain("SheetGrid.BeginThreadedCommentInlineEdit(target.Address, target.Address.ToA1(), target.ThreadedComment);");
        source.Should().Contain("SheetGrid_NoteInlineEditSubmitted");
        source.Should().Contain("SheetGrid_ThreadedCommentInlineEditSubmitted");
        source.Should().Contain("e.KeepOpen = true;");
        source.Should().Contain("e.ErrorMessage = mutation.ErrorMessage;");
        source.Should().Contain("ReviewSessionController.ApplyThreadedComment(");
        source.Should().NotContain("new ThreadedCommentDialog(addr.ToA1(), existing)");
        source.Should().Contain("ReviewSessionController.NavigateThreadedComment(previous)");
        source.Should().Contain("CommentListWindow.CreateThreadedCommentItems(sheet.ThreadedComments)");
        source.Should().Contain("ShowOrRefreshCommentListWindow(");
        source.Should().Contain("new CommentListWindow(title, items, NavigateToCell) { Owner = this }");
        source.Should().Contain("window.Show();");
        source.Should().Contain("ReviewSessionController.NavigateNote(previous)");
        source.Should().Contain("ReviewSessionController.ToggleNoteVisibility(address)");
        source.Should().Contain("ReviewSessionController.ToggleAllNotesVisibility()");
        source.Should().NotContain("new ShowHideCommentCommand");
        source.Should().NotContain("new ShowAllNotesCommand");
        source.Should().Contain("ExecuteShowAllNotes();");
        source.Should().NotContain("CommentNavigationPlanner.OrderedCommentAddresses(sheet.Comments, sheet.ThreadedComments)");
        source.Should().NotContain("CommentNavigationPlanner.FormatCommentList(sheet.Comments, sheet.ThreadedComments)");
        source.Should().NotContain("_messageService.ShowInfo(text, UiText.Get(\"MainWindowMessage_CommentsTitle\"))");
        source.Should().NotContain("_messageService.ShowInfo(text, UiText.Get(\"MainWindow_Text_Notes\"))");
        source.Should().Contain("ProtectionSession.ProjectSheet(sheet)");
        source.Should().Contain("ProtectionSession.ExecuteSheet(sheet, options)");
        source.Should().Contain("ProtectSheetOptions.FromCorePermissions(");
        source.Should().Contain("string? unprotectPassword = null;");
        source.Should().Contain("state.IsProtected && !TryConfirmSheetUnprotectPassword(sheet, out unprotectPassword)");
        source.Should().Contain("private bool TryConfirmSheetUnprotectPassword(Sheet sheet, out string? password)");
        source.Should().Contain("ProtectionSession.ProjectWorkbook()");
        source.Should().Contain("TryConfirmWorkbookUnprotectPassword(out pwd)");
        source.Should().Contain("ProtectionPasswordHelper.VerifyStoredPassword(storedPassword, password)");
        source.Should().Contain("UiText.Get(\"MainWindowMessage_ReviewPasswordIncorrect\")");
        UiText.Get("MainWindowMessage_ReviewPasswordIncorrect").Should().Be("The password you supplied is not correct.");
        source.Should().Contain("ProtectionSession.ExecuteWorkbook(pwd)");
        source.Should().NotContain("new ProtectSheetCommand");
        source.Should().NotContain("new UnprotectSheetCommand");
        source.Should().NotContain("new ProtectWorkbookCommand");
        source.Should().NotContain("new UnprotectWorkbookCommand");
        source.Should().Contain("new AllowEditRangeDialog(");
        source.Should().Contain("AllowEditRangePlanner.CreateCommandPlan(");
        source.Should().Contain("TryExecuteCommand(plan.Command, \"Allow Users to Edit Ranges\")");
        source.Should().Contain("_messageService.ShowInfo(successMessage, UiText.Get(\"MainWindowMessage_AllowEditRangesTitle\"))");
        source.Should().Contain("DocumentShareReadinessPlanner.CreatePlan(");
        source.Should().Contain("DocumentShareSurface.WindowsShare");
        source.Should().Contain("DocumentShareReadinessPlanKind.SaveAsBeforeShare");
        source.Should().NotContain("ShareWorkbookPlanner.CreatePlan(_currentFilePath)");
        source.Should().Contain("_shareService.ShareFileAsync(this, sharePath, _workbook.Name)");
        controllerSource.Should().Contain("PresentationReviewSessionController");
        controllerSource.Should().Contain("TryExecuteRepeatableCurrentRangeCommand(");
        controllerSource.Should().Contain("LocalizeCommandErrorMessage(outcome.ErrorMessage)");
        controllerSource.Should().Contain("SetActiveCell));");
    }

}
