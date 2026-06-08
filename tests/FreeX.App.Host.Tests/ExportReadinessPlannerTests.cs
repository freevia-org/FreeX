using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class ExportReadinessPlannerTests
{
    [Fact]
    public void Create_DelegatesToWorkbookExportReadinessPlanner()
    {
        var workbook = new Workbook("Budget");
        workbook.AddSheet("Sheet1");

        var plan = ExportReadinessPlanner.Create(workbook, hasSelection: true);
        var sharedPlan = WorkbookExportReadinessPlanner.Create(workbook, hasSelection: true);

        plan.IsReady.Should().Be(sharedPlan.IsReady);
        plan.StatusText.Should().Be(sharedPlan.StatusText);
    }

    [Fact]
    public void CreateForAvailableWorkbook_DelegatesToWorkbookExportReadinessPlanner()
    {
        var plan = ExportReadinessPlanner.CreateForAvailableWorkbook(hasSelection: true);
        var sharedPlan = WorkbookExportReadinessPlanner.CreateForAvailableWorkbook(hasSelection: true);

        plan.IsReady.Should().Be(sharedPlan.IsReady);
        plan.StatusText.Should().Be(sharedPlan.StatusText);
    }
}
