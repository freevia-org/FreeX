using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class ReviewProofingCommentsParityTests
{
    [Theory]
    [InlineData("Spelling", "SpellCheckBtn_Click", "ReviewSpellingButton", "MainWindow_TooltipDescription_FindKnownMisspellingsInTextCellsOnTheActiveSheetWithReplaceReplaceAllAnd_D58B6767")]
    [InlineData("Workbook Statistics", "WorkbookStatisticsBtn_Click", "ReviewWorkbookStatisticsButton", "MainWindow_AutomationHelpText_ShowWorkbookCountsForSheetsCellsFormulasCommentsAndObjects")]
    [InlineData("Check Accessibility", "AccessibilityCheckerBtn_Click", "ReviewAccessibilityCheckerButton", "MainWindow_AutomationHelpText_FindMergedCellsBlankTableHeadersObjectsMissingAlternateTextAndChartsWith_AD813E90")]
    [InlineData("New Comment", "ReviewNewThreadedCommentBtn_Click", "ReviewNewCommentButton", "MainWindow_TooltipDescription_AddOrEditAThreadedCommentOnTheSelectedCellCtrlShiftF2")]
    [InlineData("Delete Comment", "ReviewDeleteThreadedCommentBtn_Click", "ReviewDeleteCommentButton", "MainWindow_TooltipDescription_DeleteTheThreadedCommentOnTheSelectedCell")]
    [InlineData("Previous Comment", "ReviewPrevCommentBtn_Click", "ReviewPreviousCommentButton", "MainWindow_TooltipDescription_NavigateToThePreviousCommentInTheSheet")]
    [InlineData("Next Comment", "ReviewNextCommentBtn_Click", "ReviewNextCommentButton", "MainWindow_TooltipDescription_NavigateToTheNextCommentInTheSheet")]
    [InlineData("Show Comments", "ReviewShowCommentsBtn_Click", "ReviewShowCommentsButton", "MainWindow_TooltipDescription_OpenAListOfThreadedCommentsOnTheActiveSheet")]
    [InlineData("New Note", "ReviewNewCommentBtn_Click", "ReviewNewNoteButton", "MainWindow_TooltipDescription_AddASimpleCellNoteToTheSelectedCell")]
    [InlineData("Edit Note", "ReviewNewCommentBtn_Click", "ReviewEditNoteButton", "MainWindow_TooltipDescription_EditTheSimpleCellNoteOnTheSelectedCell")]
    [InlineData("Delete Note", "ReviewDeleteCommentBtn_Click", "ReviewDeleteNoteButton", "MainWindow_TooltipDescription_RemoveTheSimpleCellNoteFromTheSelectedCell")]
    [InlineData("Previous Note", "ReviewPrevNoteBtn_Click", "ReviewPreviousNoteButton", "MainWindow_TooltipDescription_NavigateToThePreviousSimpleCellNoteInTheSheet")]
    [InlineData("Next Note", "ReviewNextNoteBtn_Click", "ReviewNextNoteButton", "MainWindow_TooltipDescription_NavigateToTheNextSimpleCellNoteInTheSheet")]
    [InlineData("Show Notes", "ReviewShowNotesBtn_Click", "ReviewShowNotesButton", "MainWindow_TooltipDescription_OpenAListOfSimpleCellNotesOnTheActiveSheet")]
    public void ReviewProofingCommentAndNoteButtons_ExposeAutomationIdAndHelpText(
        string commandName,
        string handler,
        string automationId,
        string helpTextKey)
    {
        var button = LocalizedXamlTestSupport.ReadMainWindowXaml()
            .ExtractButtonElementByInvariantCommandName(commandName, $"Click=\"{handler}\"");

        button.Should().Contain($"AutomationProperties.AutomationId=\"{automationId}\"");
        button.Should().Contain($"AutomationProperties.HelpText=\"{{local:Loc Key={helpTextKey}}}\"");
    }

    [Fact]
    public void AccessibilityCheckerDialog_CleanOkClosesWithoutNavigationResult()
    {
        var source = DialogSourceTestSupport.ReadHostSources("AccessibilityCheckerDialog.cs");
        var cleanContent = source[
            source.IndexOf("private StackPanel CreateCleanContent", StringComparison.Ordinal)..
            source.IndexOf("private StackPanel CreateIssueContent", StringComparison.Ordinal)];

        cleanContent.Should().Contain("DialogResult = false");
        cleanContent.Should().NotContain("DialogResult = true");
    }
}
