using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

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

    [Fact]
    public void Plan_ResolvesDirectAndTransitivePrecedentDependentQueries()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        var d1 = new CellAddress(sheet.Id, 1, 4);
        sheet.SetCell(a1, new NumberValue(5));
        sheet.SetCell(b1, Cell.FromFormula("A1+1"));
        sheet.SetCell(c1, Cell.FromFormula("B1+1"));
        sheet.SetCell(d1, Cell.FromFormula("C1+1"));

        FormulaAuditSelectionPlanner.Plan(workbook, c1, selectDependents: false, includeTransitive: false)!
            .Matches.Should().Equal(b1);
        FormulaAuditSelectionPlanner.Plan(workbook, c1, selectDependents: false, includeTransitive: true)!
            .Matches.Should().Equal(b1, a1);
        FormulaAuditSelectionPlanner.Plan(workbook, b1, selectDependents: true, includeTransitive: false)!
            .Matches.Should().Equal(c1);
        FormulaAuditSelectionPlanner.Plan(workbook, b1, selectDependents: true, includeTransitive: true)!
            .Matches.Should().Equal(c1, d1);
    }

}
