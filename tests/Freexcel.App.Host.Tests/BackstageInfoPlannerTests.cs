using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class BackstageInfoPlannerTests
{
    [Fact]
    public void Build_SummarizesStatisticsAndAccessibilityIssuesForInfoPanel()
    {
        var workbook = new Workbook("Budget");
        var sheet = workbook.AddSheet("Budget");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(42)));
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 2), "A1*2");
        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 2, 2)));

        var plan = BackstageInfoPlanner.Build(workbook, @"C:\work\budget.xlsx");

        plan.WorkbookName.Should().Be("Budget");
        plan.FilePath.Should().Be(@"C:\work\budget.xlsx");
        plan.SheetCount.Should().Be("1");
        plan.Format.Should().Be(".xlsx");
        plan.StatisticsSummary.Should().Contain("Cells with data: 2");
        plan.StatisticsSummary.Should().Contain("Formulas: 1");
        plan.AccessibilitySummary.Should().Be("1 issue found");
        plan.FormulaErrorSummary.Should().Be("No formula errors found");
    }

    [Fact]
    public void Build_UsesSavedDefaultsWhenWorkbookHasNoCurrentPathOrAccessibilityIssues()
    {
        var workbook = new Workbook("Book1");
        workbook.AddSheet("Summary");

        var plan = BackstageInfoPlanner.Build(workbook, null);

        plan.FilePath.Should().Be("Not saved yet");
        plan.Format.Should().Be(".xlsx");
        plan.AccessibilitySummary.Should().Be("No accessibility issues found");
        plan.FormulaErrorSummary.Should().Be("No formula errors found");
    }

    [Fact]
    public void Build_SummarizesFormulaErrorIssuesForInfoPanel()
    {
        var workbook = new Workbook("Audit");
        var sheet = workbook.AddSheet("Sheet1");
        var cell = Cell.FromFormula("1/0");
        cell.Value = ErrorValue.DivByZero;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("1/2/24"));

        var plan = BackstageInfoPlanner.Build(workbook, null);

        plan.FormulaErrorSummary.Should().Be("2 issues found");
    }
}
