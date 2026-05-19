using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class FormulaAuditSelectionPlannerTests
{
    private static readonly SheetId Sheet1 = SheetId.New();
    private static readonly SheetId Sheet2 = SheetId.New();

    [Fact]
    public void Plan_TargetsFirstMatchedSheetAndKeepsOnlyMatchesOnThatSheet()
    {
        var sheet1Match = new CellAddress(Sheet1, 4, 1);
        var firstSheet2Match = new CellAddress(Sheet2, 2, 3);
        var secondSheet2Match = new CellAddress(Sheet2, 2, 4);

        var plan = FormulaAuditSelectionPlanner.Plan(
            currentSheetId: Sheet1,
            matches: [firstSheet2Match, secondSheet2Match, sheet1Match]);

        plan.Should().NotBeNull();
        plan!.TargetSheetId.Should().Be(Sheet2);
        plan.Matches.Should().Equal(firstSheet2Match, secondSheet2Match);
    }

    [Fact]
    public void Plan_PrefersCurrentSheetWhenTheFirstMatchIsLocal()
    {
        var firstLocalMatch = new CellAddress(Sheet1, 1, 1);
        var remoteMatch = new CellAddress(Sheet2, 2, 1);
        var secondLocalMatch = new CellAddress(Sheet1, 1, 2);

        var plan = FormulaAuditSelectionPlanner.Plan(
            currentSheetId: Sheet1,
            matches: [firstLocalMatch, remoteMatch, secondLocalMatch]);

        plan.Should().NotBeNull();
        plan!.TargetSheetId.Should().Be(Sheet1);
        plan.Matches.Should().Equal(firstLocalMatch, secondLocalMatch);
    }

    [Fact]
    public void Plan_ReturnsNullWhenThereAreNoMatches()
    {
        FormulaAuditSelectionPlanner.Plan(Sheet1, [])
            .Should()
            .BeNull();
    }
}
