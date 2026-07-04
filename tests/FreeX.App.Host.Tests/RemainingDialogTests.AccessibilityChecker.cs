using FreeX.App.Presentation.Accessibility;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

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
        var source = DialogSourceTestSupport.ReadHostSources("AccessibilityCheckerDialog.cs");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_resultsTree.Focus();");
        source.Should().Contain("Keyboard.Focus(_resultsTree);");
        source.Should().Contain("_messageBox.Focus();");
    }

    [Fact]
    public void AccessibilityCheckerDialog_UsesIssueListAndGoToAction()
    {
        var source = DialogSourceTestSupport.ReadHostSources("AccessibilityCheckerDialog.cs");
        var reviewSource = DialogSourceTestSupport.ReadHostSources("MainWindow.ReviewCommands.cs");

        source.Should().Contain("public sealed record AccessibilityCheckerDialogResult");
        source.Should().Contain("private readonly TreeView _resultsTree");
        source.Should().Contain("private readonly Button _goToButton");
        source.Should().Contain("AccessibilityCheckerDialogPlanner.Create(issues, UiText.Get)");
        source.Should().Contain("ApplyAction(_goToButton, _plan.GoToAction);");
        source.Should().Contain("_resultsTree.MouseDoubleClick += ResultsTree_MouseDoubleClick;");
        source.Should().Contain("private void GoToSelectedIssue()");
        reviewSource.Should().Contain("if (dialog.ShowDialog() == true)");
        reviewSource.Should().Contain("NavigateToCell(AccessibilityCheckerDialogPlanner.GetNavigationTarget(dialog.Result!.Issue));");
    }

    [Fact]
    public void AccessibilityCheckerDialog_DoubleClickGoToHandlesMouseEvent()
    {
        var source = DialogSourceTestSupport.ReadHostSources("AccessibilityCheckerDialog.cs");
        var doubleClick = source[
            source.IndexOf("private void ResultsTree_MouseDoubleClick", StringComparison.Ordinal)..
            source.IndexOf("private void FocusInitialKeyboardTarget", StringComparison.Ordinal)];

        doubleClick.Should().Contain("SelectedItem() is null");
        doubleClick.Should().Contain("GoToSelectedIssue();");
        doubleClick.Should().Contain("e.Handled = true;");
        doubleClick.IndexOf("GoToSelectedIssue();", StringComparison.Ordinal)
            .Should()
            .BeLessThan(doubleClick.IndexOf("e.Handled = true;", StringComparison.Ordinal));
    }

    [Fact]
    public void AccessibilityCheckerDialog_CleanStateUsesSingleExcelLikeOkButton()
    {
        var source = DialogSourceTestSupport.ReadHostSources("AccessibilityCheckerDialog.cs");

        source.Should().Contain("DialogButtonRowFactory.Create(_goToButton, _closeButton, new Thickness(0, 12, 0, 0));");
        source.Should().Contain("_goToButton.Visibility = Visibility.Collapsed;");
    }

    [Fact]
    public void AccessibilityCheckerDialog_ResultControlsExposeAutomationNames()
    {
        var source = DialogSourceTestSupport.ReadHostSources("AccessibilityCheckerDialog.cs");

        source.Should().Contain("ApplyAutomation(_messageBox, _plan.ResultAutomation);");
        source.Should().Contain("ApplyAutomation(_resultsTree, _plan.IssueListAutomation);");
        source.Should().Contain("ApplyAction(_goToButton, _plan.GoToAction);");
        source.Should().Contain("ApplyAction(_closeButton, _plan.CloseAction);");
        source.Should().Contain("AutomationProperties.SetAutomationId(target, automation.AutomationId);");
        UiText.Get("AccessibilityChecker_ResultAutomationName").Should().Be("Accessibility checker result");
    }

    [Fact]
    public void AccessibilityCheckerParityCapture_RendersDirectEvidenceAtDialogSize()
    {
        StaTestRunner.Run(() =>
        {
            var sheetId = SheetId.New();
            var issues = AccessibilityCheckerParityFixture.CreateDialogIssues(sheetId);

            var bitmap = ParityCapture.RenderAccessibilityCheckerDialogForTest(issues);

            bitmap.PixelWidth.Should().Be(360);
            bitmap.PixelHeight.Should().Be(520);
            ParityCapture.HasVisiblePixelsForTest(bitmap).Should().BeTrue();
        });
    }

    [Fact]
    public void ParityCaptureTargetSurfaceId_ParsesSeparateAndEqualsForms()
    {
        ParityCapture.TryGetTargetSurfaceId(["--parity-capture", "out", "--parity-capture-target", "dialog.AccessibilityChecker"])
            .Should()
            .Be("dialog.AccessibilityChecker");

        ParityCapture.TryGetTargetSurfaceId(["--parity-capture-target=dialog.AccessibilityChecker"])
            .Should()
            .Be("dialog.AccessibilityChecker");
    }

    [Fact]
    public void AccessibilityCheckerDialogPlanner_GetNavigationTarget_UsesFirstCellInIssueLocation()
    {
        var sheetId = SheetId.New();

        AccessibilityCheckerDialogPlanner.GetNavigationTarget(new AccessibilityIssue(
                AccessibilityIssueKind.ChartMissingTitle,
                sheetId,
                "Sheet1",
                "C3:E8",
                "Chart is missing a title."))
            .Should()
            .Be(new CellAddress(sheetId, 3, 3));

        AccessibilityCheckerDialogPlanner.GetNavigationTarget(new AccessibilityIssue(
                AccessibilityIssueKind.DefaultWorksheetName,
                sheetId,
                "Sheet1",
                "Sheet1",
                "Worksheet tab names should describe their contents."))
            .Should()
            .Be(new CellAddress(sheetId, 1, 1));
    }
}
