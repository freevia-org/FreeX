using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public sealed partial class AccessibilityCheckerServiceTests
{
    [Fact]
    public void FindIssues_FlagsStructuredTablesWithoutHeaderRows()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            HeaderRowCount = 0,
            HasAutoFilter = true,
        });

        var issue = AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.TableMissingHeaderRow).Subject;

        issue.SheetId.Should().Be(sheet.Id);
        issue.SheetName.Should().Be("Sales");
        issue.Location.Should().Be("A1:B3");
        issue.Message.Should().Be("Tables should include a header row.");
    }

    [Fact]
    public void FindIssues_FlagsStructuredTablesWithBlankHeaderText()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue(" "));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(42));
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2)),
            HeaderRowCount = 1,
            HasAutoFilter = true,
        });

        var issue = AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.TableMissingHeaderText).Subject;

        issue.SheetId.Should().Be(sheet.Id);
        issue.SheetName.Should().Be("Sales");
        issue.Location.Should().Be("B1");
        issue.Message.Should().Be("Table headers should not be blank.");
    }

    [Fact]
    public void FindIssues_FlagsStructuredTablesWithMissingHeaderCellDespiteColumnMetadata()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(42));
        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2)),
            HeaderRowCount = 1,
            HasAutoFilter = true,
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Region"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Sales"));
        sheet.StructuredTables.Add(table);

        var issue = AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.TableMissingHeaderText).Subject;

        issue.Location.Should().Be("B1");
        issue.Message.Should().Be("Table headers should not be blank.");
    }

    [Fact]
    public void FindIssues_FlagsStructuredTablesWithDefaultAndDuplicateHeaderText()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Column1"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue(" region "));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(42));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(43));
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 3)),
            HeaderRowCount = 1,
            HasAutoFilter = true,
        });

        var issues = AccessibilityCheckerService.FindIssues(workbook);

        issues.Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.TableDefaultHeaderText)
            .Which.Location.Should().Be("A1");
        issues.Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.TableDuplicateHeaderText)
            .Which.Location.Should().Be("C1");
    }
}
