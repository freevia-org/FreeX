using System.Globalization;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookInfoDisplayPlannerTests
{
    [Fact]
    public void Build_WindowsSurface_PreservesDetailedBackstageProfile()
    {
        var workbook = new Workbook("Budget");
        var sheet = workbook.AddSheet("Summary");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(42)));
        workbook.IsStructureProtected = true;
        var modified = new DateTime(2026, 5, 31, 14, 25, 0);
        var plan = WorkbookInfoPlanner.Build(
            workbook,
            @"C:\Work\Budget.xlsx",
            activeSheetIndex: 0,
            fileSizeBytes: 1536,
            lastModifiedLocal: modified,
            hasUnsavedChanges: true);

        var display = WorkbookInfoDisplayPlanner.Build(
            plan,
            WorkbookInfoDisplaySurface.WindowsBackstagePane,
            Strings(),
            CultureInfo.InvariantCulture);

        display.FilePath.Should().Be(@"C:\Work\Budget.xlsx");
        display.FileSize.Should().Be("1.5 KB (1,536 bytes)");
        display.LastModified.Should().Be("05/31/2026 14:25");
        display.StatisticsSummary.Should().Contain("Sheets: 1");
        display.StatisticsSummary.Should().Contain("Cells with data: 1");
        display.WorkbookProtectionSummary.Should().Be("structure protected");
        display.ActiveSheetProtectionSummary.Should().Be("active sheet unprotected");
        display.UnsavedChangesNote.Should().Be("unsaved");
    }

    [Fact]
    public void Build_AvaloniaSurface_PreservesCompactBackstageProfile()
    {
        var workbook = new Workbook("Budget");
        var sheet1 = workbook.AddSheet("Summary");
        var sheet2 = workbook.AddSheet("Detail");
        sheet1.IsProtected = true;
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), Cell.FromValue(new NumberValue(42)));
        sheet2.SetFormula(new CellAddress(sheet2.Id, 2, 1), "A1*2");

        var plan = WorkbookInfoPlanner.Build(
            workbook,
            @"C:\Work\Budget.xlsx",
            activeSheetIndex: 0,
            fileSizeBytes: 1536);

        var display = WorkbookInfoDisplayPlanner.Build(
            plan,
            WorkbookInfoDisplaySurface.AvaloniaBackstageInfoDialog,
            Strings(),
            CultureInfo.InvariantCulture);

        display.FileSize.Should().Be("1.5 KB");
        display.StatisticsSummary.Should().Be(string.Join(Environment.NewLine,
            "Cells with data: 2",
            "Formulas: 1",
            "Charts: 0",
            "Pictures: 0",
            "Named ranges: 0"));
        display.WorkbookProtectionSummary.Should().Be("1 of 2 sheets protected");
        display.ActiveSheetProtectionSummary.Should().Be("active sheet protected");
        display.UnsavedChangesNote.Should().BeNull();
    }

    [Fact]
    public void Build_UnsavedAndMissingFiles_UseSurfaceStringProvider()
    {
        var workbook = new Workbook("Book1");
        workbook.AddSheet("Sheet1");

        var unsaved = WorkbookInfoDisplayPlanner.Build(
            WorkbookInfoPlanner.Build(workbook, currentFilePath: null, activeSheetIndex: 0),
            WorkbookInfoDisplaySurface.AvaloniaBackstageInfoDialog,
            Strings(),
            CultureInfo.InvariantCulture);
        var missing = WorkbookInfoDisplayPlanner.Build(
            WorkbookInfoPlanner.Build(workbook, @"C:\Work\Missing.xlsx", activeSheetIndex: 0),
            WorkbookInfoDisplaySurface.AvaloniaBackstageInfoDialog,
            Strings(),
            CultureInfo.InvariantCulture);

        unsaved.FilePath.Should().Be("not saved");
        unsaved.FileSize.Should().Be("not saved");
        unsaved.LastModified.Should().Be("not saved");
        missing.FilePath.Should().Be(@"C:\Work\Missing.xlsx");
        missing.FileSize.Should().Be("missing");
        missing.LastModified.Should().Be("missing");
    }

    private static WorkbookInfoDisplayStrings Strings() =>
        new(GetText, (key, args) => string.Format(CultureInfo.InvariantCulture, GetText(key), args));

    private static string GetText(string key) => key switch
    {
        "Backstage_Info_NotSavedYet" => "not saved",
        "Backstage_Info_FileMissing" => "missing",
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
