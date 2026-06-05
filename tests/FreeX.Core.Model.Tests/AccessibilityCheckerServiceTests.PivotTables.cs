using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public sealed partial class AccessibilityCheckerServiceTests
{
    [Fact]
    public void FindIssues_FlagsPivotTableMissingAltTextAndDoesNotUseNameAsAccessibleText()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Pivot Summary");
        sheet.PivotTables.Add(CreatePivotTable(
            sheet,
            "Regional sales pivot",
            new CellAddress(sheet.Id, 4, 2),
            altTextTitle: " ",
            altTextDescription: null));

        var issues = AccessibilityCheckerService.FindIssues(workbook);

        var issue = issues.Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.MissingAltText).Subject;
        issue.Location.Should().Be("B4");
        issue.Message.Should().Be("PivotTable is missing alternate text.");
    }

    [Theory]
    [InlineData("PivotTable")]
    [InlineData("Pivot Table")]
    [InlineData("PivotTable1")]
    [InlineData("PivotTable 1")]
    [InlineData("PivotTable_1")]
    [InlineData("PivotTable-1")]
    [InlineData("Pivot Table 1")]
    [InlineData("Pivot Table_1")]
    [InlineData("Pivot Table-1")]
    public void FindIssues_FlagsPivotTableGenericAltText(string altText)
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Pivot Summary");
        sheet.PivotTables.Add(CreatePivotTable(
            sheet,
            "SalesPivot",
            new CellAddress(sheet.Id, 5, 3),
            altTextTitle: altText,
            altTextDescription: null));

        var issues = AccessibilityCheckerService.FindIssues(workbook);

        var issue = issues.Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.GenericAltText).Subject;
        issue.Location.Should().Be("C5");
        issue.Message.Should().Be("PivotTable alternate text should describe the object.");
    }

    [Fact]
    public void FindIssues_AllowsDescriptivePivotTableTitleOrDescription()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Pivot Summary");
        sheet.PivotTables.Add(CreatePivotTable(
            sheet,
            "PivotTable1",
            new CellAddress(sheet.Id, 2, 1),
            altTextTitle: "Regional sales by quarter",
            altTextDescription: null));
        sheet.PivotTables.Add(CreatePivotTable(
            sheet,
            "PivotTable2",
            new CellAddress(sheet.Id, 8, 1),
            altTextTitle: null,
            altTextDescription: "Expense variance by department"));
        sheet.PivotTables.Add(CreatePivotTable(
            sheet,
            "PivotTable3",
            new CellAddress(sheet.Id, 14, 1),
            altTextTitle: "PivotTable 3",
            altTextDescription: "Inventory movement by category"));

        AccessibilityCheckerService.FindIssues(workbook)
            .Where(i => i.Kind is AccessibilityIssueKind.MissingAltText or AccessibilityIssueKind.GenericAltText)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void FindIssues_FlagsGenericPivotTableDescriptionWhenTitleIsBlank()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Pivot Summary");
        sheet.PivotTables.Add(CreatePivotTable(
            sheet,
            "SalesPivot",
            new CellAddress(sheet.Id, 6, 4),
            altTextTitle: null,
            altTextDescription: "Pivot Table 1"));

        var issues = AccessibilityCheckerService.FindIssues(workbook);

        var issue = issues.Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.GenericAltText).Subject;
        issue.Location.Should().Be("D6");
        issue.Message.Should().Be("PivotTable alternate text should describe the object.");
    }

    private static PivotTableModel CreatePivotTable(
        Sheet sheet,
        string name,
        CellAddress targetStart,
        string? altTextTitle,
        string? altTextDescription)
    {
        var targetEnd = new CellAddress(sheet.Id, targetStart.Row + 3u, targetStart.Col + 2u);
        return new PivotTableModel
        {
            Name = name,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 20, 1), new CellAddress(sheet.Id, 25, 4)),
            TargetRange = new GridRange(targetStart, targetEnd),
            AltTextTitle = altTextTitle,
            AltTextDescription = altTextDescription
        };
    }
}
