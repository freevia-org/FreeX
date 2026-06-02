using System.Globalization;
using System.IO;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

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

        var plan = BackstageInfoPlanner.Build(
            workbook,
            @"C:\work\budget.xlsx",
            fileExists: path => path == @"C:\work\budget.xlsx");

        plan.WorkbookName.Should().Be("Budget");
        plan.FilePath.Should().Be(@"C:\work\budget.xlsx");
        plan.SheetCount.Should().Be("1");
        plan.Format.Should().Be(".xlsx");
        plan.StatisticsSummary.Should().Contain("Cells with data: 2");
        plan.StatisticsSummary.Should().Contain("Formulas: 1");
        plan.AccessibilitySummary.Should().Be(UiText.Get("Backstage_Info_OneIssueFound"));
        plan.FormulaErrorSummary.Should().Be(UiText.Get("Backstage_Info_NoFormulaErrors"));
        plan.SharingStatus.Should().Be(@"Ready for Windows Share from C:\work\budget.xlsx.");
    }

    [Fact]
    public void Build_UsesSavedDefaultsWhenWorkbookHasNoCurrentPathOrAccessibilityIssues()
    {
        var workbook = new Workbook("Book1");
        workbook.AddSheet("Summary");

        var plan = BackstageInfoPlanner.Build(workbook, null, culture: CultureInfo.InvariantCulture);

        plan.FilePath.Should().Be(UiText.Get("Backstage_Info_NotSavedYet"));
        plan.Format.Should().Be(".xlsx");
        plan.FileSize.Should().Be(UiText.Get("Backstage_Info_NotSavedYet"));
        plan.LastModified.Should().Be(UiText.Get("Backstage_Info_NotSavedYet"));
        plan.SharingStatus.Should().Be("Save As is required before Windows Share can send the workbook because it has not been saved yet.");
        plan.AccessibilitySummary.Should().Be(UiText.Get("Backstage_Info_NoAccessibilityIssues"));
        plan.FormulaErrorSummary.Should().Be(UiText.Get("Backstage_Info_NoFormulaErrors"));
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

        plan.FormulaErrorSummary.Should().Be(UiText.Format("Backstage_Info_MultipleIssuesFound", 2));
    }

    [Fact]
    public void Build_IncludesSavedFileSizeAndLastModifiedMetadata()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xlsx");
        File.WriteAllBytes(path, new byte[1536]);
        var workbook = new Workbook("Saved");
        var sheet = workbook.AddSheet("Sheet1");

        try
        {
            var lastWrite = new DateTime(2026, 5, 31, 14, 25, 0);
            File.SetLastWriteTime(path, lastWrite);
            var expectedLastWrite = File.GetLastWriteTime(path).ToString("g", CultureInfo.InvariantCulture);

            var plan = BackstageInfoPlanner.Build(workbook, path, sheet, CultureInfo.InvariantCulture);

            plan.FileSize.Should().Be("1.5 KB (1,536 bytes)");
            plan.LastModified.Should().Be(expectedLastWrite);
            plan.SharingStatus.Should().Be($"Ready for Windows Share from {path}.");
            plan.Summary.ActiveSheetProtectionSummary.Should().Be("Active sheet unprotected.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Build_ReportsMissingMetadataWhenSavedPathNoLongerExists()
    {
        var workbook = new Workbook("Missing");
        workbook.AddSheet("Sheet1");

        var plan = BackstageInfoPlanner.Build(
            workbook,
            @"C:\work\missing.xlsx",
            culture: CultureInfo.InvariantCulture);

        plan.FilePath.Should().Be(@"C:\work\missing.xlsx");
        plan.FileSize.Should().Be(UiText.Get("Backstage_Info_FileMissing"));
        plan.LastModified.Should().Be(UiText.Get("Backstage_Info_FileMissing"));
        plan.SharingStatus.Should().Be(@"Save As is required before Windows Share can send the workbook because the saved path is missing: C:\work\missing.xlsx.");
    }

    [Fact]
    public void Build_IncludesWorkbookAndActiveSheetProtectionSummaries()
    {
        var workbook = new Workbook("Protected") { IsStructureProtected = true };
        var sheet = workbook.AddSheet("Sheet1");
        sheet.IsProtected = true;

        var plan = BackstageInfoPlanner.Build(
            workbook,
            null,
            sheet,
            CultureInfo.InvariantCulture);

        plan.Summary.WorkbookProtectionSummary.Should().Be("Workbook structure protected.");
        plan.Summary.ActiveSheetProtectionSummary.Should().Be("Active sheet protected.");
    }
}
