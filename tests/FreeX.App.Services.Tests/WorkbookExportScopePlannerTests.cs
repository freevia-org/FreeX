using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookExportScopePlannerTests
{
    [Fact]
    public void Build_WithSelection_EnablesAllScopesAndDefaultsToActiveSheet()
    {
        var workbook = new Workbook("Budget");
        workbook.AddSheet("Sheet1");

        var plan = WorkbookExportScopePlanner.Build(workbook, hasSelection: true, WorkbookExportPrintSurface.MacOs);

        plan.CanExport.Should().BeTrue();
        plan.DefaultScope.Should().Be(WorkbookExportPrintScope.ActiveSheet);
        plan.Scopes.Should().HaveCount(3);
        plan.Scopes.Single(s => s.Scope == WorkbookExportPrintScope.SelectedRange).IsAvailable.Should().BeTrue();
        plan.Scopes.Single(s => s.Scope == WorkbookExportPrintScope.ActiveSheet).IsDefault.Should().BeTrue();
    }

    [Fact]
    public void Build_WithoutSelection_DisablesSelectedRangeScope()
    {
        var workbook = new Workbook("Budget");
        workbook.AddSheet("Sheet1");

        var plan = WorkbookExportScopePlanner.Build(workbook, hasSelection: false, WorkbookExportPrintSurface.MacOs);

        plan.Scopes.Single(s => s.Scope == WorkbookExportPrintScope.SelectedRange).IsAvailable.Should().BeFalse();
        plan.Scopes.Single(s => s.Scope == WorkbookExportPrintScope.VisibleWorkbook).IsAvailable.Should().BeTrue();
    }

    [Fact]
    public void Build_MacOsSurface_OffersPdfOnly()
    {
        var workbook = new Workbook("Budget");
        workbook.AddSheet("Sheet1");

        var plan = WorkbookExportScopePlanner.Build(workbook, hasSelection: true, WorkbookExportPrintSurface.MacOs);

        plan.SupportedOutputKinds.Should().Equal(WorkbookExportPrintOutputKind.Pdf);
        plan.DefaultOutputKind.Should().Be(WorkbookExportPrintOutputKind.Pdf);
    }

    [Fact]
    public void Build_WindowsSurface_OffersPdfAndXps()
    {
        var workbook = new Workbook("Budget");
        workbook.AddSheet("Sheet1");

        var plan = WorkbookExportScopePlanner.Build(workbook, hasSelection: false, WorkbookExportPrintSurface.WindowsDesktop);

        plan.SupportedOutputKinds.Should().Contain(WorkbookExportPrintOutputKind.Pdf);
        plan.SupportedOutputKinds.Should().Contain(WorkbookExportPrintOutputKind.Xps);
    }

    [Fact]
    public void Build_NoVisibleSheet_CannotExport()
    {
        var workbook = new Workbook("Empty");
        var sheet = workbook.AddSheet("Hidden");
        sheet.IsHidden = true;

        var plan = WorkbookExportScopePlanner.Build(workbook, hasSelection: true, WorkbookExportPrintSurface.MacOs);

        plan.CanExport.Should().BeFalse();
        plan.Scopes.Should().OnlyContain(s => !s.IsAvailable);
    }
}
