using System.IO;
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
        var button = ExtractButtonElementByTitle(ReadMainWindowXaml(), title, handler);

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
    [InlineData("Previous Note", "Prev", "PN", "ReviewPrevCommentBtn_Click")]
    [InlineData("Next Note", "Next", "N", "ReviewNextCommentBtn_Click")]
    [InlineData("Show Notes", "Show Notes", "H", "ReviewShowCommentsBtn_Click")]
    public void ReviewCommentAndNoteButtons_ExposeExpectedTitlesKeyTipsAndHandlers(
        string title,
        string content,
        string keyTip,
        string handler)
    {
        var button = ExtractButtonElementByTitle(ReadMainWindowXaml(), title, handler);

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
        var button = ExtractButtonElementByTitle(ReadMainWindowXaml(), title, handler);

        button.ShouldContainLocalizedAttribute("Content", title);
        button.ShouldContainInvariantCommandName(title);
        button.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        button.Should().Contain($"Click=\"{handler}\"");
    }

    [Fact]
    public void ReviewCommandHandlers_RouteThroughExpectedPlannersDialogsAndServices()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.ReviewCommands.cs"));

        source.Should().Contain("SpellCheckWorkflowPlanner.FilterIssues(");
        source.Should().Contain("SpellCheckWorkflowPlanner.BuildReplaceAllEdits(");
        source.Should().Contain("WorkbookStatisticsService.GetStatistics(_workbook)");
        source.Should().Contain("AccessibilityCheckerService.FindIssues(_workbook)");
        source.Should().Contain("AltTextTargetResolver.Resolve(sheet, SheetGrid.SelectedRange?.Start, preferredKind)");
        source.Should().Contain("CommentNavigationPlanner.GetDefaultCommentText(sheet.Comments, addr)");
        source.Should().Contain("new ThreadedCommentDialog(addr.ToA1(), existing)");
        source.Should().Contain("case ThreadedCommentDialogAction.EditReply");
        source.Should().Contain("new UpdateThreadedCommentReplyCommand(");
        source.Should().Contain("case ThreadedCommentDialogAction.DeleteReply");
        source.Should().Contain("new DeleteThreadedCommentReplyCommand(");
        source.Should().Contain("CommentNavigationPlanner.OrderedCommentAddresses(sheet.Comments, sheet.ThreadedComments)");
        source.Should().Contain("CommentNavigationPlanner.FormatCommentList(sheet.Comments, sheet.ThreadedComments)");
        source.Should().Contain("ProtectionDialogPlanner.CreateSheetResult(");
        source.Should().Contain("SheetProtectionWorkflow.CreateCommand(sheet, result)");
        source.Should().Contain("WorkbookProtectionWorkflow.CreateCommand(_workbook, pwd)");
        source.Should().Contain("new AllowEditRangeDialog(");
        source.Should().Contain("TryExecuteCommand(command, \"Allow Users to Edit Ranges\")");
        source.Should().Contain("_messageService.ShowInfo(successMessage, UiText.Get(\"MainWindowMessage_AllowEditRangesTitle\"))");
        source.Should().Contain("ShareWorkbookPlanner.CreatePlan(_currentFilePath)");
        source.Should().Contain("_shareService.ShareFileAsync(this, _currentFilePath, _workbook.Name)");
    }

    private static string ReadMainWindowXaml() =>
        File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));

    private static string ExtractButtonElementByTitle(string xaml, string title, string? clickHandler = null)
    {
        var matches = new List<string>();
        var searchIndex = 0;
        while (true)
        {
            var titleIndex = xaml.IndexOf($"local:RibbonMetadata.CommandName=\"{title}\"", searchIndex, StringComparison.Ordinal);
            if (titleIndex < 0)
                break;

            var start = xaml.LastIndexOf("<Button", titleIndex, StringComparison.Ordinal);
            start.Should().BeGreaterThanOrEqualTo(0, $"the {title} review command should be a Button");

            var end = xaml.IndexOf("/>", titleIndex, StringComparison.Ordinal);
            end.Should().BeGreaterThanOrEqualTo(titleIndex, $"the {title} review button should be self-closing");
            matches.Add(xaml.Substring(start, end - start + 2));
            searchIndex = end + 2;
        }

        matches.Should().NotBeEmpty($"the {title} review button should be present");
        return clickHandler is null
            ? matches[0]
            : matches.LastOrDefault(button => button.Contains($"Click=\"{clickHandler}\"", StringComparison.Ordinal)) ?? matches[0];
    }
}
