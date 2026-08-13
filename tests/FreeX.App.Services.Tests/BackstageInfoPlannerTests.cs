using System.Globalization;
using System.IO;
using FluentAssertions;
using Free.Shared.Localization;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

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
            Strings(),
            fileExists: path => path == @"C:\work\budget.xlsx",
            hasSelection: true);

        plan.WorkbookName.Should().Be("Budget");
        plan.FilePath.Should().Be(@"C:\work\budget.xlsx");
        plan.SheetCount.Should().Be("1");
        plan.Format.Should().Be(".xlsx");
        plan.StatisticsSummary.Should().Contain("Cells with data: 2");
        plan.StatisticsSummary.Should().Contain("Formulas: 1");
        plan.AccessibilitySummary.Should().Be("one issue");
        plan.FormulaErrorSummary.Should().Be("no formula errors");
        plan.SharingStatus.Should().Be(@"Ready for Windows Share from C:\work\budget.xlsx.");
        plan.ExportStatus.Should().Contain("selected range");
        plan.ExportStatus.Should().Contain("No Microsoft account or cloud service is required.");
    }

    [Fact]
    public void CreatePaneRequest_ProjectsEveryInfoPlanField()
    {
        var workbook = new Workbook("Budget");
        var sheet = workbook.AddSheet("Budget");
        var plan = BackstageInfoPlanner.Build(
            workbook,
            @"C:\work\budget.xlsx",
            Strings(),
            activeSheet: sheet,
            fileExists: _ => true,
            hasSelection: true);

        var request = BackstageInfoPlanner.CreatePaneRequest(plan);

        request.WorkbookName.Should().Be(plan.WorkbookName);
        request.FilePath.Should().Be(plan.FilePath);
        request.SheetCount.Should().Be(plan.SheetCount);
        request.Format.Should().Be(plan.Format);
        request.FileSize.Should().Be(plan.FileSize);
        request.LastModified.Should().Be(plan.LastModified);
        request.SharingStatus.Should().Be(plan.SharingStatus);
        request.ExportStatus.Should().Be(plan.ExportStatus);
        request.WorkbookProtectionSummary.Should().Be(plan.Summary.WorkbookProtectionSummary);
        request.ActiveSheetProtectionSummary.Should().Be(plan.Summary.ActiveSheetProtectionSummary);
        request.StatisticsSummary.Should().Be(plan.StatisticsSummary);
        request.AccessibilitySummary.Should().Be(plan.AccessibilitySummary);
        request.FormulaErrorSummary.Should().Be(plan.FormulaErrorSummary);
    }

    [Fact]
    public void Build_UsesSavedDefaultsWhenWorkbookHasNoCurrentPathOrAccessibilityIssues()
    {
        var workbook = new Workbook("Book1");
        workbook.AddSheet("Summary");

        var plan = BackstageInfoPlanner.Build(workbook, null, Strings(), culture: CultureInfo.InvariantCulture);

        plan.FilePath.Should().Be("not saved");
        plan.Format.Should().Be(".xlsx");
        plan.FileSize.Should().Be("not saved");
        plan.LastModified.Should().Be("not saved");
        plan.SharingStatus.Should().Be("Save As is required before Windows Share can send the workbook because it has not been saved yet.");
        plan.ExportStatus.Should().Contain("Ready for local PDF/XPS export");
        plan.ExportStatus.Should().Contain("select a range to enable selected-range export");
        plan.AccessibilitySummary.Should().Be("no accessibility issues");
        plan.FormulaErrorSummary.Should().Be("no formula errors");
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

        var plan = BackstageInfoPlanner.Build(workbook, null, Strings());

        plan.FormulaErrorSummary.Should().Be("2 issues");
    }

    // R69-meta-1: FormulaAuditingService.FindFormulaErrorIssues gained an optional cyclicCells
    // parameter in r68, but BackstageInfoPlanner.Build never threaded it through, so File > Info's
    // formula-error summary silently never counted circular-reference issues (a cyclic cell's Value
    // seeds to a plain 0 since r66, not ErrorValue.Circular, so the plain cell-value scan can't find
    // it on its own). This must count the circular issue once the caller passes the engine's cyclic set.
    [Fact]
    public void Build_SummarizesCircularReferenceIssue_WhenCyclicCellsThreadedThrough()
    {
        var workbook = new Workbook("Audit");
        var sheet = workbook.AddSheet("Sheet1");
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetFormula(a1, "A1");

        var plan = BackstageInfoPlanner.Build(workbook, null, Strings(), cyclicCells: [a1]);

        plan.FormulaErrorSummary.Should().Be("one issue");
    }

    // Sibling/no-regression: omitting cyclicCells (every other existing caller's default) must keep
    // behaving exactly as before -- the circular reference stays invisible to the summary rather than
    // throwing or double-counting.
    [Fact]
    public void Build_DoesNotSummarizeCircularReferenceIssue_WhenCyclicCellsOmitted()
    {
        var workbook = new Workbook("Audit");
        var sheet = workbook.AddSheet("Sheet1");
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetFormula(a1, "A1");

        var plan = BackstageInfoPlanner.Build(workbook, null, Strings());

        plan.FormulaErrorSummary.Should().Be("no formula errors");
    }

    [Fact]
    public void Build_IncludesSavedFileSizeAndLastModifiedMetadata()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "saved.xlsx");
        File.WriteAllBytes(path, new byte[1536]);
        var workbook = new Workbook("Saved");
        var sheet = workbook.AddSheet("Sheet1");

        var lastWrite = new DateTime(2026, 5, 31, 14, 25, 0);
        File.SetLastWriteTime(path, lastWrite);
        var expectedLastWrite = File.GetLastWriteTime(path).ToString("g", CultureInfo.InvariantCulture);

        var plan = BackstageInfoPlanner.Build(workbook, path, Strings(), sheet, CultureInfo.InvariantCulture);

        plan.FileSize.Should().Be("1.5 KB (1,536 bytes)");
        plan.LastModified.Should().Be(expectedLastWrite);
        plan.SharingStatus.Should().Be($"Ready for Windows Share from {path}.");
        plan.ExportStatus.Should().Contain("Ready for local PDF/XPS export");
        plan.Summary.ActiveSheetProtectionSummary.Should().Be("Active sheet unprotected.");
    }

    [Fact]
    public void Build_ReportsMissingMetadataWhenSavedPathNoLongerExists()
    {
        var workbook = new Workbook("Missing");
        workbook.AddSheet("Sheet1");

        var plan = BackstageInfoPlanner.Build(
            workbook,
            @"C:\work\missing.xlsx",
            Strings(),
            culture: CultureInfo.InvariantCulture);

        plan.FilePath.Should().Be(@"C:\work\missing.xlsx");
        plan.FileSize.Should().Be("missing");
        plan.LastModified.Should().Be("missing");
        plan.SharingStatus.Should().Be(@"Save As is required before Windows Share can send the workbook because the saved path is missing: C:\work\missing.xlsx.");
        plan.ExportStatus.Should().Contain("Ready for local PDF/XPS export");
    }

    [Fact]
    public void Build_ReportsInvalidSavedPathsWithoutThrowing()
    {
        var workbook = new Workbook("InvalidPath");
        workbook.AddSheet("Sheet1");

        var plan = BackstageInfoPlanner.Build(
            workbook,
            "bad\0path.xlsx",
            Strings(),
            culture: CultureInfo.InvariantCulture,
            fileExists: _ => throw new InvalidOperationException("invalid paths must not be probed"));

        plan.FilePath.Should().Be("bad\0path.xlsx");
        plan.Format.Should().Be(".xlsx");
        plan.FileSize.Should().Be("missing");
        plan.LastModified.Should().Be("missing");
        plan.SharingStatus.Should().Be("Save As is required before Windows Share can send the workbook because the saved path is not a valid local file path.");
        plan.ExportStatus.Should().Contain("Ready for local PDF/XPS export");
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
            Strings(),
            sheet,
            CultureInfo.InvariantCulture);

        plan.Summary.WorkbookProtectionSummary.Should().Be("Workbook structure protected.");
        plan.Summary.ActiveSheetProtectionSummary.Should().Be("Active sheet protected.");
    }

    private static ResourceKeyTextResolver Strings() =>
        new(GetText, (key, args) => string.Format(CultureInfo.InvariantCulture, GetText(key), args));

    private static string GetText(string key) => key switch
    {
        "Backstage_Info_NotSavedYet" => "not saved",
        "Backstage_Info_FileMissing" => "missing",
        "Backstage_Info_NoAccessibilityIssues" => "no accessibility issues",
        "Backstage_Info_NoFormulaErrors" => "no formula errors",
        "Backstage_Info_OneIssueFound" => "one issue",
        "Backstage_Info_MultipleIssuesFound" => "{0} issues",
        "Backstage_Info_ProtectionNone" => "none",
        "Backstage_Info_ProtectionStructure" => "structure protected",
        "Backstage_Info_ProtectionSheets" => "{0} of {1} sheets protected",
        "Backstage_Info_ProtectionStructureAndSheets" => "structure and {0} of {1} sheets protected",
        "Backstage_Info_ActiveSheetProtected" => "active sheet protected",
        "Backstage_Info_ActiveSheetUnprotected" => "active sheet unprotected",
        "Backstage_Info_UnsavedChanges" => "unsaved",
        "Backstage_Info_ByteSingularFormat" => "{0} byte",
        "Backstage_Info_BytePluralFormat" => "{0} bytes",
        "Backstage_Info_ByteSizeWithUnitFormat" => "{0} {1} ({2} bytes)",
        _ => key
    };
}
