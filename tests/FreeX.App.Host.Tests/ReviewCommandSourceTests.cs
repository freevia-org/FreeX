using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class ReviewCommandSourceTests
{
    [Theory]
    [InlineData("Spelling", "Spelling", "SP", "SpellCheckBtn_Click")]
    [InlineData("Workbook Statistics", "Workbook Statistics", "W", "WorkbookStatisticsBtn_Click")]
    [InlineData("Check Accessibility", "Accessibility", "CA", "AccessibilityCheckerBtn_Click")]
    [InlineData("Alt Text", "Alt Text", "T", "SetAltTextBtn_Click")]
    public void ReviewProofingAndAccessibilityButtons_ExposeExpectedTitlesKeyTipsAndHandlers(
        string title,
        string content,
        string keyTip,
        string handler)
    {
        var button = LocalizedXamlTestSupport.ReadMainWindowXaml()
            .ExtractButtonElementByInvariantCommandName(title, $"Click=\"{handler}\"");

        button.ShouldContainLocalizedAttribute("Content", content);
        button.ShouldContainInvariantCommandName(title);
        button.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        button.Should().Contain($"Click=\"{handler}\"");
    }

    [Theory]
    [InlineData("New Comment", "New Comment", "CM", "ReviewNewThreadedCommentBtn_Click")]
    [InlineData("Delete Comment", "Delete", "XC", "ReviewDeleteThreadedCommentBtn_Click")]
    [InlineData("Previous Comment", "Prev", "PC", "ReviewPrevCommentBtn_Click")]
    [InlineData("Next Comment", "Next", "JC", "ReviewNextCommentBtn_Click")]
    [InlineData("Show Comments", "Show Comments", "SC", "ReviewShowCommentsBtn_Click")]
    [InlineData("New Note", "New", "O", "ReviewNewCommentBtn_Click")]
    [InlineData("Edit Note", "Edit", "E", "ReviewNewCommentBtn_Click")]
    [InlineData("Delete Note", "Delete", "D", "ReviewDeleteCommentBtn_Click")]
    [InlineData("Previous Note", "Prev", "PN", "ReviewPrevNoteBtn_Click")]
    [InlineData("Next Note", "Next", "N", "ReviewNextNoteBtn_Click")]
    [InlineData("Show Notes", "Show Notes", "H", "ReviewShowNotesBtn_Click")]
    public void ReviewCommentAndNoteButtons_ExposeExpectedTitlesKeyTipsAndHandlers(
        string title,
        string content,
        string keyTip,
        string handler)
    {
        var button = LocalizedXamlTestSupport.ReadMainWindowXaml()
            .ExtractButtonElementByInvariantCommandName(title, $"Click=\"{handler}\"");

        button.ShouldContainLocalizedAttribute("Content", content);
        button.ShouldContainInvariantCommandName(title);
        button.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        button.Should().Contain($"Click=\"{handler}\"");
    }

    [Theory]
    [InlineData("Protect Sheet", "PS", "ProtectSheetBtn_Click")]
    [InlineData("Protect Workbook", "PW", "ProtectWorkbookBtn_Click")]
    [InlineData("Allow Users to Edit Ranges", "AR", "AllowEditRangesBtn_Click")]
    [InlineData("Share Workbook", "SH", "ShareWorkbookBtn_Click")]
    public void ReviewProtectButtons_ExposeExpectedTitlesKeyTipsAndHandlers(
        string title,
        string keyTip,
        string handler)
    {
        var button = LocalizedXamlTestSupport.ReadMainWindowXaml()
            .ExtractButtonElementByInvariantCommandName(title, $"Click=\"{handler}\"");

        button.ShouldContainLocalizedAttribute("Content", title);
        button.ShouldContainInvariantCommandName(title);
        button.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        button.Should().Contain($"Click=\"{handler}\"");
    }

    [Fact]
    public void ReviewCommandHandlers_RouteThroughExpectedPlannersDialogsAndServices()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ReviewCommands.cs");
        var normalizedSource = source.Replace("\r\n", "\n", StringComparison.Ordinal);

        source.Should().Contain("SpellCheckWorkflowPlanner.FilterIssues(");
        source.Should().Contain("SpellCheckWorkflowPlanner.BuildReplaceAllCommand(");
        source.Should().Contain("SpellCheckWorkflowPlanner.BuildReplacementCommand(");
        source.Should().Contain("WorkbookStatisticsService.GetStatistics(_workbook)");
        source.Should().Contain("AccessibilityCheckerService.FindIssues(_workbook)");
        source.Should().Contain("AltTextTargetResolver.Resolve(sheet, SheetGrid.SelectedRange?.Start, preferredKind)");
        source.Should().Contain("CommentNavigationPlanner.GetDefaultCommentText(sheet.Comments, addr)");
        source.Should().Contain("new ThreadedCommentDialog(addr.ToA1(), existing)");
        source.Should().Contain("case ThreadedCommentDialogAction.EditReply");
        source.Should().Contain("new UpdateThreadedCommentReplyCommand(");
        normalizedSource.Should().Contain("result.ReplyEditText,\n                            result.IsResolved");
        source.Should().Contain("case ThreadedCommentDialogAction.DeleteReply");
        source.Should().Contain("new DeleteThreadedCommentReplyCommand(");
        normalizedSource.Should().Contain("replyIndex,\n                            result.IsResolved");
        source.Should().Contain("CommentNavigationPlanner.OrderedThreadedCommentAddresses(sheet.ThreadedComments)");
        source.Should().Contain("CommentNavigationPlanner.FormatThreadedCommentList(sheet.ThreadedComments)");
        source.Should().Contain("CommentNavigationPlanner.OrderedNoteAddresses(sheet.Comments)");
        source.Should().Contain("CommentNavigationPlanner.FormatNoteList(sheet.Comments)");
        source.Should().NotContain("CommentNavigationPlanner.OrderedCommentAddresses(sheet.Comments, sheet.ThreadedComments)");
        source.Should().NotContain("CommentNavigationPlanner.FormatCommentList(sheet.Comments, sheet.ThreadedComments)");
        source.Should().Contain("ProtectionDialogPlanner.CreateSheetResult(");
        source.Should().Contain("SheetProtectionWorkflow.CreateCommand(sheet, result)");
        source.Should().Contain("WorkbookProtectionWorkflow.CreateCommand(_workbook, pwd)");
        source.Should().Contain("new AllowEditRangeDialog(");
        source.Should().Contain("TryExecuteCommand(command, \"Allow Users to Edit Ranges\")");
        source.Should().Contain("_messageService.ShowInfo(successMessage, UiText.Get(\"MainWindowMessage_AllowEditRangesTitle\"))");
        source.Should().Contain("ShareWorkbookPlanner.CreatePlan(_currentFilePath)");
        source.Should().Contain("_shareService.ShareFileAsync(this, sharePath, _workbook.Name)");
    }

}
