using FluentAssertions;
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
        ErrorCheckingDialogPlanner.Width.Should().Be(720);
        ErrorCheckingDialogPlanner.Height.Should().Be(420);
        ErrorCheckingDialogPlanner.ActionPanelWidth.Should().Be(180);
        ErrorCheckingDialogPlanner.ButtonHeight.Should().Be(26);
        ErrorCheckingDialogPlanner.SheetColumnWidth.Should().Be(110);
        ErrorCheckingDialogPlanner.CellColumnWidth.Should().Be(70);
        ErrorCheckingDialogPlanner.IssueColumnWidth.Should().Be(80);
        ErrorCheckingDialogPlanner.FormulaColumnWidth.Should().Be(150);
        ErrorCheckingDialogPlanner.DescriptionColumnWidth.Should().Be(260);
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
