using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookExportReadinessPlannerTests
{
    [Fact]
    public void Create_ReportsLocalPdfXpsReadinessWithSelectionScope()
    {
        var workbook = new Workbook("Budget");
        workbook.AddSheet("Sheet1");

        var plan = WorkbookExportReadinessPlanner.Create(workbook, hasSelection: true);

        plan.IsReady.Should().BeTrue();
        plan.StatusText.Should().Contain("Ready for local PDF/XPS export");
        plan.StatusText.Should().Contain("selected range");
        plan.StatusText.Should().Contain("XPS routing");
        plan.StatusText.Should().Contain("PDF-only choices called out");
        plan.StatusText.Should().Contain("PDF/A and tagged PDF are exposed as unsupported and rejected");
        plan.StatusText.Should().Contain("No Microsoft account or cloud service is required.");
    }

    [Fact]
    public void CreateForAvailableWorkbook_ExplainsSelectionScopeRequiresASelectedRange()
    {
        var plan = WorkbookExportReadinessPlanner.CreateForAvailableWorkbook();

        plan.IsReady.Should().BeTrue();
        plan.StatusText.Should().Contain("select a range to enable selected-range export");
    }

    [Fact]
    public void Create_ReportsNoVisibleWorksheetsAsNotReady()
    {
        var workbook = new Workbook("Hidden");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.IsHidden = true;

        var plan = WorkbookExportReadinessPlanner.Create(workbook, hasSelection: true);

        plan.IsReady.Should().BeFalse();
        plan.StatusText.Should().Be("No visible worksheets are available for local PDF/XPS export.");
    }
}
