using FluentAssertions;
using FreeX.Core.IO;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookFileCompletionPlannerTests
{
    [Fact]
    public void PlanOpen_ProjectsWorkbookContextAndRecentRegistration()
    {
        var workbook = WorkbookFactory.Create(new WorkbookCreationOptions(DefaultSheetCount: 2));
        workbook.ActiveSheetIndex = 1;
        var adapter = new TestFileAdapter(
            extension: ".xlsx",
            formatName: "Excel Workbook",
            formats: [new FileFormatDescriptor(".xlsx", "Excel Workbook", CanOpen: true, CanSave: true)]);
        var path = Path.Combine(Path.GetTempPath(), "Budget.xlsx");
        var identity = new WorkbookFileAccessIdentity(path, "bookmark", "payload");
        var target = new WorkbookOpenTarget(path, adapter, ".xlsx", adapter.Formats[0], identity);
        var result = new WorkbookOpenResult(
            workbook,
            FeatureReport: null,
            DisplayName: "Budget",
            OpenedAsTemplate: false,
            LoadWarnings: []);

        var plan = WorkbookFileCompletionPlanner.PlanOpen(target, result);

        plan.Workbook.Should().BeSameAs(workbook);
        plan.DisplayName.Should().Be("Budget");
        plan.ActiveSheetId.Should().Be(workbook.Sheets[1].Id);
        plan.SourcePath.Should().Be(path);
        plan.CurrentFilePath.Should().Be(path);
        plan.OpenedAsTemplate.Should().BeFalse();
        plan.SourceFileAccessIdentity.Should().Be(identity);
        plan.RecentFileRegistration.FilePath.Should().Be(path);
        plan.RecentFileRegistration.FileAccessIdentity.Should().Be(identity);
        plan.RecentFileRegistration.SuppressRecentFiles.Should().BeFalse();
        plan.Status.Should().Be("Opened .xlsx.");
    }

    [Fact]
    public void PlanOpen_TemplateSuppressesCurrentPathAndRecentFiles()
    {
        var workbook = WorkbookFactory.Create();
        var adapter = new TestFileAdapter(
            extension: ".xltx",
            formatName: "Excel Template",
            formats: [new FileFormatDescriptor(".xltx", "Excel Template", CanOpen: true, CanSave: false, OpensAsTemplate: true)]);
        var path = Path.Combine(Path.GetTempPath(), "Template.xltx");
        var target = new WorkbookOpenTarget(path, adapter, ".xltx", adapter.Formats[0]);
        var result = new WorkbookOpenResult(
            workbook,
            FeatureReport: null,
            DisplayName: "Template",
            OpenedAsTemplate: true,
            LoadWarnings: []);

        var plan = WorkbookFileCompletionPlanner.PlanOpen(
            target,
            result,
            suppressRecentFiles: true);

        plan.CurrentFilePath.Should().BeNull();
        plan.SourceFileAccessIdentity.LocalPath.Should().Be(path);
        plan.SourceFileAccessIdentity.HasBookmark.Should().BeFalse();
        plan.RecentFileRegistration.FilePath.Should().Be(path);
        plan.RecentFileRegistration.SuppressRecentFiles.Should().BeTrue();
    }

    [Fact]
    public void PlanSaveFileContext_UsesWorkbookDisplayNameAndRecentRegistration()
    {
        var path = Path.Combine(Path.GetTempPath(), "SavedWorkbook.fxl");

        var context = WorkbookFileCompletionPlanner.PlanSaveFileContext(path);

        context.Path.Should().Be(path);
        context.DisplayName.Should().Be("SavedWorkbook");
        context.FileAccessIdentity.Should().BeNull();
        context.RecentFileRegistration.FilePath.Should().Be(path);
        context.RecentFileRegistration.FileAccessIdentity.Should().BeNull();
    }
}
