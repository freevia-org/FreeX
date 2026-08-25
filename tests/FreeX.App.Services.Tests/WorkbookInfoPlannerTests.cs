using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookInfoPlannerTests
{
    [Fact]
    public void Build_UnsavedWorkbook_ReportsNotSavedAndNoFileMetadata()
    {
        var workbook = new Workbook("Book1");
        workbook.AddSheet("Sheet1");

        var plan = WorkbookInfoPlanner.Build(workbook, currentFilePath: null, activeSheetIndex: 0, hasUnsavedChanges: true);

        plan.IsSaved.Should().BeFalse();
        plan.FilePath.Should().BeNull();
        plan.FileExistsOnDisk.Should().BeFalse();
        plan.FileSizeBytes.Should().BeNull();
        plan.LastModifiedLocal.Should().BeNull();
        plan.FormatExtension.Should().Be(".xlsx");
        plan.HasUnsavedChanges.Should().BeTrue();
        plan.SheetCount.Should().Be(1);
        plan.ProtectionPosture.Should().Be(WorkbookProtectionPosture.None);
    }

    [Fact]
    public void Build_SavedWorkbookWithMetadata_SurfacesSizeModifiedAndFormat()
    {
        var workbook = new Workbook("Budget");
        workbook.AddSheet("Sheet1");
        var modified = new System.DateTime(2026, 6, 18, 9, 30, 0, System.DateTimeKind.Local);

        var plan = WorkbookInfoPlanner.Build(
            workbook,
            currentFilePath: @"C:\Work\Budget.xlsx",
            activeSheetIndex: 0,
            fileSizeBytes: 4096,
            lastModifiedUtc: modified.ToUniversalTime(),
            lastModifiedLocal: modified);

        plan.IsSaved.Should().BeTrue();
        plan.FileExistsOnDisk.Should().BeTrue();
        plan.FileSizeBytes.Should().Be(4096);
        plan.LastModifiedLocal.Should().Be(modified);
        plan.FormatExtension.Should().Be(".xlsx");
    }

    [Fact]
    public void Build_DottedFoldersAndInvalidPaths_FallBackToDefaultFormat()
    {
        var workbook = new Workbook("Budget");
        workbook.AddSheet("Sheet1");

        WorkbookInfoPlanner.Build(workbook, Path.Combine("Work.v1", "Budget"), activeSheetIndex: 0)
            .FormatExtension
            .Should()
            .Be(".xlsx");
        WorkbookInfoPlanner.Build(workbook, "bad\0path.csv", activeSheetIndex: 0)
            .FormatExtension
            .Should()
            .Be(".xlsx");
    }

    [Fact]
    public void Build_ProtectedSheetsAndStructure_ReportsCombinedPosture()
    {
        var workbook = new Workbook("Secured");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        sheet1.IsProtected = true;
        sheet2.IsProtected = true;
        workbook.IsStructureProtected = true;

        var plan = WorkbookInfoPlanner.Build(workbook, currentFilePath: null, activeSheetIndex: 0);

        plan.ProtectedSheetCount.Should().Be(2);
        plan.IsStructureProtected.Should().BeTrue();
        plan.ProtectionPosture.Should().Be(WorkbookProtectionPosture.StructureAndSheetsProtected);
        plan.ActiveSheetIsProtected.Should().BeTrue();
    }

    [Fact]
    public void Build_OnlySheetsProtected_ReportsSheetsProtectedPosture()
    {
        var workbook = new Workbook("Secured");
        var sheet1 = workbook.AddSheet("Sheet1");
        workbook.AddSheet("Sheet2");
        sheet1.IsProtected = true;

        var plan = WorkbookInfoPlanner.Build(workbook, currentFilePath: null, activeSheetIndex: 1);

        plan.ProtectionPosture.Should().Be(WorkbookProtectionPosture.SheetsProtected);
        plan.ActiveSheetIsProtected.Should().BeFalse();
    }
}
