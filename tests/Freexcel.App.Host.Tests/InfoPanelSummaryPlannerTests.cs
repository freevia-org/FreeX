using System.Globalization;
using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class InfoPanelSummaryPlannerTests
{
    [Fact]
    public void Create_UsesStatusCopyForProtectedWorkbookAndSheet()
    {
        var workbook = new Workbook { IsStructureProtected = true };
        var sheet = workbook.AddSheet("Summary");
        sheet.IsProtected = true;

        var plan = InfoPanelSummaryPlanner.Create(workbook, sheet, CultureInfo.InvariantCulture);

        plan.WorkbookProtectionSummary.Should().Be("Workbook structure protected.");
        plan.ActiveSheetProtectionSummary.Should().Be("Active sheet protected.");
    }

    [Fact]
    public void Create_UsesStatusCopyForUnprotectedWorkbookAndSheet()
    {
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Summary");

        var plan = InfoPanelSummaryPlanner.Create(workbook, sheet, CultureInfo.InvariantCulture);

        plan.WorkbookProtectionSummary.Should().Be("Workbook structure unprotected.");
        plan.ActiveSheetProtectionSummary.Should().Be("Active sheet unprotected.");
    }

    [Fact]
    public void Create_ReportsMissingActiveSheet()
    {
        var plan = InfoPanelSummaryPlanner.Create(new Workbook(), activeSheet: null, CultureInfo.InvariantCulture);

        plan.ActiveSheetProtectionSummary.Should().Be("No active sheet.");
    }

    [Theory]
    [InlineData(0, "No accessibility issues found.")]
    [InlineData(1, "1 accessibility issue found.")]
    [InlineData(2, "2 accessibility issues found.")]
    public void FormatAccessibilityIssueSummary_UsesSingularAndPluralWording(int count, string expected)
    {
        InfoPanelSummaryPlanner.FormatAccessibilityIssueSummary(count, CultureInfo.InvariantCulture)
            .Should()
            .Be(expected);
    }

    [Fact]
    public void Create_CountsAccessibilityIssuesFromWorkbook()
    {
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Summary");
        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 2)));
        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 2, 2)));

        var plan = InfoPanelSummaryPlanner.Create(workbook, sheet, CultureInfo.InvariantCulture);

        plan.AccessibilityIssueSummary.Should().Be("2 accessibility issues found.");
    }

    [Fact]
    public void Create_FormatsWorkbookStatisticsCounts()
    {
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");
        for (var row = 1u; row <= 1234; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));

        var plan = InfoPanelSummaryPlanner.Create(workbook, sheet, CultureInfo.InvariantCulture);

        plan.SheetCount.Should().Be("1");
        plan.CellsWithDataCount.Should().Be("1,234");
        plan.FormulaCount.Should().Be("0");
        plan.CommentCount.Should().Be("0");
        plan.ChartCount.Should().Be("0");
        plan.PictureCount.Should().Be("0");
        plan.ShapeCount.Should().Be("0");
        plan.NamedRangeCount.Should().Be("0");
    }
}
