using FluentAssertions;
using Free.Shared.Shell;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class ErrorCheckingDialogPlannerTests
{
    [Fact]
    public void Constants_MatchWindowsDialogChromeAndColumns()
    {
        ErrorCheckingDialogVisualMetrics.WpfWidth.Should().Be(720);
        ErrorCheckingDialogVisualMetrics.WpfHeight.Should().Be(420);
        ErrorCheckingDialogVisualMetrics.AvaloniaClientWidth.Should().Be(704);
        ErrorCheckingDialogVisualMetrics.AvaloniaClientHeight.Should().Be(383);
        ErrorCheckingDialogVisualMetrics.MinWidth.Should().Be(540);
        ErrorCheckingDialogVisualMetrics.MinHeight.Should().Be(240);
        ErrorCheckingDialogVisualMetrics.RootMargin.Should().Be(10);
        ErrorCheckingDialogVisualMetrics.ActionPanelWidth.Should().Be(180);
        ErrorCheckingDialogVisualMetrics.ButtonHeight.Should().Be(26);
        ErrorCheckingDialogVisualMetrics.GoToButtonWidth.Should().Be(80);
        ErrorCheckingDialogVisualMetrics.PreviousButtonWidth.Should().Be(84);
        ErrorCheckingDialogVisualMetrics.NextButtonWidth.Should().Be(80);
        ErrorCheckingDialogVisualMetrics.IgnoreButtonWidth.Should().Be(104);
        ErrorCheckingDialogVisualMetrics.TraceButtonWidth.Should().Be(96);
        ErrorCheckingDialogVisualMetrics.OptionsButtonWidth.Should().Be(92);
        ErrorCheckingDialogVisualMetrics.CloseButtonWidth.Should().Be(80);
        ErrorCheckingDialogVisualMetrics.SheetColumnWidth.Should().Be(110);
        ErrorCheckingDialogVisualMetrics.CellColumnWidth.Should().Be(70);
        ErrorCheckingDialogVisualMetrics.IssueColumnWidth.Should().Be(80);
        ErrorCheckingDialogVisualMetrics.FormulaColumnWidth.Should().Be(150);
        ErrorCheckingDialogVisualMetrics.DescriptionColumnWidth.Should().Be(260);
    }

    [Fact]
    public void AutomationIdentifiers_AreStableSharedRendererContracts()
    {
        ErrorCheckingDialogVisualMetrics.DialogAutomationId.Should().Be("ErrorCheckingDialog");
        ErrorCheckingDialogVisualMetrics.IssuesAutomationId.Should().Be("ErrorCheckingIssuesList");
    }

    [Fact]
    public void CreateCommandState_EnablesSelectionAndBoundaryCommandsLikeWindows()
    {
        var first = ErrorCheckingDialogPlanner.CreateCommandState(0, 2, CreateIssue("=1/0"));
        first.HasSelection.Should().BeTrue();
        first.CanShowCalculationSteps.Should().BeTrue();
        first.CanPrevious.Should().BeFalse();
        first.CanNext.Should().BeTrue();

        var second = ErrorCheckingDialogPlanner.CreateCommandState(1, 2, CreateIssue(null));
        second.HasSelection.Should().BeTrue();
        second.CanShowCalculationSteps.Should().BeFalse();
        second.CanPrevious.Should().BeTrue();
        second.CanNext.Should().BeFalse();

        var none = ErrorCheckingDialogPlanner.CreateCommandState(-1, 2, null);
        none.HasSelection.Should().BeFalse();
        none.CanShowCalculationSteps.Should().BeFalse();
        none.CanPrevious.Should().BeFalse();
        none.CanNext.Should().BeFalse();
    }

    [Fact]
    public void CreateParityIssues_ProvidesTheSharedWpfAndAvaloniaCaptureFixture()
    {
        var sheetId = SheetId.New();

        var issues = ErrorCheckingParityFixture.CreateIssues(sheetId);

        issues.Should().HaveCount(2);
        issues.Should().AllSatisfy(issue => issue.SheetName.Should().Be("Sheet1"));
        issues[0].Address.Should().Be(new CellAddress(sheetId, 6, 4));
        issues[0].ErrorCode.Should().Be(ErrorValue.DivByZero.Code);
        issues[0].FormulaText.Should().Be("=D2/0");
        issues[1].Address.Should().Be(new CellAddress(sheetId, 7, 4));
        issues[1].FormulaText.Should().BeNull();
        issues.Select(issue => issue.Description).Should().Equal(
            "Formula divides by zero.",
            "The formula in this cell is stored as text.");
    }

    private static FormulaErrorIssue CreateIssue(string? formulaText)
    {
        var sheetId = SheetId.New();
        return new FormulaErrorIssue(
            sheetId,
            "Sheet1",
            new CellAddress(sheetId, 1, 1),
            "A1",
            ErrorValue.DivByZero.Code,
            formulaText,
            "Formula divides by zero.");
    }
}
