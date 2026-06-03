using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;
using System.IO;

namespace FreeX.App.Host.Tests;

public sealed partial class RemainingDialogTests
{
    [Fact]
    public void AccessibilityCheckerDialog_CreateMessage_ReportsCleanAndIssueStates()
    {
        AccessibilityCheckerDialog.CreateMessage([])
            .Should()
            .Be(UiText.Get("AccessibilityChecker_NoIssuesMessage"));

        AccessibilityCheckerDialog.CreateMessage([
            new(
                AccessibilityIssueKind.ChartMissingTitle,
                SheetId.New(),
                "Sheet1",
                "A1:D8",
                "Chart is missing a title.")
        ]).Should().Contain("Sheet1!A1:D8: Chart is missing a title.");
    }

    [Fact]
    public void AccessibilityCheckerDialogOpenedFromKeyboard_FocusesIssueText()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "AccessibilityCheckerDialog.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_issueList.Focus();");
        source.Should().Contain("Keyboard.Focus(_issueList);");
    }

    [Fact]
    public void AccessibilityCheckerDialog_UsesIssueListAndGoToAction()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "AccessibilityCheckerDialog.cs"));
        var reviewSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.ReviewCommands.cs"));

        source.Should().Contain("public sealed record AccessibilityCheckerDialogResult");
        source.Should().Contain("private readonly ListBox _issueList");
        source.Should().Contain("private readonly Button _goToButton");
        source.Should().Contain("Content = UiText.Get(\"AccessibilityChecker_GoToButton\")");
        source.Should().Contain("_issueList.MouseDoubleClick += IssueList_MouseDoubleClick;");
        source.Should().Contain("private void GoToSelectedIssue()");
        reviewSource.Should().Contain("if (dialog.ShowDialog() == true)");
        reviewSource.Should().Contain("NavigateToCell(AccessibilityCheckerDialog.GetNavigationTarget(dialog.Result!.Issue));");
    }

    [Fact]
    public void AccessibilityCheckerDialog_DoubleClickGoToHandlesMouseEvent()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "AccessibilityCheckerDialog.cs"));
        var doubleClick = source[
            source.IndexOf("private void IssueList_MouseDoubleClick", StringComparison.Ordinal)..
            source.IndexOf("private void UpdateGoToButtonState", StringComparison.Ordinal)];

        doubleClick.Should().Contain("_issueList.SelectedItem is null");
        doubleClick.Should().Contain("GoToSelectedIssue();");
        doubleClick.Should().Contain("e.Handled = true;");
        doubleClick.IndexOf("GoToSelectedIssue();", StringComparison.Ordinal)
            .Should()
            .BeLessThan(doubleClick.IndexOf("e.Handled = true;", StringComparison.Ordinal));
    }

    [Fact]
    public void AccessibilityCheckerDialog_CleanStateUsesSingleExcelLikeOkButton()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "AccessibilityCheckerDialog.cs"));

        source.Should().Contain("DialogButtonRowFactory.CreateOkOnly");
        source.Should().NotContain("DialogButtonRowFactory.Create(() => Window.GetWindow(stack)!.DialogResult = true");
    }

    [Fact]
    public void AccessibilityCheckerDialog_ResultControlsExposeAutomationNames()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "AccessibilityCheckerDialog.cs"));

        source.Should().Contain("AutomationProperties.SetName(_messageBox, UiText.Get(\"AccessibilityChecker_ResultAutomationName\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_messageBox, \"AccessibilityCheckerResultText\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_messageBox, UiText.Get(\"AccessibilityChecker_ResultHelpText\"));");
        source.Should().Contain("AutomationProperties.SetName(_issueList, UiText.Get(\"AccessibilityChecker_IssueListAutomationName\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_issueList, \"AccessibilityCheckerIssueList\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_issueList, UiText.Get(\"AccessibilityChecker_IssueListHelpText\"));");
        source.Should().Contain("AutomationProperties.SetName(_goToButton, UiText.Get(\"AccessibilityChecker_GoToAutomationName\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_goToButton, \"AccessibilityCheckerGoToButton\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_goToButton, UiText.Get(\"AccessibilityChecker_GoToHelpText\"));");
        UiText.Get("AccessibilityChecker_ResultAutomationName").Should().Be("Accessibility checker result");
    }

    [Fact]
    public void AccessibilityCheckerDialog_GetNavigationTarget_UsesFirstCellInIssueLocation()
    {
        var sheetId = SheetId.New();

        AccessibilityCheckerDialog.GetNavigationTarget(new AccessibilityIssue(
                AccessibilityIssueKind.ChartMissingTitle,
                sheetId,
                "Sheet1",
                "C3:E8",
                "Chart is missing a title."))
            .Should()
            .Be(new CellAddress(sheetId, 3, 3));

        AccessibilityCheckerDialog.GetNavigationTarget(new AccessibilityIssue(
                AccessibilityIssueKind.DefaultWorksheetName,
                sheetId,
                "Sheet1",
                "Sheet1",
                "Worksheet tab names should describe their contents."))
            .Should()
            .Be(new CellAddress(sheetId, 1, 1));
    }
}
